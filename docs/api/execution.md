# execution API

Generated from `docs/openapi/execution_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/execution/forms` | CreateFormSubmission |  |
| `GET` | `/api/v1/execution/forms/subject/{studyId}/{subjectId}` | ListFormSubmissionsBySubject |  |
| `GET` | `/api/v1/execution/forms/{id}` | GetFormSubmissionById |  |
| `PUT` | `/api/v1/execution/forms/{id}` | UpdateFormPayload |  |
| `POST` | `/api/v1/execution/forms/{id}/approve` | ApproveFormSubmission |  |
| `POST` | `/api/v1/execution/forms/{id}/complete` | CompleteFormSubmission |  |
| `POST` | `/api/v1/execution/forms/{id}/unlock` | UnlockFormSubmission |  |
| `POST` | `/api/v1/execution/observations` | CreateClinicalObservation |  |
| `GET` | `/api/v1/execution/observations/subject/{studyId}/{subjectId}` | ListObservationsBySubject |  |
| `GET` | `/api/v1/execution/observations/{id}` | GetObservationById |  |
| `POST` | `/api/v1/execution/observations/{id}/sdv` | VerifyObservationSdv |  |
| `POST` | `/api/v1/execution/observations/{id}/sdv/flag` | FlagObservationSdv |  |
| `GET` | `/api/v1/execution/queries` | ListClinicalQueries |  |
| `POST` | `/api/v1/execution/queries` | CreateClinicalQuery |  |
| `GET` | `/api/v1/execution/queries/{id}` | GetClinicalQueryById |  |
| `POST` | `/api/v1/execution/queries/{id}/cancel` | CancelClinicalQuery |  |
| `POST` | `/api/v1/execution/queries/{id}/close` | CloseClinicalQuery |  |
| `POST` | `/api/v1/execution/queries/{id}/reopen` | ReopenClinicalQuery |  |
| `POST` | `/api/v1/execution/queries/{id}/respond` | RespondClinicalQuery |  |
| `POST` | `/api/v1/execution/sites/{siteId}/activate` | ActivateSite |  |
| `GET` | `/api/v1/execution/sites/{siteId}/compliance-status` | GetSiteComplianceStatus |  |
| `POST` | `/api/v1/execution/subjects` | CreateSubject |  |
| `GET` | `/api/v1/execution/subjects/{subjectId}` | GetSubjectDetail |  |
| `POST` | `/api/v1/execution/subjects/{subjectId}/consent` | RecordSubjectConsent |  |
| `PUT` | `/api/v1/execution/subjects/{subjectId}/demographics` | UpdateSubjectDemographics |  |
| `DELETE` | `/api/v1/execution/subjects/{subjectId}/demographics` | DeleteSubjectDemographics |  |
| `PATCH` | `/api/v1/execution/subjects/{subjectId}/state` | UpdateSubjectState |  |
| `POST` | `/api/v1/execution/webhooks/etmf` | EtmfCompletenessWebhook |  |
| `GET` | `/healthz` | ExecutionHealthCheck | Execution engine health probe. |
