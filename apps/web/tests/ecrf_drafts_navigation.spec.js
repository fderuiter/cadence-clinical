import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { ref, reactive, nextTick } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { i18n } from "../src/i18n/index.js";
import { useEcrfDrafts } from "../src/composables/useEcrfDrafts.js";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import { useClinicalStore } from "../src/stores/clinical.js";
import { useAuthStore } from "../src/stores/auth.js";
import { executionApi } from "../src/api/executionApi.js";

// Mock router
let routeLeaveHandler = null;
vi.mock("vue-router", () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: vi.fn() }),
  onBeforeRouteLeave: (handler) => {
    routeLeaveHandler = handler;
  },
}));

// Mock execution API
vi.mock("../src/api/executionApi.js", () => ({
  executionApi: {
    createFormSubmission: vi.fn().mockResolvedValue({ id: "SUB-12345" }),
    completeFormSubmission: vi.fn().mockResolvedValue({ id: "SUB-12345" }),
  },
}));

const SCOPE = ref({ tenantId: "tenant-a", userId: "user-a" });

/** Signs in a Keycloak account with a tenant, as the real sign-in does. */
function signIn() {
  useAuthStore().setAuth({
    authenticated: true,
    token: "token-a",
    tokenParsed: {
      sub: "user-a",
      tenant_id: "tenant-a",
      realm_access: { roles: ["CRC"] },
    },
  });
}

const VIEW_DRAFT_KEY =
  "cadence_user:tenant-a:user-a:ecrf_draft:STUDY-USDM-001:SUBJ-001:Screening";

