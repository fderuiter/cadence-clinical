# API Reference

Generated from `docs/openapi/*_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

Each page documents the live HTTP operations, authorization policies, GxP headers (`X-Sig-Token`, `Idempotency-Key`, `X-Change-Reason`), RFC 7807 error contracts, and deduplicated `components.schemas` exported directly from the .NET 10 hosts (`pnpm cadence sync-openapi-spec`). The `openapi` quality gate fails whenever any operation omits an `operationId` or `summary` or when `docs/api/*.md` drifts from `docs/openapi/*.json`.

- **Unified Gateway OpenAPI 3.1 Bundle:** [`cdisc_openapi.json`](../openapi/cdisc_openapi.json)
- **Contract Coverage:** `20` OpenAPI contracts (`13` Active, `7` Scaffold in `ServiceTopology`), `352` documented operations, and `560` component schemas.
- **Consolidated Composition Host (`kernel-api`):** [kernel](kernel.md) groups core `Kernel` and kernel-backed `Execution` operations alongside the dedicated module contracts [intelligence](intelligence.md) (including `POST /api/v1/ai/mcp`) and [execution_forms](execution_forms.md).
- **Legacy Reference Snapshot:** `docs/openapi/legacy/cdisc_openapi.json` retains the frozen Python-era aggregate snapshot solely for unmigrated `apps/web/src/api/types.ts` slices.

| Service | Operations | Schemas | Maturity | Stubs (`501`) | OpenAPI JSON |
| ------- | ---------- | ------- | -------- | ------------- | ------------ |
| [ctms](ctms.md) | 23 | 23 | <span class="VPBadge tip">Active</span> | 0 | [`ctms_openapi.json`](../openapi/ctms_openapi.json) |
| [designer](designer.md) | 6 | 27 | <span class="VPBadge warning">Scaffold</span> | 0 | [`designer_openapi.json`](../openapi/designer_openapi.json) |
| [econsent](econsent.md) | 6 | 6 | <span class="VPBadge tip">Active</span> | 0 | [`econsent_openapi.json`](../openapi/econsent_openapi.json) |
| [eisf](eisf.md) | 2 | 1 | <span class="VPBadge warning">Scaffold</span> | 0 | [`eisf_openapi.json`](../openapi/eisf_openapi.json) |
| [etmf](etmf.md) | 17 | 18 | <span class="VPBadge tip">Active</span> | 0 | [`etmf_openapi.json`](../openapi/etmf_openapi.json) |
| [execution](execution.md) | 65 | 91 | <span class="VPBadge tip">Active</span> | 0 | [`execution_openapi.json`](../openapi/execution_openapi.json) |
| [execution_forms](execution_forms.md) | 17 | 41 | <span class="VPBadge tip">Active</span> | 0 | [`execution_forms_openapi.json`](../openapi/execution_forms_openapi.json) |
| [fileshare](fileshare.md) | 2 | 1 | <span class="VPBadge warning">Scaffold</span> | 0 | [`fileshare_openapi.json`](../openapi/fileshare_openapi.json) |
| [gateway](gateway.md) | 3 | 6 | <span class="VPBadge tip">Active</span> | 0 | [`gateway_openapi.json`](../openapi/gateway_openapi.json) |
| [imaging](imaging.md) | 11 | 17 | <span class="VPBadge tip">Active</span> | `1` | [`imaging_openapi.json`](../openapi/imaging_openapi.json) |
| [intelligence](intelligence.md) | 13 | 24 | <span class="VPBadge tip">Active</span> | 0 | [`intelligence_openapi.json`](../openapi/intelligence_openapi.json) |
| [interop](interop.md) | 28 | 46 | <span class="VPBadge tip">Active</span> | `1` | [`interop_openapi.json`](../openapi/interop_openapi.json) |
| [kernel](kernel.md) | 75 | 164 | <span class="VPBadge tip">Active</span> | 0 | [`kernel_openapi.json`](../openapi/kernel_openapi.json) |
| [knowledge](knowledge.md) | 15 | 18 | <span class="VPBadge tip">Active</span> | 0 | [`knowledge_openapi.json`](../openapi/knowledge_openapi.json) |
| [notifications](notifications.md) | 3 | 5 | <span class="VPBadge warning">Scaffold</span> | 0 | [`notifications_openapi.json`](../openapi/notifications_openapi.json) |
| [org](org.md) | 2 | 1 | <span class="VPBadge warning">Scaffold</span> | 0 | [`org_openapi.json`](../openapi/org_openapi.json) |
| [payments](payments.md) | 38 | 40 | <span class="VPBadge tip">Active</span> | 0 | [`payments_openapi.json`](../openapi/payments_openapi.json) |
| [quality](quality.md) | 2 | 1 | <span class="VPBadge warning">Scaffold</span> | 0 | [`quality_openapi.json`](../openapi/quality_openapi.json) |
| [safety](safety.md) | 13 | 20 | <span class="VPBadge tip">Active</span> | 0 | [`safety_openapi.json`](../openapi/safety_openapi.json) |
| [tickets](tickets.md) | 11 | 10 | <span class="VPBadge warning">Scaffold</span> | 0 | [`tickets_openapi.json`](../openapi/tickets_openapi.json) |
