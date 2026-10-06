# execution API

Generated from `docs/openapi/execution_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/execution/cec/dossiers/{id}` | GetCecDossierById |  |
| `POST` | `/api/v1/execution/cec/dossiers/{id}/chair-adjudicate` | ChairAdjudicateDossier |  |
| `GET` | `/api/v1/execution/coding/synonyms` | GetActiveSynonymRules | Retrieve active synonym rules for study auto-coding engine. |
| `POST` | `/api/v1/execution/coding/synonyms/import` | ImportSynonymFile | Ingest validated synonym dictionary rules into persistent store with GxP audit lineage. |
| `GET` | `/api/v1/execution/coding/synonyms/jobs` | GetSynonymImportJobs | Retrieve GxP audit ledger of bulk synonym import jobs. |
| `POST` | `/api/v1/execution/coding/synonyms/validate` | ValidateSynonymFile | Pre-flight validation report for bulk synonym dictionary CSV/XLSX file. |
| `DELETE` | `/api/v1/execution/coding/synonyms/{id}` | DeactivateSynonymRule | Deactivate an active synonym rule. |
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
| `POST` | `/api/v1/execution/rtsm/cgt/apheresis` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/bedside-verify` |  |  |
| `GET` | `/api/v1/execution/rtsm/cgt/certificates/{studyId}` |  |  |
| `GET` | `/api/v1/execution/rtsm/cgt/chain-of-custody/{studyId}/{subjectId}` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/destruction-certificate` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/infuse` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/manufacturing-bind` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/milestone` |  |  |
| `GET` | `/api/v1/execution/rtsm/cgt/reconciliations/{studyId}` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/returns` |  |  |
| `POST` | `/api/v1/execution/rtsm/cgt/verify-barcode` |  |  |
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
