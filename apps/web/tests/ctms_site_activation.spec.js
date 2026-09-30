import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useToastStore } from "../src/stores/toasts.js";
import { executionApi } from "../src/api/executionApi";
import apiClient from "../src/services/api";
import CtmsView from "../src/views/CtmsView.vue";

const routeQuery = { value: {} };

vi.mock("vue-router", () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ path: "/ctms", query: routeQuery.value }),
}));

vi.mock("../src/api/executionApi", () => ({
  executionApi: {
    getSiteComplianceStatus: vi.fn(),
    activateSite: vi.fn(),
  },
}));

function status(overrides = {}) {
  return {
    study_id: "CAD-101",
    site_id: "SITE-7",
    milestone: "SITE_ACTIVATION",
    is_complete: true,
    missing_documents: [],
    ...overrides,
  };
}

function httpError(status, detail) {
  const err = new Error(detail ?? `Request failed with status ${status}`);
  err.status = status;
  if (detail) err.data = { title: "Bad Request", detail };
  return err;
}

// Outside a demo build the CTMS screen is site activation against Execution; nothing else there has a backend.
describe("CTMS site activation (Execution)", () => {
  let pinia;

  beforeEach(() => {
    routeQuery.value = {};
    pinia = createPinia();
    setActivePinia(pinia);
    vi.mocked(executionApi.getSiteComplianceStatus).mockReset();
    vi.mocked(executionApi.activateSite).mockReset();
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

  it("shows only site activation and calls none of the CTMS endpoints no service serves", async () => {
    const wrapper = await mountView();

    expect(wrapper.find("#site-activation-panel").exists()).toBe(true);
    expect(wrapper.find("#tab-operations").exists()).toBe(false);
    expect(wrapper.find("#kpi-total-subjects").exists()).toBe(false);
    expect(apiClient.get).not.toHaveBeenCalled();
    expect(executionApi.getSiteComplianceStatus).not.toHaveBeenCalled();
    expect(wrapper.find("#site-compliance-status").text()).toContain(
      "Enter a study and site"
    );
    expect(
      wrapper.find("#btn-check-site-compliance").attributes()
    ).toHaveProperty("disabled");
    expect(wrapper.text()).toContain("aren't available yet");
  });

  it("starts with no study or site outside a demo build", () => {
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
    expect(wrapper.find("#site-compliance-status").text()).toContain(
      "Complete: eTMF reports every activation document for site SITE-7 in CAD-101 approved."
    );
    expect(wrapper.find("#btn-activate-site").text()).toBe(
      "Activate site SITE-7"
    );
  });

  it("checks a study and site the user enters", async () => {
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status()
    );
    const wrapper = await mountView();

    await wrapper.find("#site-activation-study").setValue(" CAD-101 ");
    await wrapper.find("#site-activation-site").setValue("SITE-7");
    await wrapper.find("#site-activation-scope").trigger("submit");
    await flushPromises();

    expect(executionApi.getSiteComplianceStatus).toHaveBeenCalledWith(
      "SITE-7",
      "CAD-101"
    );
    const store = useClinicalStore();
    expect(store.activeStudyId).toBe("CAD-101");
    expect(store.activeSiteId).toBe("SITE-7");
  });

  it("lists the missing documents and offers no activation for an incomplete site", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
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
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
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
    vi.mocked(executionApi.getSiteComplianceStatus).mockRejectedValueOnce(
      httpError(503)
    );

    const wrapper = await mountView();

    const text = wrapper.find("#site-compliance-status").text();
    expect(text).toContain("couldn't be loaded");
    expect(text).not.toContain("Incomplete");
    expect(wrapper.find("#btn-activate-site").exists()).toBe(false);
    expect(useToastStore().toasts[0]).toMatchObject({
      tone: "error",
      title: "Couldn't load the site's eTMF status",
    });
  });

  it("activates a complete site with the reason given", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status()
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
    expect(wrapper.find("#site-activation-reason").element.value).toBe("");
  });

  it("reports a refused activation and keeps the reason", async () => {
    routeQuery.value = { studyId: "CAD-101", siteId: "SITE-7" };
    vi.mocked(executionApi.getSiteComplianceStatus).mockResolvedValueOnce(
      status()
    );
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

  it("keeps the full CTMS workspace in a demo build", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    vi.mocked(apiClient.get).mockResolvedValue([]);

    const wrapper = await mountView();

    expect(wrapper.find("#site-activation-panel").exists()).toBe(false);
    expect(wrapper.find("#tab-operations").exists()).toBe(true);
  });
});
