#!/usr/bin/env bash
# Start, check and stop the local Cadence core stack without Docker:
# a throwaway PostgreSQL 18 cluster (ADR-2533) (port 5433, trust auth), Keycloak from the release tarball (port 8080,
# docker/cadence-realm.json imported) and the Aspire AppHost core profile (gateway 8000, execution 8002,
# kernel 8018, web 3000, dashboard 15000) in tmux session "apphost".
# Usage, from the repository root: scripts/local_stack_nodocker.sh up|status|down
# Agents: .claude/skills/run-cadence/SKILL.md drives the running stack with a headless browser.
set -euo pipefail

STATE=${CADENCE_DEV_STATE:-/opt/cadence-dev}
KC_VERSION=26.8.1 # same as docker/docker-compose.yml
KC_HOME=$STATE/keycloak-$KC_VERSION
PG_BIN=/usr/lib/postgresql/18/bin
PG_PORT=5433
CONN="Host=127.0.0.1;Port=$PG_PORT;Username=postgres;Database=cadence"
REPO=$(git rev-parse --show-toplevel)

wait_for() { # url seconds
  timeout "$2" bash -c "until curl -sf '$1' >/dev/null; do sleep 2; done" || { echo "timed out waiting for $1" >&2; return 1; }
}

up() {
  mkdir -p "$STATE"

  # PostgreSQL: own cluster so the machine's main cluster and its passwords stay untouched.
  # The cluster is throwaway but may hold someone's work, so a cluster from another major is reported, not replaced.
  if [ -f "$STATE/pg/data/PG_VERSION" ] && [ "$(cat "$STATE/pg/data/PG_VERSION")" != "$(basename "$(dirname "$PG_BIN")")" ]; then
    echo "$STATE/pg/data is a PostgreSQL $(cat "$STATE/pg/data/PG_VERSION") cluster; the stack runs $PG_BIN (ADR-2533)." >&2
    echo "Run '$0 down', then move $STATE/pg/data aside, and run '$0 up' again for a new cluster." >&2
    return 1
  fi
  if [ ! -d "$STATE/pg/data" ]; then
    mkdir -p "$STATE/pg" && chown postgres "$STATE/pg"
    su postgres -c "$PG_BIN/initdb -D $STATE/pg/data --auth=trust -U postgres -E UTF8 --locale=C.UTF-8" >/dev/null
  fi
  if ! su postgres -c "$PG_BIN/pg_ctl -D $STATE/pg/data status" >/dev/null 2>&1; then
    su postgres -c "$PG_BIN/pg_ctl -D $STATE/pg/data -o '-p $PG_PORT -k /tmp' -l $STATE/pg/log start" >/dev/null
  fi
  psql -h 127.0.0.1 -p $PG_PORT -U postgres -tAc "select 1 from pg_database where datname='cadence'" | grep -q 1 \
    || psql -h 127.0.0.1 -p $PG_PORT -U postgres -qc "create database cadence"
  echo "postgres  ok  ($CONN)"

  # Keycloak: dev mode with the repository realm; the realm users' password is "password".
  if [ ! -d "$KC_HOME" ]; then
    curl -fsSL -o "$STATE/keycloak.tgz" "https://github.com/keycloak/keycloak/releases/download/$KC_VERSION/keycloak-$KC_VERSION.tar.gz"
    tar xzf "$STATE/keycloak.tgz" -C "$STATE"
  fi
  if ! curl -sf http://localhost:8080/realms/cadence >/dev/null; then
    mkdir -p "$KC_HOME/data/import"
    cp "$REPO/docker/cadence-realm.json" "$KC_HOME/data/import/"
    # start-dev --import-realm skips a realm Keycloak already has, so a changed realm file (such as the users' fixed
    # subject ids the kernel's demo grants name, ADR-2543) would never arrive. Re-import it, replacing the stored realm.
    if [ -d "$KC_HOME/data/h2" ]; then
      JAVA_TOOL_OPTIONS='' "$KC_HOME/bin/kc.sh" import --db dev-file --file "$KC_HOME/data/import/cadence-realm.json" --override true \
        >"$STATE/keycloak-import.log" 2>&1 || { echo "keycloak realm import failed: $STATE/keycloak-import.log" >&2; return 1; }
    fi
    KC_BOOTSTRAP_ADMIN_USERNAME=admin KC_BOOTSTRAP_ADMIN_PASSWORD=admin JAVA_TOOL_OPTIONS='' \
      nohup "$KC_HOME/bin/kc.sh" start-dev --import-realm --http-port 8080 >"$STATE/keycloak.log" 2>&1 &
    echo $! >"$STATE/keycloak.pid"
    wait_for http://localhost:8080/realms/cadence/.well-known/openid-configuration 180
  fi
  echo "keycloak  ok  (http://localhost:8080, admin/admin)"

  # Secrets persist across restarts so execution can still decrypt the demographics it wrote.
  if [ ! -f "$STATE/secrets.env" ]; then
    {
      echo "Parameters__gateway-secret=$(openssl rand -hex 24)"
      echo "Parameters__audit-log-secret-key=$(openssl rand -hex 24)"
      echo "Parameters__demographics-encryption-key=$(openssl rand -base64 32)"
    } >"$STATE/secrets.env"
  fi
  # Added with randomisation (#5607); a state directory from before it gets the key appended once.
  grep -q '^Parameters__randomization-allocation-key=' "$STATE/secrets.env" ||
    echo "Parameters__randomization-allocation-key=$(openssl rand -base64 32)" >>"$STATE/secrets.env"
  grep -q '^Parameters__biometric-liveness-key=' "$STATE/secrets.env" ||
    echo "Parameters__biometric-liveness-key=$(openssl rand -base64 32)" >>"$STATE/secrets.env"

  if ! curl -sf http://localhost:8000/healthz/live >/dev/null; then
    tmux kill-session -t apphost 2>/dev/null || true
    tmux new-session -d -s apphost -x 250 -y 50
    tmux send-keys -t apphost "cd $REPO && env \$(cat $STATE/secrets.env) AppHost__Profile=core 'ConnectionStrings__cadence=$CONN' AppHost__OidcAuthority=http://localhost:8080/realms/cadence ASPIRE_ALLOW_UNSECURED_TRANSPORT=true dotnet run --project apps/apphost/src/Cadence.AppHost.csproj --launch-profile http 2>&1 | tee $STATE/apphost.log" Enter
  fi
  wait_for http://localhost:8018/healthz/ready 600
  wait_for http://localhost:8002/healthz/ready 300
  wait_for http://localhost:8000/healthz/live 300
  wait_for http://localhost:3000/cadence-clinical/ 300

  echo "apphost   ok  (gateway :8000, execution :8002, kernel :8018, web http://localhost:3000/cadence-clinical/)"
  grep -o 'http://localhost:15000/login?t=[0-9a-f]*' "$STATE/apphost.log" | tail -1 | sed 's/^/dashboard     /'
}

