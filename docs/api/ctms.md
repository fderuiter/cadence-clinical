# ctms API

Generated from `docs/openapi/ctms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ctms/letters/compile` | CompileSiteVisitLetterPdf | Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch. |
| `POST` | `/api/v1/ctms/letters/render` | RenderSiteVisitLetter | Renders a CTMS site visit monitoring letter via Liquid template. |
| `GET` | `/api/v1/ctms/letters/{letterId}/pdf` | GetSiteVisitLetterPdf | Downloads or views the compiled GxP PDF visit letter. |
| `GET` | `/api/v1/ctms/startup/matrix` | GetCountryGreenlightMatrix | Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country. |
| `POST` | `/api/v1/ctms/startup/matrix/batch-certify` | BatchCertifyCountrySites | Batch-certifies all selected 100% compliant sites in a target country with a single e-signature. |
| `GET` | `/api/v1/ctms/startup/sites/{siteId}/greenlight` | EvaluateSiteGreenlight | Evaluates 6-facet greenlight readiness for a single trial site. |
| `POST` | `/api/v1/ctms/startup/sites/{siteId}/greenlight/certify` | CertifySiteGreenlight | Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token. |
| `GET` | `/api/v1/ctms/status` | CtmsStatus | Returns CTMS operational status. |
| `GET` | `/healthz` | CtmsHealthCheck | CTMS operations health probe. |
