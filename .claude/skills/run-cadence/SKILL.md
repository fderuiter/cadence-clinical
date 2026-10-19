---
name: run-cadence
description: Start, run and drive the local Cadence stack (Keycloak, PostgreSQL, gateway, kernel, execution and the Clinical Canvas web app) without Docker. Use when asked to run or launch Cadence, sign in to the web app, click through the Canvas, take a screenshot, call the API as a signed-in user, or check that a change works in the running app rather than only in tests.
---

# Run Cadence

`scripts/local_stack_nodocker.sh up` starts the core stack with no container runtime; `.claude/skills/run-cadence/driver.mjs`
is a headless Chromium driver (Playwright) that signs in through Keycloak, clicks through the Clinical Canvas, takes
screenshots and calls the gateway as the signed-in user. Paths are relative to the repository root. Everything here
was run in a Claude Code cloud container (Ubuntu 24.04, root, no Docker).

## Prerequisites

Already present in the cloud container: .NET 10 SDK, Node 22 + pnpm, OpenJDK 21 (Keycloak), PostgreSQL 16 binaries
(`/usr/lib/postgresql/16/bin`), tmux and Chromium at `/opt/pw-browsers/chromium`. Nothing to `apt-get`. Then, once:

```bash
pnpm install
dotnet build apps/apphost/src/Cadence.AppHost.csproj -v q -nologo
```

The build takes about 2 minutes cold; `scripts/local_stack_nodocker.sh up` also builds through `dotnet run` if you skip it.

## Start the stack

```bash
scripts/local_stack_nodocker.sh up      # about 80 s once built and Keycloak is downloaded
scripts/local_stack_nodocker.sh status  # every line 200 / 1 when healthy
scripts/local_stack_nodocker.sh down
```

`up` is idempotent and keeps its state in `/opt/cadence-dev` (`CADENCE_DEV_STATE`):

| Piece      | Where                                                                                                        |
| ---------- | ------------------------------------------------------------------------------------------------------------ |
| PostgreSQL | own cluster on `127.0.0.1:5433`, trust auth, database `cadence` (the machine's 5432 cluster untouched)       |
| Keycloak   | 26.8.1 release tarball (as compose), `http://localhost:8080`, realm `docker/cadence-realm.json`, admin/admin |
| AppHost    | `core` profile in tmux session `apphost`, log `/opt/cadence-dev/apphost.log`                                 |
| Services   | gateway `:8000`, execution `:8002`, kernel `:8018`, web `http://localhost:3000/cadence-clinical/`            |
| Dashboard  | `http://localhost:15000/login?t=…` (printed by `up`)                                                         |

Realm users (password `password`): `crc.site101` (site investigator, can add participants), `dm.lead`,
`cra.monitor`, `designer.lead`, `safety.officer`, `auditor.lead`. All carry `tenant_id=cadence-local`.

## Drive it (agent path)

The driver reads one command per line from stdin. Pipe a script:

```bash
node .claude/skills/run-cadence/driver.mjs <<'EOF'
login crc.site101
nav studies/DEMO-001
idle
click nav >> text=Participants
click #btn-add-participant
fill #add-participant-code P-0001
fill #add-participant-site SITE-101
fill #add-participant-reason Screening visit 1 for run-cadence smoke test
click #btn-confirm-add-participant
wait text=P-0001 added in screening
ss participant-added
fetch GET /api/v1/execution/subjects?study_id=DEMO-001
console
quit
EOF
```

Screenshots land in `/tmp/cadence-shots/NN-<name>.png` (`CADENCE_SHOTS`); open them with Read and look. Each run
is a new browser, so start every script with `login`. For step-by-step work keep one browser open under tmux:

```bash
tmux new-session -d -s drv -x 200 -y 50
tmux send-keys -t drv 'node .claude/skills/run-cadence/driver.mjs' Enter
timeout 30 bash -c 'until tmux capture-pane -t drv -p | grep -q "driver ready"; do sleep 0.5; done'
tmux send-keys -t drv 'login dm.lead' Enter
tmux send-keys -t drv 'nav studies/DEMO-001?stage=conduct&section=participants' Enter 'wait text=P-0001' Enter 'ss dm-participants' Enter
tmux capture-pane -t drv -p
tmux send-keys -t drv 'quit' Enter
```

| Command                               | Does                                                                                          |
| ------------------------------------- | --------------------------------------------------------------------------------------------- |
| `login <user> [password]`             | Keycloak sign-in from the web app's sign-in page; prints where it landed                      |
| `nav <path or url>`                   | Path is relative to `/cadence-clinical/`, e.g. `studies/DEMO-001`                             |
| `click <selector>`                    | Playwright selector: `#id`, `text=…`, `nav >> text=Participants`                              |
| `fill <selector> <text>`              | Fill an input or textarea                                                                     |
| `press <key>` / `wait <sel>` / `idle` | Key press / wait 20 s for a visible element / wait for network idle                           |
| `ss [name]`                           | Full-page screenshot, prints the file path                                                    |
| `text [selector]`                     | Visible text (default `main`), first 2000 characters                                          |
| `eval <js>` / `url`                   | Evaluate in the page / current URL                                                            |
| `fetch <METHOD> <path> [json]`        | Call the gateway as the signed-in user; mutations get `X-Change-Reason` and `Idempotency-Key` |
| `api`                                 | Gateway calls the page made since the last `api` (status, method, URL)                        |
| `console`                             | Console errors since the last `console`                                                       |
| `quit`                                | Close the browser                                                                             |

Web changes hot-reload through Vite. C# changes need `scripts/local_stack_nodocker.sh down` then `scripts/local_stack_nodocker.sh up` (the AppHost runs the
built projects).

