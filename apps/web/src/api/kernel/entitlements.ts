import { unwrapResponse } from "../client";
import { kernelClient, lazyKernelAction, type KernelSchemas } from "./shared";

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

function requestKernelEntitlements(): Promise<KernelEntitlements> {
  return unwrapResponse(kernelClient.GET("/api/v1/kernel/entitlements"));
}

type EntitlementsFacade = {
  entitlements: () => Promise<KernelEntitlements>;
};

let boundEntitlementsFacade: EntitlementsFacade | null = null;
let delegatingToFacade = false;

export function bindKernelEntitlementsFacade(facade: EntitlementsFacade): void {
  boundEntitlementsFacade = facade;
}

export const kernelEntitlementsApi = {
  /** Reads the caller's resolved kernel tenant commercial subscription tier and enabled product families. */
  entitlements(): Promise<KernelEntitlements> {
    if (
      !delegatingToFacade &&
      boundEntitlementsFacade &&
      boundEntitlementsFacade.entitlements !==
        kernelEntitlementsApi.entitlements
    ) {
      delegatingToFacade = true;
      try {
        return boundEntitlementsFacade.entitlements();
      } finally {
        delegatingToFacade = false;
      }
    }
    return requestKernelEntitlements();
  },

  /** Provisions or idempotently replays the authenticated OIDC caller's self-service trial workspace. */
  bootstrapTrial: lazyKernelAction("executeBootstrapTrial"),

  /** Switches the caller's tenant commercial subscription tier in place while preserving an active trial window (POST /api/v1/kernel/entitlements/tier). */
  upgradeSubscriptionTier: lazyKernelAction("executeUpgradeSubscriptionTier"),

  /** Activates or upgrades the caller's tenant commercial subscription with validated study scoping and billing confirmation (POST /api/v1/kernel/entitlements/activate). */
  activateSubscription: lazyKernelAction("executeActivateSubscription"),
};
