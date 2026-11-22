import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { setKeycloakClient } from "../src/auth/keycloak.js";
import {
  describeAssertionError,
  describeKernelError,
  describeLockError,
  KERNEL_COMMERCIAL_SCOPING_INVALID,
  KERNEL_SUBSCRIPTION_EXPIRED,
  kernelApi,
  SUBSCRIPTION_EXPIRED_GUIDANCE,
} from "../src/api/kernelApi";
import { ApiError, unwrapResponse } from "../src/api/client";
import {
  computeTrialDaysRemaining,
  formatTrialEndDate,
  useAuthStore,
} from "../src/stores/auth";
import { isScreenAvailable, screenMetadata } from "../src/router/availability";
import AppShell from "../src/components/AppShell.vue";
import LoginView from "../src/views/LoginView.vue";
import OverviewView from "../src/views/OverviewView.vue";
import PlansView from "../src/views/PlansView.vue";
import TrialSetupView from "../src/views/TrialSetupView.vue";
import UnavailableView from "../src/views/UnavailableView.vue";

const SAMPLE_TRIAL_END_ISO = new Date(Date.UTC(2030, 0, 15)).toISOString();

describe("Keycloak self-registration, /plans catalog, and kernel trial bootstrap", () => {
  beforeEach(async () => {
    window.localStorage.clear();
    setActivePinia(createPinia());
    setKeycloakClient(null);
    await router.push("/login");
  });

  afterEach(() => {
    setKeycloakClient(null);
    vi.restoreAllMocks();
  });

  it("lets signed-out visitors open /plans without authentication and renders Individual, Select, and Complete tiers without fixed prices or prohibited marketing claims", async () => {
    await router.push("/plans");
    await vi.dynamicImportSettled();
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/plans");

    const wrapper = mount(PlansView, { global: { plugins: [router] } });
    await flushPromises();

    const text = wrapper.text();
    expect(text).toContain("Individual");
    expect(text).toContain("Select");
    expect(text).toContain("Complete");
    expect(text).toContain("Blueprint");
    expect(text).toContain("Capture");
    expect(text).toContain("Castor");
    expect(text).toContain("Pre-release unvalidated environment");

    // No fixed dollar/euro prices or card/invoice fields
    expect(text).not.toMatch(/\$\d+|€\d+|credit card|invoice/i);
    // Prohibited claims guard (PRODUCT_CATALOG.md / MARKET_HYPOTHESES.md)
    expect(text).not.toMatch(/\b(first|only|free)\b/i);
    expect(text).not.toMatch(/fully compliant|FDA approved/i);

    await expect(wrapper).toBeAccessible();
    wrapper.unmount();
  });

  it("exposes /plans and Keycloak PKCE S256 registration from LoginView, /plans, and AppShell when signed out", async () => {
    const register = vi.fn().mockResolvedValue(undefined);
    setKeycloakClient({
      login: vi.fn().mockResolvedValue(undefined),
      register,
    });

    const loginWrapper = mount(LoginView, { global: { plugins: [router] } });
    expect(loginWrapper.find("#link-login-plans").attributes("href")).toBe(
      "/plans"
    );
    await loginWrapper.find("#btn-login-register").trigger("click");
    await flushPromises();
    expect(register).toHaveBeenCalledWith(
      expect.objectContaining({
        pkceMethod: "S256",
        redirectUri: expect.stringContaining("/onboarding/trial"),
      })
    );
    loginWrapper.unmount();

    register.mockClear();
    await router.push("/plans");
    const plansWrapper = mount(PlansView, { global: { plugins: [router] } });
    await plansWrapper.find("#btn-select-tier-select").trigger("click");
    await flushPromises();
    expect(register).toHaveBeenCalledWith(
      expect.objectContaining({
        pkceMethod: "S256",
        redirectUri: expect.stringContaining("/onboarding/trial?tier=Select"),
      })
    );
    plansWrapper.unmount();

    register.mockClear();
    const shellWrapper = mount(AppShell, { global: { plugins: [router] } });
    expect(shellWrapper.find("#nav-public-plans").attributes("href")).toBe(
      "/plans"
    );
    await shellWrapper.find("#btn-register").trigger("click");
    await flushPromises();
    expect(register).toHaveBeenCalledWith(
      expect.objectContaining({
        pkceMethod: "S256",
      })
    );
    shellWrapper.unmount();
  });

  it("routes a newly self-registered OIDC user without tenant_id to /onboarding/trial, bootstraps the workspace, refreshes the token, and navigates to /studies", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.accessToken = "initial-token-without-tenant";
    authStore.user = {
      id: "4e999999-1111-4444-8888-111111111111",
      username: "founder@meridian-research.example",
      tenantId: null,
    };
    authStore.rawRoles = [];

    expect(authStore.needsTrialOnboarding).toBe(true);

    // Navigating to /studies before provisioning redirects to /onboarding/trial
    await router.push("/studies");
    await vi.dynamicImportSettled();
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/onboarding/trial");

    const entitlementsPayload = {
      tenant_ref: "tenant:01960000-0000-7000-8000-000000000001",
      tenant_id: "01960000-0000-7000-8000-000000000001",
      subscription_ref: "subscription:01960000-0000-7000-8000-000000000009",
      tier: "Individual",
      status: "Trialing",
      started_at: SAMPLE_TRIAL_END_ISO,
      trial_ends_at: SAMPLE_TRIAL_END_ISO,
      product_families: ["Blueprint", "Capture"],
      is_read_only: false,
      baseline_capabilities: [
        "audit_trail",
        "electronic_signatures",
        "authorization",
        "record_export",
      ],
      environment_notice: "Pre-release unvalidated environment",
    };

    const bootstrapSpy = vi
      .spyOn(kernelApi, "bootstrapTrial")
      .mockResolvedValue({
        tenant_id: "01960000-0000-7000-8000-000000000001",
        tenant_code: "TRIAL-4E9999991111",
        organization_id: "01960000-0000-7000-8000-000000000002",
        organization_code: "ORG-4E9999991111",
        person_id: "01960000-0000-7000-8000-000000000003",
        assigned_roles: ["Sponsor Admin", "Sponsor Designer"],
        already_provisioned: false,
        entitlements: entitlementsPayload,
      });

    const entitlementsSpy = vi
      .spyOn(kernelApi, "entitlements")
      .mockResolvedValue(entitlementsPayload);

    const refreshSpy = vi
      .spyOn(authStore, "refresh")
      .mockImplementation(async () => {
        authStore.accessToken = "refreshed-token-with-tenant";
        authStore.user = {
          ...authStore.user,
          tenantId: "TRIAL-4E9999991111",
        };
        authStore.rawRoles = ["Sponsor Admin", "Sponsor Designer"];
        return true;
      });

    const wrapper = mount(TrialSetupView, { global: { plugins: [router] } });
    await expect(wrapper).toBeAccessible();

    await wrapper
      .find("#input-organization-name")
      .setValue("Meridian Vascular Research");
    await wrapper.find("#select-residency").setValue("EU");
    await wrapper.find("#form-trial-onboarding").trigger("submit");
    await flushPromises();
    await vi.dynamicImportSettled();
    await flushPromises();

    expect(bootstrapSpy).toHaveBeenCalledWith({
      organization_name: "Meridian Vascular Research",
      residency: "EU",
      tier: "Individual",
    });
    expect(refreshSpy).toHaveBeenCalledWith(-1);
    expect(entitlementsSpy).toHaveBeenCalled();
    expect(authStore.needsTrialOnboarding).toBe(false);
    expect(router.currentRoute.value.path).toBe("/studies");
    wrapper.unmount();
  });

  it("displays active plan and expired read/export badges in AppShell and OverviewView", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.user = {
      id: "4e999999-1111-4444-8888-222222222222",
      username: "sponsor_admin",
      tenantId: "TRIAL-DEMO",
    };
    authStore.rawRoles = ["Sponsor Admin"];
    authStore.entitlementsLoaded = true;
    authStore.entitlements = {
      tenant_id: "01960000-0000-7000-8000-000000000001",
      tier: "Individual",
      status: "Trialing",
      trial_ends_at: SAMPLE_TRIAL_END_ISO,
      product_families: ["Blueprint", "Capture"],
      is_read_only: false,
      baseline_capabilities: ["audit_trail"],
    };

    const shell = mount(AppShell, {
      global: { plugins: [router] },
      slots: { default: OverviewView },
    });
    await flushPromises();

    const expectedDays = computeTrialDaysRemaining(SAMPLE_TRIAL_END_ISO);
    const expectedDate = formatTrialEndDate(SAMPLE_TRIAL_END_ISO);
    const expectedTrialBadge = `Individual · Trial · ${expectedDays} ${expectedDays === 1 ? "day" : "days"} left (ends ${expectedDate})`;

    expect(shell.find("#shell-plan-badge").text()).toBe(expectedTrialBadge);
    expect(shell.find("#overview-active-plan-badge").text()).toBe(
      expectedTrialBadge
    );
    expect(shell.find("#overview-trial-countdown").text()).toContain(
      `${expectedDays} ${expectedDays === 1 ? "day" : "days"} remaining`
    );
    expect(shell.find("#overview-trial-countdown").text()).toContain(
      expectedDate
    );
    expect(shell.find("#overview-plan-summary").text()).toContain(
      "Blueprint, Capture"
    );
    expect(shell.find("#shell-expired-subscription-banner").exists()).toBe(
      false
    );

    // Expired trial preserves read/export indicator and renders shell + overview notices
    authStore.entitlements = {
      ...authStore.entitlements,
      status: "Expired",
      is_read_only: true,
    };
    await flushPromises();

    expect(shell.find("#shell-plan-badge").text()).toBe(
      "Individual · Trial Expired (Read & Export)"
    );
    expect(shell.find("#overview-active-plan-badge").text()).toBe(
      "Individual · Trial Expired (Read & Export)"
    );
    expect(shell.find("#shell-expired-subscription-banner").exists()).toBe(
      true
    );
    expect(shell.find("#overview-expired-notice").text()).toContain(
      "clinical reads, regulated exports, and Part 11 signatures remain enabled"
    );
    shell.unmount();
  });

  it("switches trial tiers on PlansView while preserving Trialing status and countdown, disables expired tier buttons, and validates scoped activation", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.user = {
      id: "4e999999-1111-4444-8888-333333333333",
      username: "sponsor_admin",
      tenantId: "TRIAL-SCOPED",
    };
    authStore.rawRoles = ["Sponsor Admin"];
    authStore.applyEntitlements({
      tenant_ref: "tenant:01960000-0000-7000-8000-000000000011",
      tenant_id: "01960000-0000-7000-8000-000000000011",
      subscription_ref: "subscription:01960000-0000-7000-8000-000000000012",
      tier: "Individual",
      status: "Trialing",
      started_at: "2026-11-01T00:00:00Z",
      trial_ends_at: SAMPLE_TRIAL_END_ISO,
      product_families: ["Blueprint", "Capture"],
      is_read_only: false,
      baseline_capabilities: [
        "audit_trail",
        "electronic_signatures",
        "authorization",
        "record_export",
      ],
      environment_notice: "Pre-release unvalidated environment",
    });

    const switchSpy = vi
      .spyOn(kernelApi, "upgradeSubscriptionTier")
      .mockResolvedValue({
        ...authStore.entitlements,
        tier: "Select",
        status: "Trialing",
        trial_ends_at: SAMPLE_TRIAL_END_ISO,
        product_families: [
          "Blueprint",
          "Capture",
          "Orbit",
          "Engage",
          "Allocate",
        ],
      });

    await router.push("/plans");
    await flushPromises();
    const wrapper = mount(PlansView, { global: { plugins: [router] } });
    await flushPromises();

    // Active trial countdown banner is rendered with exact days remaining and end date
    const expectedDays = computeTrialDaysRemaining(SAMPLE_TRIAL_END_ISO);
    const expectedDate = formatTrialEndDate(SAMPLE_TRIAL_END_ISO);
    expect(wrapper.find("#plans-trial-status-banner").text()).toContain(
      `${expectedDays} ${expectedDays === 1 ? "day" : "days"} left (ends ${expectedDate})`
    );

    // Switching tier during active trial preserves Trialing status and original trial_ends_at
    await wrapper.find("#btn-select-tier-select").trigger("click");
    await flushPromises();
    expect(switchSpy).toHaveBeenCalledWith(
      expect.objectContaining({ tier: "Select" })
    );
    expect(authStore.subscriptionStatus).toBe("Trialing");
    expect(authStore.trialEndsAt).toBe(SAMPLE_TRIAL_END_ISO);

    // Expire workspace: direct tier switching buttons become disabled and expired notice appears
    authStore.applyEntitlements({
      ...authStore.entitlements,
      status: "Expired",
      is_read_only: true,
    });
    await flushPromises();

    expect(wrapper.find("#plans-expired-banner").exists()).toBe(true);
    expect(
      wrapper.find("#btn-select-tier-individual").attributes("disabled")
    ).toBeDefined();
    expect(
      wrapper.find("#btn-select-tier-select").attributes("disabled")
    ).toBeDefined();
    expect(
      wrapper.find("#btn-select-tier-complete").attributes("disabled")
    ).toBeDefined();

    // Prohibited marketing/pricing terms and axe accessibility still pass in expired + scoping state
    const expiredText = wrapper.text();
    expect(expiredText).not.toMatch(/\$\d+|€\d+|credit card|invoice/i);
    expect(expiredText).not.toMatch(/\b(first|only|free)\b/i);
    expect(expiredText).not.toMatch(/fully compliant|FDA approved/i);
    await expect(wrapper).toBeAccessible();

    // Submitting scoped activation without required fields surfaces field errors (422 K.Commercial.ScopingInvalid)
    const activateSpy = vi
      .spyOn(kernelApi, "activateSubscription")
      .mockRejectedValueOnce(
        new ApiError(
          "Study scoping details required for subscription activation",
          422,
          "Unprocessable Entity",
          {
            code: KERNEL_COMMERCIAL_SCOPING_INVALID,
            detail:
              "Activating a commercial subscription requires valid study scoping.",
            errors: {
              study_phase: ["Study phase is required."],
              billing_confirmation: [
                "A purchase order or signed quote reference is required.",
              ],
            },
          }
        )
      )
      .mockResolvedValueOnce({
        ...authStore.entitlements,
        tier: "Complete",
        status: "Active",
        is_read_only: false,
        product_families: [
          "Blueprint",
          "Capture",
          "Orbit",
          "Engage",
          "Allocate",
          "Vigil",
          "Lens",
          "Assure",
        ],
        scoping: {
          study_phase: "Phase II",
          study_duration_months: 18,
          countries: ["US", "DE"],
          site_count: 10,
          participant_count: 180,
          active_study_count: 2,
          billing_confirmation: "PO-2026-9910",
        },
      });

    await wrapper.find("#form-scoped-activation").trigger("submit");
    await flushPromises();

    expect(wrapper.find("#error-study-phase").text()).toContain(
      "Study phase is required."
    );
    expect(wrapper.find("#error-billing-confirmation").text()).toContain(
      "purchase order or signed quote reference is required"
    );

    // Fill all required scoping fields + PO reference and activate
    await wrapper.find("#select-activation-tier").setValue("Complete");
    await wrapper.find("#select-study-phase").setValue("Phase II");
    await wrapper.find("#input-study-duration-months").setValue("18");
    await wrapper.find("#input-study-countries").setValue("US, DE");
    await wrapper.find("#input-site-count").setValue("10");
    await wrapper.find("#input-participant-count").setValue("180");
    await wrapper.find("#input-active-study-count").setValue("2");
    await wrapper.find("#input-billing-confirmation").setValue("PO-2026-9910");
    await wrapper.find("#form-scoped-activation").trigger("submit");
    await flushPromises();

    expect(activateSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({
        tier: "Complete",
        studyPhase: "Phase II",
        studyDurationMonths: 18,
        countries: ["US", "DE"],
        siteCount: 10,
        participantCount: 180,
        activeStudyCount: 2,
        billingConfirmation: "PO-2026-9910",
      })
    );
    expect(authStore.subscriptionStatus).toBe("Active");
    expect(authStore.isTrialExpired).toBe(false);
    expect(wrapper.find("#plans-expired-banner").exists()).toBe(false);
    expect(wrapper.find("#scoped-activation-success").text()).toContain(
      "Activated Complete subscription"
    );
    wrapper.unmount();
  });

  it("syncs expired subscription state and returns actionable upgrade guidance when mutations fail with KERNEL_SUBSCRIPTION_EXPIRED", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.user = {
      id: "4e999999-1111-4444-8888-444444444444",
      username: "crc_user",
      tenantId: "TRIAL-EXP",
    };
    authStore.applyEntitlements({
      tenant_ref: "tenant:01960000-0000-7000-8000-000000000021",
      tenant_id: "01960000-0000-7000-8000-000000000021",
      subscription_ref: "subscription:01960000-0000-7000-8000-000000000022",
      tier: "Individual",
      status: "Trialing",
      started_at: "2026-10-01T00:00:00Z",
      trial_ends_at: SAMPLE_TRIAL_END_ISO,
      product_families: ["Blueprint", "Capture"],
      is_read_only: false,
      baseline_capabilities: ["audit_trail"],
      environment_notice: "Pre-release unvalidated environment",
    });

    expect(authStore.isTrialExpired).toBe(false);

    const problemPayload = {
      type: "https://cadence.clinical/errors/K.Subscription.Expired",
      title: "Trial subscription expired",
      status: 403,
      code: KERNEL_SUBSCRIPTION_EXPIRED,
      detail:
        "This tenant's trial subscription has expired. Read and export access remains active.",
    };

    await expect(
      unwrapResponse(
        Promise.resolve({
          error: problemPayload,
          response: new Response(JSON.stringify(problemPayload), {
            status: 403,
            statusText: "Forbidden",
          }),
        })
      )
    ).rejects.toThrow(SUBSCRIPTION_EXPIRED_GUIDANCE);

    expect(authStore.isTrialExpired).toBe(true);
    expect(authStore.subscriptionStatus).toBe("Expired");
    expect(authStore.subscriptionExpiredNotice).toBeTruthy();

    const apiErr = new ApiError("Forbidden", 403, "Forbidden", problemPayload);
    expect(describeKernelError(apiErr)).toBe(SUBSCRIPTION_EXPIRED_GUIDANCE);
    expect(describeLockError(apiErr)).toBe(SUBSCRIPTION_EXPIRED_GUIDANCE);
    expect(describeAssertionError(apiErr)).toBe(SUBSCRIPTION_EXPIRED_GUIDANCE);
  });

  it("maps every hidden route to a product family and roadmap stage with zero #4829 links", () => {
    const hiddenRoutes = router
      .getRoutes()
      .map((r) => r.name)
      .filter((name) => name && !isScreenAvailable(name));

    expect(hiddenRoutes.length).toBeGreaterThan(0);
    for (const routeName of hiddenRoutes) {
      const meta = screenMetadata(String(routeName));
      expect(meta.title).toBeTruthy();
      expect(meta.family).toBeTruthy();
      expect(meta.roadmapStage).toBeTruthy();

      const wrapper = mount(UnavailableView, { global: { plugins: [router] } });
      expect(wrapper.html()).not.toContain("4829");
      wrapper.unmount();
    }
  });
});
