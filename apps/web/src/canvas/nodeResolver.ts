import { computed, shallowReactive, type ComputedRef } from "vue";
import {
  registerLogoutCleanup,
  registerSessionCacheClearHandler,
} from "../auth/sessionCoordinator.js";
import { kernelApi, type KernelNode } from "../api/kernelApi";
import * as kernelStudy from "./kernelStudy";
import { formatNodeRef, parseNodeRef } from "./nodeRef";

/**
 * Browser-side study object summary resolver (ADR-2481, CLINICAL_CANVAS.md §4.1).
 *
 * Resolves human-readable titles and display codes on demand via `GET /api/v1/kernel/nodes/{nodeRef}`,
 * deduplicating in-flight requests and limiting concurrency per study session so large lineage or
 * impact graphs never flood backend read services.
 */
export const MAX_CONCURRENT_NODE_LOOKUPS = 4;

export interface ResolvedNodeEntry {
  status: "loading" | "resolved" | "not_found" | "error";
  node: KernelNode | null;
  error: string | null;
}

export interface NodeLocalHint {
  kind?: string;
  title?: string | null;
  code?: string | null;
  status?: string | null;
}

export interface NodeDisplaySummary {
  ref: string;
  kind: string;
  kindLabel: string;
  label: string;
  code: string | null;
  title: string | null;
  status: string | null;
  masked: boolean;
  maskReason: string | null;
  resolved: boolean;
  loading: boolean;
}

let activeSessionKey = "";
let sessionEpoch = 0;
let activeLookups = 0;
const lookupQueue: Array<() => void> = [];

const resolvedEntries = shallowReactive<Record<string, ResolvedNodeEntry>>({});
const localHints = shallowReactive<Record<string, NodeLocalHint>>({});
const inFlight = new Map<string, Promise<KernelNode | null>>();

function acquireLookupSlot(): Promise<void> | null {
  if (activeLookups < MAX_CONCURRENT_NODE_LOOKUPS) {
    activeLookups += 1;
    return null;
  }
  return new Promise<void>((resolve) => {
    lookupQueue.push(resolve);
  });
}

function releaseLookupSlot(): void {
  activeLookups = Math.max(0, activeLookups - 1);
  while (
    activeLookups < MAX_CONCURRENT_NODE_LOOKUPS &&
    lookupQueue.length > 0
  ) {
    const next = lookupQueue.shift();
    if (next) {
      activeLookups += 1;
      next();
    }
  }
}

function clearReactiveRecord(record: Record<string, unknown>): void {
  for (const key of Object.keys(record)) {
    delete record[key];
  }
}

export function clearNodeResolutionCache(): void {
  activeSessionKey = "";
  sessionEpoch += 1;
  clearReactiveRecord(resolvedEntries);
  clearReactiveRecord(localHints);
  inFlight.clear();
  if (typeof kernelStudy.clearKernelStudyCache === "function") {
    kernelStudy.clearKernelStudyCache();
  }
  while (lookupQueue.length > 0) {
    const waiter = lookupQueue.shift();
    if (waiter) {
      activeLookups += 1;
      waiter();
    }
  }
}

registerSessionCacheClearHandler(() => {
  clearNodeResolutionCache();
});
registerLogoutCleanup(clearNodeResolutionCache);

/**
 * Resets cached node summaries when switching to a different study or caller session.
 * Calling with the already-active study code and caller scope preserves the warm session cache.
 */
export function resetNodeResolutionSession(studyCode = ""): void {
  const normalized = studyCode.trim();
  const callerScope =
    typeof kernelStudy.getKernelStudyCallerScopeKey === "function"
      ? kernelStudy.getKernelStudyCallerScopeKey()
      : "anonymous::";
  const nextSessionKey = normalized
    ? `${callerScope}::${encodeURIComponent(normalized.toUpperCase())}`
    : "";
  const activeStudySessionKey =
    typeof kernelStudy.getActiveKernelStudySessionKey === "function"
      ? kernelStudy.getActiveKernelStudySessionKey()
      : "";
  if (
    nextSessionKey &&
    (nextSessionKey === activeSessionKey ||
      (!activeSessionKey && nextSessionKey === activeStudySessionKey))
  ) {
    activeSessionKey = nextSessionKey;
    if (typeof kernelStudy.resetKernelStudySession === "function") {
      kernelStudy.resetKernelStudySession(normalized);
    }
    return;
  }
  clearNodeResolutionCache();
  activeSessionKey = nextSessionKey;
  if (typeof kernelStudy.resetKernelStudySession === "function") {
    kernelStudy.resetKernelStudySession(normalized);
  }
}

