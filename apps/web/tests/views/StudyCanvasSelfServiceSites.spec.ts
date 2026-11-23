import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { setupServer } from "msw/node";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { executionApi } from "../../src/api/executionApi";
import { kernelApi } from "../../src/api/kernelApi";
import { findStudySites } from "../../src/canvas/kernelStudy";
import SiteActivationPanel from "../../src/components/clinical/SiteActivationPanel.vue";
import { webDemoStore } from "../../src/mocks/demoStore";
import { buildWebGatewayHandlers } from "../../src/mocks/handlers";
import { useAuthStore } from "../../src/stores/auth";
import StudyCanvasView from "../../src/views/StudyCanvasView.vue";

vi.mock("../../src/api/executionApi", () => ({
  executionApi: {
    listSubjects: vi.fn(),
    addSubject: vi.fn(),
    changeSubjectState: vi.fn(),
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

describe("Self-service trial site workflows across StudyCanvasView, Participants, and SiteActivationPanel", () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;
  let server: ReturnType<typeof setupServer>;

  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    localStorage.clear();
    webDemoStore.reset();
    server = setupServer(
      ...buildWebGatewayHandlers({ forceDemo: true, store: webDemoStore })
    );
    server.listen({ onUnhandledRequest: "error" });
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
        { path: "/plans", name: "plans", component: { template: "<div />" } },
        { path: "/ecrf", name: "ecrf", component: { template: "<div />" } },
      ],
    });

    vi.mocked(executionApi.listSubjects).mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    vi.mocked(executionApi.addSubject).mockReset();
    vi.mocked(executionApi.getSiteComplianceStatus).mockReset();
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    server.close();
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  async function mountCanvas(url: string) {
    await router.push(url);
    await router.isReady();
    const wrapper = mount(
      { template: "<router-view />" },
      { global: { plugins: [pinia, router] }, attachTo: document.body }
    );
    await flushPromises();
    return wrapper;
  }

  it("registers a site under a SiteInstitution and links it to a newly created study in StudyCanvasView, and findStudySites lists it", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = {
      id: "founder-01",
      username: "founder",
      email: "",
      firstName: "",
      lastName: "",
      tenantId: "trial-a1b2c3d4e5f6",
    };
    auth.rawRoles = ["Sponsor Admin", "Sponsor Designer", "Trial Coordinator"];

    // Create a fresh study CADENCE-202 with zero pre-seeded sites
    await kernelApi.createStudy({
      code: "CADENCE-202",
      title: "Self-Service Trial Feasibility Study",
      phase: "Phase 2",
      sponsorCode: "FOUNDER-BIO",
      sponsorName: "Founder Biotech",
      reason: "Create trial study",
      idempotencyKey: "idem-create-cadence-202",
    });

    expect(await findStudySites("CADENCE-202")).toEqual([]);

    const wrapper = await mountCanvas("/studies/CADENCE-202");
    expect(wrapper.find("#nav-sites").exists()).toBe(true);
    await wrapper.find("#nav-sites").trigger("click");
    await flushPromises();
    expect(wrapper.find("#sites-section").exists()).toBe(true);
    await vi.waitFor(() => {
      expect(document.activeElement?.id).toBe("sites-section-title");
      expect(
        (wrapper.find("#btn-refresh-sites").element as HTMLButtonElement)
          .disabled
      ).toBe(false);
    });

    await wrapper.find("#register-site-institution-choice").setValue("__new__");
    await flushPromises();
    await wrapper.find("#register-site-code").setValue("SITE-01");
    await wrapper.find("#register-site-country").setValue("US");
    await wrapper
      .find("#register-site-name")
      .setValue("Metro Cardiology Research Center");
    await wrapper
      .find("#register-site-institution-name")
      .setValue("Metro University Hospital");
    await wrapper
      .find("#register-site-reason")
      .setValue("Register primary investigational site");
    await wrapper.find("#register-site-form").trigger("submit");

    await vi.waitFor(() => {
      expect(wrapper.find("#study-site-SITE-01").exists()).toBe(true);
    });
    expect(wrapper.find("#study-sites-table").text()).toContain(
      "Metro Cardiology Research Center"
    );
    expect(wrapper.find("#study-sites-table").text()).toContain(
      "Metro University Hospital"
    );

    const sites = await findStudySites("CADENCE-202");
    expect(sites).toContainEqual({
      code: "SITE-01",
      label: "SITE-01 — Metro Cardiology Research Center",
    });

    const siteOrgs = await kernelApi.listOrganizations("SiteInstitution");
    expect(siteOrgs.some((o) => o.name === "Metro University Hospital")).toBe(
      true
    );

    wrapper.unmount();
  });

  it("displays #btn-add-participant for Trial Coordinator founder, offers linked-site picker and inline site shortcut, and hides it for Sponsor Admin alone", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.user = {
      id: "sponsor-only-01",
      username: "sponsor",
      email: "",
      firstName: "",
      lastName: "",
      tenantId: "cadence-local",
    };

    // 1. Non-trial Sponsor Admin without Trial Coordinator does NOT see #btn-add-participant
    auth.rawRoles = ["Sponsor Admin", "Sponsor Designer"];
    await kernelApi.createStudy({
      code: "CADENCE-303",
      title: "Participant Gating Study",
      phase: "Phase 2",
      sponsorCode: "SPONSOR-303",
      sponsorName: "Sponsor 303",
      reason: "Create study",
      idempotencyKey: "idem-create-cadence-303",
    });

    const sponsorWrapper = await mountCanvas(
      "/studies/CADENCE-303?stage=conduct&section=participants"
    );
    expect(sponsorWrapper.find("#btn-add-participant").exists()).toBe(false);
    sponsorWrapper.unmount();

    // 2. Trial founder holding Trial Coordinator sees #btn-add-participant, opens AddParticipantForm, creates a site inline, and screens SUBJ-001
    auth.user.tenantId = "trial-a1b2c3d4e5f6";
    auth.rawRoles = ["Sponsor Admin", "Sponsor Designer", "Trial Coordinator"];
    vi.mocked(executionApi.addSubject).mockResolvedValue({
      id: "0192f3c4-5e6a-7b8c-9d0e-000000000099",
    });

    const founderWrapper = await mountCanvas(
      "/studies/CADENCE-303?stage=conduct&section=participants"
    );
    const addBtn = founderWrapper.find("#btn-add-participant");
    expect(addBtn.exists()).toBe(true);
    await addBtn.trigger("click");
    await flushPromises();

    expect(founderWrapper.find("#add-participant-form").exists()).toBe(true);
    await vi.waitFor(() => {
      expect(document.activeElement?.id).toBe("add-participant-code");
    });
    await founderWrapper.find("#btn-add-participant-new-site").trigger("click");
    await flushPromises();
    await vi.waitFor(() => {
      expect(document.activeElement?.id).toBe("inline-site-code");
    });

    await founderWrapper.find("#inline-site-code").setValue("SITE-05");
    await founderWrapper
      .find("#inline-site-name")
      .setValue("Harbor Clinical Site");
    await founderWrapper.find("#inline-site-country").setValue("US");
    await founderWrapper
      .find("#inline-site-institution")
      .setValue("Harbor Health Institute");
    await founderWrapper
      .find("#inline-site-reason")
      .setValue("Register site inline during screening");
    await founderWrapper.find("#btn-confirm-inline-site").trigger("click");

    await vi.waitFor(() => {
      const siteSelect = founderWrapper.find("select#add-participant-site");
      expect(siteSelect.exists()).toBe(true);
      expect((siteSelect.element as HTMLSelectElement).value).toBe("SITE-05");
      expect(document.activeElement?.id).toBe("add-participant-site");
    });

    await founderWrapper.find("#add-participant-code").setValue("SUBJ-001");
    await founderWrapper
      .find("#add-participant-reason")
      .setValue("Screen first trial participant");
    await founderWrapper.find("#add-participant-form").trigger("submit");

    await vi.waitFor(() => {
      expect(executionApi.addSubject).toHaveBeenCalledWith({
        studyId: "CADENCE-303",
        subjectCode: "SUBJ-001",
        siteId: "SITE-05",
        reason: "Screen first trial participant",
      });
    });

    founderWrapper.unmount();
  });

  it("populates SiteActivationPanel picker with linked study sites and supports inline site registration", async () => {
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Admin", "Sponsor Designer", "Trial Coordinator"];

    await kernelApi.createStudy({
      code: "CADENCE-404",
      title: "Site Activation Study",
      phase: "Phase 2",
      sponsorCode: "SPONSOR-404",
      sponsorName: "Sponsor 404",
      reason: "Create study",
      idempotencyKey: "idem-create-cadence-404",
    });

    const wrapper = mount(SiteActivationPanel, {
      props: { studyCode: "CADENCE-404" },
      global: { plugins: [pinia] },
      attachTo: document.body,
    });
    await flushPromises();

    await wrapper.find("#btn-site-activation-new-site").trigger("click");
    await vi.waitFor(() => {
      expect(document.activeElement?.id).toBe("site-activation-new-code");
    });
    await wrapper.find("#site-activation-new-code").setValue("SITE-404");
    await wrapper.find("#site-activation-new-country").setValue("DE");
    await wrapper
      .find("#site-activation-new-name")
      .setValue("Berlin Vascular Center");
    await wrapper
      .find("#site-activation-new-institution")
      .setValue("Berlin Charite Partner Clinic");
    await wrapper
      .find("#site-activation-new-reason")
      .setValue("Register German clinical site");
    await wrapper.find("#site-activation-create-site-form").trigger("submit");

    await vi.waitFor(() => {
      expect(
        (wrapper.find("#site-activation-site").element as HTMLInputElement)
          .value
      ).toBe("SITE-404");
      expect(document.activeElement?.id).toBe("site-activation-site-picker");
    });
    const optionTexts = wrapper
      .findAll("#site-activation-site-picker option")
      .map((opt) => opt.text());
    expect(optionTexts).toContain("SITE-404 — Berlin Vascular Center");

    wrapper.unmount();
  });
});
