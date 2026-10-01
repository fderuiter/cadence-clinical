import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  useClinicalStore,
  getStorageKey,
  readStorageItem,
} from "../../src/stores/clinical";
import { useOnboardingStore } from "../../src/stores/onboarding";
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
    it("strictly resolves getStorageKey to clinical_${persona}_${baseKey} even if unscoped key exists", () => {
      window.localStorage.setItem("subjects", JSON.stringify([{ id: "UNSCOPED-1" }]));
      window.localStorage.setItem(
        "clinical_site_crc_subjects",
        JSON.stringify([{ id: "SCOPED-CRC-1" }])
      );

      const resolvedKey = getStorageKey("subjects", "site_crc");
      expect(resolvedKey).toBe("clinical_site_crc_subjects");
    });

    it("strictly reads from persona-scoped key and returns null if persona key is missing even if unscoped key exists", () => {
      window.localStorage.setItem("formValues", JSON.stringify({ field1: "unscoped_value" }));

      // Does not fall back to unscoped baseKey or migrate it
      const val = readStorageItem("formValues", "auditor");
      expect(val).toBeNull();
      expect(window.localStorage.getItem("clinical_auditor_formValues")).toBeNull();
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
        getStorageKey("formValues", "site_crc"),
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
    it("does not read or write global onboarding keys (onboarding_global_disabled / onboarding_global_dismissed)", () => {
      const authStore = useAuthStore();
      authStore.rawRoles = ["Sponsor Admin"];
      authStore.currentPersona = "site_crc";

      const onboardingStore = useOnboardingStore();
      onboardingStore.loadPersonaState("site_crc");

      // Coordinator disables tour
      onboardingStore.disableTour();

      // Verify no global keys were written
      expect(window.localStorage.getItem("onboarding_global_disabled")).toBeNull();
      expect(window.localStorage.getItem("onboarding_global_dismissed")).toBeNull();

      // Verify persona key WAS written
      expect(window.localStorage.getItem("onboarding_site_crc_disabled")).toBe("true");

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
    it("automatically purges legacy unscoped keys upon user sign-in while preserving persona-scoped keys", () => {
      // Seed legacy unscoped keys
      window.localStorage.setItem("subjects", JSON.stringify([{ id: "LEGACY-1" }]));
      window.localStorage.setItem("formValues", JSON.stringify({ old: "data" }));
      window.localStorage.setItem("onboarding_global_disabled", "true");
      window.localStorage.setItem("onboarding_global_dismissed", "true");
      window.localStorage.setItem("cadence_clinical_subjects", JSON.stringify([]));

      // Seed valid persona-scoped key
      window.localStorage.setItem(
        "clinical_site_crc_subjects",
        JSON.stringify([{ id: "VALID-CRC-1" }])
      );

      // Run bootstrap purge routine
      purgeLegacyUnscopedKeys();

      // Legacy unscoped keys must be purged
      expect(window.localStorage.getItem("subjects")).toBeNull();
      expect(window.localStorage.getItem("formValues")).toBeNull();
      expect(window.localStorage.getItem("onboarding_global_disabled")).toBeNull();
      expect(window.localStorage.getItem("onboarding_global_dismissed")).toBeNull();
      expect(window.localStorage.getItem("cadence_clinical_subjects")).toBeNull();

      // Valid persona-scoped key must remain intact
      expect(window.localStorage.getItem("clinical_site_crc_subjects")).not.toBeNull();
      expect(
        JSON.parse(window.localStorage.getItem("clinical_site_crc_subjects")!)
      ).toEqual([{ id: "VALID-CRC-1" }]);
    });
  });
});
