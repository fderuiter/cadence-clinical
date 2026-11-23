import { createHash } from "node:crypto";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SEMANTIC_ACTIONS } from "ui";
import { executionApi } from "../../src/api/executionApi";
import { interopService } from "../../src/api/interop";
import { paymentsApi } from "../../src/api/paymentsApi";
import { soaClient } from "../../src/api/soaClient";
import DualReaderAdjudicationPanel from "../../src/components/imaging/DualReaderAdjudicationPanel.vue";
import { useAuthStore } from "../../src/stores/auth";
import {
  computeBatchBindingId,
  CTMS_GREENLIGHT_BATCH_CERTIFY_ACTION,
  INTEROP_ESOURCE_BATCH_ACCEPT_ACTION,
  INTEROP_ESOURCE_BATCH_REJECT_ACTION,
} from "../../src/utils/signatureBinding";

interface CapturedRequest {
  url: string;
  method: string;
  headers: Record<string, string>;
  body: any;
}

function mockFetchJson(
  responder: (req: CapturedRequest) => unknown | Promise<unknown>
) {
  const captured: CapturedRequest[] = [];
  const spy = vi
    .spyOn(globalThis, "fetch")
    .mockImplementation(
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const request =
          input instanceof Request ? input : new Request(String(input), init);
        const headers: Record<string, string> = {};
        request.headers.forEach((value, key) => {
          headers[key.toLowerCase()] = value;
        });
        const rawText = await request.clone().text();
        let body: any = null;
        if (rawText) {
          try {
            body = JSON.parse(rawText);
          } catch {
            body = rawText;
          }
        }
        const record: CapturedRequest = {
          url: new URL(request.url).pathname,
          method: request.method.toUpperCase(),
          headers,
          body,
        };
        captured.push(record);
        const payload = await responder(record);
        return new Response(JSON.stringify(payload ?? {}), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
    );

  return { spy, captured };
}

describe("21 CFR Part 11 step-up signature client bindings", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("computes deterministic ordinal-sorted comma-joined SHA-256 batch_id matching SigTokenBatchId.FromIds", async () => {
    const expectedHex = createHash("sha256")
      .update("SITE-101,SITE-102,SITE-205", "utf8")
      .digest("hex");

    const fromUnordered = await computeBatchBindingId([
      "SITE-205",
      " SITE-101 ",
      "   ",
      "SITE-102",
    ]);
    const fromOrdered = await computeBatchBindingId([
      "SITE-101",
      "SITE-102",
      "SITE-205",
    ]);

    expect(fromUnordered).toBe(expectedHex);
    expect(fromOrdered).toBe(expectedHex);
    expect(CTMS_GREENLIGHT_BATCH_CERTIFY_ACTION).toBe(
      "/api/v1/ctms/startup/matrix/batch-certify"
    );
    expect(INTEROP_ESOURCE_BATCH_ACCEPT_ACTION).toBe(
      "/api/v1/interop/esource/reconcile/batch-accept"
    );
    expect(INTEROP_ESOURCE_BATCH_REJECT_ACTION).toBe(
      "/api/v1/interop/esource/reconcile/batch-reject"
    );
  });

  it("verifies step-up credentials and attaches X-Sig-Token on paymentsApi.verifyAndSignRemittanceAdvice", async () => {
    const { captured } = mockFetchJson((req) => {
      if (req.url === "/api/v1/auth/signature-verification") {
        return { sig_token: "sig-remit-001" };
      }
      return {
        invoice_id: "INV-2026-09",
        signature_hash: "abc123",
        signed_by: "finance_lead",
      };
    });

    expect(() =>
      paymentsApi.signRemittanceAdvice(
        "INV-2026-09",
        { signature_reason: "Missing token check" },
        ""
      )
    ).toThrow(/X-Sig-Token/);

    const result = await paymentsApi.verifyAndSignRemittanceAdvice({
      invoiceId: "INV-2026-09",
      username: "finance_lead",
      password: "Password123!", // pragma: allowlist secret
      payload: {
        signature_reason: "Quarterly site invoice remittance release",
      },
      changeReason: "Quarterly site invoice remittance release",
    });

    expect(result).toMatchObject({ invoice_id: "INV-2026-09" });
    expect(captured).toHaveLength(2);
    expect(captured[0].url).toBe("/api/v1/auth/signature-verification");
    expect(captured[0].body).toMatchObject({
      username: "finance_lead",
      action: "/api/v1/payments/invoices/INV-2026-09/remittance-advice/sign",
      semantic_action: SEMANTIC_ACTIONS.PAYMENTS_REMITTANCE_SIGN,
    });
    expect(captured[1].url).toBe(
      "/api/v1/payments/invoices/INV-2026-09/remittance-advice/sign"
    );
    expect(captured[1].headers["x-sig-token"]).toBe("sig-remit-001");
  });

  it("verifies step-up credentials and attaches X-Sig-Token on imagingApi and DualReaderAdjudicationPanel", async () => {
    const authStore = useAuthStore();
    authStore.user = { id: "chair-1", username: "chair_smith" } as any;

    const { captured } = mockFetchJson((req) => {
      if (req.url === "/api/v1/auth/signature-verification") {
        return { sig_token: "sig-img-chair-001" };
      }
      return {
        sessionId: "sess-irc-42",
        studyId: "TRIAL-SOLID-101",
        subjectId: "PAT-001",
        timepointName: "Week 12",
        workflowStatus: "FINALIZED_BY_CHAIR",
        isResponseDiscordant: true,
        sldDiscordanceDelta: 0.428,
        discordanceReasons: ["Overall response discordant"],
        finalConsensusResponse: "PR",
        finalConsensusSldMm: 22.0,
        bindingReader: "ADJUDICATOR",
        adjudicationRationale: "Confirmed PR",
        sessionDigest: "sha256:verified",
      };
    });

    const wrapper = mount(DualReaderAdjudicationPanel, {
      props: {
        sessionId: "sess-irc-42",
        subjectId: "PAT-001",
        timepoint: "Week 12",
      },
    });

    const roleButtons = wrapper.findAll(".role-tab-btn");
    await roleButtons[2].trigger("click");
    await wrapper.find(".btn-chair-signoff").trigger("click");
    await wrapper.find("#chair-password-input").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.find(".btn-confirm-sign").trigger("click");
    await flushPromises();

    expect(captured).toHaveLength(2);
    expect(captured[0].url).toBe("/api/v1/auth/signature-verification");
    expect(captured[0].body).toMatchObject({
      username: "chair_smith",
      action: "/api/v1/imaging/adjudications/sess-irc-42/chair-adjudicate",
      semantic_action: SEMANTIC_ACTIONS.IMAGING_CHAIR_ADJUDICATE,
    });
    expect(captured[1].url).toBe(
      "/api/v1/imaging/adjudications/sess-irc-42/chair-adjudicate"
    );
    expect(captured[1].headers["x-sig-token"]).toBe("sig-img-chair-001");
    expect(captured[1].body).toMatchObject({
      finalConsensusResponse: "PR",
      finalConsensusSldMm: 22.0,
      bindingReader: "ADJUDICATOR",
      signatureMeaning: "BLINDED_IRC_ADJUDICATION_SIGN_OFF",
    });
    expect(wrapper.emitted("adjudicationCompleted")).toBeTruthy();
  });

  it("attaches X-Sig-Token headers on interopService eSource reconciliation mutations", async () => {
    const { captured } = mockFetchJson(() => ({ success: true }));

    await interopService.acceptEhrCandidate(
      { candidateId: "c-1" },
      { sigToken: "sig-acc-1", changeReason: "Accept EHR item" }
    );
    await interopService.batchAcceptEhrCandidates(
      { candidateIds: ["c-1", "c-2"] },
      { sigToken: "sig-bacc-1", changeReason: "Batch accept EHR items" }
    );
    await interopService.rejectEhrCandidate(
      { candidateId: "c-3", rejectionReason: "Out of window" },
      { sigToken: "sig-rej-1", changeReason: "Reject EHR item" }
    );
    await interopService.batchRejectEhrCandidates(
      { candidateIds: ["c-4"], rejectionReason: "Duplicate" },
      { sigToken: "sig-brej-1", changeReason: "Batch reject EHR items" }
    );

    expect(captured).toHaveLength(4);
    expect(captured[0].url).toBe("/api/v1/interop/esource/reconcile/accept");
    expect(captured[0].headers["x-sig-token"]).toBe("sig-acc-1");
    expect(captured[1].url).toBe(
      "/api/v1/interop/esource/reconcile/batch-accept"
    );
    expect(captured[1].headers["x-sig-token"]).toBe("sig-bacc-1");
    expect(captured[2].url).toBe("/api/v1/interop/esource/reconcile/reject");
    expect(captured[2].headers["x-sig-token"]).toBe("sig-rej-1");
    expect(captured[3].url).toBe(
      "/api/v1/interop/esource/reconcile/batch-reject"
    );
    expect(captured[3].headers["x-sig-token"]).toBe("sig-brej-1");
  });

  it("attaches X-Sig-Token on executionApi.changeSubjectState only when sigToken is supplied", async () => {
    const { captured } = mockFetchJson(() => ({
      id: "subj-1",
      status: "ENROLLED",
    }));

    await executionApi.changeSubjectState(
      "subj-1",
      "ENROLLED",
      "Enroll participant"
    );
    await executionApi.changeSubjectState(
      "subj-1",
      "UNBLINDED",
      "Emergency break-glass clinical justification exceeding minimum length",
      "sig-unblind-99"
    );

    expect(captured).toHaveLength(2);
    expect(captured[0].url).toBe("/api/v1/execution/subjects/subj-1/state");
    expect(captured[0].headers["x-sig-token"]).toBeUndefined();
    expect(captured[1].url).toBe("/api/v1/execution/subjects/subj-1/state");
    expect(captured[1].headers["x-sig-token"]).toBe("sig-unblind-99");
  });

  it("maps fallback semantic actions and batchId in soaClient.verifySignature", async () => {
    const { captured } = mockFetchJson(() => ({ sig_token: "sig-ok" }));

    await soaClient.verifySignature({
      username: "dm_01",
      password: "Password123!", // pragma: allowlist secret
      action: "/api/v1/ctms/startup/matrix/batch-certify",
      batchId: "batch-hash-123",
    });

    expect(captured).toHaveLength(1);
    expect(captured[0].url).toBe("/api/v1/auth/signature-verification");
    expect(captured[0].body).toMatchObject({
      action: "/api/v1/ctms/startup/matrix/batch-certify",
      semantic_action: SEMANTIC_ACTIONS.CTMS_GREENLIGHT_BATCH_CERTIFY,
      batch_id: "batch-hash-123",
    });
  });
});
