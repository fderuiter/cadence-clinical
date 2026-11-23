import { DEMO_SITES } from "demo-fixtures";
import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";
import { cloneJson, readJsonBody } from "./shared.js";

function buildSsuSiteEntry(country, siteId, siteName, overrides = {}) {
  const doaReady = overrides.facet_doa_log ?? true;
  const ipReady = overrides.facet_ip_release ?? true;
  const isReady = doaReady && ipReady;
  const unfulfilled = overrides.unfulfilled_reasons ?? [];
  return {
    study_id: overrides.study_id || "DEMO-01",
    site_id: siteId,
    site_name: siteName,
    country_code: country,
    overall_status: overrides.overall_status || "PENDING",
    readiness_percentage: overrides.readiness_percentage ?? 100,
    facet_regulatory_irb: true,
    facet_cta_budget: true,
    facet_eisf_baseline: true,
    facet_doa_log: doaReady,
    facet_staff_training: true,
    facet_ip_release: ipReady,
    is_compliant: isReady,
    is_ready_for_certification: isReady,
    facets: [
      {
        facet_code: "REGULATORY_IRB",
        label: "IRB / EC Favorable Opinion",
        is_satisfied: true,
      },
      {
        facet_code: "CTA_BUDGET",
        label: "CTA & Budget Executed",
        is_satisfied: true,
      },
      {
        facet_code: "EISF_BASELINE",
        label: "eISF Baseline Complete",
        is_satisfied: true,
      },
      {
        facet_code: "DOA_LOG",
        label: "PI Delegation Log Signed",
        is_satisfied: doaReady,
      },
      {
        facet_code: "STAFF_TRAINING",
        label: "GCP & Protocol Training",
        is_satisfied: true,
      },
      {
        facet_code: "IP_RELEASE",
        label: "Investigational Supply Released",
        is_satisfied: ipReady,
      },
    ],
    blocking_reasons: unfulfilled,
    certified_by: overrides.greenlight_certified_by ?? null,
    certified_at: overrides.greenlight_certified_at ?? null,
    greenlight_token: overrides.greenlight_token ?? null,
    greenlight_certified_by: overrides.greenlight_certified_by ?? null,
    greenlight_certified_at: overrides.greenlight_certified_at ?? null,
    is_overridden: false,
    unfulfilled_reasons: unfulfilled,
  };
}

function createStartupMatrixForCountry(countryCode = "US") {
  const country = (countryCode || "US").toUpperCase();
  if (country === "US" && DEMO_SITES.length >= 2) {
    return DEMO_SITES.map((site, idx) =>
      buildSsuSiteEntry(country, site.code, site.name, {
        overall_status: idx === 0 ? "APPROVED" : "PENDING",
        greenlight_token: idx === 0 ? `demo_gl_tok_${site.code}` : null,
        greenlight_certified_by: idx === 0 ? "designer.lead" : null,
        greenlight_certified_at: idx === 0 ? "2026-01-11T09:00:00Z" : null, // deid-ignore
      })
    );
  }
  const prefixByCountry = {
    US: "SITE-10",
    DE: "SITE-20",
    EU: "SITE-20",
    JP: "SITE-30",
    GB: "SITE-40",
    GLOBAL: "SITE-50",
  };
  const prefix = prefixByCountry[country] || "SITE-50";
  return [
    buildSsuSiteEntry(
      country,
      `${prefix}1`,
      `${country} Academic Clinical Research Center 1`,
      {
        overall_status: "APPROVED",
        greenlight_token: `demo_gl_tok_${prefix}1`,
        greenlight_certified_by: "Sponsor Operations Lead",
        greenlight_certified_at: "2026-07-20T14:00:00Z", // deid-ignore
      }
    ),
    buildSsuSiteEntry(
      country,
      `${prefix}2`,
      `${country} Regional Trial Unit 2`
    ),
    buildSsuSiteEntry(
      country,
      `${prefix}3`,
      `${country} Specialty Outpatient Institute 3`,
      {
        overall_status: "BLOCKED",
        readiness_percentage: 67,
        facet_doa_log: false,
        facet_ip_release: false,
        unfulfilled_reasons: [
          "Pending Delegation of Authority (DoA) PI eSignature",
          "Pending Investigational Product Depot Release",
        ],
      }
    ),
  ];
}

