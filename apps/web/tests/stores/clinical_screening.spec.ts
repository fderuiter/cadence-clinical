import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../../src/stores/clinical";
import { useAuthStore } from "../../src/stores/auth";
import { useToastStore } from "../../src/stores/toasts.js";

describe("Clinical Store & Auth Store - Subject Screening & CRC Gating Specification", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
    }
  });

  it("authStore.hasRole and authStore.isCrc resolve CRC and investigator roles correctly", () => {
    const authStore = useAuthStore();

    // Set CRC role
    authStore.rawRoles = ["Site Investigator"];
    expect(authStore.isCrc).toBe(true);
    expect(authStore.hasRole("crc")).toBe(true);

    // Set Monitor role
    authStore.rawRoles = ["CRA", "Monitor"];
    expect(authStore.isCrc).toBe(false);
    expect(authStore.hasRole("cra")).toBe(true);

    // Set Super Admin
    authStore.rawRoles = ["Super Admin"];
    expect(authStore.isCrc).toBe(true);
    expect(authStore.hasRole("sponsor_designer")).toBe(true);
  });

  it("clinicalStore.subjectIds exposes reactive array of all subject IDs", () => {
    const store = useClinicalStore();
    store.subjects = [
      {
        id: "SUBJ-001",
        status: "ENROLLED",
        active_protocol_version: "1.0.0",
        isGated: false,
      },
      {
        id: "SUBJ-002",
        status: "ENROLLED",
        active_protocol_version: "1.0.0",
        isGated: true,
      },
    ];
    expect(store.subjectIds).toBeDefined();
    expect(store.subjectIds).toContain("SUBJ-001");
    expect(store.subjectIds).toContain("SUBJ-002");
  });

  it("clinicalStore.screenSubject refuses and changes nothing, since no service evaluates eligibility (#4523)", () => {
    const store = useClinicalStore();
    const subjects = JSON.stringify(store.subjects);
    const blocks = store.ledgerBlocks.length;

    expect(store.screenSubject()).toBeNull();

    expect(JSON.stringify(store.subjects)).toBe(subjects);
    expect(store.ledgerBlocks).toHaveLength(blocks);
    expect(useToastStore().toasts).toEqual([
      expect.objectContaining({
        tone: "error",
        title: "Screening a subject isn't available yet",
      }),
    ]);
  });

  it("scopes clinical store localStorage keys by active persona and rehydrates state on persona switch", async () => {
    const authStore = useAuthStore();
    authStore.rawRoles = ["Sponsor Admin", "CRA", "Monitor"];
    authStore.currentPersona = "super_admin";

    const clinicalStore = useClinicalStore();

    // 1. Mutate formValues & ledger in super_admin persona
    clinicalStore.formValues.pulse = "99";
    await clinicalStore.addLedgerBlock(
      "TEST_ACTION_SA",
      { pulse: "99" },
      "Super Admin Updated Pulse"
    );

    // Verify localStorage has persona-scoped keys for super_admin
    const saFormValuesKey = "clinical_super_admin_formValues";
    const saLedgerKey = "clinical_super_admin_ledgerBlocks";
    expect(window.localStorage.getItem(saFormValuesKey)).not.toBeNull();
    expect(window.localStorage.getItem(saLedgerKey)).not.toBeNull();

    const saFormValues = JSON.parse(
      window.localStorage.getItem(saFormValuesKey)!
    );
    expect(saFormValues.pulse).toBe("99");

    // 2. Switch persona to cra_monitor
    authStore.currentPersona = "cra_monitor";
    clinicalStore.loadPersonaState("cra_monitor");

    // Verify state rehydrated cleanly for cra_monitor (default fixtures, no cross-persona pollution)
    expect(clinicalStore.formValues.pulse).toBe("72");
    expect(clinicalStore.ledgerBlocks).toHaveLength(0);

    // 3. Mutate formValues & ledger in cra_monitor persona
    clinicalStore.formValues.pulse = "140";
    await clinicalStore.addLedgerBlock(
      "TEST_ACTION_CRA",
      { pulse: "140" },
      "CRA Monitor Updated Pulse"
    );

    const craFormValuesKey = "clinical_cra_monitor_formValues";
    expect(window.localStorage.getItem(craFormValuesKey)).not.toBeNull();
    const craFormValues = JSON.parse(
      window.localStorage.getItem(craFormValuesKey)!
    );
    expect(craFormValues.pulse).toBe("140");

    // 4. Switch back to super_admin persona
    authStore.currentPersona = "super_admin";
    clinicalStore.loadPersonaState("super_admin");

    // Verify super_admin state was restored intact
    expect(clinicalStore.formValues.pulse).toBe("99");
    expect(
      clinicalStore.ledgerBlocks.some((b) => b.action === "TEST_ACTION_SA")
    ).toBe(true);
    expect(
      clinicalStore.ledgerBlocks.some((b) => b.action === "TEST_ACTION_CRA")
    ).toBe(false);
  });
});
