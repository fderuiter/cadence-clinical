import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: payments-wallet", () => {
  const api = useDemoSliceServer();

  it("processes shared expense OCR + claim thresholds, visit stipends, disbursements, and store resets", async () => {
    const wallets = await api.requestJson(
      "/api/v1/payments/participant/wallets"
    );
    expect(wallets.status).toBe(200);
    expect(wallets.data.length).toBe(2);

    const ocr = await api.requestJson(
      "/api/v1/payments/participant/expenses/ocr-extract",
      {
        method: "POST",
        body: JSON.stringify({
          raw_text: "City Cab\nDate: 2026-08-01\nTotal: $32.50", // deid-ignore
        }),
      }
    );
    expect(ocr.status).toBe(200);
    expect(ocr.data.gross_amount).toBe(32.5);

    const autoApproved = await api.requestJson(
      "/api/v1/payments/participant/expenses",
      {
        method: "POST",
        body: JSON.stringify({
          subject_id: "SUBJ-1001",
          category: "PARKING",
          gross_amount: 32.5,
          ocr_confidence: 0.95,
          merchant_name: "City Cab",
        }),
      }
    );
    expect(autoApproved.status).toBe(201);
    expect(autoApproved.data.status).toBe("APPROVED");
    expect(autoApproved.data.approvalDecision).toBe("AUTO_APPROVED");

    const manualReview = await api.requestJson(
      "/api/v1/payments/participant/expenses",
      {
        method: "POST",
        body: JSON.stringify({
          subject_id: "SUBJ-1001",
          category: "LODGING",
          gross_amount: 120,
          merchant_name: "Regional Hotel",
        }),
      }
    );
    expect(manualReview.status).toBe(201);
    expect(manualReview.data.status).toBe("PENDING_REVIEW");

    const visitCredit = await api.requestJson(
      "/api/v1/payments/participant/events/visit-verified",
      {
        method: "POST",
        body: JSON.stringify({
          subject_id: "SUBJ-1001",
          visit_code: "VISIT_BASELINE",
        }),
      }
    );
    expect(visitCredit.status).toBe(200);
    expect(visitCredit.data.current_balance).toBe(225);

    const disburse = await api.requestJson(
      "/api/v1/payments/participant/wallets/wallet-1001/disburse",
      {
        method: "POST",
        body: JSON.stringify({ amount: 50, disbursement_type: "DIRECT_ACH" }),
      }
    );
    expect(disburse.status).toBe(200);
    expect(disburse.data.current_balance).toBe(175);

    api.store.reset();
    const resetExpenses = await api.requestJson(
      "/api/v1/payments/participant/expenses"
    );
    expect(resetExpenses.data).toEqual([]);
  });
});
