# designer API

Generated from `docs/openapi/designer_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/designer/cohort-feasibility/synthetic-simulate` | SimulateDesignerCohortFeasibility | Proxies synthetic cohort feasibility simulation requests to AI Gateway and returns structured feasibility summaries. |
| `POST` | `/api/v1/protocols/compile` | CompileProtocol | Compiles CDISC USDM protocol JSON into DDF IR and EDC runtime schemas. |
| `GET` | `/api/v1/studies` | ListStudies | Lists active CDISC USDM study protocol definitions. |
| `POST` | `/api/v1/studies/{id}/rules/amendment-regression` | AmendmentRegression | Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies. |
| `POST` | `/api/v1/studies/{id}/rules/batch-validate` | BatchValidateRules | Executes study-wide parallel batch compilation and validation across pinned edit check rule packs. |
| `GET` | `/api/v1/studies/{id}/rules/batch-validate/{jobId}` | GetBatchValidationJob | Retrieves the aggregated report for an asynchronous batch validation job. |
| `GET` | `/healthz` | DesignerHealthCheck | Protocol designer metadata health probe. |