export function createCtmsSsuSliceStore(ctx) {
  const { state } = ctx;

  function reset() {
    state.siteCompliance = new Map([
      [
        "SITE-101",
        {
          study_id: "DEMO-01",
          site_id: "SITE-101",
          milestone: "SITE_ACTIVATION",
          is_complete: true,
          missing_documents: [],
          activated: true,
          is_activated: true,
          activated_at: "2026-07-20T14:00:00Z", // deid-ignore
          activated_by: "usr-dm-01",
        },
      ],
      [
        "SITE-102",
        {
          study_id: "DEMO-01",
          site_id: "SITE-102",
          milestone: "SITE_ACTIVATION",
          is_complete: true,
          missing_documents: [],
          activated: false,
          is_activated: false,
          activated_at: null,
          activated_by: null,
        },
      ],
      [
        "SITE-103",
        {
          study_id: "DEMO-01",
          site_id: "SITE-103",
          milestone: "SITE_ACTIVATION",
          is_complete: false,
          missing_documents: ["05.02.02 Principal Investigator Delegation Log"],
          activated: false,
          is_activated: false,
          activated_at: null,
          activated_by: null,
        },
      ],
    ]);

    state.startupMatrices = new Map([
      ["US", createStartupMatrixForCountry("US")],
      ["DE", createStartupMatrixForCountry("DE")],
      ["EU", createStartupMatrixForCountry("EU")],
      ["JP", createStartupMatrixForCountry("JP")],
      ["GB", createStartupMatrixForCountry("GB")],
      ["GLOBAL", createStartupMatrixForCountry("GLOBAL")],
    ]);

    state.milestones = [
      {
        id: "ms-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        milestone_name: "IRB Favorable Opinion",
        status: "COMPLETED",
        planned_date: "2026-07-01", // deid-ignore
        actual_date: "2026-06-28", // deid-ignore
      },
      {
        id: "ms-102",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        milestone_name: "Site Initiation Visit (SIV)",
        status: "IN_PROGRESS",
        planned_date: "2026-08-10", // deid-ignore
        actual_date: null,
      },
    ];

    state.monitoringVisits = [
      {
        id: "mv-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        visit_type: "INTERIM_MONITORING",
        status: "SCHEDULED",
        scheduled_date: "2026-08-25", // deid-ignore
        monitor_id: "usr-cra-01",
        sdv_percentage: 100,
      },
    ];

    state.craWorkload = [
      {
        cra_id: "usr-cra-01",
        cra_name: "Lead Clinical Monitor 01",
        assigned_sites: 2,
        pending_sdv_items: 4,
        utilization_pct: 68,
      },
    ];

    state.recruitment = [
      {
        id: "rec-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        target_enrollment: 15,
        screened_count: 6,
        enrolled_count: 4,
        screen_failure_count: 2,
      },
    ];

    state.doaLogs = new Map([
      [
        "SITE-101",
        {
          site_id: "SITE-101",
          pi_name: "Investigator Lead 01, MD",
          active_delegations: [
            {
              record_id: "doa-staff-01",
              site_id: "SITE-101",
              staff_user_id: "usr-crc-01",
              staff_name: "Site Coordinator 01",
              staff_role: "CRC",
              task_codes: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
              status: "ACTIVE",
              start_date: "2026-07-15", // deid-ignore
              pi_signed_off_by: "usr-pi-01",
              pi_signed_off_at: "2026-07-15T10:00:00Z", // deid-ignore
            },
          ],
          revoked_delegations: [],
          delegated_staff: [
            {
              id: "doa-staff-01",
              staff_id: "usr-crc-01",
              staff_name: "Site Coordinator 01",
              role: "CRC",
              tasks: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
              status: "ACTIVE",
              signed_at: "2026-07-15T10:00:00Z", // deid-ignore
            },
          ],
          audit_history: [
            {
              id: "doa-aud-01",
              action: "DELEGATION_APPROVED",
              actor: "Investigator Lead 01, MD",
              timestamp: "2026-07-15T10:00:00Z", // deid-ignore
              reason: "Initial site delegation sign-off",
            },
          ],
        },
      ],
    ]);

    state.grants = [
      {
        id: "grant-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        currency: "USD",
        total_budget: 145000,
        disbursed_amount: 32500, // deid-ignore
        holdback_percentage: 10,
        overhead_percentage: 28,
        version_index: 1,
        status: "ACTIVE",
      },
    ];

    state.financialTemplates = [
      {
        id: "ftmpl-01",
        template_name: "Academic Medical Center Tier 1",
        tier_level: "TIER_1",
        currency: "USD",
        overhead_percentage: 28,
        withholding_percentage: 10,
        fixed_startup_fee: 8500,
        fixed_closeout_fee: 3500,
        irb_fee: 2500,
        patient_stipend_amount: 75,
        travel_reimbursement_rate: 50,
      },
    ];

    state.financialAdjustments = [
      {
        id: "fadj-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        description: "Unscheduled safety lab handling fee",
        amount: 450,
        currency: "USD",
        status: "APPROVED",
      },
    ];

    state.procedureGrids = [
      {
        id: "pgrid-01",
        grant_id: "grant-101",
        procedure_code: "PROC-VS",
        procedure_name: "Vital Signs & 12-Lead ECG",
        visit_name: "Baseline",
        unit_cost: 320,
        currency: "USD",
        version_index: 1,
      },
    ];

    state.grantAmendments = [
      {
        id: "gamd-01",
        grant_id: "grant-101",
        version_index: 1,
        effective_date: "2026-07-01", // deid-ignore
        summary: "Initial executed clinical trial agreement budget",
      },
    ];
  }

  const methods = {
    getSiteCompliance(
      siteId,
      studyId = "DEMO-01",
      milestone = "SITE_ACTIVATION"
    ) {
      const existing = state.siteCompliance.get(siteId);
      if (existing) {
        const isActivated = Boolean(
          existing.is_activated ?? existing.activated
        );
        return {
          study_id: studyId || existing.study_id,
          site_id: siteId,
          milestone,
          is_complete: existing.is_complete,
          missing_documents: [...existing.missing_documents],
          is_activated: isActivated,
          activated_at: existing.activated_at ?? null,
          activated_by: existing.activated_by ?? null,
        };
      }
      return {
        study_id: studyId,
        site_id: siteId,
        milestone,
        is_complete: true,
        missing_documents: [],
        is_activated: false,
        activated_at: null,
        activated_by: null,
      };
    },

    activateSite(siteId, studyId = "DEMO-01") {
      const compliance = methods.getSiteCompliance(siteId, studyId);
      if (!compliance.is_complete) {
        return {
          blocked: true,
          problem: {
            title: "Site compliance incomplete",
            status: 400,
            detail: `Site ${siteId} cannot be activated until missing eTMF documents are filed: ${compliance.missing_documents.join(", ")}`,
          },
        };
      }
      const existing = state.siteCompliance.get(siteId);
      const alreadyActivated = Boolean(
        existing?.is_activated ?? existing?.activated
      );
      const activatedAt = alreadyActivated
        ? existing.activated_at || demoNowIso()
        : demoNowIso();
      const activatedBy = alreadyActivated
        ? existing.activated_by || "usr-dm-01"
        : "usr-dm-01";
      state.siteCompliance.set(siteId, {
        study_id: studyId,
        site_id: siteId,
        milestone: "SITE_ACTIVATION",
        is_complete: true,
        missing_documents: [],
        activated: true,
        is_activated: true,
        activated_at: activatedAt,
        activated_by: activatedBy,
      });
      for (const matrix of state.startupMatrices.values()) {
        const match = matrix.find((s) => s.site_id === siteId);
        if (match) {
          match.overall_status = "APPROVED";
          match.greenlight_certified_at = demoNowIso();
          match.greenlight_certified_by = "Sponsor Operations Lead";
          match.greenlight_token = `demo_gl_tok_${siteId}`;
        }
      }
      ctx.recordActivity(
        "execution.site.activated",
        "execution",
        "study_site",
        `study_site:${siteId}`,
        "usr-cra-01"
      );
      return {
        blocked: false,
        data: {
          status: "ACTIVATED",
          message: `Site ${siteId} activated for study ${studyId}.`,
        },
      };
    },

    getStartupMatrix(countryCode = "US") {
      const key = (countryCode || "US").toUpperCase();
      if (!state.startupMatrices.has(key)) {
        state.startupMatrices.set(key, createStartupMatrixForCountry(key));
      }
      return cloneJson(state.startupMatrices.get(key));
    },

    batchCertifyStartupMatrix({
      study_id,
      country_code,
      site_ids = [],
      meaning,
    }) {
      const key = (country_code || "US").toUpperCase();
      if (!state.startupMatrices.has(key)) {
        state.startupMatrices.set(key, createStartupMatrixForCountry(key));
      }
      const matrix = state.startupMatrices.get(key);
      const now = new Date().toISOString();
      const certified = [];
      for (const site of matrix) {
        if (site_ids.includes(site.site_id)) {
          site.overall_status = "APPROVED";
          site.greenlight_token = `demo_gl_batch_${site.site_id}`;
          site.greenlight_certified_by = "Sponsor Operations Lead";
          site.greenlight_certified_at = now;
          certified.push({
            site_id: site.site_id,
            site_name: site.site_name,
            greenlight_token: site.greenlight_token,
            certified_at: now,
            certified_by: site.greenlight_certified_by,
            status: "APPROVED",
          });
        }
      }
      return {
        success: true,
        certified_count: certified.length,
        study_id: study_id || "DEMO-01",
        country_code: key,
        meaning: meaning || "Greenlight certification",
        certified_sites: certified,
        message: `Certified ${certified.length} site(s) in ${key}.`,
      };
    },

    getSingleSiteGreenlight(siteId, studyId = "DEMO-01", countryCode = "US") {
      const matrix = methods.getStartupMatrix(countryCode);
      const found = matrix.find((s) => s.site_id === siteId) || {
        ...matrix[0],
        site_id: siteId,
        study_id: studyId,
      };
      return { ...found, study_id: studyId };
    },

    certifySingleSiteGreenlight(
      siteId,
      studyId = "DEMO-01",
      countryCode = "US"
    ) {
      const key = (countryCode || "US").toUpperCase();
      if (!state.startupMatrices.has(key)) {
        state.startupMatrices.set(key, createStartupMatrixForCountry(key));
      }
      const matrix = state.startupMatrices.get(key);
      const now = new Date().toISOString();
      let target = matrix.find((s) => s.site_id === siteId);
      if (!target) {
        target = {
          ...matrix[0],
          site_id: siteId,
        };
        matrix.push(target);
      }
      target.overall_status = "APPROVED";
      target.is_compliant = true;
      target.readiness_percentage = 100;
      target.greenlight_token = `demo_gl_tok_${siteId}`;
      target.greenlight_certified_by = "Sponsor Operations Lead";
      target.greenlight_certified_at = now;
      return cloneJson({ ...target, study_id: studyId });
    },
  };

  return {
    reset,
    methods,
  };
}

export function createCtmsSsuSliceHandlers(store, { http, HttpResponse }) {
  return [
    http.get(
      "*/api/v1/execution/sites/:siteId/compliance-status",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getSiteCompliance(
            decodeURIComponent(String(params.siteId)),
            url.searchParams.get("study_id") || "DEMO-01",
            url.searchParams.get("milestone") || "SITE_ACTIVATION"
          )
        );
      }
    ),
    http.post(
      "*/api/v1/execution/sites/:siteId/activate",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.activateSite(
          decodeURIComponent(String(params.siteId)),
          body?.study_id || "DEMO-01"
        );
        if (result.blocked) {
          return HttpResponse.json(result.problem, { status: 400 });
        }
        return HttpResponse.json(result.data);
      }
    ),

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
    http.patch(
      "*/api/v1/ctms/site-milestones/:id",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.milestones.find((m) => m.id === params.id);
        if (found) Object.assign(found, body);
        return HttpResponse.json(found || { id: params.id, ...body });
      }
    ),
    http.put(
      "*/api/v1/ctms/site-milestones/:id",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.milestones.find((m) => m.id === params.id);
        if (found) Object.assign(found, body);
        return HttpResponse.json(found || { id: params.id, ...body });
      }
    ),
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
