# Moving the web app onto the .NET API

The web app (`apps/web`) was written against the Python services. Most of its API calls target endpoints that no .NET service serves, so most screens fail against the real backend. This page lists what works, what doesn't, and how a screen moves over. The plan is epic #4833.

## Current state (2026-09-29)

Each route was traced through every store, client and component it imports, and each API path it can call was matched against the specs the .NET services serve (`docs/openapi/*.json`). A "real" match means the path exists, not that the request and response shapes match.

### What the .NET services serve

| Service                                                                                                             | Real endpoints (besides `/healthz`)                                                                                                                                                                                                                                                                                                              |
| ------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Execution                                                                                                           | subjects: list by study (site/status filters, paged), create, get, consent, state, demographics; forms: create, get, update, list by subject, complete, approve, unlock; observations: create, get, list by subject, SDV, SDV flag; queries: list, create, get, respond, close, reopen, cancel; sites: activate, compliance-status; eTMF webhook |
| Designer                                                                                                            | `GET /studies` (always empty), `POST /protocols/compile` (not routed by the gateway yet)                                                                                                                                                                                                                                                         |
| CTMS, Notifications, Safety                                                                                         | one render endpoint each (letters, notification, ICSR)                                                                                                                                                                                                                                                                                           |
| Everything else (ai, econsent, eisf, etmf, fileshare, imaging, interop, knowledge, org, payments, quality, tickets) | `GET /status` only                                                                                                                                                                                                                                                                                                                               |

Missing for any EDC screen to work end to end: study/visit schedule and form definitions (Designer), and the current user's notifications (the app shell fetched them on every screen).

### Screens

Calls = distinct API paths reachable from the screen. Missing = paths no .NET service serves.

| Screen (route)                                                                                           | Calls        | Missing | Missing by area                                                                | Verdict                                                                                                                          |
| -------------------------------------------------------------------------------------------------------- | ------------ | ------- | ------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------- |
| eCRF Data Capture (`/ecrf`)                                                                              | 74           | 70      | interop 38, studies 15, execution 8, notifications 4, offline 2, terminology 2 | Needs client change (Execution forms/observations/queries/subjects exist) + missing endpoints (visit schedule, form definitions) |
| Study Rules & Queries (`/rules`)                                                                         | 19           | 15      | execution 6, studies 4, notifications 4, dictionaries 1                        | Query panel: client change; rules/edit checks: missing                                                                           |
| CTMS Operations, SSU (`/ctms`, `/ssu`)                                                                   | 50           | 45      | ctms 29, execution 11, notifications 4                                         | Site activation + compliance status: client change; rest missing                                                                 |
| Data Lock Console (`/data-lock`)                                                                         | 3            | 3       | execution 3                                                                    | Missing (lock endpoints)                                                                                                         |
| Lab Governance (`/labs`)                                                                                 | 9            | 9       | execution 9                                                                    | Missing                                                                                                                          |
| Medical Coding (`/coding`)                                                                               | 11           | 11      | execution 5, dictionaries 2, notifications 4                                   | Missing                                                                                                                          |
| Protocol & SoA Designer (`/mdr`)                                                                         | 14           | 10      | execution 6, notifications 4 (+ SoA via Designer)                              | Missing (Designer is a stub)                                                                                                     |
| Protocol AI Digitizer (`/digitization`)                                                                  | 5            | 5       | designer 1, notifications 4                                                    | Missing                                                                                                                          |
| Amendments & Migration (`/amendment-diff`)                                                               | 25           | 21      | execution 10, designer 3, studies 3, mdr 1, notifications 4                    | Missing                                                                                                                          |
| eConsent authoring, ICF builder (`/econsent-authoring`, `/icf-builder`)                                  | 33 / 19      | 29 / 19 | econsent, etmf, execution                                                      | Missing (subject consent POST exists; authoring does not)                                                                        |
| eTMF Document Vault (`/etmf`)                                                                            | 28           | 24      | etmf 11, execution 7, econsent 2, notifications 4                              | Missing                                                                                                                          |
| Part 11 Audit Ledger, Auditor (`/audit`, `/auditor`)                                                     | 22 / 7       | 22 / 7  | etmf, execution, quality                                                       | Missing (no audit read API)                                                                                                      |
| Quality & RBQM Cockpit (`/quality`)                                                                      | 7            | 7       | quality 7                                                                      | Missing                                                                                                                          |
| Safety & DSUR (`/safety`)                                                                                | 4            | 4       | safety 4                                                                       | Missing                                                                                                                          |
| Sensors, Telehealth/DTP, Lakehouse (`/sensors`, `/dct`, `/lakehouse`)                                    | 42 / 42 / 16 | all     | interop                                                                        | Missing                                                                                                                          |
| Documents Hub (`/documents`)                                                                             | 21           | 21      | fileshare 17                                                                   | Missing                                                                                                                          |
| Study Training (`/training`)                                                                             | 16           | 16      | training 12                                                                    | Missing                                                                                                                          |
| Knowledge & SOP Hub (`/knowledge`)                                                                       | 4            | 4       | notifications 4 (articles are hardcoded)                                       | Missing                                                                                                                          |
| Tickets (`/tickets`)                                                                                     | 5            | 5       | tickets 1                                                                      | Missing                                                                                                                          |
| Notifications (`/notifications`)                                                                         | 4            | 4       | notifications 4                                                                | Missing                                                                                                                          |
| Participant Wallet, Lay Summaries (`/participant-wallet`, `/lay-summaries`)                              | 11 / 3       | all     | payments                                                                       | Missing                                                                                                                          |
| DICOM viewer, Device & IVD, CEC adjudication, Export Wizard (`/imaging`, `/devices`, `/cec`, `/exports`) | 0            | 0       | none                                                                           | No API at all: hardcoded demo data                                                                                               |

