import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts.js";
import { ctmsApi } from "../src/api/ctmsApi";
import { executionApi } from "../src/api/executionApi";
import { kernelApi } from "../src/api/kernelApi";
import { kernelGovernanceApi } from "../src/api/kernelGovernanceApi";
import apiClient from "../src/services/api";
import CtmsView from "../src/views/CtmsView.vue";

vi.mock("../src/api/kernelApi", () => ({
  kernelApi: {
    search: vi.fn(async () => []),
  },
  describeKernelError: (err) => err?.message ?? String(err),
}));

vi.mock("../src/api/kernelGovernanceApi", () => ({
  kernelGovernanceApi: {
    listStudySites: vi.fn(async () => []),
    linkStudySite: vi.fn(),
    activateStudySite: vi.fn(),
  },
}));

const routeQuery = { value: {} };

vi.mock("vue-router", () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ path: "/ctms", query: routeQuery.value }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: {
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
    listCgtCertificates: vi.fn().mockResolvedValue([]),
    listCgtReconciliations: vi.fn().mockResolvedValue([]),
    getSiteIpKitInventory: vi.fn().mockResolvedValue([]),
    listRandomizationSchemes: vi.fn().mockResolvedValue([]),
    getChainOfCustody: vi.fn(),
    recordCgtApheresis: vi.fn(),
    seedSiteIpKitInventory: vi.fn(),
  },
}));

vi.mock("../src/api/ctmsApi", () => ({
  ctmsApi: {
    evaluateSiteGreenlight: vi.fn(),
    getCountryGreenlightMatrix: vi.fn().mockResolvedValue([]),
    getCountryMatrix: vi.fn().mockResolvedValue([]),
    certifySiteGreenlight: vi.fn(),
    batchCertifyCountrySites: vi.fn(),
    listSiteMilestones: vi.fn().mockResolvedValue([]),
    createSiteMilestone: vi.fn(),
    updateSiteMilestone: vi.fn(),
    listMonitoringVisits: vi.fn().mockResolvedValue([]),
    scheduleMonitoringVisit: vi.fn(),
    completeMonitoringVisit: vi.fn(),
    signOffMonitoringVisit: vi.fn(),
    listMonitoringVisitLetters: vi.fn().mockResolvedValue([]),
    getMonitoringVisitLetters: vi.fn().mockResolvedValue([]),
    compileVisitLetter: vi.fn(),
    downloadVisitLetterPdf: vi.fn(),
    getSiteDoaLog: vi.fn().mockResolvedValue({
      site_id: "SITE-7",
      delegated_staff: [],
      audit_history: [],
    }),
    delegateSiteTasks: vi.fn(),
    revokeSiteTasks: vi.fn(),
    signOffDelegation: vi.fn(),
    exportSiteDoaPdf: vi.fn(),
  },
}));

function status(overrides = {}) {
  return {
    study_id: "CAD-101",
    site_id: "SITE-7",
    milestone: "SITE_ACTIVATION",
    is_complete: true,
    missing_documents: [],
    is_activated: false,
    activated_at: null,
    activated_by: null,
    ...overrides,
  };
}

function httpError(statusCode, detail, code) {
  const err = new Error(detail ?? `Request failed with status ${statusCode}`);
  err.status = statusCode;
  if (detail || code) {
    err.data = { title: "Error", detail, ...(code ? { code } : {}) };
  }
  return err;
}

