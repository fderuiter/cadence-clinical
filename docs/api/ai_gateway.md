# ai_gateway API

Generated from `docs/openapi/ai_gateway_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/ai/intent/route` | RouteAiIntent | Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow. |
| `GET` | `/api/v1/ai/status` | AiStatus | Returns AI Gateway operational status. |
| `GET` | `/healthz` | AiHealthCheck | AI Gateway medical coding proxy health probe. |