Result: no screen works end to end against the .NET backend. The eCRF, query, SDV and site activation parts are the only ones with a real backend behind them.

## Moved so far

| Screen | Part                 | Client call                                                    | Notes                                                                                                                                                                                                                                                                                                                            |
| ------ | -------------------- | -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| eCRF   | Subject list (#4828) | `executionApi.listSubjects` (`GET /api/v1/execution/subjects`) | The study comes from `?studyId=` or the study field above the form. The list shows the first 200 subjects in enrollment order, by subject code; a `RECONSENT_REQUIRED` subject is gated. Submitting needs a study and a listed subject, and sends the subject's site and protocol version. Sample subjects only in a demo build. |

## Hidden screens

Screens with no .NET backend are hidden until their module serves the endpoints they need:

- They are left out of the sidebar and the command palette.
- Opening their route shows the "not available yet" page (`/unavailable`), which links to #4829.
- `/` lands each role on its usual screen if that screen is available, otherwise on the first available screen the role may open.
- The shell doesn't fetch notifications, because no .NET service serves them yet.

The list of available screens is `AVAILABLE_SCREENS` in `apps/web/src/router/availability.js`. The hidden screens' code stays in the repo as a reference for the UI, the way the Python services are the reference for the .NET port.

The missing endpoints are tracked by module, not one issue per Python path:

- #4828: what the eCRF needs from Execution, Designer and Notifications (listing subjects, served and used by the eCRF, the visit schedule and form definitions, the user's notifications).
- #4829: every hidden screen, with the module and kernel roadmap phase (`docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md` §15) that will give it a backend. #4787 decides which scaffold service becomes which module.

## Actions with no backend

Some regulated actions never called an API. They recorded the action in the browser and reported success in every build (#4827). They now start with `allowUnbackedAction(...)` (`apps/web/src/utils/unbackedActions.js`). Outside a demo build the action is refused with a "not available yet, nothing was saved" alert and no state changes; a demo build still runs it on sample data. The same change stopped the wallet, lay summary, tickets, data lock, quality and safety screens from showing sample data, or closing a dialog as if it worked, when their API call fails.

| Action                                                                  | Where                                                                                                    |
| ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Enrol a subject                                                         | `stores/clinical.ts` `enrollSubject` (eCRF, CRC form)                                                    |
| Record re-consent from the eCRF                                         | `composables/useConsentGating.js`                                                                        |
| Record SDV                                                              | `views/EcrfView.vue` `saveChange`                                                                        |
| Move a query in the rules screen                                        | `views/RulesView.vue`                                                                                    |
| Ticket status, comments, attachments, creation, signature, audit export | `views/TicketsView.vue`, `components/tickets/TicketSignModal.vue`                                        |
| Verify the safety snapshot seal, physician sign-off                     | `components/clinical/AggregateSafetyConsole.vue`                                                         |
| CEC reader assessment, chair sign-off                                   | `components/clinical/CecAdjudicationConsole.vue`                                                         |
| Consent signature, publish an ICF version                               | `stores/econsent.js`, `views/ICFBuilderView.vue`                                                         |
| Export study data                                                       | `views/ExportWizardView.vue`                                                                             |
| Commit a digitized protocol                                             | `views/ProtocolDigitizationView.vue`                                                                     |
| MedDRA/WHODrug code suggestions                                         | `components/crf/widgets/MedicalAutocodeWidget.vue` (no suggestions; the verbatim term is still captured) |
| RCA, CAPA, breach, KRI, QTL and dossier actions                         | `views/QualityCockpitView.vue`                                                                           |

When a screen moves onto the real API, replace its `allowUnbackedAction` calls with calls to the real endpoints.

## Moving a screen onto the real API

1. Make sure every endpoint the screen needs is in `docs/openapi/*.json` and routed by the gateway. If one is missing, add it to the module's issue rather than stubbing it or adding a mock fallback.
   Check that its request and response schemas are complete too. A type with a custom JSON converter (a value object or a status) comes out as an empty schema unless the service describes it; Execution does this in `apps/execution/src/Infrastructure/OpenApi/ExecutionWireTypesOpenApiTransformer.cs`, so its IDs and codes are strings and its statuses are string enums.
2. Call it through the typed client generated from those specs, and delete the screen's Python-era client functions and their types. The types come from `apps/web/scripts/generate-api-types.js`: add the service to `GENERATED_SERVICES` and run `pnpm --filter web generate:api`, which writes `apps/web/src/api/generated/<service>.ts`. Put the calls in `apps/web/src/api/<service>Api.ts` (see `executionApi.ts`). `tests/api/generated_types.test.js` fails when a checked-in file no longer matches its spec, so regenerate after `pnpm cadence sync-openapi-spec`.
3. Give the screen a view test in `apps/web/tests/` that mounts it and covers its main flow against a mocked client. `tests/available_screen_tests.test.js` fails when a route in `AVAILABLE_SCREENS` has no test file that imports its view (#4529).
4. Add the route name to `AVAILABLE_SCREENS` and update this page and #4829.
