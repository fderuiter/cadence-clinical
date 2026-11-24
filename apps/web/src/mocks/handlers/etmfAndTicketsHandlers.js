import { http, HttpResponse } from "msw";
import {
  decodeReasonHeader,
  readJsonBody,
  readRequiredReasonHeader,
} from "./shared.js";

export function createEtmfAndTicketsHandlers(store) {
  return [
    http.get("*/api/v1/etmf/taxonomy", () =>
      HttpResponse.json({
        version: "DIA-TMF-v3.2.0",
        zones: [
          {
            code: "01",
            name: "Trial Management",
            artifacts: [{ code: "01.01.01", name: "Trial Master File Plan" }],
          },
          {
            code: "05",
            name: "Site Management",
            artifacts: [
              { code: "05.02.02", name: "Delegation of Authority Log" },
            ],
          },
        ],
      })
    ),
    http.get("*/api/v1/etmf/documents", () =>
      HttpResponse.json(store.state.etmfDocuments)
    ),
    http.get(
      "*/api/v1/etmf/documents/:documentId/watermark",
      ({ params }) =>
        new HttpResponse(
          `CONFIDENTIAL AUDITOR WATERMARKED COPY — Document ${decodeURIComponent(String(params.documentId))}`,
          {
            status: 200,
            headers: { "Content-Type": "text/plain; charset=utf-8" },
          }
        )
    ),
    http.post(
      "*/api/v1/etmf/documents/:documentId/sign-off",
      async ({ params, request }) => {
        const docId = decodeURIComponent(String(params.documentId));
        const body = await readJsonBody(request);
        return HttpResponse.json(store.signOffEtmfDocument(docId, body));
      }
    ),
    http.get("*/api/v1/etmf/audit-logs", () =>
      HttpResponse.json({
        items: store.state.etmfAuditLogs,
        total_count: store.state.etmfAuditLogs.length,
      })
    ),
    http.get("*/api/v1/etmf/completeness", ({ request }) => {
      const url = new URL(request.url);
      const firstDoc = store.state.etmfDocuments[0];
      return HttpResponse.json({
        study_id: url.searchParams.get("study_id") || "DEMO-01",
        milestone: url.searchParams.get("milestone") || "INITIATION",
        site_id: url.searchParams.get("site_id") || null,
        is_complete: true,
        present_count: 1,
        expected_count: 1,
        present_artifacts: ["Protocol"],
        per_artifact_detail: [
          {
            artifact_type: "Protocol",
            scope: "study",
            status: "PRESENT",
            document_id: firstDoc?.id || "doc-demo-01",
            version_index: firstDoc?.version_index ?? 1.0,
          },
        ],
      });
    }),
    http.get("*/api/v1/etmf/intelligence/qc-queue", () =>
      HttpResponse.json(store.listEtmfQcQueue())
    ),
    http.post("*/api/v1/etmf/intelligence/classify", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        document_id: null,
        filename: body?.filename || "document.pdf",
        mime_type: body?.mimeType || body?.mime_type || "application/pdf",
        sha256_hash: "sha256-demo-classified",
        confidence_tier: "HIGH",
        qc_recommendation: "AUTO_CLASSIFY",
        primary_classification: {
          zone: 5,
          section: "05.02",
          artifact_code: "05.02.01",
          artifact_type: "FDA Form 1572",
          confidence: 0.98,
        },
        alternative_candidates: [],
      });
    }),
    http.post("*/api/v1/etmf/intelligence/stage-qc", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.stageEtmfQcDocument(body), {
        status: 201,
      });
    }),
    http.post(
      "*/api/v1/etmf/intelligence/qc-review/:docId",
      async ({ params, request }) => {
        const docId = decodeURIComponent(String(params.docId));
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.reviewEtmfQcDocument(docId, body, decodeReasonHeader(request))
        );
      }
    ),
    http.get("*/api/v1/etmf/export-jobs", () => HttpResponse.json([])),
    http.get("*/api/v1/execution/audit/integrity", () =>
      HttpResponse.json({
        verified: true,
        message:
          "All sequential block hashes, Merkle roots, and historical data logs are intact and structurally unbroken.",
      })
    ),
    http.get("*/api/v1/notifications", () =>
      HttpResponse.json([
        {
          id: "notif-demo-01",
          title: "Open Query on SUBJ-1001 Baseline Vital Signs",
          severity: "INFO",
          status: "OPEN",
          version_index: 1,
          created_at: "2026-08-02T11:00:00Z",
        },
      ])
    ),
    http.get("*/api/v1/tickets", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listTickets({
          status: url.searchParams.get("status") || undefined,
          category: url.searchParams.get("category") || undefined,
          assigned_queue: url.searchParams.get("assigned_queue") || undefined,
          study_id: url.searchParams.get("study_id") || undefined,
          site_id: url.searchParams.get("site_id") || undefined,
          search: url.searchParams.get("search") || undefined,
        })
      );
    }),
    http.post("*/api/v1/tickets", async ({ request }) => {
      const decodedReason = readRequiredReasonHeader(request);
      const body = await readJsonBody(request);
      const result = store.createTicket(body, decodedReason);
      if (result.invalid) {
        return HttpResponse.json(result.problem, { status: 400 });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/tickets/:ticketId", ({ params }) => {
      const found = store.getTicket(
        decodeURIComponent(String(params.ticketId))
      );
      if (!found) {
        return HttpResponse.json(
          {
            title: "Ticket not found",
            status: 404,
            detail: `No ticket exists for ${params.ticketId}.`,
          },
          { status: 404 }
        );
      }
      return HttpResponse.json(found);
    }),
  ];
}
