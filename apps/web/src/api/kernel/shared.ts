import {
  getBearerToken,
  notifySubscriptionExpiredIfNeeded,
  SUBSCRIPTION_EXPIRED_GUIDANCE,
} from "../authorization.js";
import { encodeChangeReason } from "../changeReason";
import { ApiError, createOpenApiClient, getBaseUrl } from "../client";
import type { components, paths } from "../generated/kernel";

/**
 * Shared OpenAPI client, error helpers, bitemporal query normalizers, and streaming download utilities
 * used across the domain-sliced Clinical Kernel clients in `src/api/kernel/*.ts`.
 */
export type KernelSchemas = components["schemas"];

export const kernelClient = createOpenApiClient<paths>();

export const ASSERTION_LOCKED = "K.Assertion.Locked";
export const ASSERTION_ALREADY_LOCKED = "K.Assertion.AlreadyLocked";
export const ASSERTION_SCOPE_NOT_READY = "K.Assertion.ScopeNotReady";
export const ASSERTION_REVISION_STALE = "K.Assertion.RevisionStale";
export const ASSERTION_QUERY_OPEN = "K.Assertion.QueryOpen";
export const ASSERTION_ALREADY_VERIFIED = "K.Assertion.AlreadyVerified";
export const ASSERTION_ALREADY_RETRACTED = "K.Assertion.AlreadyRetracted";
export const KERNEL_AUTHORIZATION_DENIED = "K.Authorization.Denied";
export const KERNEL_SUBSCRIPTION_EXPIRED = "K.Subscription.Expired";
export const KERNEL_COMMERCIAL_SCOPING_INVALID = "K.Commercial.ScopingInvalid";
export const KERNEL_COMMERCIAL_SCOPING_CAP_EXCEEDED =
  "K.Commercial.ScopingCapExceeded";
export const KERNEL_COMMERCIAL_QUOTE_INVALID = "K.Commercial.QuoteInvalid";
export const KERNEL_COMMERCIAL_ONBOARDING_CALLER_INVALID =
  "K.Commercial.OnboardingCallerInvalid";
export const KERNEL_COMMERCIAL_PRODUCT_FAMILY_NOT_ENTITLED =
  "K.Commercial.ProductFamilyNotEntitled";
export { SUBSCRIPTION_EXPIRED_GUIDANCE };

/** How far and along which edges a lineage read goes, and as of when (ADR-2471). The host's defaults apply to anything left out. */
export interface KernelLineageWindow {
  depth?: number;
  edgeKinds?: string[];
  validAt?: string;
  knownAt?: string;
}

export interface KernelHiddenScopesHeader {
  moreExist: boolean;
  reasons: string[];
}

export interface KernelStreamedArtifact {
  blob: Blob;
  filename: string;
  contentType: string;
  contentSha256: string | null;
  hiddenScopes?: KernelHiddenScopesHeader | null;
}

/** Extracts the kernel error code (`K.Assertion.Locked`, …) from a failed call, or null. */
export function assertionErrorCode(error: unknown): string | null {
  const data = (error as { data?: unknown } | null)?.data;
  if (!data || typeof data !== "object") return null;
  const code = (data as { code?: unknown }).code;
  return typeof code === "string" ? code : null;
}

/** True when a thrown error carries `code === "K.Subscription.Expired"`. */
export function isSubscriptionExpiredError(error: unknown): boolean {
  return assertionErrorCode(error) === KERNEL_SUBSCRIPTION_EXPIRED;
}

/** Extracts a structured `Explanation` from an RFC 9457 ProblemDetails error when present (`K-EXP-1`). */
export function extractKernelExplanation(
  error: unknown
): KernelSchemas["Kernel_KernelExplanationResponse"] | null {
  const container =
    (error as { data?: unknown } | null)?.data ?? (error as unknown);
  if (!container || typeof container !== "object") return null;
  const explanation = (container as Record<string, unknown>).explanation;
  if (!explanation || typeof explanation !== "object") return null;
  const record = explanation as Record<string, unknown>;
  if (typeof record.claim !== "string") return null;
  return explanation as KernelSchemas["Kernel_KernelExplanationResponse"];
}

/** Extracts structured field validation errors (`Record<string, string[]>`) from RFC 9457 ProblemDetails. */
export function extractKernelFieldErrors(
  error: unknown
): Record<string, string[]> {
  if (!error || typeof error !== "object") return {};
  const data = (error as { data?: unknown }).data;
  if (!data || typeof data !== "object") return {};
  const rawErrors = (data as Record<string, unknown>).errors;
  if (!rawErrors || typeof rawErrors !== "object" || Array.isArray(rawErrors)) {
    return {};
  }
  const normalized: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(
    rawErrors as Record<string, unknown>
  )) {
    if (Array.isArray(messages)) {
      const clean = messages.filter(
        (msg): msg is string => typeof msg === "string" && msg.trim().length > 0
      );
      if (clean.length > 0) {
        normalized[field] = clean;
      }
    } else if (typeof messages === "string" && messages.trim().length > 0) {
      normalized[field] = [messages.trim()];
    }
  }
  return normalized;
}

