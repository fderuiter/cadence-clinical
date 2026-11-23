import { demoNowMs, nextDemoStep } from "../../utils/demoMode.js";
import { unwrapResponse } from "../client";
import { kernelClient, mutationHeaders, type KernelSchemas } from "./shared";

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
  bootstrapTrial(
    request: KernelTrialOnboardingRequest,
    options?: { idempotencyKey?: string; reason?: string }
  ): Promise<KernelTrialOnboardingResponse> {
    const idempotencyKey =
      options?.idempotencyKey ||
      `web-trial-bootstrap-${demoNowMs(nextDemoStep("trial-bootstrap"))}`;
    const reason =
      options?.reason ||
      `Bootstrap self-service ${request.tier || "Individual"} trial workspace`;
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/onboarding/trial", {
        headers: mutationHeaders(reason, idempotencyKey),
        body: request,
      })
    );
  },

  /** Switches the caller's tenant commercial subscription tier in place while preserving an active trial window (POST /api/v1/kernel/entitlements/tier). */
  upgradeSubscriptionTier({
    tier,
    idempotencyKey,
    reason = "Update commercial subscription tier in workspace",
  }: {
    tier: string;
    idempotencyKey: string;
    reason?: string;
  }): Promise<KernelEntitlements> {
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/entitlements/tier", {
        headers: mutationHeaders(reason, idempotencyKey),
        body: { tier },
      })
    );
  },

  /** Activates or upgrades the caller's tenant commercial subscription with validated study scoping and billing confirmation (POST /api/v1/kernel/entitlements/activate). */
  activateSubscription({
    tier,
    studyPhase,
    studyDurationMonths,
    countries,
    siteCount,
    participantCount,
    activeStudyCount,
    billingConfirmation,
    idempotencyKey,
    reason = "Activate scoped commercial subscription in workspace",
  }: {
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
    return unwrapResponse(
      kernelClient.POST("/api/v1/kernel/entitlements/activate", {
        headers: mutationHeaders(reason, idempotencyKey),
        body: {
          tier,
          study_phase: studyPhase ?? null,
          study_duration_months: studyDurationMonths ?? null,
          countries: countries ?? null,
          site_count: siteCount ?? null,
          participant_count: participantCount ?? null,
          active_study_count: activeStudyCount ?? null,
          billing_confirmation: billingConfirmation ?? null,
        },
      })
    );
  },
};
