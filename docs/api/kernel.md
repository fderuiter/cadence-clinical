# kernel API

Generated from `docs/openapi/kernel_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.

| Method | Path | Operation | Summary |
| ------ | ---- | --------- | ------- |
| `GET` | `/api/v1/kernel/activity` | KernelActivity | What happened in a scope, newest first. |
| `GET` | `/api/v1/kernel/authorization/can-i` | KernelCanI | Whether the caller may exercise a capability on an object now, and why. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}` | KernelGetNode | Resolves a NodeRef to its summary. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/children` | KernelGetChildren | The children of a scope node, with their own child counts. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/commands` | KernelGetCommands | The kernel commands offered on an object, each with the caller's dry-run decision. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/dependents` | KernelDependents | What depends on an object, grouped by kind with counts. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/lineage` | KernelTrace | Walks an object's lineage backward to its inputs or forward to its dependents. |
| `GET` | `/api/v1/kernel/nodes/{nodeRef}/path` | KernelGetZoomPath | The zoom path from the tenant down to an object. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}` | KernelGetProtocolVersion | One protocol version, without its content. |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/impact` | KernelAmendmentImpact | What depends on the version a protocol amendment changes (K-PROT-2). |
| `GET` | `/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities` | KernelGetScheduleOfActivities | A protocol version's Schedule of Activities, compiled from its design. |
| `GET` | `/api/v1/kernel/search` | KernelSearch | Finds kernel objects in the caller's tenant by code or title. |
| `GET` | `/api/v1/kernel/status` | KernelStatus | Returns the Clinical Kernel API's operational status. |
| `GET` | `/api/v1/kernel/studies/{studyRef}/protocol-versions` | KernelListProtocolVersions | A study's protocol versions, newest first, without their content. |
| `GET` | `/healthz` | KernelHealthCheck | Clinical Kernel API health probe. |
