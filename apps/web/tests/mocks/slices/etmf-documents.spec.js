import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: etmf-documents", () => {
  const api = useDemoSliceServer();

  it("manages eTMF documents, sign-offs, QC staging, and resets former closure stores", async () => {
    const initialDocs = await api.requestJson("/api/v1/etmf/documents");
    expect(initialDocs.status).toBe(200);
    expect(initialDocs.data.length).toBe(1);
    expect(initialDocs.data[0].status).toBe("APPROVED");

    const signed = await api.requestJson(
      "/api/v1/etmf/documents/doc-demo-01/sign-off",
      {
        method: "POST",
        body: JSON.stringify({
          username: "auditor.lead",
          signing_reason: "APPROVAL",
        }),
      }
    );
    expect(signed.status).toBe(200);
    expect(signed.data.status).toBe("SIGNED");

    const staged = await api.requestJson("/api/v1/etmf/intelligence/stage-qc", {
      method: "POST",
      body: JSON.stringify({
        study_id: "CADENCE-101",
        site_id: "SITE-101",
        filename: "SITE-101-FDA-1572.pdf",
      }),
    });
    expect(staged.status).toBe(201);
    expect(staged.data.status).toBe("TECHNICAL_QC");

    const qcQueue = await api.requestJson("/api/v1/etmf/intelligence/qc-queue");
    expect(qcQueue.status).toBe(200);
    expect(qcQueue.data.length).toBe(1);

    const reviewed = await api.requestJson(
      `/api/v1/etmf/intelligence/qc-review/${staged.data.document_id}`,
      {
        method: "POST",
        headers: { "X-Change-Reason": "Technical QC verified" },
        body: JSON.stringify({ decision: "ACCEPT" }),
      }
    );
    expect(reviewed.status).toBe(200);
    expect(reviewed.data.status).toBe("APPROVED");

    const auditLogs = await api.requestJson("/api/v1/etmf/audit-logs");
    expect(auditLogs.data.total_count).toBe(2);

    const watermarkRes = await api.fetchPath(
      "/api/v1/etmf/documents/doc-demo-01/watermark"
    );
    expect(watermarkRes.status).toBe(200);
    expect(await watermarkRes.text()).toContain("WATERMARKED COPY");

    api.store.reset();
    const resetDocs = await api.requestJson("/api/v1/etmf/documents");
    expect(resetDocs.data.length).toBe(1);
    expect(resetDocs.data[0].status).toBe("APPROVED");
    const resetLogs = await api.requestJson("/api/v1/etmf/audit-logs");
    expect(resetLogs.data.total_count).toBe(1);
  });
});
