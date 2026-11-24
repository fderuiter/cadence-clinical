import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  useClinicalStore,
  getStorageKey,
  readStorageItem,
} from "../../src/stores/clinical";
import {
  useOnboardingStore,
  getOnboardingStorageKey,
} from "../../src/stores/onboarding";
import { useAuthStore, purgeLegacyUnscopedKeys } from "../../src/stores/auth";

describe("Strict Persona-Scoped Namespacing & Lifecycle Rehydration", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
      window.sessionStorage.clear();
    }
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  describe("Clinical Store Key Resolution & Fallback Removal", () => {
    function signInAs(userId: string, tenantId: string) {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.user = { id: userId, tenantId } as never;
      return authStore;
    }

    it("resolves getStorageKey under the signed-in tenant, user and persona", () => {
      signInAs("user-a", "tenant-a");

      const resolvedKey = getStorageKey("subjects", "site_crc");
      expect(resolvedKey).toBe(
        "cadence_user:tenant-a:user-a:clinical:site_crc:subjects"
      );
    });

    it("has no storage key, and reads nothing, without a signed-in tenant and user (#5369)", () => {
      expect(getStorageKey("subjects", "site_crc")).toBeNull();

      const authStore = signInAs("user-a", "");
      expect(getStorageKey("subjects", "site_crc")).toBeNull();
      authStore.user = { id: "", tenantId: "tenant-a" } as never;
      expect(getStorageKey("subjects", "site_crc")).toBeNull();
    });

    it("strictly reads from the scoped key and returns null if it is missing even if unscoped keys exist", () => {
      signInAs("user-a", "tenant-a");
      window.localStorage.setItem(
        "formValues",
        JSON.stringify({ field1: "unscoped_value" })
      );
      window.localStorage.setItem(
        "clinical_auditor_formValues",
        JSON.stringify({ field1: "persona_only_value" })
      );

      // Does not fall back to an unscoped or persona-only key or migrate it
      const val = readStorageItem("formValues", "auditor");
      expect(val).toBeNull();
      expect(
        window.localStorage.getItem(getStorageKey("formValues", "auditor")!)
      ).toBeNull();
    });

    it("ensures coordinator state edits do not appear in auditor state store", () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const authStore = useAuthStore();
      authStore.startDemoSession(); // signs in as super_admin initially

      // Switch to site_crc (Coordinator)
      authStore.switchPersona("site_crc");
      const clinicalStore = useClinicalStore();

      // Coordinator modifies eCRF form values
      clinicalStore.formValues["VS_SYSBP"] = 135;
      clinicalStore.formValues["VS_DIABP"] = 85;

      // Save persistent fields (which writes under clinical_site_crc_*)
      window.localStorage.setItem(
        getStorageKey("formValues", "site_crc")!,
        JSON.stringify(clinicalStore.formValues)
      );

      // Switch to auditor (Auditor view)
      authStore.switchPersona("auditor");

      // Auditor store should not see coordinator's edits
      expect(clinicalStore.formValues["VS_SYSBP"]).not.toBe(135);
      expect(clinicalStore.formValues["VS_SYSBP"]).toBeUndefined();
      expect(readStorageItem("formValues", "auditor")).toBeNull();
    });
  });

  describe("Onboarding Store Scoping & Removal of Global Keys", () => {
    it("does not read or write global or unscoped onboarding keys (onboarding_global_* / onboarding_<persona>_*)", () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.user = { id: "user-a", tenantId: "tenant-a" } as never;
      authStore.rawRoles = ["Sponsor Admin"];
      authStore.currentPersona = "site_crc";

      const onboardingStore = useOnboardingStore();
      onboardingStore.loadPersonaState("site_crc");

      // Pre-start tour in monitor persona so we can verify Coordinator disabling does not touch it
      authStore.switchPersona("cra_monitor");
      expect(onboardingStore.isActive).toBe(false); // Opt-in on initial visit
      onboardingStore.startTour();
      expect(onboardingStore.isActive).toBe(true);

      // Switch back to Coordinator and disable tour
      authStore.switchPersona("site_crc");
      onboardingStore.disableTour();

      // Verify no global or legacy unscoped keys were written
      expect(
        window.localStorage.getItem("onboarding_global_disabled")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_global_dismissed")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_site_crc_disabled")
      ).toBeNull();

      // Verify account-scoped persona key WAS written
      expect(
        window.localStorage.getItem(
          getOnboardingStorageKey("disabled", "site_crc")!
        )
      ).toBe("true");

      // Switch to monitor persona
      authStore.switchPersona("cra_monitor");

      // Monitor view should retain its own active tour state
      expect(onboardingStore.disabled).toBe(false);
      expect(onboardingStore.isActive).toBe(true);
    });

    it("retains role-appropriate tour progress when toggling workspace views", () => {
      const authStore = useAuthStore();
      authStore.rawRoles = ["Sponsor Admin"];
      authStore.currentPersona = "site_crc";

      const onboardingStore = useOnboardingStore();
      onboardingStore.loadPersonaState("site_crc");

      // Coordinator advances to step 3
      onboardingStore.nextStep("Step 1 done");
      onboardingStore.nextStep("Step 2 done");
      expect(onboardingStore.currentStep).toBe(3);

      // Switch to auditor persona
      authStore.switchPersona("auditor");
      expect(onboardingStore.currentStep).toBe(1);

      // Auditor advances to step 2
      onboardingStore.nextStep("Auditor step 1 done");
      expect(onboardingStore.currentStep).toBe(2);

      // Switch back to site_crc persona
      authStore.switchPersona("site_crc");
      expect(onboardingStore.currentStep).toBe(3);
    });
  });

  describe("Bootstrap Legacy Unscoped Key Purging", () => {
    it("automatically purges legacy unscoped and unowned onboarding_* keys while preserving account-scoped onboarding keys", () => {
      // Seed legacy unscoped keys
      window.localStorage.setItem(
        "subjects",
        JSON.stringify([{ id: "LEGACY-1" }])
      );
      window.localStorage.setItem(
        "formValues",
        JSON.stringify({ old: "data" })
      );
      window.localStorage.setItem("onboarding_global_disabled", "true");
      window.localStorage.setItem("onboarding_global_dismissed", "true");
      window.localStorage.setItem("onboarding_site_crc_disabled", "true");
      window.localStorage.setItem(
        "cadence_clinical_subjects",
        JSON.stringify([])
      );

      // Seed valid account-scoped onboarding key
      const accountKey =
        "cadence_user:tenant-a:user-a:onboarding:site_crc:disabled";
      window.localStorage.setItem(accountKey, "true");

      // Run bootstrap purge routine
      purgeLegacyUnscopedKeys();

      // Legacy unscoped and unowned onboarding_* keys must be purged
      expect(window.localStorage.getItem("subjects")).toBeNull();
      expect(window.localStorage.getItem("formValues")).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_global_disabled")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_global_dismissed")
      ).toBeNull();
      expect(
        window.localStorage.getItem("onboarding_site_crc_disabled")
      ).toBeNull();
      expect(
        window.localStorage.getItem("cadence_clinical_subjects")
      ).toBeNull();

      // Valid account-scoped key must remain intact
      expect(window.localStorage.getItem(accountKey)).toBe("true");
    });
  });
});