/** Extracts an actionable error string from RFC 9457 ProblemDetails (including validation/import `errors[]` or field `errors` maps). */
export function describeKernelError(error: unknown): string {
  if (assertionErrorCode(error) === KERNEL_SUBSCRIPTION_EXPIRED) {
    notifySubscriptionExpiredIfNeeded(
      (error as { status?: number | null } | null)?.status ?? 403,
      (error as { data?: unknown } | null)?.data
    );
    return SUBSCRIPTION_EXPIRED_GUIDANCE;
  }
  if (error && typeof error === "object") {
    const data = (error as { data?: unknown }).data;
    if (data && typeof data === "object") {
      const record = data as Record<string, unknown>;
      const detail =
        typeof record.detail === "string" && record.detail.trim()
          ? record.detail.trim()
          : "";
      const arrayProblems = Array.isArray(record.errors)
        ? record.errors.filter(
            (item): item is string =>
              typeof item === "string" && item.trim().length > 0
          )
        : [];
      const fieldErrors = extractKernelFieldErrors(error);
      const fieldProblems = Object.values(fieldErrors).flat();
      const problems = [...arrayProblems, ...fieldProblems];
      if (detail && problems.length > 0) {
        const extra = problems.filter((p) => p !== detail);
        return extra.length > 0 ? `${detail} (${extra.join("; ")})` : detail;
      }
      if (problems.length > 0) return problems.join("; ");
      if (detail) return detail;
    }
  }
  return error instanceof Error ? error.message : String(error);
}

/** True when the reader may not see the object: show its place and `mask`, never guess at the rest. */
export function isMasked(object: { mask: string | null }): boolean {
  return object.mask !== null;
}

/** The NodeRef without its `@version`, as the observation, lock, and proposal routes take it. */
export function unversioned(nodeRef: string): string {
  const at = nodeRef.indexOf("@");
  return at < 0 ? nodeRef : nodeRef.slice(0, at);
}

export function mutationHeaders(reason: string, idempotencyKey: string) {
  return {
    "X-Change-Reason": encodeChangeReason(reason),
    "Idempotency-Key": idempotencyKey,
  };
}

export function signedMutationHeaders(
  reason: string,
  idempotencyKey: string,
  sigToken: string
) {
  return {
    ...mutationHeaders(reason, idempotencyKey),
    "X-Sig-Token": sigToken,
  };
}

export function windowQuery({
  depth,
  edgeKinds,
  validAt,
  knownAt,
}: KernelLineageWindow) {
  return {
    depth,
    edge_kind: edgeKinds && edgeKinds.length > 0 ? edgeKinds : undefined,
    valid_at: validAt,
    known_at: knownAt,
  };
}

function parseDispositionFilename(
  disposition: string | null,
  fallback: string
): string {
  if (!disposition) return fallback;
  const match = disposition.match(/filename="?([^";]+)"?/i);
  return match?.[1]?.trim() || fallback;
}

function parseHiddenScopesHeader(
  raw: string | null
): KernelHiddenScopesHeader | null {
  if (!raw) return null;
  let moreExist = false;
  let reasons: string[] = [];
  for (const part of raw.split(";")) {
    const [key, val] = part.split("=").map((s) => s.trim());
    if (key === "more_exist") {
      moreExist = val === "true";
    } else if (key === "reasons" && val) {
      reasons = val
        .split(",")
        .map((r) => r.trim())
        .filter(Boolean);
    }
  }
  return { moreExist, reasons };
}

export async function streamKernelDownload(
  pathAndQuery: string,
  fallbackFilename: string
): Promise<KernelStreamedArtifact> {
  const base = getBaseUrl().replace(/\/+$/, "");
  const headers = new Headers();
  const token = await getBearerToken();
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  const response = await globalThis.fetch(`${base}${pathAndQuery}`, {
    method: "GET",
    headers,
  });
  if (!response.ok) {
    let errData: unknown;
    try {
      errData = await response.json();
    } catch {
      errData = null;
    }
    const detail =
      (errData as Record<string, unknown> | null)?.detail ||
      (errData as Record<string, unknown> | null)?.message ||
      `Request failed with status ${response.status}`;
    throw new ApiError(
      typeof detail === "string" ? detail : JSON.stringify(detail),
      response.status,
      response.statusText,
      errData
    );
  }
  const blob = await response.blob();
  const contentType =
    response.headers.get("content-type") ||
    blob.type ||
    "application/octet-stream";
  const filename = parseDispositionFilename(
    response.headers.get("content-disposition"),
    fallbackFilename
  );
  const contentSha256 = response.headers.get("x-content-sha256");
  const hiddenScopes = parseHiddenScopesHeader(
    response.headers.get("x-cadence-hidden-scopes")
  );
  return { blob, filename, contentType, contentSha256, hiddenScopes };
}

/** Triggers a browser file download from an in-memory streamed Blob. */
export function triggerBrowserDownload(blob: Blob, filename: string): void {
  if (
    typeof document === "undefined" ||
    typeof URL.createObjectURL !== "function"
  ) {
    return;
  }
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
