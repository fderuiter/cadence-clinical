/**
 * Backward-compatible facade for the Clinical Kernel API client, generated from `docs/openapi/kernel_openapi.json`
 * (ADR-2540) and organized into capability slices under `src/api/kernel/*.ts`:
 *
 * - `kernel/navigation.ts`: graph reads, zoom path, children, commands, authority (`canI`), search, enrollment lookup, lineage, activity
 * - `kernel/studies.ts`: studies, organizations, sites, study-site links, protocol imports/versions/SoA, amendment impact, simulation
 * - `kernel/observations.ts`: observations, corrections, source data verifications, assertion retractions
 * - `kernel/proposals.ts`: AI/workflow proposals and rejections
 * - `kernel/locks.ts`: Frozen/Locked study assertions, lock readiness, and bitemporal study/casebook exports
 * - `kernel/validation.ts`: standards library items, rule packs, and validation findings
 * - `kernel/entitlements.ts`: commercial entitlements and self-service trial onboarding
 * - `kernel/shared.ts`: shared OpenAPI client, ProblemDetails error helpers, and download utilities
 *
 * Add new capability methods and types in the matching `src/api/kernel/<slice>.ts` module rather than growing this
 * barrel directly.
 */
import { kernelEntitlementsApi } from "./kernel/entitlements";
import { kernelLocksApi } from "./kernel/locks";
import { kernelNavigationApi } from "./kernel/navigation";
import { kernelObservationsApi } from "./kernel/observations";
import { kernelProposalsApi } from "./kernel/proposals";
import { kernelStudiesApi } from "./kernel/studies";
import { kernelValidationApi } from "./kernel/validation";

export {
  ASSERTION_ALREADY_LOCKED,
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_LOCKED,
  ASSERTION_REVISION_STALE,
  ASSERTION_SCOPE_NOT_READY,
  assertionErrorCode,
  describeKernelError,
  extractKernelExplanation,
  extractKernelFieldErrors,
  isMasked,
  isSubscriptionExpiredError,
  KERNEL_AUTHORIZATION_DENIED,
  KERNEL_COMMERCIAL_ONBOARDING_CALLER_INVALID,
  KERNEL_COMMERCIAL_PRODUCT_FAMILY_NOT_ENTITLED,
  KERNEL_COMMERCIAL_QUOTE_INVALID,
  KERNEL_COMMERCIAL_SCOPING_CAP_EXCEEDED,
  KERNEL_COMMERCIAL_SCOPING_INVALID,
  KERNEL_SUBSCRIPTION_EXPIRED,
  SUBSCRIPTION_EXPIRED_GUIDANCE,
  triggerBrowserDownload,
  type KernelLineageWindow,
  type KernelStreamedArtifact,
} from "./kernel/shared";

export * from "./kernel/navigation";
export * from "./kernel/studies";
export * from "./kernel/observations";
export * from "./kernel/proposals";
export * from "./kernel/locks";
export * from "./kernel/validation";
export * from "./kernel/entitlements";

export const kernelApi = {
  ...kernelObservationsApi,
  ...kernelNavigationApi,
  ...kernelStudiesApi,
  ...kernelProposalsApi,
  ...kernelEntitlementsApi,
  ...kernelLocksApi,
  ...kernelValidationApi,
};
