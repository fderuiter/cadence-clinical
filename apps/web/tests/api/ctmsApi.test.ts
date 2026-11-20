import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ctmsApi } from "../../src/api/ctmsApi";
import AdaptiveDoseEscalationConsole from "../../src/components/clinical/AdaptiveDoseEscalationConsole.vue";
import { useToastStore } from "../../src/stores/toasts.js";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("ctmsApi typed OpenAPI wrapper", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  it("evaluates and certifies site startup greenlight with encoded X-Change-Reason", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        jsonResponse({
          study_id: "CAD-101",
          site_id: "SITE-01",
          country_code: "US",
          overall_status: "READY",
          is_ready_for_certification: true,
          facets: [],
          blocking_reasons: [],
          evaluated_at: "2026-06-15T00:00:00Z",
        })
      )
      .mockResolvedValueOnce(
        jsonResponse({
          study_id: "CAD-101",
          site_id: "SITE-01",
          country_code: "US",
          overall_status: "CERTIFIED",
          is_ready_for_certification: true,
          certified_by: "dm_01",
          certified_at: "2026-06-15T01:00:00Z",
          facets: [],
          blocking_reasons: [],
          evaluated_at: "2026-06-15T01:00:00Z",
        })
      );

    const evaluated = await ctmsApi.evaluateSiteGreenlight(
      "SITE-01",
      "CAD-101",
      "US"
    );
    expect(evaluated.overall_status).toBe("READY");

    const certified = await ctmsApi.certifySiteGreenlight(
      "SITE-01",
      "CAD-101",
      "All six startup facets verified"
    );
    expect(certified.overall_status).toBe("CERTIFIED");

    const req = fetchSpy.mock.calls[1][0] as Request;
    expect(req.url).toContain(
      "/api/v1/ctms/startup/sites/SITE-01/greenlight/certify"
    );
    expect(req.headers.get("X-Change-Reason")).toBeTruthy();
  });

  it("gates AdaptiveDoseEscalationConsole cohort transitions behind demoBuild with /plans roadmap link", async () => {
    const wrapper = mount(AdaptiveDoseEscalationConsole, {
      props: { studyId: "CAD-101" },
    });

    const unbackedNote = wrapper.find("#adaptive-dose-unbacked-note");
    expect(unbackedNote.text()).toContain("plans & product-family roadmap");
    expect(unbackedNote.find('a[href="/plans"]').exists()).toBe(true);

    await wrapper.find(".btn-primary-action").trigger("click");
    expect(wrapper.find(".modal-backdrop").exists()).toBe(false);
    expect(useToastStore().toasts[0]?.title).toContain(
      "Cohort dose escalation decision isn't available yet"
    );
  });
});
