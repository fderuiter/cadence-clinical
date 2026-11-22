# ctms API

Generated from `docs/openapi/ctms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ctms/doa/delegate` | DelegateSiteTasks | Assign site trial task delegation requiring Principal Investigator sign-off. |
| `POST` | `/api/v1/ctms/doa/revoke` | RevokeSiteTasks | Revoke or end a delegated trial duty with reason for change. |
| `POST` | `/api/v1/ctms/doa/sign-off` | SignOffDelegation | Endorse Delegation of Authority task assignment with Principal Investigator eSignature. |
| `GET` | `/api/v1/ctms/doa/sites/{site_id}/export-pdf` | ExportSiteDoaPdf | Export 21 CFR Part 11 signed DOA PDF log. |
| `GET` | `/api/v1/ctms/doa/sites/{site_id}/log` | GetSiteDoaLog | Fetch active and historical DOA log matrix for a site. |
| `POST` | `/api/v1/ctms/letters/compile` | CompileSiteVisitLetterPdf | Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch. |
| `POST` | `/api/v1/ctms/letters/render` | RenderSiteVisitLetter | Renders a CTMS site visit monitoring letter via Liquid template. |
| `GET` | `/api/v1/ctms/letters/{letterId}/pdf` | GetSiteVisitLetterPdf | Downloads or views the compiled GxP PDF visit letter. |
| `GET` | `/api/v1/ctms/monitoring-visits` | ListMonitoringVisits | List monitoring visits filtered by study, site, CRA, or status. |
| `POST` | `/api/v1/ctms/monitoring-visits` | ScheduleMonitoringVisit | Schedule a new monitoring visit. |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/complete` | CompleteMonitoringVisit | Complete a scheduled monitoring visit with actual date and clinical findings. |
| `GET` | `/api/v1/ctms/monitoring-visits/{visit_id}/letters` | GetMonitoringVisitLetters | Get generated letters for a monitoring visit. |
| `GET` | `/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}` | GetMonitoringVisitLetterByType | Get a generated visit letter by type. |
| `POST` | `/api/v1/ctms/monitoring-visits/{visit_id}/sign-off` | SignOffMonitoringVisit | Perform supervisory Part 11 sign-off on a completed monitoring visit. |
| `GET` | `/api/v1/ctms/site-milestones` | ListSiteMilestones | List site milestones filtered by study or site. |
| `POST` | `/api/v1/ctms/site-milestones` | CreateSiteMilestone | Create a new site milestone. |
| `PUT` | `/api/v1/ctms/site-milestones/{milestone_id}` | UpdateSiteMilestone | Update site milestone status, planned date, or actual date. |
| `GET` | `/api/v1/ctms/startup/matrix` | GetCountryGreenlightMatrix | Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country. |
| `POST` | `/api/v1/ctms/startup/matrix/batch-certify` | BatchCertifyCountrySites | Batch-certifies all selected 100% compliant sites in a target country with a single e-signature. |
| `GET` | `/api/v1/ctms/startup/sites/{siteId}/greenlight` | EvaluateSiteGreenlight | Evaluates 6-facet greenlight readiness for a single trial site. |
| `PUT` | `/api/v1/ctms/startup/sites/{siteId}/greenlight` | UpdateSiteGreenlight | Updates 6-facet startup greenlight readiness flags for a trial site. |
| `POST` | `/api/v1/ctms/startup/sites/{siteId}/greenlight` | InitializeSiteGreenlight | Initializes a 6-facet startup greenlight gate for a trial site. |
| `POST` | `/api/v1/ctms/startup/sites/{siteId}/greenlight/certify` | CertifySiteGreenlight | Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token. |
| `GET` | `/api/v1/ctms/status` | CtmsStatus | Returns CTMS operational status. |
| `GET` | `/healthz` | CtmsHealthCheck | CTMS operations health probe. |
