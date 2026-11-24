import { http, HttpResponse } from "msw";
import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";
import { readJsonBody } from "./shared.js";

export function createCtmsAndFinancialsHandlers(store) {
  const updateMilestone = async ({ params, request }) => {
    const body = await readJsonBody(request);
    const found = store.state.milestones.find((m) => m.id === params.id);
    if (found) Object.assign(found, body);
    return HttpResponse.json(found || { id: params.id, ...body });
  };

  return [
    http.get("*/api/v1/ctms/startup/matrix", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.getStartupMatrix(url.searchParams.get("country_code") || "US")
      );
    }),
    http.post(
      "*/api/v1/ctms/startup/matrix/batch-certify",
      async ({ request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(store.batchCertifyStartupMatrix(body));
      }
    ),
    http.get(
      "*/api/v1/ctms/startup/sites/:siteId/greenlight",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getSingleSiteGreenlight(
            decodeURIComponent(String(params.siteId)),
            url.searchParams.get("study_id") || "DEMO-01",
            url.searchParams.get("country_code") || "US"
          )
        );
      }
    ),
    http.post(
      "*/api/v1/ctms/startup/sites/:siteId/greenlight/certify",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.certifySingleSiteGreenlight(
            decodeURIComponent(String(params.siteId)),
            url.searchParams.get("study_id") || "DEMO-01",
            url.searchParams.get("country_code") || "US"
          )
        );
      }
    ),
    http.get("*/api/v1/ctms/site-milestones", () =>
      HttpResponse.json(store.state.milestones)
    ),
    http.post("*/api/v1/ctms/site-milestones", async ({ request }) => {
      const body = await readJsonBody(request);
      const item = {
        id: `ms-${Date.now()}`,
        status: "PLANNED",
        ...body,
      };
      store.state.milestones.push(item);
      return HttpResponse.json(item, { status: 201 });
    }),
    http.patch("*/api/v1/ctms/site-milestones/:id", updateMilestone),
    http.put("*/api/v1/ctms/site-milestones/:id", updateMilestone),
    http.get("*/api/v1/ctms/monitoring-visits", () =>
      HttpResponse.json(store.state.monitoringVisits)
    ),
    http.post("*/api/v1/ctms/monitoring-visits", async ({ request }) => {
      const body = await readJsonBody(request);
      const step = nextDemoStep("ctms-visit-schedule");
      const visit = {
        id: `mv-demo-${step}`,
        status: "SCHEDULED",
        ...body,
      };
      store.state.monitoringVisits.push(visit);
      return HttpResponse.json(visit, { status: 201 });
    }),
    http.get("*/api/v1/ctms/monitoring-visits/:id/letters", ({ params }) =>
      HttpResponse.json([
        {
          id: `ltr-${params.id}`,
          visit_id: String(params.id),
          letter_type: "CONFIRMATION",
          subject: "Monitoring Visit Confirmation",
          body_markdown:
            "Scheduled monitoring visit confirmation and agenda summary.",
          status: "COMPILED",
          generated_at: demoNowIso(0),
        },
      ])
    ),
    http.get(
      "*/api/v1/ctms/monitoring-visits/:id/letters/:letterType",
      ({ params }) =>
        HttpResponse.json({
          id: `ltr-${params.id}-${params.letterType}`,
          visit_id: String(params.id),
          letter_type: String(params.letterType),
          subject: `Monitoring Visit ${params.letterType}`,
          body_markdown: `Generated ${params.letterType} letter for visit ${params.id}.`,
          status: "COMPILED",
          generated_at: demoNowIso(0),
        })
    ),
    http.post(
      "*/api/v1/ctms/monitoring-visits/:id/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.monitoringVisits.find(
          (v) => v.id === params.id
        );
        if (found) {
          found.status =
            params.action === "sign-off" ? "SIGNED_OFF" : "COMPLETED";
          if (body?.actual_date) found.actual_date = body.actual_date;
          if (Array.isArray(body?.findings)) found.findings = body.findings;
        }
        return HttpResponse.json(
          found || { id: params.id, status: "COMPLETED" }
        );
      }
    ),
    http.post("*/api/v1/ctms/letters/render", async ({ request }) => {
      const body = await readJsonBody(request);
      return new HttpResponse(
        `Monitoring Visit Letter Preview — Study ${body?.studyId || "CADENCE-101"} / Site ${body?.siteName || "SITE-101"}`,
        {
          status: 200,
          headers: { "Content-Type": "text/plain; charset=utf-8" },
        }
      );
    }),
    http.post("*/api/v1/ctms/letters/compile", async ({ request }) => {
      const url = new URL(request.url);
      const body = await readJsonBody(request);
      const step = nextDemoStep("ctms-letter-compile");
      const letterId = `LTR-DEMO-${step}`;
      const templateName =
        url.searchParams.get("templateName") || "follow_up_letter";
      return HttpResponse.json({
        letterId,
        letter_id: letterId,
        visitId: body?.visitId || null,
        letterType: templateName,
        template_name: templateName,
        study_id: body?.studyId || "CADENCE-101",
        site_name: body?.siteName || body?.siteId || "SITE-101",
        compiled_at: demoNowIso(step),
        dispatchedAt: demoNowIso(step),
        pdfSha256Hash: `sha256-demo-letter-${step}`,
        sha256_hash: `sha256-demo-letter-${step}`,
        isSigned: Boolean(body?.finalize),
        status: body?.finalize ? "FINALIZED" : "COMPILED",
        etmfDocumentId: `DOC-LTR-${step}`,
        etmf_document_id: `DOC-LTR-${step}`,
        pdfAccessUrl: `/api/v1/ctms/letters/${letterId}/pdf`,
        pdf_download_url: `/api/v1/ctms/letters/${letterId}/pdf`,
      });
    }),
    http.get(
      "*/api/v1/ctms/letters/:letterId/pdf",
      ({ params }) =>
        new HttpResponse(
          `%PDF-1.4\n% Demo Compiled Monitoring Letter ${params.letterId}\n`,
          {
            status: 200,
            headers: { "Content-Type": "application/pdf" },
          }
        )
    ),
    http.get("*/api/v1/ctms/cra-allocations/workload", () =>
      HttpResponse.json(store.state.craWorkload)
    ),
    http.post("*/api/v1/ctms/cra-allocations", async ({ request }) => {
      const body = await readJsonBody(request);
      const alloc = {
        cra_id: body?.cra_id || `usr-cra-${Date.now()}`,
        cra_name: body?.cra_name || "Assigned Clinical Monitor",
        assigned_sites: 1,
        pending_sdv_items: 0,
        utilization_pct: 55,
      };
      store.state.craWorkload.push(alloc);
      return HttpResponse.json(alloc, { status: 201 });
    }),
    http.get("*/api/v1/ctms/recruitment", () =>
      HttpResponse.json(store.state.recruitment)
    ),
    http.post("*/api/v1/ctms/recruitment", async ({ request }) => {
      const body = await readJsonBody(request);
      const entry = { id: `rec-${Date.now()}`, ...body };
      store.state.recruitment.push(entry);
      return HttpResponse.json(entry, { status: 201 });
    }),
    http.get("*/api/v1/ctms/doa/sites/:siteId/log", ({ params }) => {
      const siteId = decodeURIComponent(String(params.siteId));
      return HttpResponse.json(
        store.state.doaLogs.get(siteId) || store.state.doaLogs.get("SITE-101")
      );
    }),
    http.get(
      "*/api/v1/ctms/doa/sites/:siteId/export-pdf",
      ({ params }) =>
        new HttpResponse(
          `%PDF-1.4\n% Demo Delegation of Authority Log ${params.siteId}\n`,
          {
            status: 200,
            headers: { "Content-Type": "application/pdf" },
          }
        )
    ),
    http.post("*/api/v1/ctms/doa/:action", async ({ params, request }) => {
      const body = await readJsonBody(request);
      const log = store.state.doaLogs.get("SITE-101");
      if (params.action === "delegate" && log) {
        log.delegated_staff.push({
          id: `doa-${Date.now()}`,
          staff_id: body?.staff_id || "usr-crc-02",
          staff_name: body?.staff_name || "Delegated Site Coordinator",
          role: body?.role || "CRC",
          tasks: body?.tasks || ["CRF_DATA_ENTRY"],
          status: "PENDING_PI_SIGNATURE",
          signed_at: null,
        });
      }
      return HttpResponse.json({ status: "OK", action: params.action });
    }),
    http.get("*/api/v1/ctms/grants", () =>
      HttpResponse.json(store.state.grants)
    ),
    http.get("*/api/v1/ctms/financials/templates", () =>
      HttpResponse.json(store.state.financialTemplates)
    ),
    http.post("*/api/v1/ctms/financials/templates", async ({ request }) => {
      const body = await readJsonBody(request);
      const created = { id: `ftmpl-${Date.now()}`, ...body };
      store.state.financialTemplates.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.get("*/api/v1/ctms/financials/adjustments", () =>
      HttpResponse.json(store.state.financialAdjustments)
    ),
    http.post("*/api/v1/ctms/financials/adjustments", async ({ request }) => {
      const body = await readJsonBody(request);
      const created = {
        id: `fadj-${Date.now()}`,
        status: "PENDING",
        ...body,
      };
      store.state.financialAdjustments.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.post(
      "*/api/v1/ctms/financials/adjustments/:id/approve",
      ({ params }) => {
        const adj = store.state.financialAdjustments.find(
          (a) => a.id === params.id
        );
        if (adj) adj.status = "APPROVED";
        return HttpResponse.json(adj || { id: params.id, status: "APPROVED" });
      }
    ),
    http.get("*/api/v1/ctms/financials/exchange-rates", () =>
      HttpResponse.json([
        { base_currency: "USD", target_currency: "EUR", rate: 0.92 },
        { base_currency: "USD", target_currency: "GBP", rate: 0.79 },
        { base_currency: "USD", target_currency: "JPY", rate: 151.4 },
      ])
    ),
    http.get("*/api/v1/ctms/financials/procedure-grids", () =>
      HttpResponse.json(store.state.procedureGrids)
    ),
    http.post(
      "*/api/v1/ctms/financials/procedure-grids",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const created = { id: `pgrid-${Date.now()}`, ...body };
        store.state.procedureGrids.push(created);
        return HttpResponse.json(created, { status: 201 });
      }
    ),
    http.get("*/api/v1/ctms/financials/grants/:grantId/amendments", () =>
      HttpResponse.json(store.state.grantAmendments)
    ),
    http.get(
      "*/api/v1/ctms/financials/grants/:grantId/calculate-visit",
      ({ request }) => {
        const url = new URL(request.url);
        return HttpResponse.json({
          visit_name: url.searchParams.get("visit_name") || "Baseline",
          currency: url.searchParams.get("target_currency") || "USD",
          subtotal: 320,
          overhead_amount: 89.6,
          withholding_amount: 40.96,
          net_payable: 368.64,
        });
      }
    ),
    http.post(
      "*/api/v1/ctms/financials/grants/:grantId/:action",
      ({ params }) =>
        HttpResponse.json({
          status: "OK",
          grant_id: params.grantId,
          action: params.action,
        })
    ),
  ];
}
