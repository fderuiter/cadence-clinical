import { DEMO_SITES, getDemoPersonaForWorkspace } from "demo-fixtures";
import {
  findCommercialTier,
  PRE_RELEASE_ENVIRONMENT_NOTICE,
} from "../../catalog/productCatalog.js";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";
import {
  DEMO_SUBSCRIPTION_UUID,
  DEMO_TENANT_UUID,
  STUDY_REF,
  cloneJson,
  resolveCollectionTitle,
} from "./shared.js";

export function createLocksAndEntitlementsDomain(ctx) {
  const { state } = ctx;

  state.locks = [];
  state.demoLocks = [];
  state.entitlements = null;

  function seed() {
    state.locks = [
      {
        id: "lock-demo-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        subject_id: "SUBJ-1001",
        scope: "VISIT",
        status: "UNLOCKED",
        updated_at: "2026-08-01T12:00:00Z",
      },
    ];

    state.demoLocks = [];

    state.entitlements = {
      tenant_ref: `tenant:${DEMO_TENANT_UUID}`,
      tenant_id: DEMO_TENANT_UUID,
      subscription_ref: `subscription:${DEMO_SUBSCRIPTION_UUID}`,
      tier: "Complete",
      status: "Active",
      started_at: demoNowIso(),
      trial_ends_at: null,
      is_read_only: false,
      product_families: [...findCommercialTier("Complete").families],
      baseline_capabilities: [
        "audit_trail",
        "electronic_signatures",
        "authorization",
        "record_export",
      ],
      environment_notice: PRE_RELEASE_ENVIRONMENT_NOTICE,
    };
  }

  function getLockReadiness(studyRef, rawScopeRef) {
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
      const owner = answered ? "Monitor / Data Management" : "Site Coordinator";
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
  }

  const methods = {
    getEntitlements() {
      return cloneJson(state.entitlements);
    },

    getLockReadiness,

    listKernelLocks(activeOnly = false) {
      return activeOnly
        ? state.demoLocks.filter((item) => !item.released)
        : state.demoLocks;
    },

    commitKernelLock(body = {}, reasonHeader = "") {
      const now = new Date().toISOString();
      const studyRef = body?.study_ref || STUDY_REF;
      const scopeRef = body?.scope_ref || studyRef;
      const kind = body?.kind || "Frozen";
      if (String(kind).toLowerCase() === "locked") {
        const readiness = getLockReadiness(studyRef, scopeRef);
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
        study_ref: body?.study_ref || STUDY_REF,
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
          reasonHeader || body?.reason_for_change || "Demo lock",
        released: false,
        release_ref: null,
        released_at: null,
        released_by: null,
        release_reason: null,
        masked: false,
        mask: null,
      };
      state.demoLocks = [...state.demoLocks, record];
      return { conflict: false, data: record };
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
      return (
        state.demoLocks.find((item) => item.ref === lockRef) || {
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
        }
      );
    },

    upgradeSubscriptionTier(rawTier) {
      const normalized = ["Individual", "Select", "Complete"].find(
        (candidate) =>
          candidate.toLowerCase() ===
          String(rawTier || "")
            .trim()
            .toLowerCase()
      );
      if (!normalized) {
        return { error: "invalid_tier" };
      }
      if (
        state.entitlements?.is_read_only ||
        state.entitlements?.status === "Expired"
      ) {
        return { error: "expired" };
      }
      const preserveTrial = state.entitlements?.status === "Trialing";
      state.entitlements = {
        ...state.entitlements,
        tier: normalized,
        status: preserveTrial ? "Trialing" : "Active",
        is_read_only: false,
        product_families: [...findCommercialTier(normalized).families],
      };
      ctx.recordActivity(
        preserveTrial
          ? "subscription.trial_tier_switched"
          : "subscription.tier_upgraded",
        "Execution",
        "subscription",
        state.entitlements.subscription_ref,
        getDemoPersonaForWorkspace("super_admin")?.username || "designer.lead"
      );
      return { data: cloneJson(state.entitlements) };
    },

    activateSubscription(payload = {}) {
      const normalizedTier = ["Individual", "Select", "Complete"].find(
        (candidate) =>
          candidate.toLowerCase() ===
          String(payload?.tier || "")
            .trim()
            .toLowerCase()
      );
      if (!normalizedTier) {
        return { error: "invalid_tier" };
      }
      const errors = {};
      const validPhases = new Set([
        "Phase I",
        "Phase I/II",
        "Phase II",
        "Phase II/III",
        "Phase III",
        "Phase IV",
        "Observational",
      ]);
      const studyPhase = String(payload?.study_phase || "").trim();
      if (!studyPhase) {
        errors.study_phase = ["Study phase is required."];
      } else if (!validPhases.has(studyPhase)) {
        errors.study_phase = [
          "Study phase must be one of: Phase I, Phase I/II, Phase II, Phase II/III, Phase III, Phase IV, Observational.",
        ];
      }
      const duration = Number(payload?.study_duration_months);
      if (!Number.isInteger(duration) || duration < 1 || duration > 240) {
        errors.study_duration_months = [
          "Study duration in months is required and must be between 1 and 240.",
        ];
      }
      const countries = Array.isArray(payload?.countries)
        ? payload.countries.map((c) => String(c || "").trim()).filter(Boolean)
        : [];
      if (countries.length === 0) {
        errors.countries = ["At least one participating country is required."];
      }
      const siteCount = Number(payload?.site_count);
      if (!Number.isInteger(siteCount) || siteCount < 1 || siteCount > 10000) {
        errors.site_count = [
          "Planned site count is required and must be between 1 and 10,000.",
        ];
      }
      const participantCount = Number(payload?.participant_count);
      if (
        !Number.isInteger(participantCount) ||
        participantCount < 1 ||
        participantCount > 1e6
      ) {
        errors.participant_count = [
          "Planned participant count is required and must be between 1 and 1,000,000.",
        ];
      }
      const activeStudyCount = Number(payload?.active_study_count);
      if (
        !Number.isInteger(activeStudyCount) ||
        activeStudyCount < 1 ||
        activeStudyCount > 1000
      ) {
        errors.active_study_count = [
          "Active study count is required and must be between 1 and 1,000.",
        ];
      }
      const billingConfirmation = String(
        payload?.billing_confirmation || ""
      ).trim();
      if (billingConfirmation.length < 3 || billingConfirmation.length > 512) {
        errors.billing_confirmation = [
          "A signed commercial quote token is required to confirm subscription activation.",
        ];
      }
      if (Object.keys(errors).length > 0) {
        return { error: "scoping_invalid", errors };
      }
      if (!billingConfirmation.startsWith("qtk1.")) {
        return { error: "quote_invalid" };
      }

      state.entitlements = {
        ...state.entitlements,
        tier: normalizedTier,
        status: "Active",
        is_read_only: false,
        product_families: [...findCommercialTier(normalizedTier).families],
        scoping: {
          study_phase: studyPhase,
          study_duration_months: duration,
          countries,
          site_count: siteCount,
          participant_count: participantCount,
          active_study_count: activeStudyCount,
          billing_confirmation: billingConfirmation,
        },
      };
      ctx.recordActivity(
        "subscription.activated",
        "Execution",
        "subscription",
        state.entitlements.subscription_ref,
        getDemoPersonaForWorkspace("super_admin")?.username || "designer.lead"
      );
      return { data: cloneJson(state.entitlements) };
    },

    bootstrapTrial(payload = {}) {
      const requestedTier =
        ["Individual", "Select", "Complete"].find(
          (candidate) =>
            candidate.toLowerCase() ===
            String(payload?.tier || "Individual")
              .trim()
              .toLowerCase()
        ) || "Individual";
      const now = new Date(demoNowMs());
      const trialEnd = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000);
      state.entitlements = {
        ...state.entitlements,
        tier: requestedTier,
        status: "Trialing",
        is_read_only: false,
        product_families: [...findCommercialTier(requestedTier).families],
        started_at: now.toISOString(),
        trial_ends_at: trialEnd.toISOString(),
        scoping: null,
      };
      const defaultSite = DEMO_SITES[0];
      return {
        newly_provisioned: true,
        tenant_code: "demo-trial",
        tenant_ref: state.entitlements.tenant_ref,
        organization_ref: `organization:${DEMO_TENANT_UUID}`,
        sandbox_institution_ref: defaultSite
          ? `organization:${defaultSite.institutionUuid}`
          : `organization:${DEMO_TENANT_UUID}`,
        sandbox_institution_code:
          defaultSite?.institutionCode || "INST-SANDBOX-01",
        sandbox_site_ref: defaultSite
          ? `site:${defaultSite.siteUuid}`
          : `site:${DEMO_TENANT_UUID}`,
        sandbox_site_code: defaultSite?.code || "SITE-101",
        sandbox_site_name: defaultSite?.name || "Sandbox Clinical Site",
        sandbox_site_country: defaultSite?.country || "US",
        person_ref: `person:${DEMO_TENANT_UUID}`,
        membership_ref: `tenant_membership:${DEMO_TENANT_UUID}`,
        admin_role_ref: `role:${DEMO_TENANT_UUID}`,
        admin_grant_ref: `grant:${DEMO_TENANT_UUID}`,
        site_investigator_role_ref: `role:${DEMO_TENANT_UUID}`,
        site_investigator_grant_ref: `grant:${DEMO_TENANT_UUID}`,
        assigned_roles: [
          "Sponsor Admin",
          "Sponsor Designer",
          "Site Investigator",
        ],
        assigned_site_ids: [defaultSite?.code || "SITE-101"],
        token_refresh_required: false,
        entitlements: cloneJson(state.entitlements),
      };
    },
  };

  return { seed, methods };
}
