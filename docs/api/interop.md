# interop API

Generated from `docs/openapi/interop_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/interop/esource/candidates` | ListCandidates |  |
| `GET` | `/api/v1/interop/esource/candidates/{candidate_id}` | GetCandidate |  |
| `GET` | `/api/v1/interop/esource/endpoints` | ListEhrEndpoints |  |
| `POST` | `/api/v1/interop/esource/endpoints` | RegisterEhrEndpoint |  |
| `POST` | `/api/v1/interop/esource/ingest` | IngestFhirBundle |  |
| `POST` | `/api/v1/interop/esource/poll` | PollHospitalEhr |  |
| `GET` | `/api/v1/interop/esource/provenance/{candidate_id}` | GetProvenanceAudit |  |
| `POST` | `/api/v1/interop/esource/reconcile/accept` | AcceptCandidate |  |
| `POST` | `/api/v1/interop/esource/reconcile/query` | RaiseClinicalQuery |  |
| `POST` | `/api/v1/interop/esource/reconcile/reject` | RejectCandidate |  |
| `POST` | `/api/v1/interop/esource/smart/assertion` | GenerateClientAssertion |  |
| `POST` | `/api/v1/interop/esource/smart/verify` | VerifyClientAssertion |  |
| `POST` | `/api/v1/interop/sensors/ingest` | IngestSensorsStream |  |
| `GET` | `/api/v1/interop/sensors/summary` | ListSensorsSummary |  |
| `GET` | `/api/v1/interop/status` | InteropStatus | Returns Interoperability operational status. |
| `GET` | `/api/v1/interop/telemetry/alerts` | ListSafetyAlerts |  |
| `POST` | `/api/v1/interop/telemetry/stream` | IngestTelemetryStream |  |
| `GET` | `/api/v1/interop/telemetry/summary` | ListTelemetrySummaries |  |
| `GET` | `/healthz` | InteropHealthCheck | Interoperability FHIR & CDISC connector health probe. |
