import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
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
    // The shared sample subject pool exists only in a demo build (#4828); other builds list subjects from Execution.
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);

    authStore = useAuthStore();
    clinicalStore = useClinicalStore();

    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Designer", "Site Investigator", "CRC"];
  });

  it("Requirement 1 & 2: maintains a unified subject fixture pool in eCRF and consolidates AmendmentDiffView into Clinical Canvas", () => {
    const storeSubjectIds = clinicalStore.subjects.map((s: any) => s.id);
    expect(storeSubjectIds).toEqual([
      "SUBJ-101-001",
      "SUBJ-101-002",
      "SUBJ-102-001",
    ]);

    const amendmentWrapper = mount(AmendmentDiffView, {
      global: {
        plugins: [pinia],
        stubs: { RouterLink: true },
      },
    });
    const ecrfWrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });

    const ecrfSubjectIds = ecrfWrapper
      .findAll("#ecrf-subject-selector option")
      .map((option) => option.attributes("value"));

    expect(amendmentWrapper.find("#amendment-diff-view").exists()).toBe(true);
    expect(ecrfSubjectIds).toEqual(storeSubjectIds);
  });

  it("Requirement 3: clearing a re-consent gate in the amendment dashboard automatically updates subject status in the eCRF view and unlocks fields", async () => {
    const gatedSubjectId = "SUBJ-101-002";
    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(true);

    const ecrfWrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });

    await ecrfWrapper.find("#ecrf-subject-selector").setValue(gatedSubjectId);
    await ecrfWrapper.vm.$nextTick();

    const ecrfVm = ecrfWrapper.vm as Record<string, any>;
    expect(ecrfVm.isReconsentGated).toBe(true);
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
    expect(ecrfVm.isReconsentGated).toBe(false);
    expect(ecrfWrapper.find("#reconsent-gating-banner").exists()).toBe(false);
    expect(ecrfWrapper.find("fieldset").attributes("disabled")).toBeUndefined();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("Requirement 3: clearing a re-consent gate via eCRF or eConsent forms reactively unlocks data entry fields (demo build)", async () => {
    // The eCRF records re-consent on local data only in a demo build (#4827); unbacked_actions.test.js covers the refusal.
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const gatedSubjectId = "SUBJ-101-002";
    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(true);

    const ecrfWrapper = mount(EcrfView, {
      global: { plugins: [pinia] },
    });

    await ecrfWrapper.find("#ecrf-subject-selector").setValue(gatedSubjectId);
    await ecrfWrapper.vm.$nextTick();

    const ecrfVm = ecrfWrapper.vm as Record<string, any>;
    expect(ecrfVm.isReconsentGated).toBe(true);

    // Call re-consent completion directly on eCRF component view
    await ecrfVm.handleCompleteReconsent("PAPER_UPLOAD");
    await ecrfWrapper.vm.$nextTick();

    expect(clinicalStore.isSubjectGated(gatedSubjectId)).toBe(false);
    expect(ecrfVm.isReconsentGated).toBe(false);
    expect(ecrfWrapper.find("#reconsent-gating-banner").exists()).toBe(false);
  });

  it("Requirement 4: every re-consent gate clearance action generates a cryptographic audit ledger block", async () => {
    const initialLedgerCount = clinicalStore.ledgerBlocks.length;
    const testSubjectId = "SUBJ-101-002";

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
