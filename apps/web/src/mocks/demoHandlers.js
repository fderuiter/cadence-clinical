import { http, HttpResponse } from "msw";
import { isValidSemanticAction, isValidStepUpAction } from "ui";
import { demoNowIso, demoNowMs, nextDemoStep } from "../utils/demoMode.js";
import {
  evaluateCapabilityDecision,
  evaluateDemoRequestPolicy,
  evaluateNodeCommands,
  resolveDemoCaller,
} from "./demoPolicy.js";
import { webDemoStore } from "./demoStore.js";
import { parseDemoProtocolImportRequest } from "./demoStudyImport.js";
import { buildUsdmSoaSchedule } from "../stores/demo/usdmSoaSchedule";

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
  const createInitialEtmfDocuments = () => [
    {
      id: "doc-demo-01",
      filename: "DEMO-01-Protocol-v2.0.pdf",
      zone: "01. Trial Management",
      section: "01.01 Trial Steering Committee",
      artifact_type: "Protocol",
      artifact_code: "01.01.02",
      status: "APPROVED",
      version_index: 1.0,
      site_id: "SITE-101",
      uploaded_at: "2026-07-01T10:00:00Z", // deid-ignore
    },
  ];
  const createInitialEtmfAuditLogs = () => [
    {
      id: "elog-seed-01",
      timestamp: "2026-07-01T10:00:00Z", // deid-ignore
      user_id: "qa_director",
      user_role: "Sponsor Admin",
      action: "INGEST",
      document_id: "doc-demo-01",
      entity_type: "Document",
      details:
        "Ingested document: DEMO-01-Protocol-v2.0.pdf under Zone: 01. Trial Management",
      reason_for_change: "Initial protocol eTMF baseline registration",
    },
  ];
  const etmfDocuments = createInitialEtmfDocuments();
  const etmfAuditLogs = createInitialEtmfAuditLogs();
  let demoLocks = Array.isArray(store.locks) ? [...store.locks] : [];
  let resetSoaState = () => {};

  if (store && typeof store.onReset === "function") {
    store.onReset(() => {
      etmfDocuments.splice(
        0,
        etmfDocuments.length,
        ...createInitialEtmfDocuments()
      );
      etmfAuditLogs.splice(
        0,
        etmfAuditLogs.length,
        ...createInitialEtmfAuditLogs()
      );
      demoLocks = Array.isArray(store.locks) ? [...store.locks] : [];
      resetSoaState();
    });
  }

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
    http.all("*/api/*", async ({ request }) => {
      const denied = await evaluateDemoRequestPolicy(request, store);
      if (denied) {
        return HttpResponse.json(denied.problem, {
          status: denied.status,
          headers: { "Content-Type": "application/problem+json" },
        });
      }
      return undefined;
    }),
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
      const idempotencyKey =
        request.headers.get("Idempotency-Key") ||
        request.headers.get("idempotency-key") ||
        "";
      if (!idempotencyKey.trim()) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Idempotency.KeyRequired",
            title: "Missing Idempotency-Key",
            status: 400,
            code: "K.Idempotency.KeyRequired",
          },
          { status: 400 }
        );
      }
      const reason = readRequiredReasonHeader(request);
      if (!reason.trim()) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.ReasonForChange.Required",
            title: "Missing X-Change-Reason",
            status: 400,
            code: "K.ReasonForChange.Required",
          },
          { status: 400 }
        );
      }
      const body = await readJsonBody(request);
      const outcome = store.upgradeSubscriptionTier(body?.tier);
      if (!outcome || outcome.error === "invalid_tier") {
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
      if (outcome.error === "expired") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Subscription.Expired",
            title: "Trial subscription expired",
            status: 403,
            code: "K.Subscription.Expired",
            detail:
              "This tenant's trial subscription has expired. Read and export access remains active; activate a subscription to resume workspace writes.",
          },
          { status: 403 }
        );
      }
      return HttpResponse.json(outcome.data);
    }),
    http.post("*/api/v1/kernel/entitlements/activate", async ({ request }) => {
      const idempotencyKey =
        request.headers.get("Idempotency-Key") ||
        request.headers.get("idempotency-key") ||
        "";
      if (!idempotencyKey.trim()) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Idempotency.KeyRequired",
            title: "Missing Idempotency-Key",
            status: 400,
            code: "K.Idempotency.KeyRequired",
          },
          { status: 400 }
        );
      }
      const reason = readRequiredReasonHeader(request);
      if (!reason.trim()) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.ReasonForChange.Required",
            title: "Missing X-Change-Reason",
            status: 400,
            code: "K.ReasonForChange.Required",
          },
          { status: 400 }
        );
      }
      const body = await readJsonBody(request);
      const outcome = store.activateSubscription(body);
      if (outcome.error === "invalid_tier") {
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
      if (outcome.error === "scoping_invalid") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.ScopingInvalid",
            title: "Study scoping details required for subscription activation",
            status: 422,
            code: "K.Commercial.ScopingInvalid",
            detail:
              "Activating a commercial subscription requires valid study phase, duration, participating countries, site count, participant count, active study count, and a signed commercial quote token.",
            errors: outcome.errors,
          },
          { status: 422 }
        );
      }
      if (outcome.error === "quote_invalid") {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Commercial.QuoteInvalid",
            title: "Commercial quote token could not be verified",
            status: 403,
            code: "K.Commercial.QuoteInvalid",
            detail:
              "Subscription activation requires a verified signed commercial quote token (qtk1.<payload>.<signature>) bound to this tenant, tier, and study scoping.",
          },
          { status: 403 }
        );
      }
      return HttpResponse.json(outcome.data);
    }),
    http.post("*/api/v1/kernel/onboarding/trial", async ({ request }) => {
      const idempotencyKey =
        request.headers.get("Idempotency-Key") ||
        request.headers.get("idempotency-key") ||
        "";
      if (!idempotencyKey.trim()) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.Idempotency.KeyRequired",
            title: "Missing Idempotency-Key",
            status: 400,
            code: "K.Idempotency.KeyRequired",
          },
          { status: 400 }
        );
      }
      const reason = readRequiredReasonHeader(request);
      if (!reason.trim()) {
        return HttpResponse.json(
          {
            type: "https://cadence.clinical/errors/K.ReasonForChange.Required",
            title: "Missing X-Change-Reason",
            status: 400,
            code: "K.ReasonForChange.Required",
          },
          { status: 400 }
        );
      }
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
    http.get("*/api/v1/kernel/enrollments/lookup", ({ request }) => {
      const url = new URL(request.url);
      const result = store.lookupEnrollment({
        studyCode: url.searchParams.get("study_code") || "",
        subjectCode: url.searchParams.get("subject_code") || "",
        siteCode: url.searchParams.get("site_code"),
      });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
          headers: { "Content-Type": "application/problem+json" },
        });
      }
      return HttpResponse.json(result.data);
    }),
    http.get("*/api/v1/kernel/authorization/can-i", ({ request }) => {
      const url = new URL(request.url);
      const capability =
        url.searchParams.get("capability") ||
        url.searchParams.get("action") ||
        "kernel.read";
      const resource =
        url.searchParams.get("resource") ||
        url.searchParams.get("scope") ||
        "study:demo";
      return HttpResponse.json(
        evaluateCapabilityDecision(
          resolveDemoCaller(request),
          capability,
          resource,
          store
        )
      );
    }),
    http.get("*/api/v1/kernel/capabilities/can-i", ({ request }) => {
      const url = new URL(request.url);
      const capability =
        url.searchParams.get("capability") ||
        url.searchParams.get("action") ||
        "kernel.read";
      const resource =
        url.searchParams.get("resource") ||
        url.searchParams.get("scope") ||
        "study:demo";
      return HttpResponse.json(
        evaluateCapabilityDecision(
          resolveDemoCaller(request),
          capability,
          resource,
          store
        )
      );
    }),
    http.post("*/api/v1/kernel/authorization/can-i", async ({ request }) => {
      const body = await readJsonBody(request);
      const capability = body?.capability || body?.action || "kernel.read";
      const resource = body?.resource || body?.scope || "study:demo";
      return HttpResponse.json(
        evaluateCapabilityDecision(
          resolveDemoCaller(request),
          capability,
          resource,
          store
        )
      );
    }),
    http.post("*/api/v1/kernel/capabilities/can-i", async ({ request }) => {
      const body = await readJsonBody(request);
      const capability = body?.capability || body?.action || "kernel.read";
      const resource = body?.resource || body?.scope || "study:demo";
      return HttpResponse.json(
        evaluateCapabilityDecision(
          resolveDemoCaller(request),
          capability,
          resource,
          store
        )
      );
    }),
    http.get("*/api/v1/kernel/nodes/:nodeRef/path", ({ params, request }) =>
      HttpResponse.json(
        store.getZoomPath(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
      )
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef/children", ({ params, request }) =>
      HttpResponse.json(
        store.getChildren(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
      )
    ),
    http.get("*/api/v1/kernel/nodes/:nodeRef/commands", ({ params, request }) =>
      HttpResponse.json(
        evaluateNodeCommands(
          resolveDemoCaller(request),
          decodeURIComponent(String(params.nodeRef)),
          store
        )
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
            Number(url.searchParams.get("depth") || 3),
            resolveDemoCaller(request)
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
            Number(url.searchParams.get("depth") || 3),
            resolveDemoCaller(request)
          )
        );
      }
    ),
    http.get("*/api/v1/kernel/nodes", ({ request }) => {
      const url = new URL(request.url);
      const refs = url.searchParams.getAll("ref");
      const result = store.getNodes(refs, resolveDemoCaller(request));
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
          headers: { "Content-Type": "application/problem+json" },
        });
      }
      return HttpResponse.json(result.data);
    }),
    http.get("*/api/v1/kernel/nodes/:nodeRef", ({ params, request }) =>
      HttpResponse.json(
        store.getNode(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
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
    http.get("*/api/v1/kernel/organizations", ({ request }) => {
      const url = new URL(request.url);
      const kind = url.searchParams.get("kind") || undefined;
      return HttpResponse.json(store.listOrganizations(kind));
    }),
    http.post("*/api/v1/kernel/organizations", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey =
        request.headers.get("Idempotency-Key") ||
        request.headers.get("idempotency-key") ||
        "";
      const result = store.createOrganization(body, { reason, idempotencyKey });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/kernel/sites", () =>
      HttpResponse.json(store.listSites())
    ),
    http.post("*/api/v1/kernel/sites", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey =
        request.headers.get("Idempotency-Key") ||
        request.headers.get("idempotency-key") ||
        "";
      const result = store.createSite(body, { reason, idempotencyKey });
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/kernel/studies/:studyRef/sites", ({ params }) => {
      const result = store.listStudySites(
        decodeURIComponent(String(params.studyRef))
      );
      if (Array.isArray(result)) {
        return HttpResponse.json(result);
      }
      if (result.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
        });
      }
      return HttpResponse.json(result.data);
    }),
    http.post(
      "*/api/v1/kernel/studies/:studyRef/sites",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const reason = readRequiredReasonHeader(request);
        const idempotencyKey =
          request.headers.get("Idempotency-Key") ||
          request.headers.get("idempotency-key") ||
          "";
        const result = store.linkSiteToStudy(
          decodeURIComponent(String(params.studyRef)),
          body,
          { reason, idempotencyKey }
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data, {
          status: result.replayed ? 200 : 201,
        });
      }
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
    http.get(
      "*/api/v1/kernel/studies/:studyRef/proposals",
      ({ params, request }) => {
        const url = new URL(request.url);
        const studyRef = decodeURIComponent(String(params.studyRef));
        const status = url.searchParams.get("status") || undefined;
        const limit = url.searchParams.get("limit") || undefined;
        const result = store.listProposals(studyRef, { status, limit });
        if (Array.isArray(result)) {
          return HttpResponse.json(result);
        }
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
            headers: { "Content-Type": "application/problem+json" },
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/attention",
      ({ params, request }) => {
        const url = new URL(request.url);
        const studyRef = decodeURIComponent(String(params.studyRef));
        const limit = url.searchParams.get("limit") || 20;
        const result = store.getStudyAttention(studyRef, { limit });
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
            headers: { "Content-Type": "application/problem+json" },
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/locks",
      ({ params, request }) => {
        const url = new URL(request.url);
        const activeOnly = url.searchParams.get("active_only") === "true";
        const now = new Date().toISOString();
        const rawStudyRef = decodeURIComponent(String(params.studyRef));
        const resolved =
          typeof store.resolveStudy === "function"
            ? store.resolveStudy(rawStudyRef)
            : { study: { ref: rawStudyRef, code: "CAD-ONC-2026" } };
        if (resolved.error) {
          return HttpResponse.json(resolved.error.problem, {
            status: resolved.error.status,
            headers: { "Content-Type": "application/problem+json" },
          });
        }
        const canonicalRef = String(resolved.study.ref).split("@")[0];
        const locks = activeOnly
          ? demoLocks.filter((item) => !item.released)
          : demoLocks;
        return HttpResponse.json({
          study_ref: canonicalRef,
          study_code: resolved.study.code || "CAD-ONC-2026",
          valid_at: now,
          known_at: now,
          locks,
          hidden: { more_exist: false, reasons: [] },
        });
      }
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/locks/readiness",
      ({ params, request }) => {
        const url = new URL(request.url);
        const rawStudyRef = decodeURIComponent(String(params.studyRef));
        if (typeof store.resolveStudy === "function") {
          const resolved = store.resolveStudy(rawStudyRef);
          if (resolved.error) {
            return HttpResponse.json(resolved.error.problem, {
              status: resolved.error.status,
              headers: { "Content-Type": "application/problem+json" },
            });
          }
        }
        const scopeRef = url.searchParams.get("scope_ref") || undefined;
        return HttpResponse.json(store.getLockReadiness(rawStudyRef, scopeRef));
      }
    ),
    http.post("*/api/v1/kernel/locks", async ({ request }) => {
      const body = await readJsonBody(request);
      const now = new Date().toISOString();
      const studyRef = body?.study_ref || DEFAULT_STUDY_REF;
      const scopeRef = body?.scope_ref || studyRef;
      const kind = body?.kind || "Frozen";
      if (String(kind).toLowerCase() === "locked") {
        const readiness = store.getLockReadiness(studyRef, scopeRef);
        if (!readiness.ready_for_lock) {
          return HttpResponse.json(
            {
              title: "K.Assertion.ScopeNotReady",
              status: 409,
              code: "K.Assertion.ScopeNotReady",
              detail: readiness.explanation.claim,
              explanation: readiness.explanation,
            },
            { status: 409 }
          );
        }
      }
      const scopeKind =
        body?.scope_kind || String(scopeRef).split(":")[0] || "study";
      const suffix = String(Date.now()).slice(-12).padStart(12, "0");
      const record = {
        ref: body?.lock_id
          ? `assertion:${body.lock_id}`
          : `assertion:01960000-0000-7000-8000-${suffix}`, // deid-ignore
        study_ref: body?.study_ref || DEFAULT_STUDY_REF,
        scope_ref: scopeRef,
        scope_kind: scopeKind,
        subject_code: null,
        kind: body?.kind || "Frozen",
        attestation_ref: body?.attestation_id
          ? `attestation:${body.attestation_id}`
          : "attestation:01960000-0000-7000-8000-000000000099", // deid-ignore
        attestation_meaning: body?.meaning || "Approved",
        locked_at: now,
        locked_by: "usr-demo-operator",
        reason_for_change:
          decodeReasonHeader(request) || body?.reason_for_change || "Demo lock",
        released: false,
        release_ref: null,
        released_at: null,
        released_by: null,
        release_reason: null,
        masked: false,
        mask: null,
      };
      demoLocks = [...demoLocks, record];
      store.locks = demoLocks;
      return HttpResponse.json(record, { status: 201 });
    }),
    http.post(
      "*/api/v1/kernel/locks/:lockRef/release",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const lockRef = decodeURIComponent(String(params.lockRef));
        const now = new Date().toISOString();
        const reason = decodeReasonHeader(request) || "Released in demo";
        const suffix = String(Date.now()).slice(-12).padStart(12, "0");
        demoLocks = demoLocks.map((item) =>
          item.ref === lockRef
            ? {
                ...item,
                released: true,
                release_ref: body?.assertion_id
                  ? `assertion:${body.assertion_id}`
                  : `assertion:01960000-0000-7000-8001-${suffix}`, // deid-ignore
                released_at: now,
                released_by: "usr-demo-operator",
                release_reason: reason,
              }
            : item
        );
        store.locks = demoLocks;
        const updated = demoLocks.find((item) => item.ref === lockRef) || {
          ref: lockRef,
          study_ref: body?.study_ref || DEFAULT_STUDY_REF,
          scope_ref: body?.study_ref || DEFAULT_STUDY_REF,
          scope_kind: "study",
          subject_code: null,
          kind: "Frozen",
          attestation_ref: "attestation:01960000-0000-7000-8000-000000000099", // deid-ignore
          attestation_meaning: body?.meaning || "Responsibility",
          locked_at: now,
          locked_by: "usr-demo-operator",
          reason_for_change: "Demo lock",
          released: true,
          release_ref: `assertion:01960000-0000-7000-8001-${suffix}`, // deid-ignore
          released_at: now,
          released_by: "usr-demo-operator",
          release_reason: reason,
          masked: false,
          mask: null,
        };
        return HttpResponse.json(updated);
      }
    ),
    http.get("*/api/v1/kernel/work-queues", ({ request }) => {
      const url = new URL(request.url);
      const within = url.searchParams.get("within") || undefined;
      const queue = url.searchParams.get("queue") || undefined;
      if (typeof store.listWorkQueues === "function") {
        const result = store.listWorkQueues({ within, queue });
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
            headers: { "Content-Type": "application/problem+json" },
          });
        }
        return HttpResponse.json(result.data);
      }
      return HttpResponse.json({ queues: [] });
    }),
    http.post("*/api/v1/kernel/work-queues", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000900",
          code: body?.code || "custom_queue",
          version: 1,
          title: body?.title || "Custom Queue",
          capability: body?.capability || "observation.verify",
          verb_class: body?.verb_class || "Execute",
          source: body?.source || "observation.awaiting_verification",
        },
        { status: 201 }
      );
    }),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/validation/findings",
      ({ params, request }) => {
        const url = new URL(request.url);
        const studyRef = decodeURIComponent(String(params.studyRef));
        if (typeof store.listStudyValidationFindings === "function") {
          const result = store.listStudyValidationFindings(studyRef, {
            stage: url.searchParams.get("stage") || undefined,
            statuses: url.searchParams.getAll("status"),
            severity: url.searchParams.get("severity") || undefined,
            dataset: url.searchParams.get("dataset") || undefined,
            packKey: url.searchParams.get("pack_key") || undefined,
            limit: url.searchParams.get("limit") || undefined,
          });
          if (result.error) {
            return HttpResponse.json(result.error.problem, {
              status: result.error.status,
              headers: { "Content-Type": "application/problem+json" },
            });
          }
          return HttpResponse.json(result.data);
        }
        return HttpResponse.json({
          total: 0,
          findings: [],
        });
      }
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/exports",
      async ({ params, request }) => {
        const url = new URL(request.url);
        const format = (url.searchParams.get("format") || "csv").toLowerCase();
        const studyRef = decodeURIComponent(String(params.studyRef));
        const generatedAt = demoNowIso();
        const cutAt =
          url.searchParams.get("known_at") ||
          url.searchParams.get("as_of") ||
          generatedAt;
        const validAt = url.searchParams.get("valid_at") || cutAt;
        const sha256Hex = async (bytes) => {
          const digest = await globalThis.crypto.subtle.digest(
            "SHA-256",
            bytes
          );
          return Array.from(new Uint8Array(digest))
            .map((b) => b.toString(16).padStart(2, "0"))
            .join("");
        };
        const textHeaders = async (bodyText, contentType, extra = {}) => {
          const bytes = new TextEncoder().encode(bodyText);
          return {
            "Content-Type": contentType,
            "X-Content-Sha256": await sha256Hex(bytes),
            "X-Generated-At": generatedAt,
            "X-Hidden-More-Exist": "false",
            ...extra,
          };
        };
        if (format === "odm_xml" || format === "odm" || format === "xml") {
          const xml = `<?xml version="1.0" encoding="UTF-8"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="DEMO.${studyRef}" CreationDateTime="${cutAt}" ODMVersion="1.3.2"><Study OID="${studyRef}"/></ODM>`;
          return new HttpResponse(xml, {
            headers: await textHeaders(xml, "application/xml"),
          });
        }
        if (
          format === "dataset_json" ||
          format === "dataset-json" ||
          format === "json"
        ) {
          const jsonText = JSON.stringify({
            datasetJSONCreationDateTime: cutAt,
            study_ref: studyRef,
            valid_at: validAt,
            known_at: cutAt,
            hidden: { more_exist: false, reasons: [] },
            itemGroupData: {},
          });
          return new HttpResponse(jsonText, {
            headers: await textHeaders(jsonText, "application/json"),
          });
        }
        if (
          format === "sas_bundle" ||
          format === "r_bundle" ||
          format === "study_archive"
        ) {
          // Valid empty-archive PK\x05\x06 End of Central Directory (22 bytes) for browser/unit download checks
          const eocd = new Uint8Array([
            0x50, 0x4b, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
          ]);
          return new HttpResponse(eocd, {
            headers: {
              "Content-Type": "application/zip",
              "Content-Disposition": `attachment; filename="CAD-ONC-2026.${format}.zip"`,
              "X-Content-Sha256": await sha256Hex(eocd),
              "X-Generated-At": generatedAt,
              "X-Hidden-More-Exist": "false",
            },
          });
        }
        const csvText = "STUDYID,USUBJID\nCAD-ONC-2026,SUBJ-1001\n";
        return new HttpResponse(csvText, {
          headers: await textHeaders(csvText, "text/csv"),
        });
      }
    ),
    http.get(
      "*/api/v1/kernel/enrollments/:enrollmentRef/casebook",
      async ({ params, request }) => {
        const url = new URL(request.url);
        const format = (url.searchParams.get("format") || "json").toLowerCase();
        const enrollmentRef = decodeURIComponent(String(params.enrollmentRef));
        const generatedAt = demoNowIso();
        const cutAt =
          url.searchParams.get("known_at") ||
          url.searchParams.get("as_of") ||
          generatedAt;
        const validAt = url.searchParams.get("valid_at") || cutAt;
        const sha256Hex = async (bytes) => {
          const digest = await globalThis.crypto.subtle.digest(
            "SHA-256",
            bytes
          );
          return Array.from(new Uint8Array(digest))
            .map((b) => b.toString(16).padStart(2, "0"))
            .join("");
        };
        const textHeaders = async (bodyText, contentType, extra = {}) => {
          const bytes = new TextEncoder().encode(bodyText);
          return {
            "Content-Type": contentType,
            "X-Content-Sha256": await sha256Hex(bytes),
            "X-Generated-At": generatedAt,
            ...extra,
          };
        };
        if (format === "odm_xml" || format === "odm" || format === "xml") {
          const xml = `<?xml version="1.0" encoding="UTF-8"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="CASEBOOK.${enrollmentRef}" CreationDateTime="${cutAt}" ODMVersion="1.3.2"><Study OID="CAD-ONC-2026"><GlobalVariables><StudyName>CAD-ONC-2026</StudyName><StudyDescription>CAD-ONC-2026</StudyDescription><ProtocolName>CAD-ONC-2026</ProtocolName></GlobalVariables><MetaDataVersion OID="MDV.1" Name="Protocol v1"/></Study><ClinicalData StudyOID="CAD-ONC-2026" MetaDataVersionOID="MDV.1"><SubjectData SubjectKey="${enrollmentRef}"/></ClinicalData></ODM>`;
          return new HttpResponse(xml, {
            headers: await textHeaders(xml, "application/xml"),
          });
        }
        if (format === "html") {
          const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Casebook ${enrollmentRef}</title></head><body><h1>Participant Casebook</h1><p>${enrollmentRef}</p><p>Known at ${cutAt}</p></body></html>`;
          return new HttpResponse(html, {
            headers: await textHeaders(html, "text/html; charset=utf-8"),
          });
        }
        if (format === "pdf") {
          // deid-ignore: PDF-1.7 10-digit xref byte offsets are structural PDF syntax, not telephone numbers
          const pdfText = `%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\nxref\n0 3\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \ntrailer << /Size 3 /Root 1 0 R >>\nstartxref\n115\n%%EOF\n`; // deid-ignore
          return new HttpResponse(pdfText, {
            headers: await textHeaders(pdfText, "application/pdf", {
              "Content-Disposition": `attachment; filename="CAD-ONC-2026.${enrollmentRef.replace(/[^a-zA-Z0-9_-]/g, "_")}.casebook.pdf"`,
            }),
          });
        }
        const jsonText = JSON.stringify({
          enrollment_ref: enrollmentRef,
          valid_at: validAt,
          known_at: cutAt,
          generated_at: cutAt,
          observations: [],
        });
        return new HttpResponse(jsonText, {
          headers: await textHeaders(jsonText, "application/json"),
        });
      }
    ),
    http.get("*/api/v1/kernel/proposals/:proposalRef", ({ params }) =>
      HttpResponse.json(
        store.getProposal(decodeURIComponent(String(params.proposalRef)))
      )
    ),
    http.post(
      "*/api/v1/kernel/proposals/:proposalRef/rejection",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const caller = resolveDemoCaller(request);
        const reason =
          readRequiredReasonHeader(request) ||
          (typeof body?.reason === "string" ? body.reason.trim() : "") ||
          decodeReasonHeader(request);
        return HttpResponse.json(
          store.rejectProposal(
            decodeURIComponent(String(params.proposalRef)),
            reason,
            caller
          )
        );
      }
    ),
    http.get("*/api/v1/kernel/activity", ({ request }) => {
      const url = new URL(request.url);
      const scope = url.searchParams.get("scope") || undefined;
      const families = url.searchParams.getAll("family");
      const limit = url.searchParams.get("limit") || 25;
      const includeNodes = url.searchParams.get("include_nodes") === "true";
      const result = store.getActivityPage({
        scope,
        families,
        limit,
        includeNodes,
        caller: resolveDemoCaller(request),
      });
      if (result?.error) {
        return HttpResponse.json(result.error.problem, {
          status: result.error.status,
          headers: { "Content-Type": "application/problem+json" },
        });
      }
      return HttpResponse.json(result?.data ?? result);
    }),
    http.post("*/api/v1/kernel/simulation/simulate", async ({ request }) => {
      const body = await readJsonBody(request);
      const now = new Date().toISOString();
      const targetRef =
        body?.stored_version_ref || body?.base_version_ref || DEFAULT_STUDY_REF;
      return HttpResponse.json({
        scope: DEFAULT_STUDY_REF,
        as_of: { valid_at: now, known_at: now },
        candidate: {
          kind: "ProtocolVersion",
          version: body?.stored_version_ref || null,
          base_version: body?.base_version_ref || null,
          content_hash: "sha256:demo-sim-cand",
          base_content_hash: body?.base_version_ref
            ? "sha256:demo-sim-base"
            : null,
        },
        engine_versions: [
          { name: "protocol.expected_schedule", version: "1" },
          { name: "protocol.matrix_gap", version: "1" },
          { name: "protocol.structural_diff", version: "1" },
        ],
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
    http.get(
      "*/api/v1/kernel/observations/:observationRef",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getObservation(
            decodeURIComponent(String(params.observationRef)),
            {
              studyCode:
                url.searchParams.get("studyId") ||
                url.searchParams.get("study_code") ||
                "",
              siteCode:
                url.searchParams.get("siteId") ||
                url.searchParams.get("site_code") ||
                "",
              subjectCode:
                url.searchParams.get("subjectId") ||
                url.searchParams.get("subject_code") ||
                "",
            }
          )
        );
      }
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
    http.get("*/api/v1/execution/subjects/counts", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.getSubjectCounts({
          studyId: url.searchParams.get("study_id") || undefined,
          siteId: url.searchParams.get("site_id") || undefined,
        })
      );
    }),
    http.get("*/api/v1/execution/subjects/:subjectId", ({ params, request }) =>
      HttpResponse.json(
        store.getSubjectDetail(
          decodeURIComponent(String(params.subjectId)),
          resolveDemoCaller(request)
        )
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
    http.get("*/api/v1/execution/forms/queries/digests", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listQueryDigests(
          {
            studyId: url.searchParams.get("study_id"),
            site: url.searchParams.get("site"),
            recipientRole: url.searchParams.get("recipient_role"),
          },
          resolveDemoCaller(request)
        )
      );
    }),
    http.post("*/api/v1/execution/forms/queries/escalate", ({ request }) => {
      const url = new URL(request.url);
      const reason = decodeReasonHeader(request);
      if (!reason) {
        return HttpResponse.json(
          {
            title: "Reason for change required",
            status: 422,
            code: "K.Command.ReasonForChangeRequired",
            detail: "X-Change-Reason header is required.",
          },
          { status: 422 }
        );
      }
      return HttpResponse.json(
        store.escalateStudyQueries(
          url.searchParams.get("study_id"),
          reason,
          resolveDemoCaller(request)
        )
      );
    }),
    http.get("*/api/v1/execution/forms/queries", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listEnrollmentQueries(
          {
            enrollmentId: url.searchParams.get("enrollment_id"),
            studyId: url.searchParams.get("study_id"),
            status: url.searchParams.get("status"),
            owner: url.searchParams.get("owner"),
            origin: url.searchParams.get("origin"),
            priority: url.searchParams.get("priority"),
            escalated: url.searchParams.get("escalated"),
            assignee: url.searchParams.get("assignee"),
            site: url.searchParams.get("site"),
            participant: url.searchParams.get("participant"),
            valueChanged: url.searchParams.get("value_changed"),
          },
          resolveDemoCaller(request)
        )
      );
    }),
    http.get("*/api/v1/execution/forms/sdv-alerts", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listSdvAlerts(
          {
            studyId: url.searchParams.get("study_id"),
            enrollmentId: url.searchParams.get("enrollment_id"),
          },
          resolveDemoCaller(request)
        )
      );
    }),
    http.get(
      "*/api/v1/execution/forms/:formId/queries",
      ({ params, request }) =>
        HttpResponse.json(
          store.listFormQueries(
            decodeURIComponent(String(params.formId)),
            resolveDemoCaller(request)
          )
        )
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/queries",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.raiseQuery(
            decodeURIComponent(String(params.formId)),
            body,
            decodeReasonHeader(request)
          ),
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
    http.get(
      "*/api/v1/execution/rtsm/kits/inventory/:studyId/:siteId",
      ({ params }) =>
        HttpResponse.json(
          store.state.ipKits.filter(
            (k) => k.site_id === params.siteId || params.siteId === "SITE-101"
          )
        )
    ),
    http.post(
      "*/api/v1/execution/rtsm/kits/seed-inventory",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const items = Array.isArray(body?.kits) ? body.kits : [];
        for (const [idx, kit] of items.entries()) {
          store.state.ipKits.push({
            id: `kit-${demoNowMs(idx)}`,
            study_id: body?.study_id || "DEMO-01",
            site_id: body?.site_id || "SITE-101",
            kit_number: kit.kit_number || "KIT-DEMO-99",
            lot_number: kit.lot_number || "LOT-DEMO",
            status:
              kit.status === "AVAILABLE" || !kit.status
                ? "ACTIVE_AT_SITE"
                : kit.status,
            treatment_arm: kit.treatment_arm || null,
            expiration_date: kit.expiration_date || null,
          });
        }
        return HttpResponse.json({ seeded: items.length }, { status: 201 });
      }
    ),
    http.post(
      "*/api/v1/execution/rtsm/kits/validate-scan",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const kit = store.state.ipKits.find(
          (k) => k.kit_number === body?.kit_number
        );
        const scannedLot = body?.scanned_lot_number || null;
        const mismatch = Boolean(
          kit && scannedLot && kit.lot_number !== scannedLot
        );
        return HttpResponse.json({
          kit_number: body?.kit_number || "",
          study_id: body?.study_id || "DEMO-01",
          site_id: body?.site_id || "SITE-101",
          found_in_inventory: Boolean(kit),
          expected_lot_number: kit?.lot_number || null,
          scanned_lot_number: scannedLot,
          lot_mismatch_detected: mismatch,
          current_status: kit?.status || "UNKNOWN",
          recommended_disposition: mismatch
            ? "QUARANTINED"
            : "RETURNED_TO_DEPOT",
          warning: mismatch ? "Lot mismatch detected during scan." : null,
        });
      }
    ),
    http.post(
      "*/api/v1/execution/rtsm/kits/reconcile-batch",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const items = Array.isArray(body?.items) ? body.items : [];
        return HttpResponse.json({
          certificate: {
            certificate_id: `cert-${demoNowMs()}`,
            study_id: body?.study_id || "DEMO-01",
            site_id: body?.site_id || "SITE-101",
            processed_by: body?.processed_by || "usr-pharm-01",
            processed_at: demoNowIso(),
            total_kits_reconciled: items.length,
            items,
          },
        });
      }
    ),
    http.get(
      "*/api/v1/execution/rtsm/kits/certificates/:certificateId",
      ({ params }) =>
        HttpResponse.json({
          certificate_id: String(params.certificateId),
          study_id: "DEMO-01",
          site_id: "SITE-101",
          total_kits_reconciled: 1,
        })
    ),
    http.get("*/api/v1/execution/randomization/schemes", ({ request }) => {
      const url = new URL(request.url);
      const studyId = url.searchParams.get("study_id") || "DEMO-01";
      return HttpResponse.json([
        {
          id: "a1b2c3d4-e5f6-4a7b-8c9d-e0f1a2b3c4d5",
          study_id: studyId,
          version: 1,
          method: "permuted_block",
          status: "APPROVED",
          arms: { PLACEBO: 1, ACTIVE: 1 },
          stratification_factors: {},
          block_sizes: [4],
          slots_per_stratum: 8,
          preferred_arm_probability: 0,
          requires_kit: true,
          list_size: 8,
          configuration_hash: "demo-hash",
          drafted_by: "stat.author",
          drafted_at: demoNowIso(),
          approved_by: "stat.approver",
          approved_at: demoNowIso(),
          retired_at: null,
        },
      ]);
    }),
    http.post(
      "*/api/v1/execution/randomization/schemes",
      async ({ request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          {
            id: `b2c3d4e5-f6a7-4b8c-9d0e-f1a2b3c4d5e6`,
            study_id: body?.study_id || "DEMO-01",
            version: 2,
            method: body?.method || "permuted_block",
            status: "DRAFT",
            arms: body?.arms || { PLACEBO: 1, ACTIVE: 1 },
            stratification_factors: body?.stratification_factors || {},
            block_sizes: body?.block_sizes || [4],
            slots_per_stratum: body?.slots_per_stratum || 8,
            preferred_arm_probability: body?.preferred_arm_probability || 0,
            requires_kit: Boolean(body?.requires_kit ?? true),
            list_size: 0,
            configuration_hash: "demo-draft-hash",
            drafted_by: "stat.author",
            drafted_at: demoNowIso(),
            approved_by: null,
            approved_at: null,
            retired_at: null,
          },
          { status: 201 }
        );
      }
    ),
    http.post(
      "*/api/v1/execution/randomization/schemes/:schemeId/approve",
      ({ params }) =>
        HttpResponse.json({
          id: String(params.schemeId),
          study_id: "DEMO-01",
          version: 2,
          method: "permuted_block",
          status: "APPROVED",
          arms: { PLACEBO: 1, ACTIVE: 1 },
          stratification_factors: {},
          block_sizes: [4],
          slots_per_stratum: 8,
          preferred_arm_probability: 0,
          requires_kit: true,
          list_size: 8,
          configuration_hash: "demo-approved-hash",
          drafted_by: "stat.author",
          drafted_at: demoNowIso(),
          approved_by: "stat.approver",
          approved_at: demoNowIso(),
          retired_at: null,
        })
    ),
    http.post(
      "*/api/v1/execution/subjects/:subjectId/consent",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json({
          id: `consent-${demoNowMs()}`,
          subject_id: String(params.subjectId),
          study_id: body?.protocol_version?.study_id || "DEMO-01",
          version_tag: body?.protocol_version?.version_tag || "1.0",
          version_index: body?.protocol_version?.version_index || 1,
          icf_signed: Boolean(body?.icf_signed ?? true),
          icf_signed_date: body?.icf_signed_date || demoNowIso(),
          requires_reconsent: Boolean(body?.requires_reconsent ?? false),
          is_paper_override: Boolean(body?.is_paper_override ?? false),
          version: 1,
        });
      }
    ),
    http.post(
      "*/api/v1/execution/subjects/:subjectId/randomize",
      ({ params }) => {
        const subjectKey = decodeURIComponent(String(params.subjectId));
        const updated = store.updateSubjectState(subjectKey, {
          status: "RANDOMIZED",
        });
        const found = store.state.subjects.find(
          (s) =>
            s.id === updated.id ||
            s.subject_id.toUpperCase() === subjectKey.toUpperCase()
        );
        if (found) {
          found.treatment_group = null;
          found.unblinded_treatment_group = "ACTIVE";
          found.investigational_product_id = null;
        }
        return HttpResponse.json({
          randomization_id: `rand-${demoNowMs()}`,
          subject_id: updated?.subject_id || subjectKey,
          study_id: updated?.study_id || "DEMO-01",
          scheme_version: 1,
          stratum_key: "UNSTRATIFIED",
          kit_number: "KIT-1001",
          status: "RANDOMIZED",
          randomized_at: demoNowIso(),
        });
      }
    ),
    http.get(
      "*/api/v1/execution/rtsm/cgt/chain-of-custody/:studyId/:subjectId",
      ({ params }) =>
        HttpResponse.json({
          study_id: String(params.studyId),
          subject_id: String(params.subjectId),
          din: "DIN-DEMO-ALPHA",
          current_stage: "MANUFACTURING_RELEASED",
          status: "IN_CUSTODY",
          milestones: [
            {
              id: "cgt-ms-01",
              stage: "APHERESIS",
              milestone_type: "COLLECTION_VERIFIED",
              location_name: "Clinical Site Apheresis Unit",
            },
          ],
        })
    ),
    http.post("*/api/v1/execution/rtsm/cgt/apheresis", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        {
          id: `cgt-${demoNowMs()}`,
          study_id: body?.study_id || "DEMO-01",
          subject_id: body?.subject_id || "SUBJ-1001",
          din: body?.din || "DIN-DEMO-BETA",
          current_stage: "APHERESIS",
        },
        { status: 201 }
      );
    }),
    http.post("*/api/v1/execution/rtsm/cgt/milestone", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        { status: "RECORDED", ...body },
        { status: 201 }
      );
    }),
    http.get("*/api/v1/execution/rtsm/cgt/certificates/:studyId", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/rtsm/cgt/reconciliations/:studyId", () =>
      HttpResponse.json([])
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
    http.get("*/api/v1/etmf/documents", () => HttpResponse.json(etmfDocuments)),
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
        const now = new Date().toISOString();
        const existing = etmfDocuments.find((d) => d.id === docId);
        const signed = {
          id: docId,
          filename:
            existing?.filename || body?.filename || "sample_protocol.pdf",
          zone: existing?.zone || "01. Trial Management",
          section: existing?.section || "01.01 Trial Steering Committee",
          artifact_type: existing?.artifact_type || "Protocol",
          artifact_code: existing?.artifact_code || "01.01.02",
          site_id: existing?.site_id || "SITE-101",
          status: "SIGNED",
          version_index: 1.1,
          uploaded_at: existing?.uploaded_at || now,
          signature_manifestation: {
            signer_id: body?.username || body?.signer_id || "auditor_demo",
            timestamp: now,
            signing_reason: body?.signing_reason || body?.reason || "APPROVAL",
            sha256_hash: "sha256-demo-etmf-signed-789",
            signature: "demo-part11-manifestation-seal",
          },
        };
        if (existing) {
          Object.assign(existing, signed);
        } else {
          etmfDocuments.unshift(signed);
        }
        etmfAuditLogs.unshift({
          id: `elog-${Date.now()}`,
          timestamp: now,
          user_id: signed.signature_manifestation.signer_id,
          user_role: "Sponsor Admin",
          action: "SIGN",
          document_id: docId,
          entity_type: "Document",
          details: `Digitally signed document ${signed.filename} (v1.1)`,
          reason_for_change: signed.signature_manifestation.signing_reason,
        });
        return HttpResponse.json(signed);
      }
    ),
    http.get("*/api/v1/etmf/audit-logs", () =>
      HttpResponse.json({
        items: etmfAuditLogs,
        total_count: etmfAuditLogs.length,
      })
    ),
    http.get("*/api/v1/etmf/completeness", ({ request }) => {
      const url = new URL(request.url);
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
            document_id: etmfDocuments[0]?.id || "doc-demo-01",
            version_index: etmfDocuments[0]?.version_index ?? 1.0,
          },
        ],
      });
    }),
    http.get("*/api/v1/etmf/intelligence/qc-queue", () =>
      HttpResponse.json(
        etmfDocuments
          .filter((d) => d.status === "TECHNICAL_QC")
          .map((d) => ({
            document_id: d.id,
            study_id: d.study_id || "CADENCE-101",
            site_id: d.site_id || null,
            filename: d.filename,
            status: d.status,
            staged_at: d.uploaded_at || demoNowIso(0),
            staged_by: "cra.monitor",
            qc_assigned_to: null,
            discrepancy_notes: [],
            intelligence_report: {
              document_id: d.id,
              filename: d.filename,
              mime_type: "application/pdf",
              sha256_hash: "sha256-demo-qc-doc",
              confidence_tier: "HIGH",
              qc_recommendation: "AUTO_CLASSIFY",
              primary_classification: {
                zone: 5,
                section: "05.02",
                artifact_code: d.artifact_code || "05.02.01",
                artifact_type: d.artifact_type || "FDA Form 1572",
                confidence: 0.98,
              },
              alternative_candidates: [],
            },
          }))
      )
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
      const step = nextDemoStep("etmf-stage-qc");
      const docId = `DOC-DEMO-QC-${step}`;
      const stagedAt = demoNowIso(step);
      const studyId = body?.studyId || body?.study_id || "CADENCE-101";
      const siteId = body?.siteId || body?.site_id || null;
      const filename = body?.filename || "staged_document.pdf";
      etmfDocuments.unshift({
        id: docId,
        study_id: studyId,
        site_id: siteId,
        filename,
        zone: 5,
        section: "05.02",
        artifact_type: "FDA Form 1572",
        artifact_code: "05.02.01",
        status: "TECHNICAL_QC",
        version_index: 1,
        uploaded_at: stagedAt,
      });
      return HttpResponse.json(
        {
          document_id: docId,
          study_id: studyId,
          site_id: siteId,
          filename,
          status: "TECHNICAL_QC",
          staged_at: stagedAt,
          staged_by: "cra.monitor",
          qc_assigned_to: body?.assignedCra || body?.assigned_cra || null,
          discrepancy_notes: [],
          intelligence_report: {
            document_id: docId,
            filename,
            mime_type: body?.mimeType || body?.mime_type || "application/pdf",
            sha256_hash: `sha256-demo-qc-${step}`,
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
          },
        },
        { status: 201 }
      );
    }),
    http.post(
      "*/api/v1/etmf/intelligence/qc-review/:docId",
      async ({ params, request }) => {
        const docId = decodeURIComponent(String(params.docId));
        const body = await readJsonBody(request);
        const decision = String(body?.decision || "ACCEPT").toUpperCase();
        const nextStatus = decision === "REJECT" ? "REJECTED" : "APPROVED";
        const existing = etmfDocuments.find((d) => d.id === docId);
        if (existing) {
          existing.status = nextStatus;
        }
        return HttpResponse.json(
          existing || {
            id: docId,
            study_id: "CADENCE-101",
            filename: "reviewed_document.pdf",
            status: nextStatus,
            reason_for_change:
              body?.reasonForChange ||
              body?.reason_for_change ||
              decodeReasonHeader(request),
          }
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
          ocr_token: `demo_ocr_token_${Date.now()}`,
          warning: null,
        });
      }
    ),
    http.post("*/api/v1/payments/participant/expenses", async ({ request }) => {
      const body = await readJsonBody(request);
      const gross = Number(body?.gross_amount ?? body?.grossAmount ?? 0);
      const category = String(body?.category || "TRANSIT").toUpperCase();
      const isMeal = Boolean(
        body?.is_meal_allowance ?? body?.isMealAllowance ?? category === "MEAL"
      );
      const taxDeduction =
        isMeal && gross > 75 ? Number(((gross - 75) * 0.15).toFixed(2)) : 0;
      const netAmount = Number((gross - taxDeduction).toFixed(2));
      const hasServerScan = Boolean(
        body?.ocr_token ||
        body?.ocrToken ||
        body?.receipt_image_base64 ||
        body?.receiptImageBase64
      );
      const autoApproved =
        gross > 0 && gross <= 100 && (!isMeal || gross <= 75) && hasServerScan;
      const created = {
        id: `exp-${Date.now()}`,
        subject_id: body?.subject_id || body?.subjectId || "SUBJ-1001",
        expense_date: body?.expense_date || body?.expenseDate || "2026-08-01", // deid-ignore
        category,
        merchant_name:
          body?.merchant_name || body?.merchantName || "Demo Receipt",
        gross_amount: gross,
        tax_deduction: taxDeduction,
        net_amount: netAmount,
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
    http.get("*/api/v1/execution/coding/synonyms/jobs", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/coding/synonyms", () => HttpResponse.json([])),
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
    http.post("*/api/v1/designer/amendments/diff", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        study_id: body?.study_id || "DEMO-01",
        base_version_tag: body?.base_version_tag || "1.0.0",
        amended_version_tag: body?.amended_version_tag || "2.0.0",
        impact_summary: {
          impacted_sites: 2,
          subjects_requiring_reconsent: 2,
          added_procedures: 1,
          modified_visits: 1,
        },
        soa_matrix_diffs: [],
      });
    }),
    http.post("*/api/v1/designer/amendments/branch", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        status: "BRANCHED",
        study_id: body?.study_id || "DEMO-01",
        version_tag: body?.target_version_tag || "2.0.0",
      });
    }),
    http.post("*/api/v1/execution/amendments/publish", () =>
      HttpResponse.json({ status: "PUBLISHED" })
    ),
    http.post("*/api/v1/execution/amendments/reconsent", () =>
      HttpResponse.json({ status: "COMPLETED" })
    ),
    http.post("*/api/v1/execution/amendments/bulk-reconsent", () =>
      HttpResponse.json({ status: "COMPLETED", updated_count: 1 })
    ),

    // USDM Interactive SoA Builder Mutations (stateful in-memory demo projection)
    ...(() => {
      const createInitialSoaState = () =>
        buildUsdmSoaSchedule(["ACT-DEM", "ACT-VS"]);

      let soaState = createInitialSoaState();
      resetSoaState = () => {
        soaState = createInitialSoaState();
      };

      const upsertArm = (armId, props) => {
        const idx = soaState.arms.findIndex((a) => a.arm_id === armId);
        const entry = { arm_id: armId, arm_name: props?.name || armId };
        if (idx >= 0) soaState.arms[idx] = entry;
        else soaState.arms.push(entry);
        return entry;
      };

      const upsertEpoch = (epochId, props) => {
        const idx = soaState.epochs.findIndex((e) => e.epoch_id === epochId);
        const entry = {
          epoch_id: epochId,
          epoch_name: props?.name || epochId,
          sequence: Number(props?.sequence ?? soaState.epochs.length + 1),
          ...(props?.arm_id ? { arm_id: props.arm_id } : {}),
        };
        if (idx >= 0) soaState.epochs[idx] = entry;
        else soaState.epochs.push(entry);
        return entry;
      };

      const upsertVisit = (visitId, props) => {
        const idx = soaState.encounters.findIndex(
          (v) => v.encounter_id === visitId
        );
        const entry = {
          encounter_id: visitId,
          encounter_name: props?.name || visitId,
          epoch_id: props?.epoch_id || soaState.epochs[0]?.epoch_id || "EP-SCR",
          sequence: Number(props?.sequence ?? soaState.encounters.length + 1),
        };
        if (idx >= 0) soaState.encounters[idx] = entry;
        else soaState.encounters.push(entry);
        for (const row of soaState.rows) {
          if (!row.cells.some((c) => c.encounter_id === visitId)) {
            row.cells.push({ encounter_id: visitId, is_applicable: false });
          }
        }
        return entry;
      };

      const upsertProcedure = (procId, props) => {
        const idx = soaState.rows.findIndex((r) => r.activity_id === procId);
        if (idx >= 0) {
          soaState.rows[idx].activity_name =
            props?.name || soaState.rows[idx].activity_name;
          return soaState.rows[idx];
        }
        const entry = {
          activity_id: procId,
          activity_name: props?.name || procId,
          cells: soaState.encounters.map((enc) => ({
            encounter_id: enc.encounter_id,
            is_applicable: false,
          })),
        };
        soaState.rows.push(entry);
        return entry;
      };

      return [
        http.get(
          "*/api/v1/studies/:studyId/versions/:versionId/soa-projection",
          ({ params }) =>
            HttpResponse.json({
              study_id: params.studyId,
              version_id: params.versionId,
              arms: soaState.arms,
              epochs: soaState.epochs,
              encounters: soaState.encounters,
              rows: soaState.rows,
            })
        ),
        http.post(
          "*/api/v1/studies/:studyId/versions/:versionId/arms",
          async ({ request }) => {
            const body = await readJsonBody(request);
            const armId = body?.id || "ARM-DEMO";
            upsertArm(armId, body?.properties || body);
            return HttpResponse.json(
              { id: armId, properties: body?.properties || {} },
              { status: 201 }
            );
          }
        ),
        http.put(
          "*/api/v1/studies/:studyId/versions/:versionId/arms/:armId",
          async ({ params, request }) => {
            const body = await readJsonBody(request);
            upsertArm(String(params.armId), body?.properties || body);
            return HttpResponse.json({
              id: params.armId,
              properties: body?.properties || {},
            });
          }
        ),
        http.post(
          "*/api/v1/studies/:studyId/versions/:versionId/epochs",
          async ({ request }) => {
            const body = await readJsonBody(request);
            const epochId = body?.id || "EP-DEMO";
            upsertEpoch(epochId, body?.properties || body);
            return HttpResponse.json(
              { id: epochId, properties: body?.properties || {} },
              { status: 201 }
            );
          }
        ),
        http.put(
          "*/api/v1/studies/:studyId/versions/:versionId/epochs/:epochId",
          async ({ params, request }) => {
            const body = await readJsonBody(request);
            upsertEpoch(String(params.epochId), body?.properties || body);
            return HttpResponse.json({
              id: params.epochId,
              properties: body?.properties || {},
            });
          }
        ),
        http.post(
          "*/api/v1/studies/:studyId/versions/:versionId/visits",
          async ({ request }) => {
            const body = await readJsonBody(request);
            const visitId = body?.id || "V-DEMO";
            upsertVisit(visitId, body?.properties || body);
            return HttpResponse.json(
              { id: visitId, properties: body?.properties || {} },
              { status: 201 }
            );
          }
        ),
        http.put(
          "*/api/v1/studies/:studyId/versions/:versionId/visits/:visitId",
          async ({ params, request }) => {
            const body = await readJsonBody(request);
            upsertVisit(String(params.visitId), body?.properties || body);
            return HttpResponse.json({
              id: params.visitId,
              properties: body?.properties || {},
            });
          }
        ),
        http.post(
          "*/api/v1/studies/:studyId/versions/:versionId/procedures",
          async ({ request }) => {
            const body = await readJsonBody(request);
            const procId = body?.id || "ACT-DEMO";
            upsertProcedure(procId, body?.properties || body);
            return HttpResponse.json(
              { id: procId, properties: body?.properties || {} },
              { status: 201 }
            );
          }
        ),
        http.put(
          "*/api/v1/studies/:studyId/versions/:versionId/procedures/:procedureId",
          async ({ params, request }) => {
            const body = await readJsonBody(request);
            upsertProcedure(
              String(params.procedureId),
              body?.properties || body
            );
            return HttpResponse.json({
              id: params.procedureId,
              properties: body?.properties || {},
            });
          }
        ),
        http.post(
          "*/api/v1/studies/:studyId/versions/:versionId/links/:linkType",
          async ({ params, request }) => {
            const body = await readJsonBody(request);
            const payload = body?.properties || body;
            const procId = payload?.procedure_id;
            const visitId = payload?.visit_id;
            if (procId && visitId) {
              const row = soaState.rows.find((r) => r.activity_id === procId);
              if (row) {
                let cell = row.cells.find((c) => c.encounter_id === visitId);
                if (!cell) {
                  cell = { encounter_id: visitId, is_applicable: false };
                  row.cells.push(cell);
                }
                cell.is_applicable = Boolean(payload?.is_applicable ?? true);
                cell.details = cell.is_applicable
                  ? payload?.timing || undefined
                  : undefined;
              }
            }
            return HttpResponse.json({
              status: "LINKED",
              link_type: params.linkType,
              ...body,
            });
          }
        ),
      ];
    })(),

    // eConsent Authoring & Controlled Fileshare
    http.get("*/api/v1/econsent/templates", () =>
      HttpResponse.json([
        {
          template_id: "tmpl-demo-01",
          study_id: "DEMO-01",
          template_name: "DEMO-01 Master Informed Consent",
          protocol_version: "2.0",
          status: "PUBLISHED",
          version_index: 1,
          requires_reconsent: false,
          notification_channels: ["Email"],
          clauses: ["Voluntary Participation & Clinical Study Overview"],
          workflow_steps: [],
        },
      ])
    ),
    http.get("*/api/v1/econsent/clauses", () => HttpResponse.json([])),
    http.get("*/api/v1/econsent/translations", () => HttpResponse.json([])),
    http.get("*/api/v1/fileshare/files", () => {
      const xrefEntries = [9, 52, 101, 202, 318].map(
        (offset) => `${String(offset).padStart(10, "0")} ${"0".repeat(5)} n `
      );
      const minimalPdfText = [
        "%PDF-1.4",
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj",
        "2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj",
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj",
        "4 0 obj<</Length 68>>stream",
        "BT /F1 18 Tf 72 720 Td (Cadence Clinical Protocol Synopsis DEMO-01) Tj ET",
        "endstream",
        "endobj",
        "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj",
        "xref",
        "0 6",
        `${"0".repeat(10)} 65535 f `,
        ...xrefEntries,
        "trailer<</Size 6/Root 1 0 R>>",
        "startxref",
        "386",
        "%%EOF",
      ].join("\n");
      const pdfBase64 = btoa(minimalPdfText);

      return HttpResponse.json([
        {
          id: "file-demo-01",
          filename: "DEMO-01-Protocol-Synopsis.pdf",
          content_type: "application/pdf",
          mime_type: "application/pdf",
          download_url: `data:application/pdf;base64,${pdfBase64}`,
          size_bytes: 245760,
          study_id: "DEMO-01",
          site_id: "SITE-101",
          uploaded_by: "designer_demo",
          created_at: "2026-07-01T10:00:00Z", // deid-ignore
          is_on_hold: false,
          approval_status: "APPROVED",
          version_number: 1,
        },
        {
          id: "file-demo-02",
          filename: "DEMO-01-Site-Initiation-Briefing.mp4",
          content_type: "video/mp4",
          mime_type: "video/mp4",
          transcode_status: "READY",
          size_bytes: 12582912,
          study_id: "DEMO-01",
          site_id: "SITE-101",
          uploaded_by: "cra_demo",
          created_at: "2026-07-02T14:30:00Z", // deid-ignore
          is_on_hold: false,
          approval_status: "APPROVED",
          version_number: 1,
        },
      ]);
    }),
    http.get(
      "*/api/v1/fileshare/files/:fileId/stream-url",
      ({ params, request }) => {
        const reqUrl = new URL(request.url);
        const apiIdx = reqUrl.pathname.indexOf("/api/v1/fileshare/");
        const prefix = apiIdx > 0 ? reqUrl.pathname.slice(0, apiIdx) : "";
        return HttpResponse.json({
          file_id: String(params.fileId),
          stream_url: `${prefix}/api/v1/fileshare/demo-stream/master.m3u8`,
          expires_in: 300,
          transcode_status: "READY",
          message: "Demo HLS stream ready",
        });
      }
    ),
    http.get(
      "*/api/v1/fileshare/demo-stream/master.m3u8",
      () =>
        new HttpResponse(
          [
            "#EXTM3U",
            "#EXT-X-VERSION:3",
            "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360",
            "prog_index.m3u8",
            "",
          ].join("\n"),
          {
            status: 200,
            headers: { "Content-Type": "application/vnd.apple.mpegurl" },
          }
        )
    ),
    http.get(
      "*/api/v1/fileshare/demo-stream/prog_index.m3u8",
      () =>
        new HttpResponse(
          [
            "#EXTM3U",
            "#EXT-X-VERSION:3",
            "#EXT-X-TARGETDURATION:2",
            "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXTINF:1.0,",
            "seg0.ts",
            "#EXT-X-ENDLIST",
            "",
          ].join("\n"),
          {
            status: 200,
            headers: { "Content-Type": "application/vnd.apple.mpegurl" },
          }
        )
    ),
    http.get("*/api/v1/fileshare/demo-stream/seg0.ts", () => {
      // Three 188-byte MPEG-TS packets (sync byte 0x47, null PID 0x1FFF stuffing)
      const tsBytes = new Uint8Array(188 * 3);
      tsBytes.fill(0xff);
      for (let pkt = 0; pkt < 3; pkt++) {
        const offset = pkt * 188;
        tsBytes[offset] = 0x47;
        tsBytes[offset + 1] = 0x1f;
        tsBytes[offset + 2] = 0xff;
        tsBytes[offset + 3] = 0x10 | (pkt & 0x0f);
      }
      return new HttpResponse(tsBytes, {
        status: 200,
        headers: { "Content-Type": "video/mp2t" },
      });
    }),
    http.post("*/api/v1/fileshare/audit/playback", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        id: "audit-demo-playback-01",
        file_record_id: body?.file_record_id || "file-demo-02",
        event: body?.event || "PLAY_START",
        session_id: body?.session_id || "sess-demo",
        recorded_at: demoNowIso(nextDemoStep("fileshare-playback-audit")),
      });
    }),
    http.get("*/api/v1/fileshare/files/:fileId/grants", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/fileshare/files/:fileId/guest-links", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/fileshare/files/:fileId/audit-logs", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/fileshare/files/:fileId/activity", () =>
      HttpResponse.json({ items: [], total: 0 })
    ),

    // Central Lab Governance & Toxicity
    http.get("*/api/v1/execution/lab-ranges", () =>
      HttpResponse.json([
        {
          id: "lr-demo-01",
          study_id: "DEMO-01",
          lab_source: "CENTRAL",
          test_code: "ALT",
          test_name: "Alanine Aminotransferase",
          sex: "ALL",
          age_low: 18,
          age_high: 85,
          ref_low: 7,
          ref_high: 40,
          unit: "U/L",
          loinc_code: "1742-6",
          is_active: true,
        },
      ])
    ),
    http.get("*/api/v1/execution/labs/toxicity/review-queue", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/labs/certifications", () =>
      HttpResponse.json([
        {
          id: "cert-demo-01", // deid-ignore
          site_id: "SITE-101",
          lab_name: "SITE-101 Clinical Pathology Laboratory",
          cert_type: "CLIA",
          cert_number: "CLIA-DEMO-101",
          effective_date: "2026-01-01", // deid-ignore
          expiration_date: "2028-01-01", // deid-ignore
          status: "VERIFIED",
        },
      ])
    ),
    http.get("*/api/v1/execution/labs/batch-status", () =>
      HttpResponse.json([
        {
          batch_id: "BATCH-DEMO-01",
          study_id: "DEMO-01",
          status: "COMPLETED",
          total_records: 24,
          ingested_count: 24,
          staged_aes_count: 0,
          created_at: "2026-07-15T09:00:00Z", // deid-ignore
        },
      ])
    ),
    http.get("*/api/v1/execution/labs/loinc-catalog", () =>
      HttpResponse.json({
        items: [
          {
            loinc_code: "1742-6",
            test_code: "ALT",
            component: "Alanine aminotransferase",
            standard_unit: "U/L",
            category: "Chemistry",
          },
          {
            loinc_code: "2160-0",
            test_code: "CREAT",
            component: "Creatinine",
            standard_unit: "umol/L",
            category: "Chemistry",
          },
        ],
      })
    ),
    http.post("*/api/v1/execution/labs/convert-unit", async ({ request }) => {
      const body = await readJsonBody(request);
      const val = Number(body?.value ?? 1) || 1;
      const factor = 88.4;
      return HttpResponse.json({
        converted_value: Number((val * factor).toFixed(2)),
        to_unit: body?.to_unit || "umol/L",
        factor,
        loinc_code: "2160-0",
      });
    }),
    http.post(
      "*/api/v1/execution/labs/toxicity/evaluate",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const val = Number(body?.value ?? 25);
        const uln = Number(body?.ref_high ?? 40) || 40;
        const grade = val > uln * 5 ? 3 : val > uln * 3 ? 2 : val > uln ? 1 : 0;
        return HttpResponse.json({
          ctcae_grade: grade,
          toxicity_description:
            grade === 0
              ? "Within normal reference limits"
              : `CTCAE v5.0 Grade ${grade} elevation`,
          is_critical_ae_trigger: grade >= 3,
          is_hys_law_warning: false,
        });
      }
    ),

    // Sensor Telemetry & Decentralized Clinical Trials (DCT)
    http.get("*/api/v1/interop/sensors/devices", () =>
      HttpResponse.json({ data: { devices: [] } })
    ),
    http.get("*/api/v1/interop/sensors/daily-summaries", () =>
      HttpResponse.json({ data: { summaries: [] } })
    ),
    http.get("*/api/v1/interop/sensors/safety-alerts", () =>
      HttpResponse.json({ data: { alerts: [] } })
    ),
    http.get("*/api/v1/interop/sensors/metrics/:studyId", () =>
      HttpResponse.json({
        data: {
          metrics: {
            active_devices: 2,
            daily_compliance_pct: 96.5,
            open_safety_alerts: 0,
            ingested_epochs_24h: 1440,
          },
        },
      })
    ),
    http.get("*/api/v1/interop/dct/telehealth/sessions", () =>
      HttpResponse.json({ sessions: [] })
    ),
    http.get("*/api/v1/interop/dct/econsent/pediatric-assent", () =>
      HttpResponse.json({ assents: [] })
    ),
    http.get("*/api/v1/interop/dct/dtp/shipments", () =>
      HttpResponse.json({ shipments: [] })
    ),
    http.get("*/api/v1/interop/dct/home-health/visits", () =>
      HttpResponse.json({ visits: [] })
    ),
    http.get("*/api/v1/interop/dct/metrics/:studyId", () =>
      HttpResponse.json({
        total_telehealth_sessions: 1,
        active_video_rooms: 0,
        pediatric_assents_completed: 1,
        total_dtp_shipments: 2,
        active_cold_chain_shipments: 1,
        temperature_excursion_alerts: 0,
      })
    ),

    // Study Training & Qualification
    http.get("*/api/v1/training/curricula", () =>
      HttpResponse.json([
        {
          id: "curr-demo-01",
          study_id: "DEMO-01",
          title: "DEMO-01 Protocol v2.0 Core Qualification",
          version: "2.0",
          status: "ACTIVE",
          modules: [
            {
              id: "mod-01",
              title: "Protocol Synopsis & Eligibility",
              content: "Synthetic protocol qualification module.",
              quiz_questions: [],
            },
          ],
        },
      ])
    ),
    http.get("*/api/v1/training/assignments", () =>
      HttpResponse.json([
        {
          id: "asn-demo-01",
          study_id: "DEMO-01",
          curriculum_id: "curr-demo-01",
          status: "COMPLETED",
          time_spent_seconds: 180,
          quiz_score: 100,
        },
      ])
    ),
    http.get(
      "*/api/v1/training/assignments/:assignmentId/certificate",
      ({ params }) =>
        HttpResponse.json({
          certificate_id: "cert-trn-01", // deid-ignore
          assignment_id: params.assignmentId,
          status: "ISSUED",
          issued_at: "2026-07-02T12:00:00Z", // deid-ignore
          sha256_hash: "sha256-demo-trn-cert",
        })
    ),
    http.post("*/api/v1/training/assignments/:assignmentId/progress", () =>
      HttpResponse.json({ status: "OK" })
    ),
    http.get("*/api/v1/training/assignments/:assignmentId", ({ params }) =>
      HttpResponse.json({
        id: params.assignmentId,
        study_id: "DEMO-01",
        status: "COMPLETED",
        time_spent_seconds: 180,
        quiz_score: 100,
        curriculum: {
          id: "curr-demo-01",
          study_id: "DEMO-01",
          title: "DEMO-01 Protocol v2.0 Core Qualification",
          version: "2.0",
          modules: [
            {
              id: "mod-01",
              title: "Protocol Synopsis & Eligibility",
              content: "Synthetic protocol qualification module.",
              quiz_questions: [],
            },
          ],
        },
      })
    ),
    http.get("*/api/v1/training/curricula/:curriculumId", ({ params }) =>
      HttpResponse.json({
        id: String(params.curriculumId),
        study_id: "DEMO-01",
        title: "DEMO-01 Protocol v2.0 Core Qualification",
        version: "2.0",
        status: "ACTIVE",
        modules: [
          {
            id: "mod-01",
            title: "Protocol Synopsis & Eligibility",
            content: "Synthetic protocol qualification module.",
            quiz_questions: [],
          },
        ],
      })
    ),
    http.post(
      "*/api/v1/training/curricula/batch-assign",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const step = nextDemoStep("training-batch-assign");
        return HttpResponse.json({
          batch_transaction_id: `TRN-BATCH-${step}`,
          curriculum_id: body?.curriculum_id || "curr-demo-01",
          study_id: body?.study_id || "CADENCE-101",
          total_eligible: 1,
          total_assigned: 1,
          total_skipped: 0,
          assignments: [
            {
              id: `asn-batch-${step}`,
              study_id: body?.study_id || "CADENCE-101",
              curriculum_id: body?.curriculum_id || "curr-demo-01",
              status: "ASSIGNED",
            },
          ],
          skipped_items: [],
          executed_at: demoNowIso(step),
        });
      }
    ),
    http.get("*/api/v1/training/delegations", () => HttpResponse.json([])),
    http.post(
      "*/api/v1/training/delegations/bulk-signoff",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const step = nextDemoStep("training-delegations-signoff");
        const ids = Array.isArray(body?.delegation_ids)
          ? body.delegation_ids
          : [];
        return HttpResponse.json({
          batch_transaction_id: `DLG-BATCH-${step}`,
          total_requested: ids.length,
          total_signed: ids.length,
          total_failed: 0,
          signed_delegations: ids.map((id) => ({
            id,
            status: "SIGNED",
          })),
          failed_items: [],
          sha256_seal: `sha256-demo-dlg-${step}`,
          etmf_artifact_id: `DOC-DLG-${step}`,
          executed_at: demoNowIso(step),
        });
      }
    ),
    http.get("*/api/v1/training/gating-status", () =>
      HttpResponse.json({ is_gated: false })
    ),
  ];
}
