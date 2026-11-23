/**
 * Generated from packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs by scripts/generate-canvas-contracts.js.
 * Do not edit manually; run `pnpm --filter web generate:api` to regenerate.
 */

/** Registered kernel node kinds, in the exact order `NodeKinds.cs` declares them. */
export const NODE_KINDS = [
  "tenant",
  "organization",
  "person",
  "site",
  "study",
  "study_site",
  "participant",
  "enrollment",
  "protocol_version",
  "observation",
  "assertion",
  "collection_instance",
  "attestation",
  "artifact",
  "artifact_version",
  "role",
  "grant",
  "qualification",
  "qualification_requirement",
  "effectivity",
  "applicability",
  "consent_status",
  "tenant_membership",
  "code_system",
  "code_system_version",
  "concept",
  "concept_mapping",
  "standard_version",
  "library_item",
  "study_standards_profile",
  "work_queue_definition",
  "validation_run",
  "validation_finding",
  "protocol_element",
  "proposal",
  "form_version_snapshot",
  "form_query",
  "subscription",
  "product_entitlement",
  "task_configuration",
] as const;

export type NodeKind = (typeof NODE_KINDS)[number];