status() {
  for u in http://127.0.0.1:8080/realms/cadence http://localhost:8000/healthz/live http://localhost:8002/healthz/ready \
    http://localhost:8018/healthz/ready http://localhost:3000/cadence-clinical/; do
    printf '%-45s %s\n' "$u" "$(curl -s -o /dev/null -w '%{http_code}' "$u")"
  done
  printf '%-45s %s\n' "postgres :$PG_PORT" "$(psql -h 127.0.0.1 -p $PG_PORT -U postgres -tAc 'select 1' 2>/dev/null || echo down)"
}

down() {
  tmux send-keys -t apphost C-c 2>/dev/null || true
  timeout 60 bash -c 'while curl -s -o /dev/null http://localhost:8000/healthz/live; do sleep 1; done' || true
  tmux kill-session -t apphost 2>/dev/null || true
  # kc.sh execs java, so the pid is Keycloak's own. (pkill -f keycloak would also match the calling shell.)
  if [ -f "$STATE/keycloak.pid" ]; then
    if kill "$(cat "$STATE/keycloak.pid")" 2>/dev/null; then :; fi
    rm -f "$STATE/keycloak.pid"
  fi
  timeout 60 bash -c 'while curl -s -o /dev/null http://localhost:8080/; do sleep 1; done' || true
  # The cluster's own major's pg_ctl, so a cluster from before the move to PostgreSQL 18 still stops.
  local pg_bin=$PG_BIN
  [ -f "$STATE/pg/data/PG_VERSION" ] && pg_bin=/usr/lib/postgresql/$(cat "$STATE/pg/data/PG_VERSION")/bin
  su postgres -c "$pg_bin/pg_ctl -D $STATE/pg/data stop" >/dev/null 2>&1 || true
  echo stopped
}

"${1:-status}"
