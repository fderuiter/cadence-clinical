# execution API

Generated from `docs/openapi/execution_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/execution/batch-sign-off` | BatchSignOff |  |
| `GET` | `/api/v1/execution/cec/dossiers/{id}` | GetCecDossierById |  |
| `POST` | `/api/v1/execution/cec/dossiers/{id}/chair-adjudicate` | ChairAdjudicateDossier |  |
| `GET` | `/api/v1/execution/coding/synonyms` | GetActiveSynonymRules | Retrieve active synonym rules for study auto-coding engine. |
| `POST` | `/api/v1/execution/coding/synonyms/import` | ImportSynonymFile | Ingest validated synonym dictionary rules into persistent store with GxP audit lineage. |
| `GET` | `/api/v1/execution/coding/synonyms/jobs` | GetSynonymImportJobs | Retrieve GxP audit ledger of bulk synonym import jobs. |
| `POST` | `/api/v1/execution/coding/synonyms/validate` | ValidateSynonymFile | Pre-flight validation report for bulk synonym dictionary CSV/XLSX file. |
| `DELETE` | `/api/v1/execution/coding/synonyms/{id}` | DeactivateSynonymRule | Deactivate an active synonym rule. |
| `GET` | `/api/v1/execution/devices/deficiencies` | ListDeviceDeficiencies | List Device Deficiencies |
| `POST` | `/api/v1/execution/devices/deficiencies` | LogDeviceDeficiency | Log Device Deficiency |
| `GET` | `/api/v1/execution/devices/deficiencies/{deficiency_id}` | GetDeviceDeficiencyById | Get Device Deficiency |
| `POST` | `/api/v1/execution/devices/dispense` | DispenseDevice | Dispense Device |
| `GET` | `/api/v1/execution/devices/inventory` | ListDeviceInventory | List Device Inventory |
| `POST` | `/api/v1/execution/devices/inventory` | RegisterDeviceInventory | Register Device Inventory |
| `GET` | `/api/v1/execution/devices/investigation-summary` | GetDeviceInvestigationSummary | Get Investigation Summary |
| `POST` | `/api/v1/execution/devices/parse-udi` | ParseUdi | Parse UDI Barcode |
| `GET` | `/api/v1/execution/devices/quarantine` | ListDeviceQuarantines | List Device Quarantines |
| `POST` | `/api/v1/execution/devices/quarantine` | InitiateDeviceQuarantine | Initiate Quarantine |
| `POST` | `/api/v1/execution/devices/quarantine/release` | ReleaseDeviceQuarantine | Release Quarantine |
| `POST` | `/api/v1/execution/forms` | CreateFormSubmission |  |
| `GET` | `/api/v1/execution/forms/subject/{studyId}/{subjectId}` | ListFormSubmissionsBySubject |  |
| `GET` | `/api/v1/execution/forms/{id}` | GetFormSubmissionById |  |
| `PUT` | `/api/v1/execution/forms/{id}` | UpdateFormPayload |  |
| `POST` | `/api/v1/execution/forms/{id}/approve` | ApproveFormSubmission |  |
| `POST` | `/api/v1/execution/forms/{id}/complete` | CompleteFormSubmission |  |
| `POST` | `/api/v1/execution/forms/{id}/unlock` | UnlockFormSubmission |  |
| `POST` | `/api/v1/execution/labs/toxicity/evaluate` | EvaluateLabToxicityEndpoint | Evaluate Lab Toxicity Endpoint |
| `GET` | `/api/v1/execution/labs/toxicity/review-queue` | ListStagedAeReviewQueue | List Staged Ae Review Queue |
| `POST` | `/api/v1/execution/labs/toxicity/review/{review_id}/action` | ActionStagedAeReview | Action Staged Ae Review |
| `POST` | `/api/v1/execution/observations` | CreateClinicalObservation |  |
| `GET` | `/api/v1/execution/observations/subject/{studyId}/{subjectId}` | ListObservationsBySubject |  |
| `GET` | `/api/v1/execution/observations/{id}` | GetObservationById |  |
| `POST` | `/api/v1/execution/observations/{id}/sdv` | VerifyObservationSdv |  |
| `POST` | `/api/v1/execution/observations/{id}/sdv/flag` | FlagObservationSdv |  |
| `GET` | `/api/v1/execution/queries` | ListClinicalQueries |  |
| `POST` | `/api/v1/execution/queries` | CreateClinicalQuery |  |
| `POST` | `/api/v1/execution/queries/batch-cancel` | BatchCancelClinicalQueries |  |
| `POST` | `/api/v1/execution/queries/batch-close` | BatchCloseClinicalQueries |  |
| `POST` | `/api/v1/execution/queries/batch-respond` | BatchRespondClinicalQueries |  |
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