/**
 * Seeds a human code/title already loaded in the workspace (e.g. participants list or study search)
 * so references render immediately without waiting on an extra network round-trip.
 */
export function seedNodeSummary(
  nodeRefText: string,
  hint: NodeLocalHint
): void {
  const parsed = parseNodeRef(nodeRefText);
  if (!parsed) return;
  const fullKey = formatNodeRef(parsed);
  const baseKey = `${parsed.kind}:${parsed.id}`;
  const merged: NodeLocalHint = {
    kind: hint.kind ?? parsed.kind,
    title: hint.title ?? localHints[fullKey]?.title ?? null,
    code: hint.code ?? localHints[fullKey]?.code ?? null,
    status: hint.status ?? localHints[fullKey]?.status ?? null,
  };
  localHints[fullKey] = merged;
  localHints[baseKey] = {
    kind: merged.kind,
    title: merged.title ?? localHints[baseKey]?.title ?? null,
    code: merged.code ?? localHints[baseKey]?.code ?? null,
    status: merged.status ?? localHints[baseKey]?.status ?? null,
  };
}

/** Can the browser call `kernelApi.getNode` in this environment? */
export function canResolveNodesRemotely(): boolean {
  return typeof (kernelApi as { getNode?: unknown }).getNode === "function";
}

/**
 * Resolves a `NodeRef` via `kernelApi.getNode`, deduplicating concurrent requests for the same
 * key and capping in-flight lookups at `MAX_CONCURRENT_NODE_LOOKUPS`.
 */
export function resolveNode(nodeRefText: string): Promise<KernelNode | null> {
  const parsed = parseNodeRef(nodeRefText);
  if (!parsed) return Promise.resolve(null);

  const key = formatNodeRef(parsed);
  const baseKey = `${parsed.kind}:${parsed.id}`;
  const existing = resolvedEntries[key];
  if (existing?.status === "resolved") {
    return Promise.resolve(existing.node);
  }
  if (existing?.status === "not_found") {
    return Promise.resolve(null);
  }

  const pending = inFlight.get(key);
  if (pending) return pending;

  if (!canResolveNodesRemotely()) {
    const hint = localHints[key] ?? localHints[baseKey];
    if (hint && (hint.title || hint.code)) {
      return Promise.resolve({
        ref: key,
        kind: hint.kind ?? parsed.kind,
        title: hint.title ?? null,
        code: hint.code ?? null,
        status: hint.status ?? null,
        mask: null,
      });
    }
    return Promise.resolve(null);
  }

  const epochAtStart = sessionEpoch;
  resolvedEntries[key] = {
    status: "loading",
    node: existing?.node ?? null,
    error: null,
  };

  const request = (async (): Promise<KernelNode | null> => {
    const slotWait = acquireLookupSlot();
    if (slotWait) await slotWait;
    try {
      if (epochAtStart !== sessionEpoch) return null;
      const node = await kernelApi.getNode(key);
      if (epochAtStart !== sessionEpoch) return node;
      resolvedEntries[key] = { status: "resolved", node, error: null };
      if (key !== baseKey && !resolvedEntries[baseKey]) {
        resolvedEntries[baseKey] = { status: "resolved", node, error: null };
      }
      return node;
    } catch (err) {
      if (epochAtStart === sessionEpoch) {
        const httpStatus = (err as { status?: number | null } | null)?.status;
        const message = err instanceof Error ? err.message : String(err);
        resolvedEntries[key] = {
          status: httpStatus === 404 ? "not_found" : "error",
          node: null,
          error: message,
        };
      }
      return null;
    } finally {
      inFlight.delete(key);
      releaseLookupSlot();
    }
  })();

  inFlight.set(key, request);
  return request;
}

/** Triggers background resolution for one or more `NodeRef` strings if not yet cached. */
export function ensureNodesResolved(
  refs: Iterable<string | null | undefined>
): void {
  if (!canResolveNodesRemotely()) return;
  for (const raw of refs) {
    if (!raw) continue;
    const parsed = parseNodeRef(raw);
    if (!parsed) continue;
    const key = formatNodeRef(parsed);
    if (!resolvedEntries[key] && !inFlight.has(key)) {
      void resolveNode(key);
    }
  }
}

