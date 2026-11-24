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
| `GET` | `/api/v1/execution/lab-ranges` | GetReferenceRanges | Get reference ranges for study/site |
| `POST` | `/api/v1/execution/lab-ranges` | CreateReferenceRange | Create laboratory reference range |
| `GET` | `/api/v1/execution/lab-ranges/{id}` | GetReferenceRangeById | Get reference range by ID |
| `PUT` | `/api/v1/execution/lab-ranges/{id}` | UpdateReferenceRange | Update laboratory reference range |
| `DELETE` | `/api/v1/execution/lab-ranges/{id}` | DeleteReferenceRange | Deactivate laboratory reference range |
| `GET` | `/api/v1/execution/labs/batch-status` | GetLabBatchStatus | Retrieve batch status history for bulk lab range imports |
| `GET` | `/api/v1/execution/labs/batch-status/{id}` | GetLabBatchStatusById | Get lab batch import job status by ID |
| `POST` | `/api/v1/execution/labs/convert-unit` | ConvertLabUnit | Convert clinical laboratory unit via UCUM SI formulas |
| `POST` | `/api/v1/execution/labs/import/commit` | CommitLabRangeImport | Commit validated bulk lab ranges into persistent store with Part 11 audit logging |
| `POST` | `/api/v1/execution/labs/import/dry-run` | DryRunLabRangeImport | Pre-flight validation preview for bulk lab range CSV/XLSX file |
| `GET` | `/api/v1/execution/labs/loinc-catalog` | GetLoincCatalog | Retrieve standardized LOINC catalog mappings |
| `POST` | `/api/v1/execution/labs/toxicity/evaluate` | EvaluateLabToxicityEndpoint | Evaluate Lab Toxicity Endpoint |
| `GET` | `/api/v1/execution/labs/toxicity/review-queue` | ListStagedAeReviewQueue | List Staged Ae Review Queue |
| `POST` | `/api/v1/execution/labs/toxicity/review/{review_id}/action` | ActionStagedAeReview | Action Staged Ae Review |
| `GET` | `/api/v1/execution/randomization/schemes` | ListRandomizationSchemes | Lists a study's scheme versions, newest first, without lists or allocations. |
| `POST` | `/api/v1/execution/randomization/schemes` | DraftRandomizationScheme | Drafts the study's next randomisation scheme version. |
| `POST` | `/api/v1/execution/randomization/schemes/{schemeId}/approve` | ApproveRandomizationScheme | Approves a draft version for go-live, retiring the previous one and generating its list. |
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
| `GET` | `/api/v1/execution/rtsm/kits/certificates/{certificateId}` |  |  |
| `GET` | `/api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}` |  |  |
| `POST` | `/api/v1/execution/rtsm/kits/reconcile-batch` |  |  |
| `POST` | `/api/v1/execution/rtsm/kits/seed-inventory` |  |  |
| `POST` | `/api/v1/execution/rtsm/kits/validate-scan` |  |  |
| `POST` | `/api/v1/execution/sites/{siteId}/activate` | ActivateSite |  |
| `GET` | `/api/v1/execution/sites/{siteId}/compliance-status` | GetSiteComplianceStatus |  |
| `GET` | `/api/v1/execution/subjects` | ListSubjects | Lists one page of a study's subjects in enrollment order. |
| `POST` | `/api/v1/execution/subjects` | CreateSubject |  |
| `GET` | `/api/v1/execution/subjects/counts` | GetSubjectCounts | Counts a study's subjects grouped by lifecycle status. |
| `GET` | `/api/v1/execution/subjects/{subjectId}` | GetSubjectDetail |  |
| `POST` | `/api/v1/execution/subjects/{subjectId}/consent` | RecordSubjectConsent |  |
| `PUT` | `/api/v1/execution/subjects/{subjectId}/demographics` | UpdateSubjectDemographics |  |
| `DELETE` | `/api/v1/execution/subjects/{subjectId}/demographics` | DeleteSubjectDemographics |  |
| `POST` | `/api/v1/execution/subjects/{subjectId}/randomize` | RandomizeSubject | Randomises an enrolled participant under the live scheme and assigns a kit when it needs one. |
| `PATCH` | `/api/v1/execution/subjects/{subjectId}/site` | AssignSubjectSite | Assigns a clinical site to a screening subject and synchronizes screening enrollment with the kernel. |
| `PATCH` | `/api/v1/execution/subjects/{subjectId}/state` | UpdateSubjectState |  |
| `POST` | `/api/v1/execution/subjects/{subjectId}/unblind` | UnblindSubject | Performs an emergency unblinding on a randomized participant with a Part 11 electronic signature. |
| `POST` | `/api/v1/execution/webhooks/etmf` | EtmfCompletenessWebhook |  |
| `GET` | `/healthz` | ExecutionHealthCheck | Execution engine health probe. |
