# safety API

Generated from `docs/openapi/safety_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/safety/dsur` | ListDsurReports | Lists all DSUR periodic safety report records. |
| `POST` | `/api/v1/safety/dsur/compile` | CompileDsurReport | Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation. |
| `GET` | `/api/v1/safety/dsur/{id}` | GetDsurReportById | Retrieves a DSUR periodic safety report record by identifier. |
| `PUT` | `/api/v1/safety/dsur/{id}` | UpdateDsurReport | Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED. |
| `POST` | `/api/v1/safety/dsur/{id}/approve` | ApproveDsurReport | Signs and locks a DSUR as the verified caller: needs safety_report:approve, a step-up signature token bound to the report and a reason for change; the compiler cannot approve. |
| `POST` | `/api/v1/safety/icsr/render` | RenderIcsrReport | Renders an ICH E2B(R3) ICSR as an HL7 v3 MCCI_IN200100UV01 message validated against the ICH schema; a report that breaks an E2B(R3) conformance rule is refused with 400 naming each member. |
| `GET` | `/api/v1/safety/periodic-reports` | ListPeriodicReports |  |
| `POST` | `/api/v1/safety/periodic-reports/compile` | CompilePeriodicReport |  |
| `GET` | `/api/v1/safety/periodic-reports/{id}` | GetPeriodicReportById |  |
| `PUT` | `/api/v1/safety/periodic-reports/{id}` | UpdatePeriodicReport |  |
| `POST` | `/api/v1/safety/periodic-reports/{id}/approve` | ApprovePeriodicReport |  |
| `GET` | `/api/v1/safety/status` | SafetyStatus | Returns Safety operational status. |
| `GET` | `/healthz` | SafetyHealthCheck | Clinical Safety & Pharmacovigilance health probe. |
