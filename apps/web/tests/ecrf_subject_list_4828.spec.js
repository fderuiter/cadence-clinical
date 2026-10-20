import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore, subjectFromExecution } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";
import { useToastStore } from "../src/stores/toasts.js";
import { executionApi } from "../src/api/executionApi";
import EcrfView from "../src/views/EcrfView.vue";
import CrcFormRenderer from "../src/components/persona/CrcFormRenderer.vue";
import StudySubjectsBar from "../src/components/clinical/StudySubjectsBar.vue";

const routeQuery = { value: {} };

vi.mock("vue-router", () => ({
  onBeforeRouteLeave: () => {},
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: routeQuery.value }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
  },
}));

function summary(code, overrides = {}) {
  return {
    id: `rec-${code}`,
    subject_id: code,
    study_id: "CAD-101",
    site_id: "SITE-7",
    status: "ENROLLED",
    active_protocol_version: "2.0.0",
    enrollment_index: 1,
    ...overrides,
  };
}

function page(items, total = items.length) {
  return { items, total, limit: 200, offset: 0 };
}

function httpError(status) {
  const err = new Error(`Request failed with status ${status}`);
  err.status = status;
  return err;
}

// #4828: the eCRF lists a study's subjects from Execution, and only a demo build shows sample subjects.
describe("eCRF subject list from Execution (#4828)", () => {
  let pinia;

  beforeEach(() => {
    vi.useRealTimers();
    routeQuery.value = {};
    vi.clearAllMocks();
    vi.spyOn(window, "alert").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  function createStore() {
    pinia = createPinia();
    setActivePinia(pinia);
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Site Investigator"];
    return useClinicalStore();
  }

  describe("store", () => {
    it("starts with no subjects and no study outside a demo build", () => {
      const store = createStore();
      expect(store.subjects).toEqual([]);
      expect(store.activeStudyId).toBe("");
    });

    it("keeps its sample subjects and study in a demo build and does not call the API", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      const store = createStore();
      expect(store.subjects.length).toBeGreaterThan(0);
      expect(store.activeStudyId).toBe("STUDY-USDM-001");

      expect(await store.loadSubjects("CAD-101")).toBe(true);
      expect(executionApi.listSubjects).not.toHaveBeenCalled();
    });

    it("ignores subjects saved in the browser outside a demo build", () => {
      window.localStorage.setItem(
        "subjects",
        JSON.stringify([{ id: "SAVED-1", status: "ACTIVE", isGated: false }])
      );
      try {
        expect(createStore().subjects).toEqual([]);
      } finally {
        window.localStorage.removeItem("subjects");
      }
    });

    it("lists the study's subjects by subject code, with record id, site and re-consent gate", async () => {
      const store = createStore();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce(
        page([
          summary("CAD-001"),
          summary("CAD-002", { status: "RECONSENT_REQUIRED", site_id: null }),
        ])
      );

      expect(await store.loadSubjects(" CAD-101 ")).toBe(true);

      expect(executionApi.listSubjects).toHaveBeenCalledWith({
        studyId: "CAD-101",
        limit: 200,
      });
      expect(store.subjects).toEqual([
        {
          id: "CAD-001",
          recordId: "rec-CAD-001",
          siteId: "SITE-7",
          status: "ENROLLED",
          active_protocol_version: "2.0.0",
          isGated: false,
          consentText: undefined,
          consentColor: undefined,
        },
        {
          id: "CAD-002",
          recordId: "rec-CAD-002",
          siteId: undefined,
          status: "RECONSENT_REQUIRED",
          active_protocol_version: "2.0.0",
          isGated: true,
          consentText: "Re-consent required",
          consentColor: "yellow",
        },
      ]);
      expect(store.subjectsTotal).toBe(2);
      expect(store.subjectsError).toBeNull();
      expect(store.subjectsLoading).toBe(false);
    });

    it("reads a total the service sent as a string", () => {
      const store = createStore();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce({
        ...page([summary("CAD-001")]),
        total: "41",
      });
      return store.loadSubjects("CAD-101").then(() => {
        expect(store.subjectsTotal).toBe(41);
      });
    });

    it("leaves the list as it was and shows an error when the call fails", async () => {
      const store = createStore();
      const before = [subjectFromExecution(summary("CAD-001"))];
      store.subjects = before;
      vi.mocked(executionApi.listSubjects).mockRejectedValueOnce(
        httpError(503)
      );

      expect(await store.loadSubjects("CAD-101")).toBe(false);

      expect(store.subjects).toEqual(before);
      expect(store.subjectsError).toContain("503");
      const toasts = useToastStore().toasts;
      expect(toasts).toHaveLength(1);
      expect(toasts[0]).toMatchObject({
        tone: "error",
        title: "Couldn't load the study's subjects",
      });
    });

    it("clears the list without calling the API when no study is given", async () => {
      const store = createStore();
      store.subjects = [subjectFromExecution(summary("CAD-001"))];
      store.subjectsTotal = 1;

      expect(await store.loadSubjects("  ")).toBe(false);

      expect(executionApi.listSubjects).not.toHaveBeenCalled();
      expect(store.subjects).toEqual([]);
      expect(store.subjectsTotal).toBe(0);
    });
  });

  describe("screen", () => {
    function mountView() {
      return mount(EcrfView, { global: { plugins: [pinia] } });
    }

    it("lists the subjects of the study it was opened for and selects the first one", async () => {
      routeQuery.value = { studyId: "CAD-101", subjectId: "NOT-LISTED" };
      createStore();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce(
        page([summary("CAD-001"), summary("CAD-002")])
      );

      const wrapper = mountView();
      await flushPromises();

      expect(executionApi.listSubjects).toHaveBeenCalledWith({
        studyId: "CAD-101",
        limit: 200,
      });
      const options = wrapper
        .findAll("#ecrf-subject-selector option")
        .map((o) => o.attributes("value"));
      expect(options).toEqual(["CAD-001", "CAD-002"]);
      expect(
        wrapper.findComponent(CrcFormRenderer).props("selectedSubjectId")
      ).toBe("CAD-001");
      expect(wrapper.find("#study-subjects-status").text()).toBe("2 subjects.");
    });

    it("keeps the subject it was opened for when the study lists it", async () => {
      routeQuery.value = { studyId: "CAD-101", subjectId: "CAD-002" };
      createStore();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce(
        page([summary("CAD-001"), summary("CAD-002")])
      );

      const wrapper = mountView();
      await flushPromises();

      expect(
        wrapper.findComponent(CrcFormRenderer).props("selectedSubjectId")
      ).toBe("CAD-002");
    });

    it("says when only the first page of subjects is shown", async () => {
      routeQuery.value = { studyId: "CAD-101" };
      createStore();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce(
        page([summary("CAD-001")], 250)
      );

      const wrapper = mountView();
      await flushPromises();

      expect(wrapper.find("#study-subjects-status").text()).toBe(
        "Showing the first 1 of 250 subjects."
      );
    });

    it("asks for a study and lists nothing when opened without one", async () => {
      createStore();

      const wrapper = mountView();
      await flushPromises();

      expect(executionApi.listSubjects).not.toHaveBeenCalled();
      expect(wrapper.find("#study-subjects-status").text()).toBe(
        "Enter a study code to list its subjects."
      );
      expect(wrapper.find("#ecrf-subject-selector").text()).toContain(
        "No subjects listed"
      );
      expect(
        wrapper.find("#btn-load-study-subjects").attributes()
      ).toHaveProperty("disabled");
    });

    it("lists a study's subjects when the user enters its code", async () => {
      const store = createStore();
      const wrapper = mountView();
      await flushPromises();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce(
        page([summary("CAD-001")])
      );

      await wrapper.find("#study-subjects-code").setValue("CAD-101");
      await wrapper.find("#study-subjects-bar").trigger("submit");
      await flushPromises();

      expect(store.activeStudyId).toBe("CAD-101");
      expect(
        wrapper.findComponent(StudySubjectsBar).emitted("loaded")
      ).toHaveLength(1);
      expect(
        wrapper.findComponent(CrcFormRenderer).props("selectedSubjectId")
      ).toBe("CAD-001");
      expect(wrapper.find("#study-subjects-status").text()).toBe("1 subject.");
    });

    it("says the study has no subjects yet", async () => {
      routeQuery.value = { studyId: "CAD-101" };
      createStore();
      vi.mocked(executionApi.listSubjects).mockResolvedValueOnce(page([]));

      const wrapper = mountView();
      await flushPromises();

      expect(wrapper.find("#study-subjects-status").text()).toBe(
        "No subjects in this study yet."
      );
    });

    it("reports a failed load on the screen", async () => {
      routeQuery.value = { studyId: "CAD-101" };
      createStore();
      vi.mocked(executionApi.listSubjects).mockRejectedValueOnce(
        httpError(502)
      );

      const wrapper = mountView();
      await flushPromises();

      expect(wrapper.find("#study-subjects-status").text()).toBe(
        "Subjects couldn't be loaded."
      );
      expect(wrapper.find("#ecrf-subject-selector").text()).toContain(
        "No subjects listed"
      );
    });

    it("shows no study bar in a demo build", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      createStore();

      const wrapper = mountView();
      await flushPromises();

      expect(wrapper.find("#study-subjects-bar").exists()).toBe(false);
      expect(executionApi.listSubjects).not.toHaveBeenCalled();
    });
  });
});
