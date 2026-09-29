# safety API

Generated from `docs/openapi/safety_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `POST` | `/api/v1/safety/icsr/render` | RenderIcsrReport | Renders an ICSR E2B(R3) safety report as Liquid XML. |
| `GET` | `/api/v1/safety/status` | SafetyStatus | Returns Safety operational status. |
| `GET` | `/healthz` | SafetyHealthCheck | Clinical Safety & Pharmacovigilance health probe. |
