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

import { NODE_KINDS, type NodeKind } from "./generated/nodeKinds";

export { NODE_KINDS, type NodeKind };

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

/** Canonical `collection_instance:<id>` reference from a form UUID or existing NodeRef, or null. */
export function collectionInstanceRef(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim().toLowerCase();
  const parsed =
    parseNodeRef(trimmed) ?? tryNodeRef("collection_instance", trimmed);
  return parsed?.kind === "collection_instance"
    ? formatNodeRef({ kind: "collection_instance", id: parsed.id })
    : null;
}

/** Extracts the lower-case UUID from a `collection_instance:<id>` reference or raw UUID, or null. */
export function collectionInstanceId(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim().toLowerCase();
  const parsed =
    parseNodeRef(trimmed) ?? tryNodeRef("collection_instance", trimmed);
  return parsed?.kind === "collection_instance" ? parsed.id : null;
}

/** Canonical unversioned `observation:<id>` reference from an observation UUID or NodeRef, or null. */
export function observationNodeRef(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim().toLowerCase();
  const parsed = parseNodeRef(trimmed) ?? tryNodeRef("observation", trimmed);
  return parsed?.kind === "observation"
    ? formatNodeRef({ kind: "observation", id: parsed.id })
    : null;
}

/** Extracts the unversioned `enrollment:<uuid>` reference from a participant summary when present. */
export function enrollmentNodeRefFromParticipant(
  participant: { enrollment_ref?: string | null } | null | undefined
): string | null {
  const parsed = parseNodeRef(participant?.enrollment_ref);
  return parsed?.kind === "enrollment"
    ? formatNodeRef({ kind: "enrollment", id: parsed.id })
    : null;
}

/** Extracts the lower-case enrollment UUID from `participant.enrollment_ref`, or null when absent. */
export function enrollmentIdFromParticipant(
  participant: { enrollment_ref?: string | null } | null | undefined
): string | null {
  const parsed = parseNodeRef(participant?.enrollment_ref);
  return parsed?.kind === "enrollment" ? parsed.id : null;
}

/**
 * Canonical Clinical Canvas `NodeRef` for a participant: `enrollment:<uuid>` when Execution has persisted
 * `KernelEnrollmentId`, falling back to `participant:<uuid>` for legacy or unlinked Execution rows.
 */
export function participantNodeRef(
  participant: { id: string; enrollment_ref?: string | null } | null | undefined
): string {
  const enrollmentRef = enrollmentNodeRefFromParticipant(participant);
  if (enrollmentRef) return enrollmentRef;
  const fallback = participant
    ? tryNodeRef("participant", participant.id)
    : null;
  return fallback ? formatNodeRef(fallback) : "";
}
