import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { useAuthStore } from "../../src/stores/auth";
import { useToastStore } from "../../src/stores/toasts.js";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    getSubjectCounts: vi.fn(),
    addSubject: vi.fn(),
    changeSubjectState: vi.fn(),
    assignSubjectSite: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

const IDS = [
  "0192f3c4-5e6a-7b8c-9d0e-000000000001",
  "0192f3c4-5e6a-7b8c-9d0e-000000000002",
  "0192f3c4-5e6a-7b8c-9d0e-000000000003",
];

function participant(index, overrides = {}) {
  return {
    id: IDS[index],
    subject_id: `017-000${index + 1}`,
    study_id: "CAD-101",
    site_id: "017",
    status: "ACTIVE",
    active_protocol_version: "3.0",
    enrollment_index: index + 1,
    ...overrides,
  };
}

let participants;

// Answers the list endpoint like Execution does: filtered by state and site, paged by limit and offset.
function serveParticipants() {
  vi.mocked(executionApi.listSubjects).mockImplementation(
    async ({ status, siteId, limit = 50, offset = 0 }) => {
      const matching = participants.filter(
        (p) =>
          (!status || p.status === status) && (!siteId || p.site_id === siteId)
      );
      return {
        items: matching.slice(offset, offset + limit),
        total: matching.length,
        limit,
        offset,
      };
    }
  );
  vi.mocked(executionApi.getSubjectCounts).mockImplementation(
    async ({ siteId } = {}) => {
      const matching = participants.filter(
        (p) => !siteId || p.site_id === siteId
      );
      const counts = {};
      for (const p of matching) {
        counts[p.status] = (counts[p.status] || 0) + 1;
      }
      return {
        total: matching.length,
        counts,
      };
    }
  );
}

