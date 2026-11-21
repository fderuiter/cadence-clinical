import { http, HttpResponse } from "msw";
import { isValidSemanticAction, isValidStepUpAction } from "ui";
import { webDemoStore } from "./demoStore.js";
import { parseDemoProtocolImportRequest } from "./demoStudyImport.js";

const DEFAULT_STUDY_REF = "study:00000000-0000-0000-0000-000000000001"; // deid-ignore

function decodeReasonHeader(request) {
  const raw =
    request.headers.get("X-Change-Reason") ||
    request.headers.get("x-change-reason");
  if (!raw) return "Recorded in demo session";
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

function readRequiredReasonHeader(request) {
  const raw =
    request.headers.get("X-Change-Reason") ||
    request.headers.get("x-change-reason") ||
    "";
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

async function readJsonBody(request) {
  try {
    return await request.json();
  } catch {
    return {};
  }
}

export function createWebDemoHandlers(store = webDemoStore) {
  return [
    // Health & identity
    http.get("*/health", () =>
      HttpResponse.json({ status: "healthy", mode: "demo" })
    ),
    http.get("*/api/v1/kernel/health", () =>
      HttpResponse.json({ status: "healthy", module: "kernel-demo" })
    ),
    http.get("*/api/v1/execution/health", () =>
      HttpResponse.json({
        status: "healthy",
        service: "execution-demo",
        version: "1.0.0",
      })
    ),
    http.post("*/api/v1/auth/signature-verification", async ({ request }) => {
      const body = await readJsonBody(request);
      if (
        !isValidStepUpAction(body?.action) ||
        !isValidSemanticAction(body?.semantic_action)
      ) {
        return HttpResponse.json(
          {
            title: "Invalid Step-Up Signature Payload",
            status: 400,
            code: "INVALID_STEP_UP_PAYLOAD",
            detail:
              "Both '/api/v1/...' action and governed snake_case semantic_action are required.",
          },
          { status: 400 }
        );
      }
      return HttpResponse.json({
        sig_token: `demo_sig_tok_${Date.now()}`,
        action: body.action,
        semantic_action: body.semantic_action,
        expires_in: 300,
      });
    }),

    // Kernel Search, Nodes, Zoom Path, Children, Commands, Authorization
    http.get("*/api/v1/kernel/entitlements", () =>
      HttpResponse.json(store.getEntitlements())
    ),
    http.post("*/api/v1/kernel/entitlements/tier", async ({ request }) => {
      const body = await readJsonBody(request);
      const updated = store.upgradeSubscriptionTier(body?.tier);
      if (!updated) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.TierInvalid",
            title: "Invalid commercial tier",
            status: 400,
            code: "K.Commercial.TierInvalid",
          },
          { status: 400 }
        );
      }
      return HttpResponse.json(updated);
    }),
    http.post("*/api/v1/kernel/onboarding/trial", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.bootstrapTrial(body));
    }),
    http.get("*/api/v1/kernel/search", ({ request }) => {
      const url = new URL(request.url);
      const q = url.searchParams.get("q") || "";
      const kinds = url.searchParams.getAll("kind");
      const limit = url.searchParams.get("limit") || "25";
      return HttpResponse.json(store.searchKernel({ q, kinds, limit }));
    }),
    http.get("*/api/v1/kernel/authorization/can-i", ({ request }) => {
      const url = new URL(request.url);
      const capability = url.searchParams.get("capability") || "kernel.read";
      const resource = url.searchParams.get("resource") || "study:demo";
      return HttpResponse.json(store.buildDecision(capability, resource, true));
    }),
    http.post("*/api/v1/kernel/authorization/can-i", async ({ request }) => {
      const body = await readJsonBody(request);
      const capability = body?.capability || "kernel.read";
      const resource = body?.resource || "study:demo";
      return HttpResponse.json(store.buildDecision(capability, resource, true));
    }),
    http.get("*/api/v1/kernel/nodes/:nodeRef/path", ({ params }) =>
      HttpResponse.json(
        store.getZoomPath(decodeURIComponent(String(params.nodeRef)))
      )
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef/children", ({ params }) =>
      HttpResponse.json(
        store.getChildren(decodeURIComponent(String(params.nodeRef)))
      )
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef/commands", ({ params }) =>
      HttpResponse.json(
        store.getCommands(decodeURIComponent(String(params.nodeRef)))
      )
    ),
    http.get(
      "*/api/v1/kernel/nodes/:nodeRef/lineage",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getLineage(
            decodeURIComponent(String(params.nodeRef)),
            url.searchParams.get("direction") || "upstream",
            Number(url.searchParams.get("depth") || 3)
          )
        );
      }
    ),
    http.get(
      "*/api/v1/kernel/nodes/:nodeRef/dependents",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getDependents(
            decodeURIComponent(String(params.nodeRef)),
            Number(url.searchParams.get("depth") || 3)
          )
        );
      }
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef", ({ params }) =>
      HttpResponse.json(
        store.getNode(decodeURIComponent(String(params.nodeRef)))
      )
    ),

    // Kernel Studies, Organizations, Protocol Templates, Protocol Versions, SoA, Proposals
    http.get("*/api/v1/kernel/studies", () =>
      HttpResponse.json(store.listStudies())
    ),
    http.post("*/api/v1/kernel/studies", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey =
        request.headers.get("Idempotency-Key") ||
        request.headers.get("idempotency-key") ||
        "";
      const result = store.createStudy(body, { reason, idempotencyKey });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/kernel/organizations", () =>
      HttpResponse.json(store.listOrganizations())
    ),
    http.get("*/api/v1/kernel/protocol-templates", () =>
      HttpResponse.json(store.listProtocolTemplates())
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/protocol-versions",
      ({ params }) =>
        HttpResponse.json(
          store.listProtocolVersions(
            decodeURIComponent(String(params.studyRef))
          )
        )
    ),
    http.post(
      "*/api/v1/kernel/studies/:studyRef/protocol-imports",
      async ({ params, request }) => {
        const reason = readRequiredReasonHeader(request);
        const idempotencyKey =
          request.headers.get("Idempotency-Key") ||
          request.headers.get("idempotency-key") ||
          "";
        const parsedRequest = await parseDemoProtocolImportRequest(request);
        const result = store.importProtocolDraft(
          decodeURIComponent(String(params.studyRef)),
          parsedRequest,
          { reason, idempotencyKey }
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get(
      "*/api/v1/kernel/protocol-versions/:versionRef/design-summary",
      ({ params }) =>
        HttpResponse.json(
          store.getProtocolDesignSummary(
            decodeURIComponent(String(params.versionRef))
          )
        )
    ),
    http.get(
      "*/api/v1/kernel/protocol-versions/:versionRef/schedule-of-activities",
      ({ params }) =>
        HttpResponse.json(
          store.getScheduleOfActivities(
            decodeURIComponent(String(params.versionRef))
          )
        )
    ),
    http.get(
      "*/api/v1/kernel/protocol-versions/:versionRef/impact",
      ({ params }) =>
        HttpResponse.json(
          store.getAmendmentImpact(
            decodeURIComponent(String(params.versionRef))
          )
        )
    ),
    http.get("*/api/v1/kernel/protocol-versions/:versionRef", ({ params }) =>
      HttpResponse.json(
        store.getProtocolVersion(decodeURIComponent(String(params.versionRef)))
      )
    ),
    http.get("*/api/v1/kernel/studies/:studyRef/proposals", () =>
      HttpResponse.json(store.listProposals())
    ),
    http.get("*/api/v1/kernel/proposals/:proposalRef", ({ params }) =>
      HttpResponse.json(
        store.getProposal(decodeURIComponent(String(params.proposalRef)))
      )
    ),
    http.post(
      "*/api/v1/kernel/proposals/:proposalRef/rejection",
      ({ params, request }) =>
        HttpResponse.json(
          store.rejectProposal(
            decodeURIComponent(String(params.proposalRef)),
            decodeReasonHeader(request)
          )
        )
    ),
    http.get("*/api/v1/kernel/activity", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.getActivityPage({ limit: url.searchParams.get("limit") || 25 })
      );
    }),
    http.post("*/api/v1/kernel/simulation/simulate", async ({ request }) => {
      const body = await readJsonBody(request);
      const now = new Date().toISOString();
      const targetRef =
        body?.stored_version_ref || body?.base_version_ref || DEFAULT_STUDY_REF;
      return HttpResponse.json({
        scope: targetRef,
        as_of: { valid_at: now, known_at: now },
        candidate: body?.stored_version_ref
          ? { source: "stored", version_ref: body.stored_version_ref }
          : {
              title: "Candidate Protocol Simulation",
              content_hash: "sha256-demo-sim-cand",
              element_count: 14,
            },
        engine_versions: [{ component: "demo-simulator", version: "1.0.0" }],
        input_hash: "sha256-demo-sim-in",
        result_hash: "sha256-demo-sim-out",
        structural_diff: [],
        schedule_changes: [],
        masked: [],
        dependents: store.getDependents(targetRef),
        matrix_gaps: [],
        is_empty: true,
      });
    }),
    http.post("*/api/v1/kernel/protocols/matrix-gap-check", () =>
      HttpResponse.json([])
    ),
    http.post("*/api/v1/ai/intent/route", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        query: body?.query || "",
        recommendations: [
          {
            id: "rec-study-canvas",
            name: "Open Clinical Study Workspace",
            description:
              "Inspect DEMO-01 participants, SoA, and site readiness.",
            category: "Navigation",
            icon: "compass",
            target_route: "/studies/DEMO-01",
            action: null,
            confidence: 0.96,
            roles: ["sponsor_admin", "crc", "cra", "data_manager"],
          },
        ],
      });
    }),

    // Kernel Observations & Stateful Corrections
    http.get("*/api/v1/kernel/observations/:observationRef", ({ params }) =>
      HttpResponse.json(
        store.getObservation(decodeURIComponent(String(params.observationRef)))
      )
    ),
    http.post(
      "*/api/v1/kernel/observations/:observationRef/corrections",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const reason = decodeReasonHeader(request);
        const result = store.correctObservation(
          decodeURIComponent(String(params.observationRef)),
          body,
          reason
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.post(
      "*/api/v1/kernel/observations/:observationRef/verifications",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.verifyObservation(
          decodeURIComponent(String(params.observationRef)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data, { status: 201 });
      }
    ),
    http.post(
      "*/api/v1/kernel/assertions/:assertionRef/retraction",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.retractAssertion(
          decodeURIComponent(String(params.assertionRef)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),

    // Execution — Subjects, Sites, Compliance, Activation
    http.get("*/api/v1/execution/subjects", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listSubjects({
          studyId: url.searchParams.get("study_id") || undefined,
          siteId: url.searchParams.get("site_id") || undefined,
          status: url.searchParams.get("status") || undefined,
          q: url.searchParams.get("q") || undefined,
          limit: url.searchParams.get("limit") || 50,
          offset: url.searchParams.get("offset") || 0,
        })
      );
    }),
    http.post("*/api/v1/execution/subjects", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.createSubject(body), { status: 201 });
    }),
    http.post(
      "*/api/v1/execution/subjects/activation-codes/redeem",
      async ({ request }) => {
        const body = await readJsonBody(request);
        if (!body?.activation_code || !String(body.activation_code).trim()) {
          return HttpResponse.json(
            {
              title: "Validation failed",
              status: 400,
              errors: { activation_code: ["Activation code is required."] },
            },
            { status: 400 }
          );
        }
        return HttpResponse.json(
          store.redeemSubjectActivationCode(body.activation_code)
        );
      }
    ),
    http.get("*/api/v1/execution/subjects/activation-codes/me", () => {
      const redeemed = store.getRedeemedSubjectActivationCode();
      if (!redeemed) {
        return HttpResponse.json(
          { title: "No redeemed activation code found.", status: 404 },
          { status: 404 }
        );
      }
      return HttpResponse.json(redeemed);
    }),
    http.get("*/api/v1/execution/subjects/:subjectId", ({ params }) =>
      HttpResponse.json(
        store.getSubjectDetail(decodeURIComponent(String(params.subjectId)))
      )
    ),
    http.post(
      "*/api/v1/execution/subjects/:subjectId/activation-code",
      ({ params }) =>
        HttpResponse.json(
          store.issueSubjectActivationCode(
            decodeURIComponent(String(params.subjectId))
          ),
          { status: 201 }
        )
    ),
    http.patch(
      "*/api/v1/execution/subjects/:subjectId/state",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.updateSubjectState(
            decodeURIComponent(String(params.subjectId)),
            body
          )
        );
      }
    ),
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

    // Execution — Stateful eCRF Forms, Values, Signatures, and Queries
    // Note: Register specific `/queries` routes BEFORE `:formId` catch-alls!
    http.get("*/api/v1/execution/forms/queries", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listEnrollmentQueries({
          enrollmentId: url.searchParams.get("enrollment_id"),
          studyId: url.searchParams.get("study_id"),
          status: url.searchParams.get("status"),
          site: url.searchParams.get("site"),
          participant: url.searchParams.get("participant"),
          valueChanged: url.searchParams.get("value_changed"),
        })
      );
    }),
    http.get("*/api/v1/execution/forms/:formId/queries", ({ params }) =>
      HttpResponse.json(
        store.listFormQueries(decodeURIComponent(String(params.formId)))
      )
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/queries",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.raiseQuery(decodeURIComponent(String(params.formId)), body),
          { status: 201 }
        );
      }
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/queries/:queryId/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.transitionQuery(
          decodeURIComponent(String(params.formId)),
          decodeURIComponent(String(params.queryId)),
          String(params.action),
          body,
          decodeReasonHeader(request)
        );
        if (result.notFound) {
          return HttpResponse.json(result.problem, { status: 404 });
        }
        if (result.invalid) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get("*/api/v1/execution/forms", ({ request }) => {
      const url = new URL(request.url);
      const enrollmentId = (url.searchParams.get("enrollment_id") || "").trim();
      if (!enrollmentId) {
        return HttpResponse.json(
          {
            title: "Missing enrollment_id",
            status: 400,
            detail: "Query parameter 'enrollment_id' is required.",
          },
          { status: 400 }
        );
      }
      return HttpResponse.json(store.listEnrollmentForms(enrollmentId));
    }),
    http.post("*/api/v1/execution/forms", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.openForm(body), { status: 201 });
    }),
    http.get("*/api/v1/execution/forms/:formId", ({ params }) =>
      HttpResponse.json(
        store.getForm(decodeURIComponent(String(params.formId)))
      )
    ),
    http.put(
      "*/api/v1/execution/forms/:formId/values",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.saveFormValues(
          decodeURIComponent(String(params.formId)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/signatures",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.signForm(
          decodeURIComponent(String(params.formId)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data, { status: 201 });
      }
    ),

    // RTSM & Data Locks
    http.get("*/api/v1/execution/rtsm/sites/:siteId/inventory", ({ params }) =>
      HttpResponse.json(
        store.state.ipKits.filter(
          (k) => k.site_id === params.siteId || params.siteId === "SITE-101"
        )
      )
    ),
    http.get("*/api/v1/execution/locks", () =>
      HttpResponse.json(store.state.locks)
    ),
    http.post(
      "*/api/v1/execution/locks/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const nextStatus = params.action === "unlock" ? "UNLOCKED" : "LOCKED";
        if (store.state.locks[0]) {
          store.state.locks[0].status = nextStatus;
          store.state.locks[0].updated_at = new Date().toISOString();
        }
        return HttpResponse.json({
          status: nextStatus,
          study_id: body?.study_id || "DEMO-01",
        });
      }
    ),

    // CTMS & SSU Startup Matrix + Operations + DoA + Financials
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
    http.get("*/api/v1/ctms/monitoring-visits", () =>
      HttpResponse.json(store.state.monitoringVisits)
    ),
    http.post("*/api/v1/ctms/monitoring-visits", async ({ request }) => {
      const body = await readJsonBody(request);
      const visit = {
        id: `mv-${Date.now()}`,
        status: "SCHEDULED",
        ...body,
      };
      store.state.monitoringVisits.push(visit);
      return HttpResponse.json(visit, { status: 201 });
    }),
    http.post("*/api/v1/ctms/monitoring-visits/:id/:action", ({ params }) => {
      const found = store.state.monitoringVisits.find(
        (v) => v.id === params.id
      );
      if (found) {
        found.status =
          params.action === "sign-off" ? "SIGNED_OFF" : "COMPLETED";
      }
      return HttpResponse.json(found || { id: params.id, status: "COMPLETED" });
    }),
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

    // Contextual Help & Knowledge Hub
    http.get("*/api/v1/knowledge/contextual-help", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.resolveContextualHelp(
          url.searchParams.get("route") || "/ecrf",
          url.searchParams.get("persona") || "super_admin"
        )
      );
    }),
    http.get("*/api/v1/knowledge/categories", () =>
      HttpResponse.json(store.state.knowledgeCategories)
    ),
    http.post("*/api/v1/knowledge/categories", async ({ request }) => {
      const body = await readJsonBody(request);
      const cat = {
        id: `cat-${Date.now()}`,
        is_deleted: false,
        created_at: new Date().toISOString(),
        ...body,
      };
      store.state.knowledgeCategories.push(cat);
      return HttpResponse.json(cat, { status: 201 });
    }),
    http.get("*/api/v1/knowledge/articles", ({ request }) => {
      const url = new URL(request.url);
      const status = url.searchParams.get("status_filter");
      const categoryId = url.searchParams.get("category_id");
      const q = (url.searchParams.get("q") || "").trim().toLowerCase();
      let list = [...store.state.knowledgeArticles];
      if (status && status !== "ALL") {
        list = list.filter((a) => a.status === status);
      }
      if (categoryId) {
        list = list.filter((a) => a.category_id === categoryId);
      }
      if (q) {
        list = list.filter((a) => {
          const title = String(a.title || "").toLowerCase();
          const body = String(
            a.body_markdown || a.body_html || ""
          ).toLowerCase();
          const tags = Array.isArray(a.tags)
            ? a.tags.some((t) => String(t).toLowerCase().includes(q))
            : false;
          return title.includes(q) || body.includes(q) || tags;
        });
      }
      return HttpResponse.json(list);
    }),
    http.post("*/api/v1/knowledge/articles", async ({ request }) => {
      const body = await readJsonBody(request);
      const created = {
        id: `art-${Date.now()}`,
        slug: `sop-demo-${Date.now()}`,
        status: "DRAFT",
        version_index: 1,
        version_label: "1.0",
        tags: body?.tags || ["demo-sop"],
        created_at: new Date().toISOString(),
        updated_at: new Date().toISOString(),
        ...body,
      };
      store.state.knowledgeArticles.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.get("*/api/v1/knowledge/articles/:articleId/versions", ({ params }) =>
      HttpResponse.json([
        {
          id: `ver-${params.articleId}`,
          version_index: 1,
          version_label: "1.0",
          status_at_snapshot: "PUBLISHED",
          is_locked: true,
          created_by: "qa_director",
          reason_for_change: "Published controlled SOP",
          created_at: "2026-06-01T11:15:00Z",
        },
      ])
    ),
    http.get("*/api/v1/knowledge/articles/:articleId/audit-log", ({ params }) =>
      HttpResponse.json([
        {
          id: `kaud-${params.articleId}`,
          action: "ARTICLE_PUBLISHED",
          actor_id: "qa_director",
          timestamp: "2026-06-01T11:15:00Z",
          reason_for_change: "Initial controlled publication",
        },
      ])
    ),
    http.get("*/api/v1/knowledge/articles/:articleId", ({ params }) => {
      const found =
        store.state.knowledgeArticles.find(
          (a) => a.id === params.articleId || a.slug === params.articleId
        ) || store.state.knowledgeArticles[0];
      return HttpResponse.json(found);
    }),

    // MDR, Terminology, Studies, Rules, eTMF, Quality, Notifications, Wallet, Lay Summaries
    http.get("*/api/v1/terminology/validate/:code", ({ params }) => {
      const code = decodeURIComponent(String(params.code));
      return HttpResponse.json({
        code,
        valid: true,
        preferred_term: `CDISC Controlled Concept (${code})`,
        codelist_code: "C66741",
        synonyms: [code],
      });
    }),
    http.get("*/api/v1/terminology/search", ({ request }) => {
      const url = new URL(request.url);
      const term = url.searchParams.get("term") || "VS";
      return HttpResponse.json({
        total: 2,
        items: [
          {
            code: "C25298",
            preferred_term: `Systolic Blood Pressure (${term})`,
            codelist_code: "C66741",
            definition:
              "Maximum arterial pressure during ventricular contraction.",
          },
          {
            code: "C25299",
            preferred_term: "Diastolic Blood Pressure",
            codelist_code: "C66741",
            definition: "Minimum arterial pressure between contractions.",
          },
        ],
      });
    }),
    http.get("*/api/v1/mdr/concepts", () =>
      HttpResponse.json([
        {
          code: "SYSBP",
          nci_code: "C25298",
          name: "Systolic Blood Pressure",
          domain: "VS",
          datatype: "integer",
          unit: "mm[Hg]",
        },
        {
          code: "DIABP",
          nci_code: "C25299",
          name: "Diastolic Blood Pressure",
          domain: "VS",
          datatype: "integer",
          unit: "mm[Hg]",
        },
      ])
    ),
    http.get("*/api/v1/studies/:studyId/terminology-validation", ({ params }) =>
      HttpResponse.json({
        study_id: params.studyId,
        valid: true,
        issues: [],
      })
    ),
    http.get("*/api/v1/studies/:studyId/ct-validation", ({ params }) =>
      HttpResponse.json({
        study_id: params.studyId,
        valid: true,
        findings: [],
      })
    ),
    http.get("*/api/v1/studies/:studyId/rules", () =>
      HttpResponse.json(store.state.rules)
    ),
    http.post("*/api/v1/studies/:studyId/rules/validate", () =>
      HttpResponse.json({ valid: true, errors: [] })
    ),
    http.post("*/api/v1/studies/:studyId/rules/preview", () =>
      HttpResponse.json({ matched_count: 1, sample_subjects: ["SUBJ-1001"] })
    ),
    http.post(
      "*/api/v1/studies/:studyId/rules",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const created = {
          id: `rule-${Date.now()}`,
          study_id: params.studyId,
          is_active: true,
          ...body,
        };
        store.state.rules.push(created);
        return HttpResponse.json(created, { status: 201 });
      }
    ),
    http.put(
      "*/api/v1/studies/:studyId/rules/:ruleId",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.rules.find((r) => r.id === params.ruleId);
        if (found) Object.assign(found, body);
        return HttpResponse.json(found || { id: params.ruleId, ...body });
      }
    ),
    http.delete("*/api/v1/studies/:studyId/rules/:ruleId", ({ params }) => {
      store.state.rules = store.state.rules.filter(
        (r) => r.id !== params.ruleId
      );
      return HttpResponse.json({ deleted: true, id: params.ruleId });
    }),
    http.get("*/api/v1/studies/:studyId", ({ params }) =>
      HttpResponse.json({
        id: params.studyId,
        code: params.studyId,
        title: `Synthetic Clinical Study ${params.studyId}`,
        status: "ACTIVE",
      })
    ),
    http.get("*/api/v1/studies", () =>
      HttpResponse.json([...store.state.studies.values()])
    ),
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
      HttpResponse.json([
        {
          id: "doc-demo-01",
          filename: "DEMO-01-Protocol-v2.0.pdf",
          artifact_code: "01.01.02",
          status: "APPROVED",
          site_id: "SITE-101",
          uploaded_at: "2026-07-01T10:00:00Z",
        },
      ])
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
      const rawReason =
        request.headers.get("X-Change-Reason") ||
        request.headers.get("x-change-reason") ||
        "";
      let decodedReason;
      try {
        decodedReason = decodeURIComponent(rawReason);
      } catch {
        decodedReason = rawReason;
      }
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
    http.get("*/api/v1/payments/participant/wallets", () =>
      HttpResponse.json(store.state.participantWallets)
    ),
    http.get("*/api/v1/payments/participant/stipend-rules", () =>
      HttpResponse.json(store.state.stipendRules)
    ),
    http.get("*/api/v1/payments/participant/expenses", () =>
      HttpResponse.json(store.state.expenseClaims)
    ),
    http.get("*/api/v1/payments/participant/transactions", () =>
      HttpResponse.json(store.state.walletTransactions)
    ),
    http.get(
      "*/api/v1/payments/participant/wallets/:walletId/tax-1099-status",
      ({ params }) => {
        const wallet =
          store.state.participantWallets.find(
            (w) => w.id === params.walletId
          ) || store.state.participantWallets[0];
        const taxableYtd = Number(wallet?.taxable_ytd || 0);
        const pct = Number(((taxableYtd / 600) * 100).toFixed(1));
        return HttpResponse.json({
          wallet_id: wallet?.id || params.walletId,
          taxable_ytd: taxableYtd,
          threshold_amount: 600,
          threshold_percentage: pct,
          warning_level:
            pct >= 100 ? "EXCEEDED" : pct >= 80 ? "APPROACHING" : "NORMAL",
          w9_status: wallet?.w9_status || "NOT_REQUIRED",
        });
      }
    ),
    http.get(
      "*/api/v1/payments/participant/wallets/:walletId/balance",
      ({ params }) => {
        const wallet =
          store.state.participantWallets.find(
            (w) => w.id === params.walletId
          ) || store.state.participantWallets[0];
        const pendingTotal = store.state.expenseClaims
          .filter((c) => c.status === "PENDING_REVIEW")
          .reduce((sum, c) => sum + Number(c.gross_amount || 0), 0);
        return HttpResponse.json({
          wallet_id: wallet?.id || params.walletId,
          available_balance: Number(wallet?.current_balance || 0),
          pending_claims_total: pendingTotal,
          lifetime_stipends: Number(wallet?.lifetime_stipends || 0),
          lifetime_reimbursements: Number(wallet?.lifetime_reimbursements || 0),
          lifetime_disbursements: Number(wallet?.lifetime_disbursements || 0),
          taxable_ytd: Number(wallet?.taxable_ytd || 0),
        });
      }
    ),
    http.post(
      "*/api/v1/payments/participant/wallets/:walletId/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const wallet =
          store.state.participantWallets.find(
            (w) => w.id === params.walletId
          ) || store.state.participantWallets[0];
        const action = String(params.action);
        if (wallet && action === "issue-card") {
          wallet.card_status = "ACTIVE";
          wallet.virtual_card_mask = "**** **** **** 4821";
          wallet.virtual_card_token = `CARD-DEMO-${Date.now()}`;
        } else if (wallet && action === "submit-w9") {
          wallet.w9_status = "SUBMITTED";
          wallet.tax_id_last_four = String(
            body?.tax_id_last_four || body?.taxIdLastFour || "0000"
          );
        } else if (wallet && action === "verify-w9") {
          wallet.w9_status = "VERIFIED";
        } else if (wallet && action === "disburse") {
          const amount = Number(body?.amount ?? wallet.current_balance ?? 0);
          wallet.current_balance = Math.max(
            0,
            Number(wallet.current_balance || 0) - amount
          );
          wallet.lifetime_disbursements =
            Number(wallet.lifetime_disbursements || 0) + amount;
          store.state.walletTransactions.unshift({
            id: `wtx-${Date.now()}`,
            created_at: new Date().toISOString(),
            subject_id: wallet.subject_id,
            study_id: wallet.study_id,
            transaction_type: "DISBURSEMENT",
            amount: -amount,
            balance_after: wallet.current_balance,
            description: `Digital payout (${body?.disbursement_type || body?.disbursementType || "DIRECT_ACH"})`,
          });
        }
        return HttpResponse.json(wallet || { id: params.walletId });
      }
    ),
    http.post(
      "*/api/v1/payments/participant/events/visit-verified",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const subjectId = body?.subject_id || body?.subjectId || "SUBJ-1001";
        const wallet =
          store.state.participantWallets.find(
            (w) => w.subject_id === subjectId
          ) || store.state.participantWallets[0];
        const credited = 75;
        if (wallet) {
          wallet.current_balance =
            Number(wallet.current_balance || 0) + credited;
          wallet.lifetime_stipends =
            Number(wallet.lifetime_stipends || 0) + credited;
          wallet.taxable_ytd = Number(wallet.taxable_ytd || 0) + credited;
          store.state.walletTransactions.unshift({
            id: `wtx-${Date.now()}`,
            created_at: new Date().toISOString(),
            subject_id: wallet.subject_id,
            study_id: wallet.study_id,
            transaction_type: "VISIT_STIPEND",
            amount: credited,
            balance_after: wallet.current_balance,
            description: `Verified visit ${body?.visit_code || body?.visitCode || "VISIT"}`,
          });
        }
        return HttpResponse.json({
          amount: credited,
          current_balance: wallet?.current_balance ?? credited,
        });
      }
    ),
    http.post(
      "*/api/v1/payments/participant/expenses/ocr-extract",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const rawText = String(body?.rawText || body?.raw_text || "").trim();
        if (!rawText) {
          return HttpResponse.json({
            ocr_readable: false,
            merchant_name: "",
            expense_date: "",
            gross_amount: 0,
            tax_deduction: 0,
            net_amount: 0,
            category: "OTHER",
            confidence_score: 0,
            raw_text: "",
            warning:
              "Receipt text could not be extracted automatically in demo mode; enter claim details below.",
          });
        }
        return HttpResponse.json({
          ocr_readable: true,
          merchant_name: "Demo Transit Authority",
          expense_date: "2026-08-01", // deid-ignore
          gross_amount: 24.5,
          tax_deduction: 0,
          net_amount: 24.5,
          category: "TRANSIT",
          confidence_score: 0.95,
          raw_text: rawText,
          warning: null,
        });
      }
    ),
    http.post("*/api/v1/payments/participant/expenses", async ({ request }) => {
      const body = await readJsonBody(request);
      const gross = Number(body?.gross_amount ?? body?.grossAmount ?? 0);
      const conf = Number(body?.ocr_confidence ?? body?.ocrConfidence ?? 0);
      const autoApproved = gross > 0 && gross <= 100 && conf >= 0.9;
      const created = {
        id: `exp-${Date.now()}`,
        subject_id: body?.subject_id || body?.subjectId || "SUBJ-1001",
        expense_date: body?.expense_date || body?.expenseDate || "2026-08-01", // deid-ignore
        category: body?.category || "TRANSIT",
        merchant_name:
          body?.merchant_name || body?.merchantName || "Demo Receipt",
        gross_amount: gross,
        tax_deduction: 0,
        net_amount: gross,
        status: autoApproved ? "APPROVED" : "PENDING_REVIEW",
        auto_approved: autoApproved,
        receipt_image_name:
          body?.receipt_image_name || body?.receiptImageName || "receipt.png",
      };
      store.state.expenseClaims.unshift(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.post(
      "*/api/v1/payments/participant/expenses/:claimId/review",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.expenseClaims.find(
          (c) => c.id === params.claimId
        );
        if (found) {
          found.status = body?.status || "APPROVED";
        }
        return HttpResponse.json(
          found || { id: params.claimId, status: body?.status || "APPROVED" }
        );
      }
    ),
    http.get("*/api/v1/payments/lay-summaries", () =>
      HttpResponse.json([
        {
          id: "lsum-01",
          study_id: "DEMO-01",
          title: "Plain-Language Summary of Study DEMO-01",
          language: "en",
          reading_grade_level: 6.5,
          status: "PUBLISHED",
        },
      ])
    ),
    http.get("*/api/v1/execution/coding/assignments", () =>
      HttpResponse.json([])
    ),
    http.get(
      "*/api/v1/execution/amendments/:studyId/subject-impact",
      ({ params }) =>
        HttpResponse.json({
          study_id: params.studyId,
          target_version: "2.0",
          impacted_subjects: [],
          total_impacted: 0,
        })
    ),
  ];
}
