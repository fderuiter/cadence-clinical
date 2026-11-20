import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ref } from "vue";
import { useConsentGating } from "../src/composables/useConsentGating";
import { useVerification } from "../src/composables/useVerification";
import { useSchemaIngestion } from "../src/composables/useSchemaIngestion";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import CraVerificationConsole from "../src/components/persona/CraVerificationConsole.vue";
import DesignerSchemaPanel from "../src/components/persona/DesignerSchemaPanel.vue";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts.js";
import { useAuthStore } from "../src/stores/auth";

describe("Persona Sub-Components & Domain Composables Unit Tests", () => {
  let pinia;
  let clinicalStore;
  let authStore;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    clinicalStore = useClinicalStore();
    authStore = useAuthStore();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  describe("useConsentGating Composable", () => {
    it("correctly identifies gated subjects and clears gating upon consent completion (demo build)", async () => {
      // Re-consent runs on local data only in a demo build (#4827); unbacked_actions.test.js covers the refusal.
      vi.stubEnv("VITE_DEMO_MODE", "true");
      clinicalStore.subjects = [
        {
          id: "SUBJ-002",
          status: "ACTIVE",
          active_protocol_version: "1.0.0",
          isGated: true,
        },
      ];
      const activeSubject = ref("SUBJ-002");
      const {
        isReconsentGated,
        openEconsentModal,
        showEconsentModal,
        handleCompleteReconsent,
      } = useConsentGating(activeSubject);

      expect(isReconsentGated.value).toBe(true);

      openEconsentModal();
      expect(showEconsentModal.value).toBe(true);

      const mockStore = { addLedgerBlock: vi.fn().mockResolvedValue({}) };
      await handleCompleteReconsent("ECONSENT", mockStore);

      expect(isReconsentGated.value).toBe(false);
      expect(mockStore.addLedgerBlock).toHaveBeenCalledWith(
        "RECONSENT_COMPLETED",
        expect.objectContaining({ subject_id: "SUBJ-002", method: "ECONSENT" }),
        expect.any(String)
      );
    });
  });

  describe("useVerification Composable", () => {
    it("handles SDV toggle and invalidates status on field value edit", () => {
      const activeRole = ref("cra");
      const {
        sdvStates,
        getSdvKey,
        isCraUser,
        handleVerificationInvalidationOnEdit,
      } = useVerification(activeRole);

      expect(isCraUser.value).toBe(true);

      const key = getSdvKey("SUBJ-001", "Screening", "vssbp");
      sdvStates[key] = true;

      const mockStore = {
        addLedgerBlock: vi.fn(),
        user: { username: "monitor1" },
      };

      handleVerificationInvalidationOnEdit(
        { id: "vssbp", label: "Systolic BP" },
        "120",
        "130",
        "SUBJ-001",
        "Screening",
        mockStore
      );

      expect(sdvStates[key]).toBe(false);
      expect(mockStore.addLedgerBlock).toHaveBeenCalledWith(
        "SDV_CLEAR",
        expect.objectContaining({
          fieldId: "vssbp",
          oldValue: "120",
          newValue: "130",
        }),
        expect.any(String)
      );
    });
  });

  describe("useSchemaIngestion Composable", () => {
    it("calculates unreviewed items count and validates candidate promotion rationale", async () => {
      clinicalStore.candidateDraft = {
        id: "cand_001",
        status: "PENDING_REVIEW",
        items: {
          item1: { id: "item1", review_status: "PENDING" },
          item2: { id: "item2", review_status: "ACCEPTED" },
        },
      };

      const { unreviewedCount, promoteChangeReason, promoteCandidate } =
        useSchemaIngestion(clinicalStore);

      expect(unreviewedCount.value).toBe(1);

      promoteChangeReason.value = "";
      const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});

      await promoteCandidate();
      expect(alertSpy).not.toHaveBeenCalled();
      expect(useToastStore().toasts).toContainEqual(
        expect.objectContaining({
          tone: "error",
          message: expect.stringContaining("mandatory"),
        })
      );

      alertSpy.mockRestore();
    });
  });

  describe("Persona Sub-Components Rendering", () => {
    it("renders CrcFormRenderer form fields and controls", () => {
      const wrapper = mount(CrcFormRenderer, {
        props: {
          store: clinicalStore,
          selectedSubjectId: "SUBJ-001",
          selectedVisitId: "V-SCR",
          isReconsentGated: false,
          showEconsentModal: false,
          showPaperIcfModal: false,
          reconsentSubmitting: false,
          econsentSignerName: "",
          paperIcfDate: "",
          paperIcfNote: "",
          lookupStatuses: {},
          getValidationError: () => null,
          isCraUser: false,
          sdvStates: {},
          getSdvKey: () => "",
        },
      });

      expect(wrapper.find(".crc-form-renderer").exists()).toBe(true);
    });

    it("renders CraVerificationConsole batch verification bar when fields are selected", () => {
      const wrapper = mount(CraVerificationConsole, {
        props: {
          selectedBatchFields: ["vssbp", "vsdpb"],
          isAuthorizedForBulkSdv: true,
        },
      });

      expect(wrapper.find("#batch-sdv-bar").exists()).toBe(true);
      expect(wrapper.find("#btn-batch-verify").exists()).toBe(true);
      expect(wrapper.text()).toContain("Selected 2 fields");
    });

    it("renders DesignerSchemaPanel protocol ingestion controls and draft items", () => {
      clinicalStore.candidateDraft = {
        id: "cand_test",
        status: "PENDING_REVIEW",
        items: {
          v1: {
            id: "v1",
            type: "visit",
            name: "Week 2 Visit",
            confidence: 0.95,
            confidence_level: "auto",
            source_citation: "Page 12, Table 3",
            review_status: "PENDING",
          },
        },
      };

      const wrapper = mount(DesignerSchemaPanel, {
        props: {
          store: clinicalStore,
          selectedFileName: "protocol.pdf",
          unreviewedCount: 1,
          getConfidenceClass: () => "lookup-valid",
          getStatusClass: () => "",
        },
      });

      expect(wrapper.find(".candidate-draft-section").exists()).toBe(true);
      expect(wrapper.find(".candidate-item-card").exists()).toBe(true);
      expect(wrapper.find("#promote-change-reason").exists()).toBe(true);
      expect(wrapper.find("#btn-promote-candidate").exists()).toBe(true);
    });
  });
});
