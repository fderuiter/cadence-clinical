import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { mount } from "@vue/test-utils";
import { useClinicalStore } from "@/stores/clinical";
import { useAuthStore } from "@/stores/auth";
import AmendmentDiffView from "@/views/AmendmentDiffView.vue";
import EcrfView from "@/views/EcrfView.vue";

vi.mock("@/api/apiClient", () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue([]),
    post: vi.fn().mockResolvedValue({ status: "SUCCESS" }),
    put: vi.fn().mockResolvedValue({ status: "SUCCESS" }),
    delete: vi.fn().mockResolvedValue({ status: "SUCCESS" }),
  },
}));

describe("Shared Clinical Store State & Reactive Subject Re-Consent Gating Specification", () => {
  let pinia: any;
  let clinicalStore: any;
  let authStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    authStore = useAuthStore();
    clinicalStore = useClinicalStore();

    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Sponsor Designer", "Site Investigator", "CRC"];
  });

  it("Requirement 1 & 2: maintains a unified subject fixture pool matching across amendment and eCRF views", () => {
    const storeSubjectIds = clinicalStore.subjects.map((s: any) => s.id);
    expect(storeSubjectIds).toContain("SUBJ-101");
    expect(storeSubjectIds).toContain("SUBJ-102");
    expect(storeSubjectIds).toContain("SUBJ-103");
    expect(storeSubjectIds).toContain("SUBJ-104");
    expect(storeSubjectIds).toContain("SUBJ-105");
    expect(storeSubjectIds).toContain("SUBJ-001");
    expect(storeSubjectIds).toContain("SUBJ-002");
    expect(storeSubjectIds).toContain("SUBJ-003");

    const amendmentWrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });
    const ecrfWrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });

    const amendmentSubjectIds = amendmentWrapper.vm.subjectsList.map(
      (s: any) => s.id
    );
    const ecrfSubjectIds = ecrfWrapper.vm.availableSubjects;

    expect(amendmentSubjectIds).toEqual(storeSubjectIds);
    expect(ecrfSubjectIds).toEqual(storeSubjectIds);
  });

  it("Requirement 3: clearing a re-consent gate in the amendment dashboard automatically updates subject status in the eCRF view and unlocks fields", async () => {
    const gatedSubjectId = "SUBJ-102";
    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(true);

    const ecrfWrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });

    await ecrfWrapper.find("#ecrf-subject-selector").setValue(gatedSubjectId);
    await ecrfWrapper.vm.$nextTick();

    expect(ecrfWrapper.vm.isReconsentGated).toBe(true);
    expect(ecrfWrapper.find("#reconsent-gating-banner").exists()).toBe(true);
    expect(ecrfWrapper.find("fieldset").attributes("disabled")).toBe("");

    // Clear re-consent gate in central store (simulating Amendment Dashboard clearance action)
    await clinicalStore.clearReconsentGate(
      gatedSubjectId,
      "ECONSENT",
      "Cleared via Amendment Dashboard test"
    );
    await ecrfWrapper.vm.$nextTick();

    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(false);
    expect(ecrfWrapper.vm.isReconsentGated).toBe(false);
    expect(ecrfWrapper.find("#reconsent-gating-banner").exists()).toBe(false);
    expect(ecrfWrapper.find("fieldset").attributes("disabled")).toBeUndefined();
  });

  it("Requirement 3: clearing a re-consent gate via eCRF or eConsent forms reactively unlocks data entry fields", async () => {
    const gatedSubjectId = "SUBJ-002";
    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(true);

    const ecrfWrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });

    await ecrfWrapper.find("#ecrf-subject-selector").setValue(gatedSubjectId);
    await ecrfWrapper.vm.$nextTick();

    expect(ecrfWrapper.vm.isReconsentGated).toBe(true);

    // Call re-consent completion directly on eCRF component view
    await ecrfWrapper.vm.handleCompleteReconsent("PAPER_UPLOAD");
    await ecrfWrapper.vm.$nextTick();

    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(false);
    expect(ecrfWrapper.vm.isReconsentGated).toBe(false);
    expect(ecrfWrapper.find("#reconsent-gating-banner").exists()).toBe(false);
  });

  it("Requirement 4: every re-consent gate clearance action generates a cryptographic audit ledger block", async () => {
    const initialLedgerCount = clinicalStore.ledgerBlocks.length;
    const testSubjectId = "SUBJ-103";

    await clinicalStore.clearReconsentGate(
      testSubjectId,
      "ECONSENT",
      "Subject completed digital eConsent v2.0.0"
    );

    expect(clinicalStore.ledgerBlocks.length).toBe(initialLedgerCount + 1);

    const latestBlock =
      clinicalStore.ledgerBlocks[clinicalStore.ledgerBlocks.length - 1];
    expect(latestBlock.action).toBe("RECONSENT_COMPLETED");
    expect(latestBlock.details.subject_id).toBe(testSubjectId);
    expect(latestBlock.details.method).toBe("ECONSENT");
    expect(latestBlock.reason).toBe(
      "Subject completed digital eConsent v2.0.0"
    );
    expect(latestBlock.hash).toBeDefined();
  });
});
