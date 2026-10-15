# ai_gateway API

Generated from `docs/openapi/ai_gateway_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ai/protocol-optimization/eligibility` | SimulateEligibilityOptimization | Evaluates USDM eligibility criteria feasibility against RWE baseline distributions and reports attrition waterfalls. |
| `GET` | `/api/v1/ai/status` | AiStatus | Returns AI Gateway operational status. |
| `POST` | `/api/v1/ai/synthetic-controls/simulate` | SimulateSyntheticControlArm | Executes propensity score matching and Monte Carlo sampling to generate a synthetic control arm cohort. |
| `GET` | `/healthz` | AiHealthCheck | AI Gateway medical coding proxy health probe. |
