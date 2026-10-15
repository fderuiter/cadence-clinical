# econsent API

Generated from `docs/openapi/econsent_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/econsent/status` | EconsentStatus | Returns eConsent operational status. |
| `POST` | `/api/v1/econsent/templates/{template_id}/capture-consent` | CaptureConsentLegacy |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/publish` | PublishTemplate |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` | CaptureConsentVersioned |  |
| `POST` | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/publish` | PublishTemplateVersioned |  |
| `GET` | `/healthz` | EconsentHealthCheck | eConsent service health probe. |