const KIND_LABELS: Record<string, string> = {
  study: "Study",
  study_site: "Study site",
  participant: "Participant",
  enrollment: "Enrollment",
  protocol_version: "Protocol version",
  collection_instance: "Form",
  collection_spec: "Form definition",
  observation: "Observation",
  assertion: "Verification",
  proposal: "Proposal",
  artifact_version: "Document version",
  attestation: "Signature record",
  consent: "Consent record",
  grant: "Role grant",
  role: "Role",
  query: "Query",
  protocol_element: "Protocol element",
  form_version_snapshot: "Form snapshot",
  subscription: "Subscription",
  product_entitlement: "Product entitlement",
};

/** Human-readable singular noun phrase for a `NodeRef` kind (`study_site` -> `"Study site"`). */
export function formatKindTitle(kindOrRef: string | null | undefined): string {
  const safe = String(kindOrRef ?? "");
  const parsed = parseNodeRef(safe);
  const rawKind = (parsed?.kind ?? safe.split(":", 1)[0] ?? "")
    .trim()
    .toLowerCase();
  if (!rawKind) return "Record";
  if (KIND_LABELS[rawKind]) return KIND_LABELS[rawKind];
  const words = rawKind
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .trim();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : "Record";
}

/** Lowercase kind phrase for inline sentences (`protocol_version` -> `"protocol version"`). */
export function formatKindInline(kindOrRef: string | null | undefined): string {
  const safe = String(kindOrRef ?? "");
  const parsed = parseNodeRef(safe);
  const rawKind = (parsed?.kind ?? safe.split(":", 1)[0] ?? "")
    .trim()
    .toLowerCase();
  if (!rawKind) return "record";
  return rawKind.replace(/[._-]+/g, " ");
}

/** Plain-language reason for a masked placeholder (`ADR-2471`, `CLINICAL_CANVAS.md`). */
export function maskReasonWords(mask: string | null | undefined): string {
  if (!mask) return "restricted";
  if (mask === "Blinded") return "blinded";
  if (mask === "OutsideScope") return "outside your site";
  if (mask === "IdentityProtected") return "identity protected";
  if (mask === "NotPermitted") return "not permitted";
  return mask.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
}

/** Masked placeholder text preserving kind and mask reason (`Hidden participant (outside your site)`). */
export function formatMaskedNode(
  kind: string,
  mask: string | null | undefined
): string {
  return `Hidden ${formatKindInline(kind)} (${maskReasonWords(mask)})`;
}

function buildHumanLabel(
  kind: string,
  version: number | string | undefined,
  code: string | null,
  title: string | null
): string {
  const cleanCode = code?.trim() || null;
  const cleanTitle = title?.trim() || null;

  if (kind === "study_site" && cleanCode) {
    const prefix = /^site\b/i.test(cleanCode) ? cleanCode : `Site ${cleanCode}`;
    return cleanTitle &&
      cleanTitle !== cleanCode &&
      cleanTitle.toLowerCase() !== prefix.toLowerCase()
      ? `${prefix} · ${cleanTitle}`
      : prefix;
  }

  if (cleanCode && cleanTitle && cleanCode !== cleanTitle) {
    return `${cleanCode} · ${cleanTitle}`;
  }
  if (cleanTitle) return cleanTitle;
  if (cleanCode) return cleanCode;

  const baseTitle = formatKindTitle(kind);
  return version ? `${baseTitle} v${version}` : baseTitle;
}

