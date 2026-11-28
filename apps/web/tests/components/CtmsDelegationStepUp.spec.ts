import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ctmsApi } from "../../src/api/ctmsApi";
import * as signatureVerification from "../../src/api/signatureVerification";
import CtmsDelegationSection from "../../src/components/clinical/CtmsDelegationSection.vue";
import { useAuthStore } from "../../src/stores/auth";
import { signInAs } from "../helpers/signIn.js";

describe("CtmsDelegationSection 21 CFR Part 11 step-up signatures", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi_chen",
    });

    vi.spyOn(ctmsApi, "getSiteDoaLog").mockResolvedValue({
      site_id: "SITE-101",
      pi_name: "Dr. Evelyn Chen",
      delegated_staff: [
        {
          record_id: "doa-rec-101",
          site_id: "SITE-101",
          staff_user_id: "crc_jordan",
          staff_name: "Jordan Lee",
          role: "CRC",
          task_codes: ["CRF_DATA_ENTRY"],
          start_date: "2026-10-01",
          end_date: null,
          is_active: true,
          signed_off: false,
          status: "PENDING_PI_SIGNATURE",
        },
      ],
      audit_history: [],
    } as never);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("acquires a single-use X-Sig-Token via ctms.doa.sign_off before PI sign-off", async () => {
    vi.spyOn(signatureVerification, "requestStepUpSignature").mockResolvedValue(
      { sig_token: "sig-doa-signoff-token" }
    );
    vi.spyOn(ctmsApi, "signOffDelegation").mockResolvedValue({
      record_id: "doa-rec-101",
      status: "ACTIVE",
      signed_off: true,
    } as never);

    const wrapper = mount(CtmsDelegationSection, {
      props: { studyCode: "CADENCE-101", siteId: "SITE-101" },
    });
    await flushPromises();
    await wrapper.find("#delegation-scope-form").trigger("submit");
    await flushPromises();

    await wrapper.find("#btn-doa-signoff-doa-rec-101").trigger("click");
    await flushPromises();

    // Confirm button remains disabled until both attestation and credential are provided
    expect(
      wrapper.find("#btn-confirm-doa-signoff").attributes("disabled")
    ).toBeDefined();

    await wrapper
      .find("#doa-signoff-reason")
      .setValue("I attest CRC qualifications and GCP training are verified.");
    await wrapper
      .find("#doa-signoff-credential")
      .setValue("InvestigatorPass123!"); // pragma: allowlist secret
    await wrapper.find("#btn-confirm-doa-signoff").trigger("click");
    await flushPromises();

    expect(signatureVerification.requestStepUpSignature).toHaveBeenCalledWith(
      expect.objectContaining({
        password: "InvestigatorPass123!", // pragma: allowlist secret
        action: "/api/v1/ctms/doa/sign-off",
        semanticAction: "ctms.doa.sign_off",
      })
    );
    expect(ctmsApi.signOffDelegation).toHaveBeenCalledWith(
      {
        record_id: "doa-rec-101",
        reason_for_change:
          "I attest CRC qualifications and GCP training are verified.",
      },
      "I attest CRC qualifications and GCP training are verified.",
      "sig-doa-signoff-token"
    );
  });

  it("acquires a single-use X-Sig-Token via ctms.doa.export_pdf before exporting DoA PDF", async () => {
    vi.spyOn(signatureVerification, "requestStepUpSignature").mockResolvedValue(
      { sig_token: "sig-doa-export-token" }
    );
    const pdfBlob = new Blob(["%PDF-1.4 demo"], { type: "application/pdf" });
    vi.spyOn(ctmsApi, "exportSiteDoaPdf").mockResolvedValue(pdfBlob);
    const createObjectUrlSpy = vi
      .spyOn(URL, "createObjectURL")
      .mockReturnValue("blob:doa-pdf");
    const revokeObjectUrlSpy = vi
      .spyOn(URL, "revokeObjectURL")
      .mockImplementation(() => {});

    const wrapper = mount(CtmsDelegationSection, {
      props: { studyCode: "CADENCE-101", siteId: "SITE-101" },
    });
    await flushPromises();
    await wrapper.find("#delegation-scope-form").trigger("submit");
    await flushPromises();

    await wrapper.find("#btn-export-doa-pdf").trigger("click");
    await flushPromises();
    expect(wrapper.find("#doa-export-card").exists()).toBe(true);

    await wrapper
      .find("#doa-export-credential")
      .setValue("InvestigatorPass123!"); // pragma: allowlist secret
    await wrapper.find("#btn-confirm-doa-export").trigger("click");
    await flushPromises();

    expect(signatureVerification.requestStepUpSignature).toHaveBeenCalledWith(
      expect.objectContaining({
        password: "InvestigatorPass123!", // pragma: allowlist secret
        action: "/api/v1/ctms/doa/sites/SITE-101/export-pdf",
        semanticAction: "ctms.doa.export_pdf",
      })
    );
    expect(ctmsApi.exportSiteDoaPdf).toHaveBeenCalledWith(
      "SITE-101",
      "sig-doa-export-token"
    );
    expect(createObjectUrlSpy).toHaveBeenCalledWith(pdfBlob);
    expect(revokeObjectUrlSpy).toHaveBeenCalledWith("blob:doa-pdf");
  });
});
