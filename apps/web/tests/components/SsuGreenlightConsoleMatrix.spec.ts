import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { nextTick } from "vue";
import {
  ctmsApi,
  type SiteGreenlightGateResponse,
} from "../../src/api/ctmsApi";
import SsuGreenlightConsole from "../../src/components/clinical/SsuGreenlightConsole.vue";
import { useAuthStore } from "../../src/stores/auth";
import { useToastStore } from "../../src/stores/toasts";
import { signInAs } from "../helpers/signIn.js";

const SAMPLE_MATRIX: SiteGreenlightGateResponse[] = [
  {
    id: "gate-site-101",
    study_id: "PROTOCOL-2026-01",
    site_id: "SITE-101",
    site_name: "Memorial Clinical Research Center",
    country_code: "US",
    overall_status: "READY",
    readiness_percentage: 100,
    facet_regulatory_irb: true,
    facet_cta_budget: true,
    facet_eisf_baseline: true,
    facet_doa_log: true,
    facet_staff_training: true,
    facet_ip_release: true,
    is_compliant: true,
    greenlight_token: null,
    greenlight_certified_by: null,
    greenlight_certified_at: null,
    is_overridden: false,
    unfulfilled_reasons: [],
    is_ready_for_certification: true,
  },
  {
    id: "gate-site-102",
    study_id: "PROTOCOL-2026-01",
    site_id: "SITE-102",
    site_name: "University Cardiology Site",
    country_code: "US",
    overall_status: "BLOCKED",
    readiness_percentage: 67,
    facet_regulatory_irb: false,
    facet_cta_budget: true,
    facet_eisf_baseline: true,
    facet_doa_log: true,
    facet_staff_training: true,
    facet_ip_release: false,
    is_compliant: false,
    greenlight_token: null,
    greenlight_certified_by: null,
    greenlight_certified_at: null,
    is_overridden: false,
    unfulfilled_reasons: ["IRB approval pending", "RTSM depot not ready"],
    is_ready_for_certification: false,
  },
];

describe("SsuGreenlightConsole Country-Level Multi-Site Activation Matrix", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["Data Manager"], { username: "sponsor_dm_01" });
    vi.spyOn(ctmsApi, "getCountryMatrix").mockResolvedValue(
      structuredClone(SAMPLE_MATRIX)
    );
    vi.spyOn(ctmsApi, "batchCertifyCountrySites").mockResolvedValue({
      success: true,
      study_id: "PROTOCOL-2026-01",
      country_code: "US",
      certified_count: 1,
      total_certified: 1,
      message: "Batch certification completed.",
      certified_sites: [
        {
          site_id: "SITE-101",
          site_name: "Memorial Clinical Research Center",
          greenlight_token: "server-signed-token-site-101",
          certified_at: "2026-10-05T12:00:00Z",
          certified_by: "sponsor_dm_01",
          status: "APPROVED",
        },
      ],
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function mountMatrix() {
    const wrapper = mount(SsuGreenlightConsole, {
      props: {
        studyId: "PROTOCOL-2026-01",
        countryCode: "US",
      },
      global: { plugins: [pinia] },
    });
    await flushPromises();
    await nextTick();
    return wrapper;
  }

  it("renders the country matrix view with summary cards and site table from ctmsApi", async () => {
    const wrapper = await mountMatrix();

    expect(ctmsApi.getCountryMatrix).toHaveBeenCalledWith(
      "PROTOCOL-2026-01",
      "US"
    );
    expect(wrapper.text()).toContain("Country Multi-Site Activation Matrix");
    expect(wrapper.text()).toContain("Total Country Sites");
    expect(wrapper.text()).toContain("Batch Certify Country Sites");
  });

  it("disables selection checkboxes for non-compliant sites with red indicator", async () => {
    const wrapper = await mountMatrix();

    const disabledCheckboxes = wrapper.findAll(
      ".site-select-checkbox:disabled"
    );
    expect(disabledCheckboxes.length).toBeGreaterThan(0);
    expect(wrapper.text()).toContain("Non-Compliant");
  });

  it("allows selecting fully-compliant sites and opens batch certification modal", async () => {
    const wrapper = await mountMatrix();

    await wrapper.find(".btn-select-all").trigger("click");
    const batchBtn = wrapper.find(".btn-batch-certify");
    expect(batchBtn.exists()).toBe(true);
    expect(batchBtn.attributes("disabled")).toBeUndefined();

    await batchBtn.trigger("click");
    await nextTick();

    expect(wrapper.text()).toContain(
      "21 CFR Part 11 Country-Level Batch Greenlight Certification"
    );
  });

  it("executes batch certification via ctmsApi and renders server-returned certification state", async () => {
    vi.mocked(ctmsApi.getCountryMatrix)
      .mockResolvedValueOnce(structuredClone(SAMPLE_MATRIX))
      .mockResolvedValueOnce([
        {
          ...SAMPLE_MATRIX[0],
          overall_status: "APPROVED",
          greenlight_certified_at: "2026-10-05T12:00:00Z",
          greenlight_certified_by: "sponsor_dm_01",
          greenlight_token: "server-signed-token-site-101",
        },
        SAMPLE_MATRIX[1],
      ]);

    const wrapper = await mountMatrix();
    await wrapper.find(".btn-select-all").trigger("click");
    await wrapper.find(".btn-batch-certify").trigger("click");
    await nextTick();

    await wrapper.find(".btn-submit-batch-certify").trigger("click");
    await flushPromises();
    await nextTick();

    expect(ctmsApi.batchCertifyCountrySites).toHaveBeenCalledTimes(1);
    expect(wrapper.find(".matrix-table").text()).toContain(
      "Greenlight Certified"
    );
    expect(wrapper.text()).toContain("server-signed-token-site-101");
    expect(wrapper.text()).not.toContain("hmac_sha256_token_");
  });

  it("surfaces HTTP failures without synthesizing fallback sites, APPROVED states, or hmac_sha256_token_ strings", async () => {
    vi.mocked(ctmsApi.getCountryMatrix).mockRejectedValueOnce(
      new Error("Country matrix endpoint unavailable (HTTP 503)")
    );

    const wrapper = await mountMatrix();

    expect(wrapper.find("#matrix-load-error").exists()).toBe(true);
    const tableText = wrapper.find(".matrix-table").text();
    expect(tableText).not.toContain("SITE-101");
    expect(tableText).not.toContain("Greenlight Certified");
    expect(wrapper.text()).not.toContain("hmac_sha256_token_");
    expect(useToastStore().toasts.some((t) => t.tone === "error")).toBe(true);
  });

  it("disables batch greenlight certification for CRA and Monitor roles with clear role explanation", async () => {
    signInAs(useAuthStore(), ["CRA"], { username: "cra_01" });
    const wrapper = await mountMatrix();

    await wrapper.find(".btn-select-all").trigger("click");
    expect(
      wrapper.find(".btn-batch-certify").attributes("disabled")
    ).toBeDefined();
    expect(wrapper.find("#greenlight-certify-role-note").text()).toContain(
      "ctms_greenlight:certify"
    );
  });
});
