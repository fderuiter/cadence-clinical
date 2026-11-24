import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { sha256 } from "ui";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ctmsApi } from "../../src/api/ctmsApi";
import { etmfService } from "../../src/api/etmf";
import CtmsDelegationSection from "../../src/components/clinical/CtmsDelegationSection.vue";
import CtmsGreenlightSection from "../../src/components/clinical/CtmsGreenlightSection.vue";
import { useAuthStore } from "../../src/stores/auth";
import { signInAs } from "../helpers/signIn.js";

describe("CtmsGreenlightSection and CtmsDelegationSection Part 11 signature step-up workflows", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.spyOn(etmfService, "verifySignature").mockReset();
    vi.spyOn(window.URL, "createObjectURL").mockReturnValue("blob:doa-pdf");
    vi.spyOn(window.URL, "revokeObjectURL").mockImplementation(() => {});
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("certifies single-site and multi-site batch greenlight gates via SignatureCaptureModal with sorted SHA-256 batchId", async () => {
    signInAs(useAuthStore(), ["Data Manager"], { username: "dm_lead_01" });

    const readyGate = (siteId: string) => ({
      study_id: "CAD-101",
      site_id: siteId,
      country_code: "US",
      overall_status: "READY",
      is_ready_for_certification: true,
      facets: [
        {
          facet_code: "REGULATORY_IRB",
          display_name: "Regulatory & IRB",
          is_satisfied: true,
          evidence_summary: "Approved",
        },
      ],
      blocking_reasons: [],
      evaluated_at: "2026-10-01T00:00:00Z",
    });

    vi.spyOn(ctmsApi, "getCountryGreenlightMatrix").mockResolvedValue([
      readyGate("SITE-202"),
      readyGate("SITE-101"),
    ]);
    vi.spyOn(ctmsApi, "evaluateSiteGreenlight").mockResolvedValue(
      readyGate("SITE-101")
    );
    vi.spyOn(ctmsApi, "certifySiteGreenlight").mockResolvedValue({
      ...readyGate("SITE-101"),
      overall_status: "CERTIFIED",
      certified_by: "dm_lead_01",
      certified_at: "2026-10-02T12:00:00Z",
    });
    vi.spyOn(ctmsApi, "batchCertifyCountrySites").mockResolvedValue({
      study_id: "CAD-101",
      country_code: "US",
      total_certified: 2,
      certified_sites: [],
    } as any);

    const wrapper = mount(CtmsGreenlightSection, {
      props: { studyCode: "CAD-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // 1. Single-site certification
    vi.mocked(etmfService.verifySignature).mockResolvedValueOnce({
      sig_token: "sig-greenlight-single-1",
    });
    await wrapper
      .find("#greenlight-certify-reason")
      .setValue("All six startup readiness facets verified");
    await wrapper.find("#btn-certify-site-greenlight").trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("SPONSOR_APPROVAL");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        username: "dm_lead_01",
        action: "/api/v1/ctms/startup/sites/SITE-101/greenlight/certify",
        semanticAction: "ctms.greenlight.certify",
      })
    );
    expect(ctmsApi.certifySiteGreenlight).toHaveBeenCalledWith(
      "SITE-101",
      "CAD-101",
      "All six startup readiness facets verified",
      "US",
      "sig-greenlight-single-1"
    );

    // 2. Batch certification across SITE-202 and SITE-101 computes sorted SHA-256("SITE-101,SITE-202")
    vi.mocked(etmfService.verifySignature).mockResolvedValueOnce({
      sig_token: "sig-greenlight-batch-2",
    });
    const checkboxes = wrapper.findAll('tbody input[type="checkbox"]');
    expect(checkboxes).toHaveLength(2);
    await checkboxes[0].setValue(true); // SITE-202
    await checkboxes[1].setValue(true); // SITE-101
    await wrapper
      .find("#batch-certify-reason")
      .setValue("Batch country startup greenlight certification");
    await wrapper.find("#btn-batch-certify-greenlight").trigger("click");
    await vi.waitFor(() => {
      expect(wrapper.find("#sig-password").exists()).toBe(true);
    });

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("SPONSOR_APPROVAL");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    const expectedBatchHash = await sha256("SITE-101,SITE-202");
    expect(etmfService.verifySignature).toHaveBeenLastCalledWith(
      expect.objectContaining({
        username: "dm_lead_01",
        action: "/api/v1/ctms/startup/matrix/batch-certify",
        semanticAction: "ctms.greenlight.batch_certify",
        batchId: expectedBatchHash,
      })
    );
    expect(ctmsApi.batchCertifyCountrySites).toHaveBeenCalledWith(
      {
        study_id: "CAD-101",
        country_code: "US",
        site_ids: ["SITE-202", "SITE-101"],
      },
      "Batch country startup greenlight certification",
      "sig-greenlight-batch-2"
    );
  });

  it("wires SignatureCaptureModal for PI DoA sign-off and separates unsigned working PDF export from signed DoA PDF export", async () => {
    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi_site_101",
    });

    vi.spyOn(ctmsApi, "getSiteDoaLog").mockResolvedValue({
      site_id: "SITE-101",
      pi_name: "Dr. Alice Chen",
      delegated_staff: [
        {
          record_id: "doa-rec-1",
          site_id: "SITE-101",
          staff_user_id: "crc_01",
          staff_name: "Jordan Taylor",
          role: "CRC",
          task_codes: ["CRF_DATA_ENTRY"],
          start_date: "2026-10-01",
          status: "PENDING_PI_SIGNATURE",
        } as any,
      ],
      audit_history: [],
    });
    vi.spyOn(ctmsApi, "signOffDelegation").mockResolvedValue({
      record_id: "doa-rec-1",
      site_id: "SITE-101",
      staff_user_id: "crc_01",
      staff_name: "Jordan Taylor",
      task_codes: ["CRF_DATA_ENTRY"],
      start_date: "2026-10-01",
      status: "ACTIVE",
    } as any);
    vi.spyOn(ctmsApi, "exportSiteDoaPdf").mockResolvedValue(
      new Blob(["%PDF-unsigned"], { type: "application/pdf" })
    );
    vi.spyOn(ctmsApi, "exportSignedSiteDoaPdf").mockResolvedValue(
      new Blob(["%PDF-signed"], { type: "application/pdf" })
    );

    const wrapper = mount(CtmsDelegationSection, {
      props: { studyCode: "CAD-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // 1. Unsigned working PDF export does NOT prompt SignatureCaptureModal
    await wrapper.find("#btn-export-doa-pdf").trigger("click");
    await flushPromises();
    expect(etmfService.verifySignature).not.toHaveBeenCalled();
    expect(ctmsApi.exportSiteDoaPdf).toHaveBeenCalledWith("SITE-101");

    // 2. PI delegation sign-off prompts SignatureCaptureModal (/api/v1/ctms/doa/sign-off)
    vi.mocked(etmfService.verifySignature).mockResolvedValueOnce({
      sig_token: "sig-doa-signoff-1",
    });
    const piSignOffBtn = wrapper
      .findAll("tbody button")
      .find((b) => b.text().includes("PI sign-off"));
    expect(piSignOffBtn).toBeDefined();
    await piSignOffBtn!.trigger("click");
    await wrapper
      .find("#doa-signoff-reason")
      .setValue("Verified GCP and protocol training currency");
    await wrapper.find("#btn-confirm-doa-signoff").trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("INVESTIGATOR_SIGNATURE");
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        username: "pi_site_101",
        action: "/api/v1/ctms/doa/sign-off",
        semanticAction: "ctms.doa.sign_off",
      })
    );
    expect(ctmsApi.signOffDelegation).toHaveBeenCalledWith(
      {
        record_id: "doa-rec-1",
        reason_for_change: "Verified GCP and protocol training currency",
      },
      "Verified GCP and protocol training currency",
      "sig-doa-signoff-1"
    );

    // 3. Signed DoA PDF export prompts SignatureCaptureModal (/api/v1/ctms/doa/sites/SITE-101/export-signed-pdf)
    vi.mocked(etmfService.verifySignature).mockResolvedValueOnce({
      sig_token: "sig-doa-export-2",
    });
    await wrapper.find("#btn-export-signed-doa-pdf").trigger("click");
    await flushPromises();

    await wrapper.find("#sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper
      .find("#sig-reason")
      .setValue(
        "I certify the accuracy and completeness of this Delegation of Authority log"
      );
    await wrapper.find("#btn-confirm-sig").trigger("click");
    await flushPromises();

    expect(etmfService.verifySignature).toHaveBeenLastCalledWith(
      expect.objectContaining({
        username: "pi_site_101",
        action: "/api/v1/ctms/doa/sites/SITE-101/export-signed-pdf",
        semanticAction: "ctms.doa.export_signed_pdf",
      })
    );
    expect(ctmsApi.exportSignedSiteDoaPdf).toHaveBeenCalledWith(
      "SITE-101",
      "I certify the accuracy and completeness of this Delegation of Authority log",
      "sig-doa-export-2",
      "CAD-101"
    );
  });
});