## Run (human path)

`apps/apphost/README.md` has the documented `dotnet run` lines and the full profile, which needs Docker for its
PostgreSQL, Keycloak and MinIO containers. Headless, it gives you nothing to look at; use the driver.

## Test

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~AppHostModelTests"
pnpm --filter web exec vitest run tests/available_screen_tests.test.js
```

The first takes about a minute (mostly the build), the second about 4 s. The full gates are in AGENTS.md
(`pnpm cadence check`, `pnpm cadence ci`); they are not part of this skill.

## Gotchas

- **The gateway has no CORS, and the AppHost points the web app at `http://localhost:8000`.** Every API call from
  `:3000` is blocked in a real browser ("No 'Access-Control-Allow-Origin' header"), so the Canvas shows "Failed to
  fetch" everywhere. The driver answers gateway calls itself (fetches them from Node, adds CORS headers);
  `CADENCE_CORS_SHIM=0` turns that off to see the bug. A human browser on the AppHost stack hits it too. Tracked in #5429.
- **Kernel reads find nothing.** The kernel host provisions the `cadence-local` tenant on start (ADR-2542), but the
  realm's users hold no kernel grants (#5452): `search` answers 200 with no hits and `activity` answers 403 "You hold no
  grant in effect". A 403 "not an active kernel tenant" means the kernel was not started in Development.
- **`ui` must be built.** `apps/web/vite.config.js` aliases `ui` to `packages/ui/dist/index.js`, which `pnpm install`
  does not create; Vite shows "Failed to resolve import "ui" from "src/main.js"". `scripts/local_stack_nodocker.sh up` runs
  `pnpm --filter ui build`. If Vite already started without it, restart the AppHost: Vite keeps the failed
  resolution.
- **Only gateway, execution and kernel run** (`core` profile). Knowledge calls answer 502 or 503 and the first one 401;
  those console errors are expected noise. Every other service's route answers as down.
- **Data persists** across `down`/`up` in the 5433 cluster: a participant added in an earlier run is still there, so
  use a new participant code each run.
- **Who can do what.** `crc.site101` gets "Add participant"; `dm.lead` does not. Signing in lands on the persona's
  default screen, which for most personas is a "isn't available yet" page; navigate with `nav`.
- **`pkill -f keycloak` kills your own shell** when the pattern is in the command line you are running. `scripts/local_stack_nodocker.sh
down` stops Keycloak by its pid file.
- **Keycloak under the proxy.** The container exports `JAVA_TOOL_OPTIONS` with proxy settings; `scripts/local_stack_nodocker.sh` clears it
  for Keycloak, which only talks to localhost.

## Troubleshooting

- **`scripts/local_stack_nodocker.sh up` prints `timed out waiting for …`**: read `/opt/cadence-dev/apphost.log` or
  `tmux capture-pane -t apphost -p`.
- **`login` fails with `Timeout … exceeded`**: the sign-in page never rendered; `ss` and look. A Vite error overlay
  means the `ui` gotcha above.
- **Start over from nothing**: `scripts/local_stack_nodocker.sh down && rm -rf /opt/cadence-dev`, then `scripts/local_stack_nodocker.sh up` (it downloads Keycloak
  again, about 200 MB).
