# execution API

Generated from `docs/openapi/execution_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/execution/cec/dossiers/{id}` | GetCecDossierById |  |
| `POST` | `/api/v1/execution/cec/dossiers/{id}/chair-adjudicate` | ChairAdjudicateDossier |  |
| `POST` | `/api/v1/execution/forms` | CreateFormSubmission |  |
| `GET` | `/api/v1/execution/forms/subject/{studyId}/{subjectId}` | ListFormSubmissionsBySubject |  |
| `GET` | `/api/v1/execution/forms/{id}` | GetFormSubmissionById |  |
| `PUT` | `/api/v1/execution/forms/{id}` | UpdateFormPayload |  |
| `POST` | `/api/v1/execution/forms/{id}/approve` | ApproveFormSubmission |  |
| `POST` | `/api/v1/execution/forms/{id}/complete` | CompleteFormSubmission |  |
| `POST` | `/api/v1/execution/forms/{id}/unlock` | UnlockFormSubmission |  |
| `GET` | `/api/v1/execution/locks` | ListLocks |  |
| `POST` | `/api/v1/execution/locks/form/{form_id}/freeze` |  |  |
| `POST` | `/api/v1/execution/locks/form/{form_id}/lock` |  |  |
| `POST` | `/api/v1/execution/locks/form/{form_id}/unfreeze` |  |  |
| `POST` | `/api/v1/execution/locks/form/{form_id}/unlock` |  |  |
| `POST` | `/api/v1/execution/locks/lock` | LockData |  |
| `POST` | `/api/v1/execution/locks/pre-flight` | PreflightLockCheck |  |
| `POST` | `/api/v1/execution/locks/site/{site_id}/freeze` |  |  |
| `POST` | `/api/v1/execution/locks/site/{site_id}/lock` |  |  |
| `POST` | `/api/v1/execution/locks/site/{site_id}/unfreeze` |  |  |
| `POST` | `/api/v1/execution/locks/site/{site_id}/unlock` |  |  |
| `POST` | `/api/v1/execution/locks/subject/{subject_id}/freeze` |  |  |
| `POST` | `/api/v1/execution/locks/subject/{subject_id}/lock` |  |  |
| `POST` | `/api/v1/execution/locks/subject/{subject_id}/unfreeze` |  |  |
| `POST` | `/api/v1/execution/locks/subject/{subject_id}/unlock` |  |  |
| `POST` | `/api/v1/execution/locks/trial/freeze` |  |  |
| `POST` | `/api/v1/execution/locks/trial/lock` |  |  |
| `POST` | `/api/v1/execution/locks/trial/unfreeze` |  |  |
| `POST` | `/api/v1/execution/locks/trial/unlock` |  |  |
| `POST` | `/api/v1/execution/locks/unlock` | UnlockData |  |
| `POST` | `/api/v1/execution/locks/visit/{visit_id}/freeze` |  |  |
| `POST` | `/api/v1/execution/locks/visit/{visit_id}/lock` |  |  |
| `POST` | `/api/v1/execution/locks/visit/{visit_id}/unfreeze` |  |  |
| `POST` | `/api/v1/execution/locks/visit/{visit_id}/unlock` |  |  |
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
| `GET` | `/api/v1/execution/subjects` | ListSubjects | Lists one page of a study's subjects in enrollment order. |
| `POST` | `/api/v1/execution/subjects` | CreateSubject |  |
| `GET` | `/api/v1/execution/subjects/{subjectId}` | GetSubjectDetail |  |
| `POST` | `/api/v1/execution/subjects/{subjectId}/consent` | RecordSubjectConsent |  |
| `PUT` | `/api/v1/execution/subjects/{subjectId}/demographics` | UpdateSubjectDemographics |  |
| `DELETE` | `/api/v1/execution/subjects/{subjectId}/demographics` | DeleteSubjectDemographics |  |
| `PATCH` | `/api/v1/execution/subjects/{subjectId}/state` | UpdateSubjectState |  |
| `POST` | `/api/v1/execution/webhooks/etmf` | EtmfCompletenessWebhook |  |
| `GET` | `/healthz` | ExecutionHealthCheck | Execution engine health probe. |
