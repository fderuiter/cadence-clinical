# ctms API

Generated from `docs/openapi/ctms_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ctms/letters/compile` | CompileSiteVisitLetterPdf | Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch. |
| `POST` | `/api/v1/ctms/letters/render` | RenderSiteVisitLetter | Renders a CTMS site visit monitoring letter via Liquid template. |
| `GET` | `/api/v1/ctms/letters/{letterId}/pdf` | GetSiteVisitLetterPdf | Downloads or views the compiled GxP PDF visit letter. |
| `GET` | `/api/v1/ctms/status` | CtmsStatus | Returns CTMS operational status. |
| `GET` | `/healthz` | CtmsHealthCheck | CTMS operations health probe. |
