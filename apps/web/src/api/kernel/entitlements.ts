import { unwrapResponse } from "../client";
import { kernelClient, type KernelSchemas } from "./shared";

const loadRouteTransport = () => import("./protocolImport");

export type KernelEntitlements =
  KernelSchemas["Kernel_KernelEntitlementsResponse"];
export type KernelSubscriptionScoping =
  KernelSchemas["Kernel_KernelSubscriptionScopingResponse"];
export type KernelTrialOnboardingRequest =
  KernelSchemas["Kernel_KernelTrialOnboardingRequest"];
export type KernelTrialOnboardingResponse =
  KernelSchemas["Kernel_KernelTrialOnboardingResponse"];
export type KernelUpgradeSubscriptionRequest =
  KernelSchemas["Kernel_KernelUpgradeSubscriptionRequest"];
export type KernelActivateSubscriptionRequest =
  KernelSchemas["Kernel_KernelActivateSubscriptionRequest"];

export const kernelEntitlementsApi = {
  /** Reads the caller's resolved kernel tenant commercial subscription tier and enabled product families. */
  entitlements(): Promise<KernelEntitlements> {
    return unwrapResponse(kernelClient.GET("/api/v1/kernel/entitlements"));
  },

  /** Provisions or idempotently replays the authenticated OIDC caller's self-service trial workspace. */
  async bootstrapTrial(
    request: KernelTrialOnboardingRequest,
    options?: { idempotencyKey?: string; reason?: string }
  ): Promise<KernelTrialOnboardingResponse> {
    const { executeBootstrapTrial } = await loadRouteTransport();
    return executeBootstrapTrial(request, options);
  },

  /** Switches the caller's tenant commercial subscription tier in place while preserving an active trial window (POST /api/v1/kernel/entitlements/tier). */
  async upgradeSubscriptionTier(input: {
    tier: string;
    idempotencyKey: string;
    reason?: string;
  }): Promise<KernelEntitlements> {
    const { executeUpgradeSubscriptionTier } = await loadRouteTransport();
    return executeUpgradeSubscriptionTier(input);
  },

  /** Activates or upgrades the caller's tenant commercial subscription with validated study scoping and billing confirmation (POST /api/v1/kernel/entitlements/activate). */
  async activateSubscription(input: {
    tier: string;
    studyPhase?: string | null;
    studyDurationMonths?: number | null;
    countries?: string[] | null;
    siteCount?: number | null;
    participantCount?: number | null;
    activeStudyCount?: number | null;
    billingConfirmation?: string | null;
    idempotencyKey: string;
    reason?: string;
  }): Promise<KernelEntitlements> {
    const { executeActivateSubscription } = await loadRouteTransport();
    return executeActivateSubscription(input);
  },
};
