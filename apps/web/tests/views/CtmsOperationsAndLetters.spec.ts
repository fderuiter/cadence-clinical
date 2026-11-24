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
    await wrapper.find("#signoff-visit-password").setValue("Password123!");
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

  it("renders persisted GeneratedVisitLetter rendered_content and etmf_document_id in #monitoring-letters-panel", async () => {
    vi.spyOn(ctmsApi, "listMonitoringVisitLetters").mockResolvedValue([
      {
        id: "LTR-PERSISTED-1",
        visit_id: "visit-comp-1",
        study_id: "CAD-101",
        site_id: "SITE-101",
        letter_type: "follow_up_letter",
        rendered_content: "Persisted SIV follow-up body for SITE-101",
        status: "FINALIZED",
        is_signed: true,
        pdf_sha256_hash: "deadbeef12345678", // pragma: allowlist secret
        etmf_document_id: "ETMF-050206-99",
        created_at: "2026-10-12T12:00:00Z",
        created_by: "cra_alice",
        reason_for_change: "Issued letter",
        version_index: 1,
      },
    ]);

    signInAs(useAuthStore(), ["CRA"], { username: "cra_alice" });
    const wrapper = mount(CtmsView, {
      global: { plugins: [pinia] },
    });
    await flushPromises();

    await wrapper
      .find('[data-testid="btn-view-letters-visit-comp-1"]')
      .trigger("click");
    await flushPromises();

    const panelText = wrapper.find("#monitoring-letters-panel").text();
    expect(panelText).toContain("Persisted SIV follow-up body for SITE-101");
    expect(panelText).toContain("ETMF-050206-99");
    expect(panelText).not.toContain("undefined");
  });
});

