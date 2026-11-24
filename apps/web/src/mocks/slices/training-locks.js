import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";
import {
  STUDY_REF,
  cloneJson,
  decodeReasonHeader,
  readJsonBody,
  resolveCollectionTitle,
} from "./shared.js";

export function createTrainingLocksSliceStore(ctx) {
  const { state } = ctx;

  function reset() {
    state.locks = [
      {
        id: "lock-demo-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        subject_id: "SUBJ-1001",
        scope: "VISIT",
        status: "UNLOCKED",
        updated_at: "2026-08-01T12:00:00Z", // deid-ignore
      },
    ];
    state.demoLocks = [];
    state.trainingCurricula = [
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
    ];
    state.trainingAssignments = [
      {
        id: "asn-demo-01",
        study_id: "DEMO-01",
        curriculum_id: "curr-demo-01",
        status: "COMPLETED",
        time_spent_seconds: 180,
        quiz_score: 100,
      },
    ];
  }

  const methods = {
    getLockReadiness(studyRef, rawScopeRef) {
      const study = ctx.getStudyByRefOrCode(studyRef);
      const cleanStudyRef = String(study?.ref || studyRef || STUDY_REF).split(
        "@"
      )[0];
      const cleanScopeRef = String(rawScopeRef || cleanStudyRef).split("@")[0];
      const [scopeKind = "study", scopeUuid = ""] = cleanScopeRef.split(":");

      const matchesScope = (item) => {
        if (scopeKind === "study") return true;
        if (scopeKind === "study_site") {
          return String(item.study_site_id || "") === scopeUuid;
        }
        if (scopeKind === "enrollment") {
          return String(item.enrollment_id || "") === scopeUuid;
        }
        if (scopeKind === "collection_instance") {
          return String(item.form_id || "") === scopeUuid;
        }
        if (scopeKind === "observation") {
          return String(item.observation_id || "") === scopeUuid;
        }
        return true;
      };

      const blockers = [];
      let openQueryCount = 0;
      let missingRequiredItemCount = 0;

      for (const q of state.queries.values()) {
        const status = String(q.status || "").toLowerCase();
        if (status === "closed" || status === "cancelled") continue;
        if (!matchesScope(q)) continue;
        openQueryCount += 1;
        const answered = status === "answered";
        const owner = answered
          ? "Monitor / Data Management"
          : "Site Coordinator";
        const targetRef = q.observation_id
          ? `observation:${q.observation_id}`
          : `collection_instance:${q.form_id}`;
        const nextAction = answered
          ? `${owner}: review answered query on ${targetRef} (${q.subject_code || "participant"}) and close or reopen before locking.`
          : `${owner}: answer and close open query on ${targetRef} (${q.subject_code || "participant"}: "${q.text}") before locking.`;
        blockers.push({
          kind: "OpenQuery",
          owner,
          target_ref: targetRef,
          target_kind: q.observation_id ? "observation" : "collection_instance",
          subject_code: q.subject_code || null,
          collection_spec_id: q.collection_spec_id || null,
          collection_title:
            resolveCollectionTitle(q.collection_spec_id) ||
            q.collection_spec_id ||
            null,
          item_id: q.item_id || null,
          item_label: q.item_id || null,
          query_ref: `assertion:${q.query_id}`,
          query_text: q.text || null,
          answered,
          next_action: nextAction,
          next_capability: answered ? "query.close" : "query.answer",
          masked: false,
          mask: null,
        });
      }

      if (scopeKind !== "observation") {
        for (const f of state.forms.values()) {
          if (!matchesScope(f)) continue;
          const subj = state.subjects.find((s) => s.id === f.enrollment_id);
          const missingIds = Array.isArray(f.missing_required)
            ? f.missing_required
            : [];
          for (const missingId of missingIds) {
            missingRequiredItemCount += 1;
            const targetRef = `collection_instance:${f.form_id}`;
            const itemObj = (f.items || []).find(
              (it) => it.item_id === missingId
            );
            const label = itemObj?.concept || missingId;
            blockers.push({
              kind: "MissingRequiredItem",
              owner: "Site Coordinator",
              target_ref: targetRef,
              target_kind: "collection_instance",
              subject_code: subj?.subject_id || null,
              collection_spec_id: f.collection_spec_id || null,
              collection_title:
                f.collection_title ||
                resolveCollectionTitle(f.collection_spec_id) ||
                null,
              item_id: missingId,
              item_label: label,
              query_ref: null,
              query_text: null,
              answered: false,
              next_action: `Site Coordinator: record required item '${label}' on ${targetRef} (${subj?.subject_id || "participant"}) before locking.`,
              next_capability: "observation.record",
              masked: false,
              mask: null,
            });
          }
        }
      }

      const readyForLock = blockers.length === 0;
      const claim = readyForLock
        ? `Scope ${cleanScopeRef} has 0 open queries and 0 missing required items and is ready for Locked.`
        : `Scope ${cleanScopeRef} cannot be committed as Locked because ${openQueryCount} open ${openQueryCount === 1 ? "query" : "queries"} and ${missingRequiredItemCount} missing required ${missingRequiredItemCount === 1 ? "item" : "items"} remain unresolved (Frozen remains available).`;

      return {
        study_ref: cleanStudyRef,
        study_code: study?.code || "DEMO-01",
        scope_ref: cleanScopeRef,
        scope_kind: scopeKind,
        ready_for_lock: readyForLock,
        can_freeze: true,
        open_query_count: openQueryCount,
        missing_required_item_count: missingRequiredItemCount,
        unverified_value_count: 0,
        unsigned_form_count: 0,
        unopened_visit_count: 0,
        open_error_finding_count: 0,
        blocker_count: blockers.length,
        blockers,
        explanation: {
          subject: cleanScopeRef,
          claim,
          producer: "kernel/locks/readiness",
          steps: blockers.map((b) => ({
            claim: b.next_action,
            evidence: [b.query_ref, b.target_ref].filter(Boolean),
          })),
          conditions: [
            {
              code: "NO_OPEN_QUERIES",
              description: "Zero open queries remain in target scope.",
              met: openQueryCount === 0,
              evidence: blockers
                .filter((b) => b.kind === "OpenQuery" && b.target_ref)
                .map((b) => b.target_ref),
            },
            {
              code: "REQUIRED_ITEMS_COMPLETE",
              description:
                "All required collection items are recorded in target scope.",
              met: missingRequiredItemCount === 0,
              evidence: blockers
                .filter((b) => b.kind === "MissingRequiredItem" && b.target_ref)
                .map((b) => b.target_ref),
            },
          ],
          next_actions: blockers.map((b) => ({
            action: b.next_action,
            capability: b.next_capability,
            target: b.target_ref,
          })),
        },
      };
    },

    listKernelLocks(studyRef, { activeOnly = false } = {}) {
      const now = new Date().toISOString();
      const locks = activeOnly
        ? state.demoLocks.filter((item) => !item.released)
        : state.demoLocks;
      return {
        study_ref: studyRef,
        study_code: "CAD-ONC-2026",
        valid_at: now,
        known_at: now,
        locks: cloneJson(locks),
      };
    },

    commitKernelLock(body = {}, reason = "Demo lock") {
      const now = new Date().toISOString();
      const studyRef = body?.study_ref || STUDY_REF;
      const scopeRef = body?.scope_ref || studyRef;
      const kind = body?.kind || "Frozen";
      if (String(kind).toLowerCase() === "locked") {
        const readiness = methods.getLockReadiness(studyRef, scopeRef);
        if (!readiness.ready_for_lock) {
          return {
            conflict: true,
            problem: {
              title: "K.Assertion.ScopeNotReady",
              status: 409,
              code: "K.Assertion.ScopeNotReady",
              detail: readiness.explanation.claim,
              explanation: readiness.explanation,
            },
          };
        }
      }
      const scopeKind =
        body?.scope_kind || String(scopeRef).split(":")[0] || "study";
      const suffix = String(Date.now()).slice(-12).padStart(12, "0");
      const record = {
        ref: body?.lock_id
          ? `assertion:${body.lock_id}`
          : `assertion:01960000-0000-7000-8000-${suffix}`, // deid-ignore
        study_ref: studyRef,
        scope_ref: scopeRef,
        scope_kind: scopeKind,
        subject_code: null,
        kind,
        attestation_ref: body?.attestation_id
          ? `attestation:${body.attestation_id}`
          : "attestation:01960000-0000-7000-8000-000000000099", // deid-ignore
        attestation_meaning: body?.meaning || "Approved",
        locked_at: now,
        locked_by: "usr-demo-operator",
        reason_for_change: reason || body?.reason_for_change || "Demo lock",
        released: false,
        release_ref: null,
        released_at: null,
        released_by: null,
        release_reason: null,
        masked: false,
        mask: null,
      };
      state.demoLocks = [...state.demoLocks, record];
      return { conflict: false, data: cloneJson(record) };
    },

    releaseKernelLock(lockRef, body = {}, reason = "Released in demo") {
      const now = new Date().toISOString();
      const suffix = String(Date.now()).slice(-12).padStart(12, "0");
      state.demoLocks = state.demoLocks.map((item) =>
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
      const updated = state.demoLocks.find((item) => item.ref === lockRef) || {
        ref: lockRef,
        study_ref: body?.study_ref || STUDY_REF,
        scope_ref: body?.study_ref || STUDY_REF,
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
      return cloneJson(updated);
    },
  };

  return {
    reset,
    methods,
  };
}

export function createTrainingLocksSliceHandlers(
  store,
  { http, HttpResponse }
) {
  return [
    http.get(
      "*/api/v1/kernel/studies/:studyRef/locks",
      ({ params, request }) => {
        const url = new URL(request.url);
        const activeOnly = url.searchParams.get("active_only") === "true";
        const studyRef = decodeURIComponent(String(params.studyRef));
        return HttpResponse.json(
          store.listKernelLocks(studyRef, { activeOnly })
        );
      }
    ),
    http.get(
      "*/api/v1/kernel/studies/:studyRef/locks/readiness",
      ({ params, request }) => {
        const url = new URL(request.url);
        const studyRef = decodeURIComponent(String(params.studyRef));
        const scopeRef = url.searchParams.get("scope_ref") || undefined;
        return HttpResponse.json(store.getLockReadiness(studyRef, scopeRef));
      }
    ),
    http.post("*/api/v1/kernel/locks", async ({ request }) => {
      const body = await readJsonBody(request);
      const outcome = store.commitKernelLock(body, decodeReasonHeader(request));
      if (outcome.conflict) {
        return HttpResponse.json(outcome.problem, { status: 409 });
      }
      return HttpResponse.json(outcome.data, { status: 201 });
    }),
    http.post(
      "*/api/v1/kernel/locks/:lockRef/release",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const lockRef = decodeURIComponent(String(params.lockRef));
        return HttpResponse.json(
          store.releaseKernelLock(lockRef, body, decodeReasonHeader(request))
        );
      }
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

    // Study Training & Qualification
    http.get("*/api/v1/training/curricula", () =>
      HttpResponse.json(store.state.trainingCurricula)
    ),
    http.get("*/api/v1/training/assignments", () =>
      HttpResponse.json(store.state.trainingAssignments)
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
        curriculum: cloneJson(store.state.trainingCurricula[0]),
      })
    ),
    http.get("*/api/v1/training/curricula/:curriculumId", ({ params }) =>
      HttpResponse.json({
        ...cloneJson(store.state.trainingCurricula[0]),
        id: String(params.curriculumId),
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
