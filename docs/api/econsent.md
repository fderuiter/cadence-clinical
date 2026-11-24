# econsent API

Generated from `docs/openapi/econsent_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/econsent/reconsent/complete/{requirement_id}` | CompleteReconsentRequirement |  |
| `GET` | `/api/v1/econsent/reconsent/pending/{study_id}` | GetPendingReconsents |  |
| `GET` | `/api/v1/econsent/status` | EconsentStatus | Returns eConsent operational status. |
| `GET` | `/api/v1/econsent/templates/{template_id}/approved-content` | GetApprovedConsentContent |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/capture-consent` | CaptureConsentLegacy |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/publish` | PublishTemplate |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` | CaptureConsentVersioned |  |
| `GET` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks` | GetComprehensionChecks |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/publish` | PublishTemplateVersioned |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers` | SubmitComprehensionAnswers |  |
| `GET` | `/healthz` | EconsentHealthCheck | eConsent service health probe. |