describe("Clinical Canvas: adding participants and changing their state", () => {
  let pinia;
  let router;
  let wrapper;

  beforeEach(() => {
    localStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: StudyCanvasView,
          meta: { layout: "canvas" },
        },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });
    participants = [
      participant(0),
      participant(1, { status: "SCREENING" }),
      participant(2, { status: "COMPLETED" }),
    ];
    for (const fn of Object.values(executionApi)) vi.mocked(fn).mockReset();
    serveParticipants();
    signInWith(["crc"]);
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  function signInWith(roles) {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = roles;
  }

  afterEach(() => {
    wrapper?.unmount();
    wrapper = undefined;
    vi.restoreAllMocks();
  });

  async function open(url) {
    await router.push(url);
    await router.isReady();
    wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  async function fillAddForm({ code, site, reason }) {
    await wrapper.find("#btn-add-participant").trigger("click");
    await wrapper.find("#add-participant-code").setValue(code);
    if (site !== undefined) {
      await wrapper.find("#add-participant-site").setValue(site);
    }
    await wrapper.find("#add-participant-reason").setValue(reason);
    await flushPromises();
    await wrapper.find("#add-participant-form").trigger("submit");
    await router.isReady();
    await flushPromises();
  }

  it("adds a participant through Execution, then lists and selects it", async () => {
    vi.mocked(executionApi.addSubject).mockImplementation(async (request) => {
      participants.push(
        participant(3, {
          id: "0192f3c4-5e6a-7b8c-9d0e-000000000004",
          subject_id: request.subjectCode,
          site_id: request.siteId,
          status: "SCREENING",
          enrollment_index: 4,
        })
      );
      return { id: "0192f3c4-5e6a-7b8c-9d0e-000000000004" };
    });
    await open("/studies/CAD-101?stage=conduct&section=participants&site=017");

    await wrapper.find("#btn-add-participant").trigger("click");
    // The site filter in view is the default site.
    expect(wrapper.find("#add-participant-site").element.value).toBe("017");
    expect(wrapper.find("#btn-confirm-add-participant").element.disabled).toBe(
      true
    );
    await wrapper.find("#add-participant-code").setValue(" 017-0009 ");
    await wrapper.find("#add-participant-reason").setValue("Pre-screened");
    await wrapper.find("#add-participant-form").trigger("submit");
    await flushPromises();

    expect(executionApi.addSubject).toHaveBeenCalledWith({
      studyId: "CAD-101",
      subjectCode: "017-0009",
      siteId: "017",
      reason: "Pre-screened",
    });
    expect(wrapper.find("#add-participant-form").exists()).toBe(false);
    expect(wrapper.find("#participant-017-0009").exists()).toBe(true);
    expect(router.currentRoute.value.query.node).toBe(
      "participant:0192f3c4-5e6a-7b8c-9d0e-000000000004"
    );
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0009");
    expect(wrapper.find("#inspector-state").text()).toBe("Screening");
    expect(
      useToastStore().toasts.some(
        (t) => t.title === "017-0009 added in screening"
      )
    ).toBe(true);
  });

  it("adds a participant to a study with none yet", async () => {
    participants = [];
    vi.mocked(executionApi.addSubject).mockImplementation(async () => {
      participants.push(participant(0, { status: "SCREENING" }));
      return { id: IDS[0] };
    });
    await open("/studies/CAD-101?stage=conduct&section=participants");
    expect(wrapper.find("#participants-status-line").text()).toBe(
      "No participants in CAD-101 yet."
    );

    await wrapper.find("#btn-add-participant").trigger("click");
    await wrapper.find("#add-participant-code").setValue("017-0001");
    await wrapper.find("#add-participant-reason").setValue("First participant");
    await flushPromises();
    // Site is required before submission so enrollment sync never creates siteless subjects.
    expect(wrapper.find("#btn-confirm-add-participant").element.disabled).toBe(
      true
    );
    await wrapper.find("#add-participant-site").setValue("017");
    await flushPromises();
    expect(wrapper.find("#btn-confirm-add-participant").element.disabled).toBe(
      false
    );
    await wrapper.find("#add-participant-form").trigger("submit");
    await router.isReady();
    await flushPromises();

    expect(executionApi.addSubject).toHaveBeenCalledWith(
      expect.objectContaining({ siteId: "017" })
    );
    expect(wrapper.find("#participant-017-0001").exists()).toBe(true);
    expect(router.currentRoute.value.query.node).toBe(`participant:${IDS[0]}`);
  });

  it("keeps the form and says so when Execution refuses the participant", async () => {
    vi.mocked(executionApi.addSubject).mockRejectedValue(
      Object.assign(new Error("Subject code already exists."), { status: 409 })
    );
    await open("/studies/CAD-101?stage=conduct&section=participants");

    await fillAddForm({ code: "017-0001", site: "017", reason: "Duplicate" });

    expect(wrapper.find("#add-participant-form").exists()).toBe(true);
    expect(router.currentRoute.value.query.node).toBeUndefined();
    expect(
      useToastStore().toasts.some((t) => t.title === "017-0001 was not added")
    ).toBe(true);
  });

  it("offers only the changes Execution allows, never unblinding", async () => {
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[0]}`
    );

    const options = wrapper
      .findAll("#state-change-target option")
      .filter((o) => o.attributes("value"))
      .map((o) => o.text());
    expect(options).toEqual(["Re-consent required", "Completed", "Withdrawn"]);
    expect(wrapper.find("#state-change-unblinding-note").exists()).toBe(true);
    expect(wrapper.find("#btn-change-state").element.disabled).toBe(true);
  });

  it("says a final state doesn't change and offers nothing", async () => {
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[2]}`
    );

    expect(wrapper.find("#state-change-final").text()).toBe(
      "Completed is a final state; it doesn't change."
    );
    expect(wrapper.find("#state-change-target").exists()).toBe(false);
  });

  it("moves a participant and shows its new state and counts from Execution", async () => {
    vi.mocked(executionApi.changeSubjectState).mockImplementation(
      async (id, state) => {
        const target = participants.find((p) => p.id === id);
        target.status = state;
        return { ...target };
      }
    );
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&status=SCREENING&node=participant:${IDS[1]}`
    );
    expect(wrapper.find("#participant-017-0002").exists()).toBe(true);

    await wrapper.find("#state-change-target").setValue("ENROLLED");
    expect(wrapper.find("#state-change-enrol-note").exists()).toBe(true);
    await wrapper.find("#state-change-reason").setValue("Eligible — 承認");
    expect(wrapper.find("#btn-change-state").text()).toBe("Move to enrolled");
    await wrapper.find("#btn-change-state").trigger("click");
    await flushPromises();

    expect(executionApi.changeSubjectState).toHaveBeenCalledWith(
      IDS[1],
      "ENROLLED",
      "Eligible — 承認"
    );
    expect(wrapper.find("#inspector-state").text()).toBe("Enrolled");
    // The screening list is read again, so the participant leaves it; the inspector keeps it.
    expect(wrapper.find("#participant-017-0002").exists()).toBe(false);
    expect(wrapper.find("#canvas-inspector-title").text()).toBe("017-0002");
    expect(wrapper.find("#state-change-reason").element.value).toBe("");
  });

  it("leaves the state as it was when Execution refuses the change", async () => {
    vi.mocked(executionApi.changeSubjectState).mockRejectedValue(
      Object.assign(
        new Error("Enrollment blocked: site 017 is not activated."),
        { status: 400 }
      )
    );
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[1]}`
    );

    await wrapper.find("#state-change-target").setValue("ENROLLED");
    await wrapper.find("#state-change-reason").setValue("Eligible");
    await wrapper.find("#btn-change-state").trigger("click");
    await flushPromises();

    expect(wrapper.find("#inspector-state").text()).toBe("Screening");
    expect(wrapper.find("#state-change-reason").element.value).toBe("Eligible");
    expect(
      useToastStore().toasts.some(
        (t) => t.title === "017-0002 was not moved to enrolled"
      )
    ).toBe(true);
  });

  it.each([
    ["a monitor", ["cra"]],
    ["an auditor", ["auditor"]],
    [
      "a sponsor admin, whom Execution does not treat as site staff",
      ["sponsor_admin"],
    ],
  ])(
    "offers %s no participant changes, since Execution would refuse them",
    async (_, roles) => {
      signInWith(roles);
      await open(
        `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[1]}`
      );

      expect(wrapper.find("#participant-017-0002").exists()).toBe(true);
      expect(wrapper.find("#btn-add-participant").exists()).toBe(false);
      expect(wrapper.find("#inspector-state").text()).toBe("Screening");
      expect(wrapper.find("#inspector-state-change").exists()).toBe(false);
    }
  );

  it("offers participant changes to site staff and data managers as Execution names them", async () => {
    signInWith(["Site Investigator"]);
    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[1]}`
    );

    expect(wrapper.find("#btn-add-participant").exists()).toBe(true);
    expect(wrapper.find("#inspector-state-change").exists()).toBe(true);
  });

  it("assigns a clinical site to a siteless participant and updates inspector state", async () => {
    participants = [participant(0, { site_id: null, status: "SCREENING" })];
    vi.mocked(executionApi.assignSubjectSite).mockImplementation(
      async (id, siteId) => {
        const target = participants.find((p) => p.id === id);
        target.site_id = siteId;
        return { ...target };
      }
    );

    await open(
      `/studies/CAD-101?stage=conduct&section=participants&node=participant:${IDS[0]}`
    );

    expect(wrapper.find("#participant-site-assignment").exists()).toBe(true);
    await wrapper.find("#participant-site-assign-input").setValue("017");
    await wrapper
      .find("#participant-site-assign-reason")
      .setValue("Assign participant to activated site 017");
    await wrapper.find("#participant-site-assignment").trigger("submit");
    await flushPromises();

    expect(executionApi.assignSubjectSite).toHaveBeenCalledWith(
      IDS[0],
      "017",
      "Assign participant to activated site 017"
    );
    expect(wrapper.find("#inspector-site").text()).toBe("017");
    expect(wrapper.find("#participant-site-assignment").exists()).toBe(false);
    expect(
      useToastStore().toasts.some(
        (t) => t.title === "Site 017 assigned to 017-0001"
      )
    ).toBe(true);
  });
});
