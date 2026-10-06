# safety API

Generated from `docs/openapi/safety_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/safety/dsur` | ListDsurReports | Lists all DSUR periodic safety report records. |
| `POST` | `/api/v1/safety/dsur/compile` | CompileDsurReport | Compiles an aggregate DSUR periodic safety report with patient-years exposure and MedDRA cross-tabulation. |
| `GET` | `/api/v1/safety/dsur/snapshots/{snapshotId}` | GetDsurSnapshotById | Retrieves an immutable point-in-time DSUR snapshot record by snapshot ID. |
| `POST` | `/api/v1/safety/dsur/snapshots/{snapshotId}/verify` | VerifyDsurSnapshot | Cryptographically verifies SHA-256 payload digest, Part 11 e-signature, and GxP Merkle audit chain. |
| `GET` | `/api/v1/safety/dsur/{id}` | GetDsurReportById | Retrieves a DSUR periodic safety report record by identifier. |
| `PUT` | `/api/v1/safety/dsur/{id}` | UpdateDsurReport | Updates a draft DSUR periodic safety report record. Returns 409 Conflict if LOCKED. |
| `POST` | `/api/v1/safety/dsur/{id}/approve` | ApproveDsurReport | Applies a 21 CFR Part 11 PKCS#7 e-signature block via GxPSigner and locks the DSUR record. |
| `GET` | `/api/v1/safety/dsur/{id}/snapshots` | GetDsurReportSnapshots | Retrieves all versioned point-in-time snapshot summaries for a given DSUR report. |
| `POST` | `/api/v1/safety/icsr/render` | RenderIcsrReport | Renders an ICSR E2B(R3) safety report as Liquid XML. |
| `GET` | `/api/v1/safety/periodic-reports` | ListPeriodicReports |  |
| `POST` | `/api/v1/safety/periodic-reports/compile` | CompilePeriodicReport |  |
| `GET` | `/api/v1/safety/periodic-reports/snapshots/{snapshotId}` | GetPeriodicReportSnapshotById |  |
| `POST` | `/api/v1/safety/periodic-reports/snapshots/{snapshotId}/verify` | VerifyPeriodicReportSnapshot |  |
| `GET` | `/api/v1/safety/periodic-reports/{id}` | GetPeriodicReportById |  |
| `PUT` | `/api/v1/safety/periodic-reports/{id}` | UpdatePeriodicReport |  |
| `POST` | `/api/v1/safety/periodic-reports/{id}/approve` | ApprovePeriodicReport |  |
| `POST` | `/api/v1/safety/periodic-reports/{id}/sign` | SignPeriodicReport |  |
| `GET` | `/api/v1/safety/periodic-reports/{id}/snapshots` | GetPeriodicReportSnapshots |  |
| `GET` | `/api/v1/safety/snapshots/{snapshotId}` | GetSnapshotById |  |
| `GET` | `/api/v1/safety/snapshots/{snapshotId}/verify` | GetSnapshotVerification |  |
| `POST` | `/api/v1/safety/snapshots/{snapshotId}/verify` | VerifySnapshot |  |
| `GET` | `/api/v1/safety/status` | SafetyStatus | Returns Safety operational status. |
| `GET` | `/healthz` | SafetyHealthCheck | Clinical Safety & Pharmacovigilance health probe. |
