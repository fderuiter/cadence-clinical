import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ref } from "vue";
import { useConsentGating } from "../src/composables/useConsentGating";
import { useVerification } from "../src/composables/useVerification";
import { usePiSignoff } from "../src/composables/usePiSignoff";
import { useSchemaIngestion } from "../src/composables/useSchemaIngestion";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import CraVerificationConsole from "../src/components/persona/CraVerificationConsole.vue";
import PiSignatureDrawer from "../src/components/persona/PiSignatureDrawer.vue";
import DesignerSchemaPanel from "../src/components/persona/DesignerSchemaPanel.vue";
import { useClinicalStore } from "../src/stores/clinical";
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

  describe("useConsentGating Composable", () => {
    it("correctly identifies gated subjects and clears gating upon consent completion", async () => {
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

  describe("usePiSignoff Composable", () => {
    it("manages target selection and re-authentication modal states", () => {
      const {
        signoffTargetType,
        signoffTargetId,
        handleSignOffSubmit,
        showReauthModal,
        reauthAction,
        cancelReauth,
      } = usePiSignoff(clinicalStore, authStore);

      signoffTargetType.value = "VISIT";
      signoffTargetId.value = "V-SCR";

      handleSignOffSubmit();

      expect(showReauthModal.value).toBe(true);
      expect(reauthAction.value).toBe("BATCH_SIGN_OFF");

      cancelReauth();
      expect(showReauthModal.value).toBe(false);
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
      expect(alertSpy).toHaveBeenCalledWith(
        expect.stringContaining("mandatory")
      );

      alertSpy.mockRestore();
    });
  });

  describe("Persona Sub-Components Rendering", () => {
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

    it("renders PiSignatureDrawer worklist and triggers signoff submission", async () => {
      const wrapper = mount(PiSignatureDrawer, {
        props: {
          signoffTargetType: "FORM",
          signoffTargetId: "FSUB-001",
          customTargetId: "",
          signoffReason: "PI approval and sign-off.",
          availableSubjects: ["SUBJ-001"],
          availableVisits: ["V-SCR"],
          availableFormSubmissions: ["FSUB-001"],
          validSigningReasons: ["PI approval and sign-off."],
          showReauthModal: false,
          reauthUsername: "fderuiter",
          reauthPassword: "",
          reauthTotp: "",
          reauthError: "",
          simulateDelay: false,
        },
      });

      expect(wrapper.find("#signoff-target-type").exists()).toBe(true);
      expect(wrapper.find("#btn-pi-signoff").exists()).toBe(true);

      await wrapper.find("#btn-pi-signoff").trigger("click");
      expect(wrapper.emitted("submit-signoff")).toBeTruthy();
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