/** Reads the current reactive display summary for a `NodeRef` without mutating state. */
export function getNodeDisplay(
  nodeRefText: string,
  fallbackTitle?: string | null
): NodeDisplaySummary {
  const parsed = parseNodeRef(nodeRefText);
  if (!parsed) {
    const label = fallbackTitle?.trim() || nodeRefText.trim() || "Record";
    return {
      ref: nodeRefText,
      kind: "record",
      kindLabel: "Record",
      label,
      code: null,
      title: fallbackTitle ?? null,
      status: null,
      masked: false,
      maskReason: null,
      resolved: Boolean(fallbackTitle),
      loading: false,
    };
  }

  const key = formatNodeRef(parsed);
  const baseKey = `${parsed.kind}:${parsed.id}`;
  const entry = resolvedEntries[key] ?? resolvedEntries[baseKey];
  const hint = localHints[key] ?? localHints[baseKey];
  const node = entry?.node ?? null;

  if (node && node.mask !== null) {
    return {
      ref: key,
      kind: node.kind || parsed.kind,
      kindLabel: formatKindTitle(node.kind || parsed.kind),
      label: formatMaskedNode(node.kind || parsed.kind, node.mask),
      code: null,
      title: null,
      status: null,
      masked: true,
      maskReason: maskReasonWords(node.mask),
      resolved: true,
      loading: false,
    };
  }

  const code = node?.code ?? hint?.code ?? null;
  const title = fallbackTitle?.trim() || node?.title || hint?.title || null;
  const status = node?.status ?? hint?.status ?? null;
  const resolved = Boolean(code || title || entry?.status === "resolved");

  return {
    ref: key,
    kind: node?.kind || parsed.kind,
    kindLabel: formatKindTitle(node?.kind || parsed.kind),
    label: buildHumanLabel(
      node?.kind || parsed.kind,
      parsed.version,
      code,
      title
    ),
    code,
    title,
    status,
    masked: false,
    maskReason: null,
    resolved,
    loading: entry?.status === "loading",
  };
}

/** Reactive computed wrapper returning the human display summary for a `NodeRef`. */
export function useNodeDisplay(
  nodeRefGetter: () => string,
  fallbackTitleGetter?: () => string | null | undefined
): ComputedRef<NodeDisplaySummary> {
  return computed(() =>
    getNodeDisplay(nodeRefGetter(), fallbackTitleGetter?.() ?? null)
  );
}

/** Reads the cached resolution entry for a `NodeRef`, if any. */
export function getCachedNodeEntry(
  nodeRefText: string
): ResolvedNodeEntry | null {
  const parsed = parseNodeRef(nodeRefText);
  if (!parsed) return null;
  const key = formatNodeRef(parsed);
  return (
    resolvedEntries[key] ??
    resolvedEntries[`${parsed.kind}:${parsed.id}`] ??
    null
  );
}

/**
 * Formats internal PascalCase or dot/snake command identifiers into readable clinical action titles
 * (`ChangeParticipantState` -> `"Change participant state"`).
 */
export function formatCommandTitle(command: string): string {
  const trimmed = command.trim();
  if (!trimmed) return "";
  const spaced = trimmed
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  if (!spaced) return trimmed;
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

/**
 * Formats RFC 6901 JSON Pointers and snake_case field paths into readable clinical field names
 * (`/window/upper` -> `"Window · Upper"`, `/epochs/0/name` -> `"Epochs #1 · Name"`).
 */
export function formatFieldPath(pointerOrKey: string): string {
  const trimmed = pointerOrKey.trim();
  if (!trimmed || trimmed === "/") return "Document root";

  const rawSegments = (trimmed.startsWith("/") ? trimmed.slice(1) : trimmed)
    .split("/")
    .map((seg) => seg.replace(/~1/g, "/").replace(/~0/g, "~").trim())
    .filter(Boolean);

  if (rawSegments.length === 0) return "Document root";

  const formatted: string[] = [];
  for (const seg of rawSegments) {
    if (/^\d+$/.test(seg)) {
      const itemNumber = `#${Number(seg) + 1}`;
      if (formatted.length > 0) {
        formatted[formatted.length - 1] =
          `${formatted[formatted.length - 1]} ${itemNumber}`;
      } else {
        formatted.push(`Item ${itemNumber}`);
      }
      continue;
    }
    const words = seg
      .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
      .replace(/[._-]+/g, " ")
      .replace(/\s+/g, " ")
      .trim();
    const label = words
      ? words.charAt(0).toUpperCase() + words.slice(1).toLowerCase()
      : seg;
    formatted.push(label);
  }

  return formatted.join(" · ");
}

/**
 * Replaces inline raw `NodeRef` substrings inside an error message with their human kind phrase
 * so primary error summaries stay readable while full diagnostics remain in support details.
 */
export function sanitizeDiagnosticSummary(message: string): string {
  if (!message) return "";
  return message
    .replace(
      /\b([a-z][a-z0-9_]*):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:@[0-9]+)?\b/gi,
      (_match, kind: string) => `this ${formatKindInline(kind)}`
    )
    .replace(/\s*\(NotPermitted\)/gi, "")
    .trim();
}
