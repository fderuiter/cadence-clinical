#!/usr/bin/env bash
# Start, check and stop the local Cadence core stack without Docker:
# a throwaway PostgreSQL 16 cluster (port 5433, trust auth), Keycloak from the release tarball (port 8080,
# docker/cadence-realm.json imported) and the Aspire AppHost core profile (gateway 8000, execution 8002,
# kernel 8018, web 3000, dashboard 15000) in tmux session "apphost".
# Usage, from the repository root: scripts/local_stack_nodocker.sh up|status|down
# Agents: .claude/skills/run-cadence/SKILL.md drives the running stack with a headless browser.
set -euo pipefail

STATE=${CADENCE_DEV_STATE:-/opt/cadence-dev}
KC_VERSION=26.8.1 # same as docker/docker-compose.yml
KC_HOME=$STATE/keycloak-$KC_VERSION
PG_BIN=/usr/lib/postgresql/16/bin
PG_PORT=5433
CONN="Host=127.0.0.1;Port=$PG_PORT;Username=postgres;Database=cadence"
REPO=$(git rev-parse --show-toplevel)

wait_for() { # url seconds
  timeout "$2" bash -c "until curl -sf '$1' >/dev/null; do sleep 2; done" || { echo "timed out waiting for $1" >&2; return 1; }
}

up() {
  mkdir -p "$STATE"

  # PostgreSQL: own cluster so the machine's main cluster and its passwords stay untouched.
  if [ ! -d "$STATE/pg/data" ]; then
    mkdir -p "$STATE/pg" && chown postgres "$STATE/pg"
    su postgres -c "$PG_BIN/initdb -D $STATE/pg/data --auth=trust -U postgres" >/dev/null
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
    KC_BOOTSTRAP_ADMIN_USERNAME=admin KC_BOOTSTRAP_ADMIN_PASSWORD=admin JAVA_TOOL_OPTIONS= \
      nohup "$KC_HOME/bin/kc.sh" start-dev --import-realm --http-port 8080 >"$STATE/keycloak.log" 2>&1 &
    echo $! >"$STATE/keycloak.pid"
    wait_for http://localhost:8080/realms/cadence/.well-known/openid-configuration 180
  fi
  echo "keycloak  ok  (http://localhost:8080, admin/admin)"

  # The web app's vite.config aliases "ui" to packages/ui/dist, which pnpm install does not build.
  [ -f "$REPO/packages/ui/dist/index.js" ] || (cd "$REPO" && pnpm --filter ui build >/dev/null)

  # Secrets persist across restarts so execution can still decrypt the demographics it wrote.
  if [ ! -f "$STATE/secrets.env" ]; then
    {
      echo "Parameters__gateway-secret=$(openssl rand -hex 24)"
      echo "Parameters__audit-log-secret-key=$(openssl rand -hex 24)"
      echo "Parameters__demographics-encryption-key=$(openssl rand -base64 32)"
    } >"$STATE/secrets.env"
  fi

  if ! curl -sf http://localhost:8000/healthz/live >/dev/null; then
    tmux kill-session -t apphost 2>/dev/null || true
    tmux new-session -d -s apphost -x 250 -y 50
    tmux send-keys -t apphost "cd $REPO && env \$(cat $STATE/secrets.env) AppHost__Profile=core 'ConnectionStrings__cadence=$CONN' AppHost__OidcAuthority=http://localhost:8080/realms/cadence ASPIRE_ALLOW_UNSECURED_TRANSPORT=true dotnet run --project apps/apphost/src/Cadence.AppHost.csproj --launch-profile http 2>&1 | tee $STATE/apphost.log" Enter
  fi
  wait_for http://localhost:8018/healthz/ready 600
  wait_for http://localhost:8002/healthz/ready 300
  wait_for http://localhost:8000/healthz/live 300
  wait_for http://localhost:3000/cadence-clinical/ 300

  # The realm's users carry tenant_id=cadence-local, but nothing provisions that kernel tenant, so every kernel read
  # answers 403 "not an active kernel tenant". Local-only shortcut: insert it (no audit entry).
  psql -h 127.0.0.1 -p $PG_PORT -U postgres cadence -qc "insert into kernel.tenants (id, code, name, is_active, created_at, created_by, reason_for_change, version_index) values (gen_random_uuid(), 'cadence-local', 'Local development', true, now(), 'run-cadence skill', 'Local dev tenant for the cadence-local realm users', 1) on conflict (code) do nothing"
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
  [ -f "$STATE/keycloak.pid" ] && kill "$(cat "$STATE/keycloak.pid")" 2>/dev/null && rm "$STATE/keycloak.pid" || true
  timeout 60 bash -c 'while curl -s -o /dev/null http://localhost:8080/; do sleep 1; done' || true
  su postgres -c "$PG_BIN/pg_ctl -D $STATE/pg/data stop" >/dev/null 2>&1 || true
  echo stopped
}

"${1:-status}"
