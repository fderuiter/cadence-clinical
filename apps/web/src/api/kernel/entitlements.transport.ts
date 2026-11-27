import { demoNowMs, nextDemoStep } from "../../utils/demoMode.js";
import { unwrapResponse } from "../client";
import type {
  KernelEntitlements,
  KernelTrialOnboardingRequest,
  KernelTrialOnboardingResponse,
} from "./entitlements";
import { kernelClient, mutationHeaders } from "./shared";

export function executeBootstrapTrial(
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
}

export function executeUpgradeSubscriptionTier({
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
}

export function executeActivateSubscription({
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
}