describe("CtmsGreenlightSection and CtmsDelegationSection OpenAPI DTO alignment & Part 11 X-Sig-Token step-up", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.spyOn(apiClient, "post").mockResolvedValue({
      sig_token: "sig-token-ctms-99",
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders six-facet greenlight gates, country matrix, and executes step-up signed single & batch certifications without TypeError", async () => {
    const { default: CtmsGreenlightSection } =
      await import("../../src/components/clinical/CtmsGreenlightSection.vue");

    const readyGate = {
      id: "gl-101",
      study_id: "CAD-101",
      site_id: "SITE-101",
      country_code: "US",
      overall_status: "READY",
      is_ready_for_certification: true,
      blocking_reasons: [],
      facets: [
        {
          facet_code: "IRB_APPROVAL",
          display_name: "IRB / EC Approval",
          is_satisfied: true,
          evidence_summary: "Approved in eTMF",
        },
        {
          facet_code: "EXECUTED_CTA",
          display_name: "Executed Clinical Trial Agreement",
          is_satisfied: true,
          evidence_summary: "Milestone COMPLETED",
        },
        {
          facet_code: "PI_FINANCIAL_DISCLOSURE",
          display_name: "PI Financial Disclosure",
          is_satisfied: true,
          evidence_summary: "Filed in eTMF",
        },
        {
          facet_code: "STAFF_TRAINING",
          display_name: "Protocol Training Qualification",
          is_satisfied: true,
          evidence_summary: "All delegated staff qualified",
        },
        {
          facet_code: "DOA_SIGNED",
          display_name: "PI Signed Delegation Log",
          is_satisfied: true,
          evidence_summary: "Signed DoA log active",
        },
        {
          facet_code: "LAB_NORMALS_BOUND",
          display_name: "Lab Normal Ranges & Pharmacy Readiness",
          is_satisfied: true,
          evidence_summary: "Active IP inventory verified",
        },
      ],
    };

    vi.spyOn(ctmsApi, "evaluateSiteGreenlight").mockResolvedValue(
      readyGate as any
    );
    vi.spyOn(ctmsApi, "getCountryGreenlightMatrix").mockResolvedValue([
      readyGate as any,
    ]);
    vi.spyOn(ctmsApi, "certifySiteGreenlight").mockResolvedValue({
      ...readyGate,
      overall_status: "CERTIFIED",
      certified_by: "dm_lead",
      certified_at: "2026-11-23T12:00:00Z",
    } as any);
    vi.spyOn(ctmsApi, "batchCertifyCountrySites").mockResolvedValue({
      study_id: "CAD-101",
      country_code: "US",
      total_certified: 1,
      certified_count: 1,
      certified_sites: ["SITE-101"],
      skipped_sites: [],
    } as any);

    signInAs(useAuthStore(), ["Data Manager"], { username: "dm_lead" });
    const wrapper = mount(CtmsGreenlightSection, {
      props: { studyCode: "CAD-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.findAll(".facet-row")).toHaveLength(6);
    expect(wrapper.text()).toContain("6 / 6");

    // Batch certification flow
    const matrixCheckbox = wrapper.find(
      'input[aria-label="Select SITE-101 for batch certification"]'
    );
    await matrixCheckbox.setValue(true);
    await wrapper
      .find("#batch-certify-reason")
      .setValue("US cohort ready for activation");
    await wrapper.find("#batch-certify-password").setValue("Password123!");
    await wrapper.find("#btn-batch-certify-greenlight").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        action: "/api/v1/ctms/startup/matrix/batch-certify",
        semantic_action: "ctms.greenlight.batch_certify",
      })
    );
    expect(ctmsApi.batchCertifyCountrySites).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CAD-101",
        country_code: "US",
        site_ids: ["SITE-101"],
        meaning: "US cohort ready for activation",
      }),
      "US cohort ready for activation",
      "sig-token-ctms-99"
    );

    // Single-site certification flow
    await wrapper
      .find("#greenlight-certify-reason")
      .setValue("Final greenlight review complete");
    await wrapper.find("#greenlight-certify-password").setValue("Password123!");
    await wrapper.find("#btn-certify-site-greenlight").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        action: "/api/v1/ctms/startup/sites/SITE-101/greenlight/certify",
        semantic_action: "ctms.greenlight.certify",
      })
    );
    expect(ctmsApi.certifySiteGreenlight).toHaveBeenCalledWith(
      "SITE-101",
      "CAD-101",
      "Final greenlight review complete",
      "US",
      "sig-token-ctms-99"
    );
  });

  it("renders PENDING_PI_SIGNATURE, ACTIVE, and REVOKED DoA status badges and executes PI X-Sig-Token sign-off", async () => {
    const { default: CtmsDelegationSection } =
      await import("../../src/components/clinical/CtmsDelegationSection.vue");

    vi.spyOn(ctmsApi, "getSiteDoaLog").mockResolvedValue({
      study_id: "CAD-101",
      site_id: "SITE-101",
      delegated_staff: [
        {
          record_id: "doa-rec-1",
          study_id: "CAD-101",
          site_id: "SITE-101",
          staff_user_id: "crc.site101",
          staff_name: "Jordan Lee, RN",
          role: "crc",
          task_codes: ["OBTAIN_INFORMED_CONSENT", "CRF_DATA_ENTRY"],
          duties: "Informed consent administration and eCRF entry",
          start_date: "2026-11-01",
          status: "PENDING_PI_SIGNATURE",
          signed_off: false,
        },
        {
          record_id: "doa-rec-2",
          study_id: "CAD-101",
          site_id: "SITE-101",
          staff_user_id: "subi.site101",
          staff_name: "Dr. Samira Patel",
          role: "investigator",
          task_codes: ["PHYSICAL_EXAM"],
          start_date: "2026-10-15",
          status: "ACTIVE",
          signed_off: true,
          pi_signature_hash: "ab12cd34ef56", // pragma: allowlist secret
          etmf_document_id: "ETMF-DOA-050204",
        },
        {
          record_id: "doa-rec-3",
          study_id: "CAD-101",
          site_id: "SITE-101",
          staff_user_id: "former.crc",
          task_codes: ["CRF_DATA_ENTRY"],
          start_date: "2026-08-01",
          end_date: "2026-10-01",
          status: "REVOKED",
          signed_off: true,
        },
      ],
    } as any);

    vi.spyOn(ctmsApi, "signOffDelegation").mockResolvedValue({
      record_id: "doa-rec-1",
      study_id: "CAD-101",
      site_id: "SITE-101",
      staff_user_id: "crc.site101",
      staff_name: "Jordan Lee, RN",
      role: "crc",
      task_codes: ["OBTAIN_INFORMED_CONSENT", "CRF_DATA_ENTRY"],
      start_date: "2026-11-01",
      status: "ACTIVE",
      signed_off: true,
      pi_signature_hash: "998877665544", // pragma: allowlist secret
      etmf_document_id: "ETMF-DOA-NEW-1",
    } as any);

    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi.site101",
    });
    const wrapper = mount(CtmsDelegationSection, {
      props: { studyCode: "CAD-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find(".doa-pending_pi_signature").exists()).toBe(true);
    expect(wrapper.find(".doa-active").exists()).toBe(true);
    expect(wrapper.find(".doa-revoked").exists()).toBe(true);
    expect(wrapper.text()).toContain("ETMF-DOA-050204");

    await wrapper
      .find('[data-testid="btn-doa-signoff-doa-rec-1"]')
      .trigger("click");
    await wrapper
      .find("#doa-signoff-reason")
      .setValue("Verified GCP and protocol training credentials");
    await wrapper.find("#doa-signoff-password").setValue("Password123!");
    await wrapper.find("#btn-confirm-doa-signoff").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        username: "pi.site101",
        action: "/api/v1/ctms/doa/sign-off",
        semantic_action: "ctms.doa.sign_off",
      })
    );
    expect(ctmsApi.signOffDelegation).toHaveBeenCalledWith(
      expect.objectContaining({
        record_id: "doa-rec-1",
        study_id: "CAD-101",
        reason_for_change: "Verified GCP and protocol training credentials",
      }),
      "Verified GCP and protocol training credentials",
      "sig-token-ctms-99"
    );
  });
});