describe("CTMS site activation and live workspaces (Execution + CTMS)", () => {
  let pinia;

  beforeEach(() => {
    routeQuery.value = {};
    pinia = createPinia();
    setActivePinia(pinia);
    // Default to Sponsor Data Manager so tests exercising site activation hold site_compliance:activate
    useAuthStore().rawRoles = ["Data Manager"];
    vi.mocked(executionApi.getSiteComplianceStatus).mockReset();
    vi.mocked(executionApi.activateSite).mockReset();
    vi.mocked(kernelApi.search).mockReset();
    vi.mocked(kernelApi.search).mockResolvedValue([]);
    vi.mocked(kernelGovernanceApi.listStudySites).mockReset();
    vi.mocked(kernelGovernanceApi.listStudySites).mockResolvedValue([]);
    vi.mocked(kernelGovernanceApi.linkStudySite).mockReset();
    vi.mocked(kernelGovernanceApi.activateStudySite).mockReset();
    vi.mocked(ctmsApi.listSiteMilestones).mockReset();
    vi.mocked(ctmsApi.listSiteMilestones).mockResolvedValue([]);
    vi.mocked(ctmsApi.listMonitoringVisits).mockReset();
    vi.mocked(ctmsApi.listMonitoringVisits).mockResolvedValue([]);
    vi.spyOn(apiClient, "get");
    vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  async function mountView() {
    const wrapper = mount(CtmsView, { global: { plugins: [pinia] } });
    await flushPromises();
    return wrapper;
  }

  it("shows backed workspace tabs and calls none of the legacy unbacked CTMS endpoints", async () => {
    const wrapper = await mountView();

    expect(wrapper.find("#ctms-live-workspace").exists()).toBe(true);
    expect(wrapper.find("#site-activation-panel").exists()).toBe(true);
    expect(wrapper.find("#site-activation-site-picker").exists()).toBe(true);
    expect(wrapper.find("#ctms-tab-startup").exists()).toBe(true);
    expect(wrapper.find("#ctms-tab-milestones").exists()).toBe(true);
    expect(wrapper.find("#ctms-tab-monitoring").exists()).toBe(true);
    expect(wrapper.find("#ctms-tab-delegation").exists()).toBe(true);
    expect(wrapper.find("#ctms-tab-supply").exists()).toBe(true);
    expect(wrapper.find("#tab-operations").exists()).toBe(false);
    expect(wrapper.find("#kpi-total-subjects").exists()).toBe(false);
    expect(wrapper.find("#ctms-workload-container").exists()).toBe(false);
    expect(wrapper.find("#ctms-recruitment-container").exists()).toBe(false);
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(executionApi.getSiteComplianceStatus).not.toHaveBeenCalled();
    expect(ctmsApi.listSiteMilestones).not.toHaveBeenCalled();
    expect(wrapper.find("#site-compliance-status").text()).toContain(
      "Enter a study and site"
    );
    expect(
      wrapper.find("#btn-check-site-compliance").attributes()
    ).toHaveProperty("disabled");
    expect(wrapper.text()).toContain("aren't available yet");
    expect(wrapper.text()).not.toContain("#4829");
    expect(wrapper.find("#ctms-unbacked-scope-note").text()).toContain(
      "Orbit and Allocate roadmap"
    );
    expect(
      wrapper.find("#ctms-unbacked-scope-note a").attributes("href")
    ).toMatch(/\/plans$/);
  });

  it("starts with no study or site outside a demo build when no claims are assigned", () => {
    const store = useClinicalStore();
    expect(store.activeStudyId).toBe("");
    expect(store.activeSiteId).toBe("");
  });

  it("checks the study and site it was opened for", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status()
    );

    const wrapper = await mountView();

    expect(executionApi.getSiteComplianceStatus).toHaveBeenCalledWith(
      "SITE-7",
      "CAD-101"
    );
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(wrapper.find("#site-compliance-status").text()).toContain(
      "Complete: eTMF reports every activation document for site SITE-7 in CAD-101 approved."
    );
    expect(wrapper.find("#btn-activate-site").text()).toBe(
      "Activate site SITE-7"
    );
  });

  it("hides Activate site for read-only clinical roles (CRA, CRC, Investigator, Auditor)", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    for (const role of ["CRA", "CRC", "Site Investigator", "Auditor"]) {
      pinia = createPinia();
      setActivePinia(pinia);
      useAuthStore().rawRoles = [role];
      vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
        status()
      );

      const wrapper = await mountView();

      expect(wrapper.find("#site-compliance-status").text(), role).toContain(
        "Complete: eTMF reports every activation document"
      );
      expect(wrapper.find("#btn-activate-site").exists(), role).toBe(false);
      expect(
        wrapper.find("#site-activation-readonly-note").exists(),
        role
      ).toBe(true);
      expect(
        wrapper.find("#site-activation-readonly-note").text(),
        role
      ).toContain("site_compliance:activate");
      wrapper.unmount();
    }
  });

  it("renders persistent Activated summary and hides Activate site when compliance.is_activated is true", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status({
        is_activated: true,
        activated_by: "dm_sponsor_01",
        activated_at: "2026-06-15T10:30:00Z",
      })
    );

    const wrapper = await mountView();

    expect(wrapper.find("#site-activated-summary").exists()).toBe(true);
    expect(wrapper.find("#site-activated-summary").text()).toContain(
      "Activated"
    );
    expect(wrapper.find("#site-activated-summary").text()).toContain(
      "dm_sponsor_01"
    );
    expect(wrapper.find("#btn-activate-site").exists()).toBe(false);
  });

  it("renders UX-9 scope-assignment guidance on HTTP 403 FORBIDDEN_SCOPE instead of claiming eTMF failed to load", async () => {
    routeQuery.value = { studyId: "STUDY-OTHER", siteId: "SITE-99" };
    const auth = useAuthStore();
    auth.assignedStudies = ["CAD-101"];
    auth.assignedSites = ["SITE-7"];
    vi.mocked(executionApi.getSiteComplianceStatus).mockRejectedValueOnce(
      httpError(
        403,
        "Caller is not assigned to study 'STUDY-OTHER'.",
        "FORBIDDEN_SCOPE"
      )
    );

    const wrapper = await mountView();

    const text = wrapper.find("#site-compliance-status").text();
    expect(text).toContain("Scope access restricted");
    expect(text).toContain("Select an assigned study and site");
    expect(text).toContain("CAD-101");
    expect(text).not.toContain("couldn't be loaded");
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("populates scoped study/site pickers from assigned token claims and defaults scope", async () => {
    const auth = useAuthStore();
    auth.assignedStudies = ["CAD-101", "CAD-202"];
    auth.assignedSites = ["SITE-7"];
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status()
    );

    const wrapper = await mountView();

    expect(executionApi.getSiteComplianceStatus).toHaveBeenCalledWith(
      "SITE-7",
      "CAD-101"
    );
    expect(wrapper.find("#site-activation-assigned-scope").text()).toContain(
      "CAD-202"
    );
  });

  it("switches to Startup readiness, Milestones, Monitoring, Delegation, and Supply tabs outside demo mode", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status()
    );
    vi.mocked(ctmsApi.evaluateSiteGreenlight).mockResolvedValueOnce({
      study_id: "CAD-101",
      site_id: "SITE-7",
      country_code: "US",
      overall_status: "READY",
      is_ready_for_certification: true,
      facets: [],
      blocking_reasons: [],
      evaluated_at: "2026-06-15T10:00:00Z",
    });

    const wrapper = await mountView();

    await wrapper.find("#ctms-tab-startup").trigger("click");
    await flushPromises();
    expect(wrapper.find("#ctms-greenlight-section").exists()).toBe(true);
    expect(ctmsApi.getCountryGreenlightMatrix).toHaveBeenCalledWith(
      "CAD-101",
      "US"
    );

    await wrapper.find("#ctms-tab-milestones").trigger("click");
    await flushPromises();
    expect(wrapper.find("#ctms-milestones-section").exists()).toBe(true);
    expect(ctmsApi.listSiteMilestones).toHaveBeenCalled();

    await wrapper.find("#ctms-tab-monitoring").trigger("click");
    await flushPromises();
    expect(wrapper.find("#ctms-monitoring-section").exists()).toBe(true);
    expect(ctmsApi.listMonitoringVisits).toHaveBeenCalled();

    await wrapper.find("#ctms-tab-delegation").trigger("click");
    await flushPromises();
    expect(wrapper.find("#ctms-delegation-section").exists()).toBe(true);
    expect(ctmsApi.getSiteDoaLog).toHaveBeenCalledWith("SITE-7");

    await wrapper.find("#ctms-tab-supply").trigger("click");
    await flushPromises();
    expect(wrapper.find("#rtsm-supply-section").exists()).toBe(true);
    expect(wrapper.find("#rtsm-pharmacist-separation-note").exists()).toBe(
      true
    );
  });

  it("checks a study and site selected via the site picker or custom input", async () => {
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status({ site_id: "SITE-101" })
    );
    const wrapper = await mountView();

    await wrapper.find("#site-activation-study").setValue(" CAD-101 ");
    await wrapper.find("#site-activation-site-picker").setValue("SITE-101");
    await wrapper.find("#site-activation-scope").trigger("submit");
    await flushPromises();

    expect(executionApi.getSiteComplianceStatus).toHaveBeenCalledWith(
      "SITE-101",
      "CAD-101"
    );
    const store = useClinicalStore();
    expect(store.activeStudyId).toBe("CAD-101");
    expect(store.activeSiteId).toBe("SITE-101");
  });

  it("lists the missing documents and offers no activation for an incomplete site", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue(
      status({
        is_complete: false,
        missing_documents: ["IRB approval", "Signed 1572"],
      })
    );

    const wrapper = await mountView();

    expect(wrapper.find("#site-compliance-status").text()).toContain(
      "Incomplete"
    );
    expect(
      wrapper.findAll("#site-missing-documents li").map((li) => li.text())
    ).toEqual(["IRB approval", "Signed 1572"]);
    expect(wrapper.find("#btn-activate-site").exists()).toBe(false);
  });

  it("says eTMF has not confirmed a site it reported nothing for", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue(
      status({ is_complete: false })
    );

    const wrapper = await mountView();

    expect(wrapper.find("#site-compliance-status").text()).toContain(
      "Not confirmed"
    );
    expect(wrapper.find("#btn-activate-site").exists()).toBe(false);
  });

  it("reports a failed status check instead of calling the site non-compliant", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockRejectedValue(
      httpError(503)
    );

    const wrapper = await mountView();

    const text = wrapper.find("#site-compliance-status").text();
    expect(text).toContain("couldn't be loaded");
    expect(text).not.toContain("Incomplete");
    expect(wrapper.find("#btn-activate-site").exists()).toBe(false);
    expect(
      useToastStore().toasts.some(
        (t) =>
          t.tone === "error" &&
          t.title === "Couldn't load the site's eTMF status"
      )
    ).toBe(true);
  });

  it("activates a complete site with the reason given and transitions to persistent Activated state", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus)
      .mockResolvedValueOnce(status())
      .mockResolvedValueOnce(
        status({
          is_activated: true,
          activated_by: "dm_user",
          activated_at: "2026-06-15T10:30:00Z",
        })
      );
    vi.mocked(executionApi.activateSite).mockResolvedValueOnce({
      status: "success",
      message: "Site SITE-7 activated successfully.",
    });
    const wrapper = await mountView();

    expect(wrapper.find("#btn-activate-site").attributes()).toHaveProperty(
      "disabled"
    );
    await wrapper
      .find("#site-activation-reason")
      .setValue("  IRB approval and 1572 on file.  ");
    await wrapper.find("#btn-activate-site").trigger("click");
    await flushPromises();

    expect(executionApi.activateSite).toHaveBeenCalledWith(
      "SITE-7",
      "CAD-101",
      "IRB approval and 1572 on file."
    );
    expect(useToastStore().toasts[0]).toMatchObject({
      tone: "success",
      title: "Site SITE-7 activated",
      message: "Site SITE-7 activated successfully.",
    });
    expect(wrapper.find("#site-activated-summary").exists()).toBe(true);
    expect(wrapper.find("#btn-activate-site").exists()).toBe(false);
  });

  it("reports a refused activation and keeps the reason", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue(status());
    vi.mocked(executionApi.activateSite).mockRejectedValueOnce(
      httpError(400, "Site SITE-7 is not compliant for SITE_ACTIVATION.")
    );
    const wrapper = await mountView();

    await wrapper.find("#site-activation-reason").setValue("Ready.");
    await wrapper.find("#btn-activate-site").trigger("click");
    await flushPromises();

    const toast = useToastStore().toasts[0];
    expect(toast).toMatchObject({
      tone: "error",
      title: "Site SITE-7 was not activated",
    });
    expect(toast.message).toContain("not compliant");
    expect(wrapper.find("#site-activation-reason").element.value).toBe(
      "Ready."
    );
  });

  it("keeps demo-only tabs and KPIs in a demo build", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    vi.mocked(apiClient.get).mockResolvedValue([]);

    const wrapper = await mountView();

    expect(wrapper.find("#site-activation-panel").exists()).toBe(false);
    expect(wrapper.find("#tab-operations").exists()).toBe(true);
    expect(wrapper.find("#tab-financials").exists()).toBe(true);
    expect(wrapper.find("#tab-rtsm").exists()).toBe(true);
  });

  it("links and activates a study site in the Clinical Kernel when an effective protocol version exists", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    const auth = useAuthStore();
    auth.isAuthenticated = true;
    auth.rawRoles = ["Sponsor Admin"];
    const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1";
    const STUDY_SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-000000000077";

    const kernelSites = [];
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValue(status());
    vi.mocked(kernelApi.search).mockResolvedValue([
      {
        ref: STUDY_REF,
        kind: "study",
        title: "Cadence 101",
        code: "CAD-101",
        detail: null,
        exact_code: true,
        rank: 1,
      },
    ]);
    vi.mocked(kernelGovernanceApi.listStudySites).mockImplementation(
      async () => [...kernelSites]
    );
    vi.mocked(kernelGovernanceApi.linkStudySite).mockImplementation(
      async (payload) => {
        const linked = {
          study_site_ref: STUDY_SITE_REF,
          study_site_id: "0192f3c4-5e6a-7b8c-9d0e-000000000077",
          study_ref: STUDY_REF,
          site_ref: "site:0192f3c4-5e6a-7b8c-9d0e-000000000076",
          site_code: payload.siteCode,
          site_name: payload.siteName,
          country: payload.country,
          status: "Selected",
          effective_versions: [
            {
              protocol_version_ref:
                "protocol_version:0192f3c4-5e6a-7b8c-9d0e-0000000000b1",
              number: 1,
              effectivity_ref:
                "effectivity:0192f3c4-5e6a-7b8c-9d0e-0000000000d1",
              scope_level: "Study",
            },
          ],
        };
        kernelSites.push(linked);
        return linked;
      }
    );
    vi.mocked(kernelGovernanceApi.activateStudySite).mockImplementation(
      async () => {
        kernelSites[0] = { ...kernelSites[0], status: "Active" };
        return kernelSites[0];
      }
    );

    const wrapper = await mountView();

    expect(wrapper.find("#kernel-site-governance").exists()).toBe(true);
    await wrapper.find("#kernel-site-name").setValue("Metro Heart Institute");
    await wrapper.find("#kernel-site-country").setValue("US");
    await wrapper
      .find("#kernel-site-link-reason")
      .setValue("Link SITE-7 to CAD-101 in kernel");
    await wrapper.find("#btn-link-kernel-site").trigger("click");
    await flushPromises();

    expect(kernelGovernanceApi.linkStudySite).toHaveBeenCalledWith(
      expect.objectContaining({
        studyRef: STUDY_REF,
        siteCode: "SITE-7",
        siteName: "Metro Heart Institute",
        country: "US",
        reason: "Link SITE-7 to CAD-101 in kernel",
      })
    );

    expect(wrapper.find("#btn-activate-kernel-site").exists()).toBe(true);
    await wrapper
      .find("#kernel-site-activate-reason")
      .setValue("Activate SITE-7 in kernel with effective protocol v1");
    await wrapper.find("#btn-activate-kernel-site").trigger("click");
    await flushPromises();

    expect(kernelGovernanceApi.activateStudySite).toHaveBeenCalledWith(
      expect.objectContaining({
        studySiteRef: STUDY_SITE_REF,
        reason: "Activate SITE-7 in kernel with effective protocol v1",
      })
    );
    expect(wrapper.find("#kernel-site-active-banner").exists()).toBe(true);
  });

  it("passes studyCode=CADENCE-101 and siteId=SITE-101 props across all six live tabs when opened from /studies/CADENCE-101?site=SITE-101", async () => {
    // Seed stale store scope to verify URL parameters are primary authority
    const store = useClinicalStore();
    store.activeStudyId = "STALE-STUDY";
    store.activeSiteId = "STALE-SITE";
    useAuthStore().rawRoles = ["Sponsor Admin"];

    routeQuery.value = { studyId: "CADENCE-101", siteId: "SITE-101" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status({ study_id: "CADENCE-101", site_id: "SITE-101" })
    );
    vi.mocked(ctmsApi.evaluateSiteGreenlight).mockResolvedValueOnce({
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      country_code: "US",
      overall_status: "READY",
      is_ready_for_certification: true,
      facets: [],
      blocking_reasons: [],
      evaluated_at: "2026-06-15T10:00:00Z",
    });

    const wrapper = await mountView();

    // Tab 1: Activation
    expect(executionApi.getSiteComplianceStatus).toHaveBeenCalledWith(
      "SITE-101",
      "CADENCE-101"
    );
    expect(store.activeStudyId).toBe("CADENCE-101");
    expect(store.activeSiteId).toBe("SITE-101");

    // Tab 2: Startup readiness
    await wrapper.find("#ctms-tab-startup").trigger("click");
    await flushPromises();
    expect(ctmsApi.getCountryGreenlightMatrix).toHaveBeenCalledWith(
      "CADENCE-101",
      "US"
    );
    expect(ctmsApi.evaluateSiteGreenlight).toHaveBeenCalledWith(
      "SITE-101",
      "CADENCE-101",
      "US"
    );

    // Tab 3: Milestones
    await wrapper.find("#ctms-tab-milestones").trigger("click");
    await flushPromises();
    expect(ctmsApi.listSiteMilestones).toHaveBeenCalledWith({
      siteId: "SITE-101",
      studyId: "CADENCE-101",
    });

    // Tab 4: Monitoring
    await wrapper.find("#ctms-tab-monitoring").trigger("click");
    await flushPromises();
    expect(ctmsApi.listMonitoringVisits).toHaveBeenCalledWith({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
    });

    // Tab 5: Delegation
    await wrapper.find("#ctms-tab-delegation").trigger("click");
    await flushPromises();
    expect(ctmsApi.getSiteDoaLog).toHaveBeenCalledWith("SITE-101");

    // Tab 6: Supply (Sponsor Admin sees randomization schemes for CADENCE-101; Pharmacist sees kit inventory for SITE-101)
    useAuthStore().rawRoles = ["Sponsor Admin", "Pharmacist"];
    await wrapper.find("#ctms-tab-supply").trigger("click");
    await flushPromises();
    expect(executionApi.getSiteIpKitInventory).toHaveBeenCalledWith(
      "CADENCE-101",
      "SITE-101"
    );
    expect(executionApi.listRandomizationSchemes).toHaveBeenCalledWith(
      "CADENCE-101"
    );
  });
});
