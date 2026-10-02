# safety API

Generated from `docs/openapi/safety_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path                                           | Operation             | Summary                                                                                                    |
| ------ | ---------------------------------------------- | --------------------- | ---------------------------------------------------------------------------------------------------------- |
| `GET`  | `/api/v1/safety/dsur`                          | ListDsurReports       | Lists all DSUR periodic safety report records.                                                             |
| `POST` | `/api/v1/safety/dsur/compile`                  | CompileDsurReport     | Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation. |
| `GET`  | `/api/v1/safety/dsur/{id}`                     | GetDsurReportById     | Retrieves a DSUR periodic safety report record by identifier.                                              |
| `PUT`  | `/api/v1/safety/dsur/{id}`                     | UpdateDsurReport      | Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED.                        |
| `POST` | `/api/v1/safety/dsur/{id}/approve`             | ApproveDsurReport     | Applies a 21 CFR Part 11 PKCS#7 e-signature block via GxPSigner and locks the DSUR record.                 |
| `POST` | `/api/v1/safety/icsr/render`                   | RenderIcsrReport      | Renders an ICSR E2B(R3) safety report as Liquid XML.                                                       |
| `GET`  | `/api/v1/safety/periodic-reports`              | ListPeriodicReports   |                                                                                                            |
| `POST` | `/api/v1/safety/periodic-reports/compile`      | CompilePeriodicReport |                                                                                                            |
| `GET`  | `/api/v1/safety/periodic-reports/{id}`         | GetPeriodicReportById |                                                                                                            |
| `PUT`  | `/api/v1/safety/periodic-reports/{id}`         | UpdatePeriodicReport  |                                                                                                            |
| `POST` | `/api/v1/safety/periodic-reports/{id}/approve` | ApprovePeriodicReport |                                                                                                            |
| `POST` | `/api/v1/safety/periodic-reports/{id}/sign`    | SignPeriodicReport    |                                                                                                            |
| `GET`  | `/api/v1/safety/status`                        | SafetyStatus          | Returns Safety operational status.                                                                         |
| `GET`  | `/healthz`                                     | SafetyHealthCheck     | Clinical Safety & Pharmacovigilance health probe.                                                          |