describe("eCRF Draft Persistence & Navigation Guards", () => {
  let pinia;

  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    routeLeaveHandler = null;
    pinia = createPinia();
    setActivePinia(pinia);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  describe("useEcrfDrafts Composable", () => {
    it("generates correct local storage draft key format", () => {
      const studyId = ref("STUDY-101");
      const subjectId = ref("SUBJ-202");
      const visitId = ref("Week2");
      const formValues = reactive({ vssbp: "120" });

      const { getStorageKey } = useEcrfDrafts(
        studyId,
        subjectId,
        visitId,
        formValues,
        SCOPE
      );

      expect(getStorageKey()).toBe(
        "cadence_user:tenant-a:user-a:ecrf_draft:STUDY-101:SUBJ-202:Week2"
      );
    });

    it("identifies dirty fields and isDirty flag correctly", () => {
      const studyId = ref("STUDY-101");
      const subjectId = ref("SUBJ-001");
      const visitId = ref("Screening");
      const formValues = reactive({ vssbp: "120", vsdpb: "80" });

      const { dirtyFields, isDirty, setInitialValues } = useEcrfDrafts(
        studyId,
        subjectId,
        visitId,
        formValues,
        SCOPE
      );

      setInitialValues({ vssbp: "120", vsdpb: "80" });
      expect(isDirty.value).toBe(false);
      expect(dirtyFields.value.vssbp).toBeUndefined();

      // Modify a field value
      formValues.vssbp = "135";
      expect(isDirty.value).toBe(true);
      expect(dirtyFields.value.vssbp).toBe(true);
      expect(dirtyFields.value.vsdpb).toBeUndefined();
    });

    it("saves draft to localStorage after 500ms debounce when form values change", async () => {
      const studyId = ref("STUDY-101");
      const subjectId = ref("SUBJ-001");
      const visitId = ref("Screening");
      const formValues = reactive({ vssbp: "120" });

      const { setInitialValues, getStorageKey } = useEcrfDrafts(
        studyId,
        subjectId,
        visitId,
        formValues,
        SCOPE
      );

      setInitialValues({ vssbp: "120" });
      formValues.vssbp = "140";

      await nextTick();

      // Before 500ms, localStorage should not be updated
      vi.advanceTimersByTime(200);
      expect(localStorage.getItem(getStorageKey())).toBeNull();

      // After 500ms, draft is saved
      vi.advanceTimersByTime(350);
      const saved = JSON.parse(localStorage.getItem(getStorageKey()));
      expect(saved).toEqual({ vssbp: "140" });
    });

    it("restores draft values and sets hasDraft / draftRestored flags", () => {
      const studyId = ref("STUDY-101");
      const subjectId = ref("SUBJ-001");
      const visitId = ref("Screening");
      const formValues = reactive({ vssbp: "", vsdpb: "" });

      const { restoreDraft, draftRestored, hasDraft } = useEcrfDrafts(
        studyId,
        subjectId,
        visitId,
        formValues,
        SCOPE
      );

      localStorage.setItem(
        "cadence_user:tenant-a:user-a:ecrf_draft:STUDY-101:SUBJ-001:Screening",
        JSON.stringify({ vssbp: "128", vsdpb: "82" })
      );

      const restored = restoreDraft();
      expect(restored).toBe(true);
      expect(formValues.vssbp).toBe("128");
      expect(formValues.vsdpb).toBe("82");
      expect(hasDraft.value).toBe(true);
      expect(draftRestored.value).toBe(true);
    });

    it("clears draft from localStorage and resets flags", () => {
      const studyId = ref("STUDY-101");
      const subjectId = ref("SUBJ-001");
      const visitId = ref("Screening");
      const formValues = reactive({ vssbp: "128" });

      const { clearDraft, isDirty, setInitialValues, getStorageKey } =
        useEcrfDrafts(studyId, subjectId, visitId, formValues, SCOPE);

      setInitialValues({ vssbp: "120" });
      localStorage.setItem(getStorageKey(), JSON.stringify({ vssbp: "128" }));

      clearDraft();

      expect(localStorage.getItem(getStorageKey())).toBeNull();
      expect(isDirty.value).toBe(false);
    });
  });

  describe("CrcFormRenderer Component Draft Badges & Banner", () => {
    it("renders draft status banner when isDirty or draftRestored is true", () => {
      const store = useClinicalStore();
      const wrapper = mount(CrcFormRenderer, {
        global: {
          plugins: [pinia, i18n],
          stubs: { ClinicalFormField: true, "router-link": true },
        },
        props: {
          store,
          selectedSubjectId: "SUBJ-001",
          selectedVisitId: "Screening",
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
          isAuthorizedForBulkSdv: false,
          sdvStates: {},
          getSdvKey: () => "key",
          selectedBatchFields: [],
          dirtyFields: { vssbp: true },
          isDirty: true,
          draftRestored: false,
        },
      });

      const banner = wrapper.find("#ecrf-draft-status-banner");
      expect(banner.exists()).toBe(true);
      expect(banner.text()).toContain("Unsaved Draft Changes");

      const dirtyBadge = wrapper.find("#dirty-badge-vssbp");
      expect(dirtyBadge.exists()).toBe(true);
      expect(dirtyBadge.text()).toContain("Unsaved");
    });
  });

  describe("EcrfView Integration & Navigation Guards", () => {
    beforeEach(signIn);

    it("restores local draft on session load if present", async () => {
      const store = useClinicalStore();
      store.subjects = [{ id: "SUBJ-001", status: "ENROLLED" }];
      store.ecrfFields = [
        { id: "vssbp", label: "Systolic BP", type: "number" },
        { id: "vsdpb", label: "Diastolic BP", type: "number" },
      ];

      localStorage.setItem(
        VIEW_DRAFT_KEY,
        JSON.stringify({ vssbp: "145", vsdpb: "95" })
      );

      const wrapper = mount(EcrfView, {
        global: {
          plugins: [pinia, i18n],
          stubs: {
            StudySubjectsBar: true,
            SubjectEnrollmentForm: true,
            PiSignatureDrawer: true,
            DesignerSchemaPanel: true,
            ReasonModal: true,
            ConflictResolutionModal: true,
            EhrReconciliationDrawer: true,
            "router-link": true,
          },
        },
      });

      await nextTick();

      expect(store.formValues.vssbp).toBe("145");
      expect(store.formValues.vsdpb).toBe("95");
      expect(wrapper.find("#ecrf-draft-status-banner").exists()).toBe(true);
    });

    it("triggers route leave prompt when form is dirty", async () => {
      const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);

      const store = useClinicalStore();
      store.subjects = [{ id: "SUBJ-001", status: "ENROLLED" }];
      store.ecrfFields = [
        { id: "vssbp", label: "Systolic BP", type: "number" },
      ];

      mount(EcrfView, {
        global: {
          plugins: [pinia, i18n],
          stubs: {
            StudySubjectsBar: true,
            SubjectEnrollmentForm: true,
            PiSignatureDrawer: true,
            DesignerSchemaPanel: true,
            ReasonModal: true,
            ConflictResolutionModal: true,
            EhrReconciliationDrawer: true,
            "router-link": true,
          },
        },
      });

      await nextTick();

      // Modify form value to make it dirty
      store.formValues.vssbp = "130";
      await nextTick();

      expect(typeof routeLeaveHandler).toBe("function");

      // Attempt to leave route when user cancels
      const leaveResult = routeLeaveHandler();
      expect(confirmSpy).toHaveBeenCalledWith(
        "You have unsaved eCRF draft changes. Are you sure you want to leave?"
      );
      expect(leaveResult).toBe(false);

      // Attempt to leave route when user accepts
      confirmSpy.mockReturnValue(true);
      const leaveResultAllowed = routeLeaveHandler();
      expect(leaveResultAllowed).toBeUndefined();
    });

    it("clears draft from localStorage when form is submitted", async () => {
      const store = useClinicalStore();
      store.subjects = [{ id: "SUBJ-001", status: "ENROLLED" }];
      store.ecrfFields = [
        { id: "vssbp", label: "Systolic BP", type: "number" },
      ];

      const draftKey = VIEW_DRAFT_KEY;
      localStorage.setItem(draftKey, JSON.stringify({ vssbp: "120" }));

      const wrapper = mount(EcrfView, {
        global: {
          plugins: [pinia, i18n],
          stubs: {
            StudySubjectsBar: true,
            SubjectEnrollmentForm: true,
            PiSignatureDrawer: true,
            DesignerSchemaPanel: true,
            ReasonModal: true,
            ConflictResolutionModal: true,
            EhrReconciliationDrawer: true,
            "router-link": true,
          },
        },
      });

      await nextTick();

      const crcRenderer = wrapper.findComponent(CrcFormRenderer);
      expect(crcRenderer.exists()).toBe(true);

      // Emit submit-ecrf from CrcFormRenderer and flush async operations
      crcRenderer.vm.$emit("submit-ecrf");
      await vi.runAllTimersAsync();
      await flushPromises();

      expect(executionApi.createFormSubmission).toHaveBeenCalled();
      // The submit chain hashes a ledger block with Web Crypto, which resolves outside the
      // flushed microtasks when the suite runs under load; wait for the draft to clear.
      await vi.waitFor(() => {
        expect(localStorage.getItem(draftKey)).toBeNull();
      });
    });
  });
});
