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
| `POST` | `/api/v1/interop/esource/reconcile/batch-accept` | BatchAcceptCandidates |  |
| `POST` | `/api/v1/interop/esource/reconcile/batch-reject` | BatchRejectCandidates |  |
| `POST` | `/api/v1/interop/esource/reconcile/query` | RaiseClinicalQuery |  |
| `POST` | `/api/v1/interop/esource/reconcile/reject` | RejectCandidate |  |
| `POST` | `/api/v1/interop/esource/smart/assertion` | GenerateClientAssertion |  |
| `POST` | `/api/v1/interop/esource/smart/verify` | VerifyClientAssertion |  |
| `POST` | `/api/v1/interop/sce/execute` |  |  |
| `GET` | `/api/v1/interop/sce/lock-tokens` |  |  |
| `POST` | `/api/v1/interop/sce/lock-tokens` |  |  |
| `POST` | `/api/v1/interop/sce/lock-tokens/{token_id}/revoke` |  |  |
| `GET` | `/api/v1/interop/sce/programs` |  |  |
| `POST` | `/api/v1/interop/sce/programs` |  |  |
| `GET` | `/api/v1/interop/sce/programs/{program_id}` |  |  |
| `GET` | `/api/v1/interop/sce/runs` |  |  |
| `GET` | `/api/v1/interop/sce/runs/{run_id}` |  |  |
| `GET` | `/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}` |  |  |
| `POST` | `/api/v1/interop/sce/runs/{run_id}/verify` |  |  |
| `POST` | `/api/v1/interop/sce/validate-lockfile` |  |  |
| `GET` | `/api/v1/interop/status` | InteropStatus | Returns Interoperability operational status. |
| `GET` | `/healthz` | InteropHealthCheck | Interoperability FHIR & CDISC connector health probe. |
