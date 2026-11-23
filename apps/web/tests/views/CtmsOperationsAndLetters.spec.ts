import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ctmsApi } from "../../src/api/ctmsApi";
import apiClient from "../../src/services/api";
import { useAuthStore } from "../../src/stores/auth";
import CtmsView from "../../src/views/CtmsView.vue";
import { signInAs } from "../helpers/signIn.js";

vi.mock("vue-router", () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({
    path: "/ctms",
    query: { studyId: "CAD-101", siteId: "SITE-101", tab: "monitoring" },
  }),
}));

describe("CtmsMonitoringSection in live CTMS workspace: structured findings, compiled letters, and CRA/Monitor split", () => {
  let pinia: ReturnType<typeof createPinia>;

  const scheduledVisit = {
    id: "visit-sched-2",
    study_id: "CAD-101",
    site_id: "SITE-101",
    cra_id: "cra_alice",
    visit_type: "IMV",
    scheduled_date: "2026-10-12T00:00:00Z",
    actual_date: null,
    status: "SCHEDULED",
    findings: [],
    created_at: "2026-10-01T00:00:00Z",
    created_by: "cra_alice",
    reason_for_change: "Scheduled IMV",
    version_index: 1,
  };

  const completedVisit = {
    id: "visit-comp-1",
    study_id: "CAD-101",
    site_id: "SITE-101",
    cra_id: "cra_alice",
    visit_type: "SIV",
    scheduled_date: "2026-09-10T00:00:00Z",
    actual_date: "2026-09-11T00:00:00Z",
    status: "COMPLETED",
    findings: [
      {
        text: "Temperature log signature missing on Day 3",
        severity: "MAJOR",
        resolution_status: "OPEN",
      },
    ],
    created_at: "2026-09-01T00:00:00Z",
    created_by: "cra_alice",
    reason_for_change: "Completed SIV",
    version_index: 2,
  };

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.spyOn(apiClient, "get");
    vi.spyOn(ctmsApi, "listMonitoringVisits").mockResolvedValue([
      completedVisit,
      scheduledVisit,
    ]);
    vi.spyOn(ctmsApi, "completeMonitoringVisit").mockResolvedValue({
      ...scheduledVisit,
      status: "COMPLETED",
      actual_date: "2026-10-12T17:00:00Z",
    });
    vi.spyOn(ctmsApi, "signOffMonitoringVisit").mockResolvedValue({
      ...completedVisit,
      status: "SIGNED_OFF",
    });
    vi.spyOn(ctmsApi, "compileVisitLetter").mockResolvedValue({
      letterId: "LTR-ABCD1234",
      visitId: "visit-comp-1",
      letterType: "follow_up_letter",
      pdfSha256Hash: "a1b2c3d4e5f6", // pragma: allowlist secret
      pdfObjectKey: "letters/CAD-101/SITE-101/LTR-ABCD1234.pdf",
      isSigned: true,
      status: "FINALIZED",
      signatureManifestation: "Signed by cra_alice",
      etmfDocumentId: "ETMF-LTR-001",
      dispatchedAt: "2026-10-12T12:00:00Z",
      pdfAccessUrl: "/api/v1/ctms/letters/LTR-ABCD1234/pdf",
      pdfBase64: "JVBERi0xLjc=",
    });
    vi.spyOn(apiClient, "post").mockResolvedValue({
      sig_token: "sig-token-letter-1",
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("enforces CRA execution (complete visit with structured findings + compile signed letter) vs Monitor sign-off", async () => {
    signInAs(useAuthStore(), ["CRA"], { username: "cra_alice" });

    const wrapper = mount(CtmsView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find("#ctms-monitoring-section").exists()).toBe(true);
    expect(apiClient.get).not.toHaveBeenCalled();

    // CRA can complete scheduled visits and compile letters, while supervisory Sign-off is gated
    expect(
      wrapper.find('[data-testid="btn-signoff-visit-visit-comp-1"]').exists()
    ).toBe(false);
    expect(wrapper.find("#ctms-visit-signoff-role-note").text()).toContain(
      "ctms_monitoring_visit:sign_off"
    );

    // Complete a selected SCHEDULED visit with structured findings
    await wrapper
      .find('[data-testid="btn-complete-visit-visit-sched-2"]')
      .trigger("click");
    expect(wrapper.find("#modal-complete-visit").exists()).toBe(true);

    await wrapper.find("#btn-add-visit-finding").trigger("click");
    await wrapper
      .find("#finding-text-0")
      .setValue("Informed consent version date mismatch on Subject 004");
    await wrapper.find("#finding-severity-0").setValue("MAJOR");
    await wrapper.find("#finding-resolution-0").setValue("IN_PROGRESS");
    await wrapper
      .find("#complete-visit-reason")
      .setValue("Completed on-site IMV review");
    await wrapper.find("#btn-confirm-complete-visit").trigger("click");
    await flushPromises();

    expect(ctmsApi.completeMonitoringVisit).toHaveBeenCalledWith(
      "visit-sched-2",
      expect.objectContaining({
        findings: [
          {
            text: "Informed consent version date mismatch on Subject 004",
            severity: "MAJOR",
            resolution_status: "IN_PROGRESS",
          },
        ],
        reason_for_change: "Completed on-site IMV review",
      }),
      "Completed on-site IMV review"
    );

    // Compile a signed follow-up letter via POST /api/v1/ctms/letters/compile
    await wrapper
      .find('[data-testid="btn-compile-letter-visit-comp-1"]')
      .trigger("click");
    expect(wrapper.find("#modal-compile-letter").exists()).toBe(true);
    await wrapper.find("#compile-sig-password").setValue("Password123!");
    await wrapper
      .find("#compile-letter-reason")
      .setValue("Issue signed SIV follow-up report");
    await wrapper.find("#btn-confirm-compile-letter").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        action: "/api/v1/ctms/letters/compile",
        semantic_action: "ctms.letter.compile",
      })
    );
    expect(ctmsApi.compileVisitLetter).toHaveBeenCalledWith(
      "follow_up_letter",
      expect.objectContaining({
        studyId: "CAD-101",
        siteId: "SITE-101",
        visitId: "visit-comp-1",
        finalize: true,
        findings: [
          {
            text: "Temperature log signature missing on Day 3",
            severity: "MAJOR",
            resolutionStatus: "OPEN",
          },
        ],
      }),
      expect.objectContaining({
        changeReason: "Issue signed SIV follow-up report",
        sigToken: "sig-token-letter-1",
      })
    );
    expect(wrapper.find("#ctms-compiled-letters-container").text()).toContain(
      "LTR-ABCD1234"
    );
    expect(wrapper.find("#ctms-compiled-letters-container").text()).toContain(
      "ETMF-LTR-001"
    );
  });

  it("allows Monitor role to perform supervisory visit sign-off while gating CRA scheduling and letter compilation", async () => {
    signInAs(useAuthStore(), ["Monitor"], { username: "monitor_lead" });

    const wrapper = mount(CtmsView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find("#schedule-visit-form").exists()).toBe(false);
    expect(
      wrapper.find('[data-testid="btn-compile-letter-visit-comp-1"]').exists()
    ).toBe(false);
    expect(wrapper.find("#ctms-visit-schedule-role-note").text()).toContain(
      "ctms_monitoring_visit:create"
    );

    const signOffBtn = wrapper.find(
      '[data-testid="btn-signoff-visit-visit-comp-1"]'
    );
    expect(signOffBtn.exists()).toBe(true);
    await signOffBtn.trigger("click");
    await wrapper.find("#signoff-visit-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper
      .find("#signoff-visit-reason")
      .setValue("Supervisory MVR review complete");
    await wrapper.find("#btn-confirm-signoff-visit").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        action: "/api/v1/ctms/monitoring-visits/visit-comp-1/sign-off",
        semantic_action: "ctms.monitoring_visit.sign_off",
      })
    );
    expect(ctmsApi.signOffMonitoringVisit).toHaveBeenCalledWith(
      "visit-comp-1",
      "Supervisory MVR review complete",
      "sig-token-letter-1"
    );
  });

  it("compiles a draft visit letter (finalize=false) without calling step-up signature verification", async () => {
    signInAs(useAuthStore(), ["CRA"], { username: "cra_alice" });

    const wrapper = mount(CtmsView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    await wrapper
      .find('[data-testid="btn-compile-letter-visit-comp-1"]')
      .trigger("click");
    await wrapper.find("#compile-letter-finalize").setValue(false);
    expect(wrapper.find("#compile-sig-password").exists()).toBe(false);

    await wrapper
      .find("#compile-letter-reason")
      .setValue("Draft SIV follow-up preview");
    await wrapper.find("#btn-confirm-compile-letter").trigger("click");
    await flushPromises();

    expect(apiClient.post).not.toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.anything()
    );
    expect(ctmsApi.compileVisitLetter).toHaveBeenCalledWith(
      "follow_up_letter",
      expect.objectContaining({
        visitId: "visit-comp-1",
        finalize: false,
      }),
      expect.objectContaining({
        changeReason: "Draft SIV follow-up preview",
        sigToken: undefined,
      })
    );
  });
});
