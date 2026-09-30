/**
 * The kernel's public object reference (ADR-2481, spec §3.4), mirrored for the browser: `kind:id` or
 * `kind:id@version`, for example `participant:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e`. The Clinical Canvas uses
 * the same string in its URLs, its palette and its inspector, so a link to a selected object names exactly
 * one kernel object.
 *
 * The rules match `Cadence.Kernel.References.NodeRef`: the kind is registered, the id is a lower-case
 * hyphenated non-empty UUID, and the version is a positive integer without leading zeros. Each reference has
 * one string form. `tests/canvas/contracts.spec.ts` fails when `NODE_KINDS` drifts from `NodeKinds.cs`.
 */

/** Registered node kinds, in the order `NodeKinds.cs` declares them. */
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
  "attestation",
  "artifact",
  "artifact_version",
  "role",
  "grant",
  "qualification",
  "qualification_requirement",
  "effectivity",
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
] as const;

export type NodeKind = (typeof NODE_KINDS)[number];

export interface NodeRef {
  kind: NodeKind;
  id: string;
  version?: number;
}

const KINDS: ReadonlySet<string> = new Set(NODE_KINDS);
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const EMPTY_UUID = "00000000-0000-0000-0000-000000000000";
const VERSION = /^[1-9][0-9]*$/;

export function isNodeKind(kind: string): kind is NodeKind {
  return KINDS.has(kind);
}

/** Parses the canonical string form; returns null for anything else. */
export function parseNodeRef(text: unknown): NodeRef | null {
  if (typeof text !== "string") return null;
  const colon = text.indexOf(":");
  if (colon <= 0) return null;
  const kind = text.slice(0, colon);
  if (!isNodeKind(kind)) return null;

  const rest = text.slice(colon + 1);
  const at = rest.indexOf("@");
  const id = at < 0 ? rest : rest.slice(0, at);
  if (!UUID.test(id) || id === EMPTY_UUID) return null;
  if (at < 0) return { kind, id };

  const versionText = rest.slice(at + 1);
  if (!VERSION.test(versionText)) return null;
  const version = Number(versionText);
  if (!Number.isSafeInteger(version)) return null;
  return { kind, id, version };
}

/** The canonical string form; throws for a reference `parseNodeRef` would reject. */
export function formatNodeRef(ref: NodeRef): string {
  const text =
    ref.version === undefined
      ? `${ref.kind}:${ref.id}`
      : `${ref.kind}:${ref.id}@${ref.version}`;
  if (!parseNodeRef(text)) {
    throw new Error(`'${text}' is not a canonical node reference (ADR-2481).`);
  }
  return text;
}

/** A reference built from an id the API returned, or null when the id is not a canonical UUID. */
export function tryNodeRef(kind: NodeKind, id: unknown): NodeRef | null {
  return typeof id === "string" ? parseNodeRef(`${kind}:${id}`) : null;
}
