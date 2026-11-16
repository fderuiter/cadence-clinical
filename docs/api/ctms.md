# ctms API

Generated from `docs/openapi/ctms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ctms/letters/compile` | CompileSiteVisitLetterPdf | Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch. |
| `POST` | `/api/v1/ctms/letters/render` | RenderSiteVisitLetter | Renders a CTMS site visit monitoring letter via Liquid template. |
| `GET` | `/api/v1/ctms/letters/{letterId}/pdf` | GetSiteVisitLetterPdf | Downloads or views the compiled GxP PDF visit letter. |
| `GET` | `/api/v1/ctms/monitoring-visits` | ListMonitoringVisits | List monitoring visits filtered by study, site, CRA, or status. |
| `POST` | `/api/v1/ctms/monitoring-visits` | ScheduleMonitoringVisit | Schedule a new monitoring visit. |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/complete` | CompleteMonitoringVisit | Complete a scheduled monitoring visit with actual date and clinical findings. |
| `GET` | `/api/v1/ctms/monitoring-visits/{visit_id}/letters` | GetMonitoringVisitLetters | Get generated letters for a monitoring visit. |
| `GET` | `/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}` | GetMonitoringVisitLetterByType | Get a generated visit letter by type. |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/sign-off` | SignOffMonitoringVisit | Perform supervisory Part 11 sign-off on a completed monitoring visit. |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/start` | StartMonitoringVisit | Start an on-site monitoring visit. |
| `GET` | `/api/v1/ctms/site-milestones` | ListSiteMilestones | List site milestones filtered by study or site. |
| `POST` | `/api/v1/ctms/site-milestones` | CreateSiteMilestone | Create a new site milestone. |
| `PUT` | `/api/v1/ctms/site-milestones/{milestone_id}` | UpdateSiteMilestone | Update site milestone status, planned date, or actual date. |
| `GET` | `/api/v1/ctms/startup/matrix` | GetCountryGreenlightMatrix | Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country. |
| `POST` | `/api/v1/ctms/startup/matrix/batch-certify` | BatchCertifyCountrySites | Batch-certifies all selected 100% compliant sites in a target country with a single e-signature. |
| `GET` | `/api/v1/ctms/startup/sites/{siteId}/greenlight` | EvaluateSiteGreenlight | Evaluates 6-facet greenlight readiness for a single trial site. |
| `POST` | `/api/v1/ctms/startup/sites/{siteId}/greenlight/certify` | CertifySiteGreenlight | Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token. |
| `GET` | `/api/v1/ctms/status` | CtmsStatus | Returns CTMS operational status. |
| `GET` | `/healthz` | CtmsHealthCheck | CTMS operations health probe. |
