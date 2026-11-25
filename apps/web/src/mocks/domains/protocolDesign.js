import { DEMO_STUDY, getDemoPersonaForWorkspace } from "demo-fixtures";
import { buildUsdmSoaSchedule } from "../../stores/demo/usdmSoaSchedule";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";
import {
  DEMO_PROTOCOL_TEMPLATES,
  buildImportedDraftDesign,
} from "../demoStudyImport.js";
import {
  ENROLLMENT_1001_UUID,
  PROPOSAL_1_REF,
  SITE_101_UUID,
  STUDY_REF,
  VERSION_V1_REF,
  VERSION_V2_REF,
  cloneJson,
  makeUuidFromIndex,
  resolveEnrollmentByIdOrCode,
} from "./shared.js";

function createInitialSoaProjection() {
  return buildUsdmSoaSchedule(["ACT-DEM", "ACT-VS"]);
}

export function createProtocolDesignDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.importedProtocolVersions = new Map();
  state.protocolDesignSummaries = new Map();
  state.idempotentImportResponses = new Map();
  state.proposals = new Map();
  state.soaProjection = createInitialSoaProjection();

  function seed() {
    state.importedProtocolVersions = new Map();
    state.protocolDesignSummaries = new Map();
    state.idempotentImportResponses = new Map();
    state.soaProjection = createInitialSoaProjection();

    const canonicalStudyRef =
      state.studies.get(DEMO_STUDY.code || "CADENCE-101")?.ref ||
      DEMO_STUDY.ref ||
      STUDY_REF;

    state.proposals = new Map([
      [
        PROPOSAL_1_REF,
        {
          ref: PROPOSAL_1_REF,
          study: canonicalStudyRef,
          intent: "Extend Week 4 visit window from +/-2 days to +/-3 days",
          plan: {
            ref: "plan:00000000-0000-0000-0000-000000004101", // deid-ignore
            title: "Protocol Amendment 2 Visit Window Adjustment",
            number: 2,
            media_type: "application/vnd.cadence.usdm+json",
            content_hash: "sha256-demo-plan-4101",
            mask: null,
          },
          affected: [{ ref: VERSION_V2_REF, mask: null }],
          simulated_impact:
            "2 scheduled visit windows updated; 0 re-consents required.",
          requested_capabilities: ["protocol.amend"],
          proposed_at: demoNowIso(),
          expires_at: demoNowIso(30, 86_400_000),
          proposed_by: "agent:protocol-visit-window",
          proposed_by_kind: "AutomationAgent",
          requested_by:
            getDemoPersonaForWorkspace("sponsor_designer")?.username ||
            "designer_demo",
          model_id: "cadence-protocol-guardian-1.2",
          confidence_score: 0.89,
          content_hash: "sha256-demo-prop-4001",
          status: "Open",
          decided_at: null,
          decided_by: null,
          approval_attestation: null,
          decision_reason: null,
          is_escalated: false,
          escalated_to: null,
          escalation_reason: null,
          mask: null,
        },
      ],
    ]);
  }

  function listProtocolVersions(studyRef) {
    const study = ctx.getStudyByRefOrCode(studyRef);
    const imported = state.importedProtocolVersions.get(study.code) || [];
    if (study.isCustomCreated) {
      return cloneJson(imported);
    }
    return cloneJson([
      ...imported,
      {
        ref: VERSION_V2_REF,
        study: study.ref,
        number: 2,
        parent_version: VERSION_V1_REF,
        status: "PUBLISHED",
        content_hash: "sha256-demo-v2",
        amendment_intent: "Added Week 4 cardiometabolic biomarker panel",
        approval_attestation:
          "attestation:00000000-0000-0000-0000-000000000802", // deid-ignore
        withdrawal_reason: null,
        mask: null,
      },
      {
        ref: VERSION_V1_REF,
        study: study.ref,
        number: 1,
        parent_version: null,
        status: "SUPERSEDED",
        content_hash: "sha256-demo-v1",
        amendment_intent: null,
        approval_attestation:
          "attestation:00000000-0000-0000-0000-000000000801", // deid-ignore
        withdrawal_reason: null,
        mask: null,
      },
    ]);
  }

  function getProtocolVersion(versionRef) {
    for (const versions of state.importedProtocolVersions.values()) {
      const found = versions.find(
        (v) =>
          v.ref === versionRef ||
          v.ref.split("@")[0] === String(versionRef || "").split("@")[0]
      );
      if (found) return cloneJson(found);
    }
    const versions = listProtocolVersions(STUDY_REF);
    return cloneJson(versions.find((v) => v.ref === versionRef) || versions[0]);
  }

  const methods = {
    listProtocolVersions,
    getProtocolVersion,

    listProtocolTemplates() {
      return DEMO_PROTOCOL_TEMPLATES.map((tpl) => ({
        id: tpl.id,
        category: tpl.category,
        form_oid: tpl.form_oid,
        form_name: tpl.form_name,
        file_name: tpl.file_name,
        item_count: tpl.item_count,
        repeating: Boolean(tpl.repeating),
      }));
    },

    getProtocolDesignSummary(versionRef) {
      const unpinned = String(versionRef || "").split("@")[0];
      for (const [key, summary] of state.protocolDesignSummaries.entries()) {
        if (key === versionRef || key.split("@")[0] === unpinned) {
          return cloneJson(summary);
        }
      }
      const version = getProtocolVersion(versionRef);
      const vitalsTpl = DEMO_PROTOCOL_TEMPLATES[0];
      const defaultForm = {
        id: vitalsTpl.form_oid,
        display_order: 1,
        name: vitalsTpl.form_name,
        item_count: vitalsTpl.items.length,
        required_item_count: vitalsTpl.items.filter((i) => i.required).length,
        items: cloneJson(vitalsTpl.items),
      };
      return {
        version,
        title: `Protocol Version ${version.number} Design`,
        form_count: 1,
        item_count: defaultForm.item_count,
        forms: [defaultForm],
        import_notes: [],
      };
    },

    importProtocolDraft(
      studyRef,
      parsedRequest,
      { reason = "", idempotencyKey = "" } = {}
    ) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Missing X-Change-Reason",
              status: 400,
              code: "Kernel.Audit.MissingReasonForChange",
              detail:
                "Header 'X-Change-Reason' is required when importing a protocol draft.",
            },
          },
        };
      }
      if (
        idempotencyKey &&
        state.idempotentImportResponses.has(idempotencyKey)
      ) {
        return {
          data: cloneJson(state.idempotentImportResponses.get(idempotencyKey)),
        };
      }

      let foundStudy = null;
      const rawRef = String(studyRef || "").trim();
      for (const s of state.studies.values()) {
        if (
          s.ref === rawRef ||
          s.ref.split("@")[0] === rawRef.split("@")[0] ||
          s.code.toUpperCase() === rawRef.toUpperCase()
        ) {
          foundStudy = s;
          break;
        }
      }
      if (!foundStudy) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Study not found",
              status: 404,
              code: "Kernel.Study.NotFound",
              detail: `Study '${rawRef}' was not found in the active tenant.`,
            },
          },
        };
      }

      const built = buildImportedDraftDesign(parsedRequest);
      if (built.error) {
        return built;
      }

      const existingVersions = listProtocolVersions(foundStudy.ref);
      const existingDraft = existingVersions.find(
        (v) => String(v.status).toLowerCase() === "draft"
      );

      let mergedForms = built.forms;
      let mergedNotes = built.importNotes;
      let targetVersion = existingDraft;

      if (existingDraft && !parsedRequest.replaceForms) {
        const prevSummary = state.protocolDesignSummaries.get(
          existingDraft.ref
        );
        if (prevSummary) {
          const byId = new Map(prevSummary.forms.map((f) => [f.id, f]));
          for (const nextForm of built.forms) {
            byId.set(nextForm.id, nextForm);
          }
          mergedForms = [...byId.values()].map((f, idx) => ({
            ...f,
            display_order: idx + 1,
          }));
          mergedNotes = [...prevSummary.import_notes, ...built.importNotes];
        }
      }

      if (!targetVersion) {
        const maxNum = existingVersions.reduce(
          (acc, v) => Math.max(acc, Number(v.number || 0)),
          0
        );
        const parent = existingVersions.find((v) => {
          const upper = String(v.status).toUpperCase();
          return upper === "APPROVED" || upper === "PUBLISHED";
        });
        const vIdx = nextCounter();
        targetVersion = {
          ref: `protocol_version:${makeUuidFromIndex("00000150", vIdx)}@1`,
          study: foundStudy.ref,
          number: maxNum + 1,
          parent_version: parent ? parent.ref : null,
          status: "Draft",
          content_hash: null,
          amendment_intent: parent ? "Protocol design import" : null,
          approval_attestation: null,
          withdrawal_reason: null,
          mask: null,
        };
        const list = state.importedProtocolVersions.get(foundStudy.code) || [];
        list.unshift(targetVersion);
        state.importedProtocolVersions.set(foundStudy.code, list);
      }

      const prevSummaryForAmendment = state.protocolDesignSummaries.get(
        targetVersion.ref
      );
      const amendment = targetVersion.parent_version
        ? {
            intent: targetVersion.amendment_intent || "Protocol design import",
            rationale: reason,
            classification:
              parsedRequest.amendmentClassification ||
              prevSummaryForAmendment?.amendment?.classification ||
              "NonSubstantial",
            summary_of_changes: `Imported ${mergedForms.length} protocol form specification(s) (${mergedForms.length} impacted element(s)).`,
            impacted_elements: mergedForms.map((f) => f.id),
            transition_policy:
              parsedRequest.transitionPolicy ||
              prevSummaryForAmendment?.amendment?.transition_policy ||
              "NewParticipantsOnly",
          }
        : null;

      const summary = {
        version: cloneJson(targetVersion),
        title: built.title,
        form_count: mergedForms.length,
        item_count: mergedForms.reduce((acc, f) => acc + f.item_count, 0),
        forms: cloneJson(mergedForms),
        import_notes: cloneJson(mergedNotes),
        amendment: cloneJson(amendment),
      };
      state.protocolDesignSummaries.set(targetVersion.ref, summary);
      ctx.recordActivity(
        "kernel.protocol.import",
        "protocol",
        "protocol_version",
        targetVersion.ref,
        "usr-demo-operator"
      );

      if (idempotencyKey) {
        state.idempotentImportResponses.set(idempotencyKey, cloneJson(summary));
      }
      return { data: cloneJson(summary) };
    },

    getScheduleOfActivities(versionRef) {
      return {
        version: versionRef || VERSION_V2_REF,
        content_hash: "sha256-demo-soa-v2",
        hash: "sha256-demo-soa-matrix",
        title: "DEMO-01 Schedule of Activities (Protocol v2.0)",
        epochs: [
          {
            epoch_id: "epoch-screening",
            name: "Screening",
            order: 1,
            first_column: 0,
            column_count: 1,
          },
          {
            epoch_id: "epoch-treatment",
            name: "Treatment",
            order: 2,
            first_column: 1,
            column_count: 2,
          },
        ],
        columns: [
          {
            activity_id: "visit-screening",
            name: "Screening",
            epoch_id: "epoch-screening",
            study_day: -14,
            window_before: 7,
            window_after: 0,
            repeating: false,
            locations: ["CLINIC"],
            footnotes: [1],
          },
          {
            activity_id: "visit-baseline",
            name: "Baseline (Day 1)",
            epoch_id: "epoch-treatment",
            study_day: 1,
            window_before: 0,
            window_after: 1,
            repeating: false,
            locations: ["CLINIC"],
            footnotes: [],
          },
          {
            activity_id: "visit-week4",
            name: "Week 4",
            epoch_id: "epoch-treatment",
            study_day: 28,
            window_before: 2,
            window_after: 2,
            repeating: false,
            locations: ["CLINIC"],
            footnotes: [],
          },
        ],
        rows: [
          {
            collection_spec_id: "form-vital-signs",
            name: "Vital Signs (CDASH VS)",
            item_ids: ["item-sysbp", "item-diabp", "item-pulse", "item-notes"],
            concepts: ["SYSBP", "DIABP", "PULSE", "VSCOMM"],
            columns: [0, 1, 2],
            frequency: 3,
            repeating: false,
            footnotes: [1],
          },
          {
            collection_spec_id: "form-chem-panel",
            name: "Clinical Chemistry Panel (CDASH LB)",
            item_ids: ["item-hba1c", "item-egfr"],
            concepts: ["HBA1C", "EGFR"],
            columns: [0, 2],
            frequency: 2,
            repeating: false,
            footnotes: [],
          },
          {
            collection_spec_id: "form-adverse-event",
            name: "Adverse Event (CDASH AE)",
            item_ids: ["item-aeterm", "item-aesev"],
            concepts: ["AETERM", "AESEV"],
            columns: [],
            frequency: 0,
            repeating: true,
            footnotes: [],
          },
        ],
        footnotes: [
          {
            number: 1,
            kind: "procedure",
            text: "Measure seated blood pressure after 5 minutes of quiet rest.",
          },
        ],
      };
    },

    getEnrollmentSchedule(enrollmentRef, { asOf = null } = {}) {
      const normalized = String(enrollmentRef || "")
        .trim()
        .split("@")[0]
        .replace(/^enrollment:/i, "");
      const subject =
        resolveEnrollmentByIdOrCode(state.subjects, enrollmentRef) ||
        state.subjects.find(
          (s) => String(s.id || "").toLowerCase() === normalized.toLowerCase()
        ) ||
        state.subjects[0];
      const studyCode = subject?.study_id || "DEMO-01";
      const siteCode = subject?.site_id || "SITE-101";
      const studyEntry = ctx.getStudyByRefOrCode(studyCode);
      const studyRef = studyEntry?.ref || `${STUDY_REF}@1`;
      const siteResult = this.listStudySites(studyCode);
      const siteEntries = Array.isArray(siteResult)
        ? siteResult
        : Array.isArray(siteResult?.data)
          ? siteResult.data
          : [];
      const siteEntry =
        siteEntries.find(
          (s) => String(s.site_code).toUpperCase() === siteCode.toUpperCase()
        ) || siteEntries[0];
      const effectiveVersions = Array.isArray(siteEntry?.effective_versions)
        ? siteEntry.effective_versions
        : [];
      const dayMs = 86_400_000;
      const baseMs = demoNowMs();
      const dateAtOffset = (offsetDays) =>
        new Date(baseMs + offsetDays * dayMs).toISOString().slice(0, 10);
      const statusSinceIso = demoNowIso();
      const referenceDate = dateAtOffset(0);
      const effectiveAsOf = asOf || referenceDate;

      if (effectiveVersions.length === 0) {
        return {
          enrollment_ref: `enrollment:${normalized || ENROLLMENT_1001_UUID}@1`,
          study_ref: studyRef,
          study_site_ref: siteEntry?.ref || `study_site:${SITE_101_UUID}@1`,
          site_code: siteCode,
          country: siteEntry?.country || "US",
          enrollment_status: subject?.status || "SCREENED",
          status_since: statusSinceIso,
          reference_date: referenceDate,
          as_of: effectiveAsOf,
          governance_status: "NoEffectiveVersion",
          latest_study_version_status: "Draft",
          guidance:
            "No protocol version is effective at this study site yet. Submit and approve the protocol draft, then declare and activate site effectivity before capturing scheduled forms.",
          governing_version: null,
          effective_versions: [],
          schedule: null,
          expected_visits: [],
        };
      }

      const governing = effectiveVersions[0];
      return {
        enrollment_ref: `enrollment:${normalized || ENROLLMENT_1001_UUID}@1`,
        study_ref: studyRef,
        study_site_ref: siteEntry?.ref || `study_site:${SITE_101_UUID}@1`,
        site_code: siteCode,
        country: siteEntry?.country || "US",
        enrollment_status: subject?.status || "ENROLLED",
        status_since: statusSinceIso,
        reference_date: referenceDate,
        as_of: effectiveAsOf,
        governance_status: "Effective",
        latest_study_version_status: "Approved",
        guidance: null,
        governing_version: governing,
        effective_versions: effectiveVersions,
        schedule: methods.getScheduleOfActivities(
          governing.protocol_version_ref
        ),
        expected_visits: [
          {
            activity_id: "visit-screening",
            name: "Screening",
            epoch_id: "epoch-screening",
            study_day: -14,
            target_date: dateAtOffset(-14),
            window_start: dateAtOffset(-21),
            window_end: dateAtOffset(-14),
            window_status: "Closed",
            locations: ["CLINIC"],
            collection_spec_ids: ["form-vital-signs", "form-chem-panel"],
          },
          {
            activity_id: "visit-baseline",
            name: "Baseline (Day 1)",
            epoch_id: "epoch-treatment",
            study_day: 1,
            target_date: referenceDate,
            window_start: referenceDate,
            window_end: dateAtOffset(1),
            window_status: "Open",
            locations: ["CLINIC"],
            collection_spec_ids: ["form-vital-signs"],
          },
          {
            activity_id: "visit-week4",
            name: "Week 4",
            epoch_id: "epoch-treatment",
            study_day: 28,
            target_date: dateAtOffset(27),
            window_start: dateAtOffset(25),
            window_end: dateAtOffset(29),
            window_status: "NotYetOpen",
            locations: ["CLINIC"],
            collection_spec_ids: ["form-vital-signs", "form-chem-panel"],
          },
        ],
      };
    },

    getAmendmentImpact(versionRef) {
      return {
        amended: versionRef || VERSION_V2_REF,
        parent: VERSION_V1_REF,
        impacted_elements: ["visit-week4", "form-chem-panel"],
        dependents: ctx.getDependents(versionRef || VERSION_V2_REF),
        changed_elements: [
          {
            element_id: "visit-week4",
            instance_type: "PlannedVisit",
            change: "Modified",
            properties: ["window_before", "window_after"],
          },
        ],
        schedule: {
          directives: [],
          impacted_elements: ["visit-week4"],
          impacted_concepts: ["HBA1C"],
          undeclared: [],
          changes_schedule: true,
          changes_eligibility: false,
        },
      };
    },

    listProposals(studyOrOptions = "", maybeOptions = {}) {
      const hasStudyArg = typeof studyOrOptions === "string";
      const studyRefOrCode = hasStudyArg ? studyOrOptions : "";
      const { status, limit } = hasStudyArg
        ? maybeOptions || {}
        : studyOrOptions || {};
      if (studyRefOrCode) {
        const resolved = ctx.resolveExistingStudy(studyRefOrCode);
        if (resolved.error) return resolved;
      }
      let items = [...state.proposals.values()];
      if (status) {
        const upper = String(status).trim().toUpperCase();
        items = items.filter((p) => {
          const itemStatus = String(p.status || "")
            .trim()
            .toUpperCase();
          if (upper === "OPEN") {
            return itemStatus === "OPEN" || itemStatus === "PENDING";
          }
          return itemStatus === upper;
        });
      }
      const cap = Math.min(Math.max(1, Number(limit) || 50), 200);
      const sliced = cloneJson(items.slice(0, cap));
      return studyRefOrCode ? { data: sliced } : sliced;
    },

    listStudyValidationFindings(
      studyRefOrCode = "",
      { stage, statuses = [], severity, dataset, packKey, limit } = {}
    ) {
      const resolved = ctx.resolveExistingStudy(studyRefOrCode);
      if (resolved.error) return resolved;
      const studyRefUnpinned = String(resolved.study.ref).split("@")[0];
      const normalizedStatuses = (
        Array.isArray(statuses) ? statuses : [statuses]
      )
        .filter(Boolean)
        .map((s) => String(s).toUpperCase());

      let matched = (state.validationFindings || []).filter((f) => {
        if (
          f.study_ref &&
          String(f.study_ref).split("@")[0] !== studyRefUnpinned &&
          f.study_code?.toUpperCase() !== resolved.study.code.toUpperCase()
        ) {
          return false;
        }
        if (
          stage &&
          String(f.stage || "").toUpperCase() !== stage.toUpperCase()
        )
          return false;
        if (
          normalizedStatuses.length > 0 &&
          !normalizedStatuses.includes(String(f.status || "").toUpperCase())
        ) {
          return false;
        }
        if (
          severity &&
          String(f.severity || "").toUpperCase() !== severity.toUpperCase()
        ) {
          return false;
        }
        if (
          dataset &&
          String(f.dataset || "").toUpperCase() !== dataset.toUpperCase()
        ) {
          return false;
        }
        if (packKey && f.pack_key !== packKey) return false;
        return true;
      });

      const total = matched.length;
      if (limit !== undefined && limit !== null && Number(limit) > 0) {
        matched = matched.slice(0, Math.min(Number(limit), 200));
      }
      return {
        data: {
          total,
          findings: cloneJson(matched),
        },
      };
    },

    getStudyAttention(studyRefOrCode = "", { limit = 20 } = {}) {
      const resolved = ctx.resolveExistingStudy(studyRefOrCode);
      if (resolved.error) return resolved;
      const cap = Math.min(Math.max(1, Number(limit) || 20), 200);
      const proposalsRes = methods.listProposals(studyRefOrCode, {
        status: "Open",
        limit: cap,
      });
      const findingsRes = methods.listStudyValidationFindings(studyRefOrCode, {
        statuses: ["Open", "Investigating"],
        limit: cap,
      });
      return {
        data: {
          study_ref: String(resolved.study.ref).split("@")[0],
          proposals: proposalsRes.data || [],
          findings: findingsRes.data?.findings || [],
        },
      };
    },

    listWorkQueues({ within, queue } = {}) {
      if (within) {
        const check = ctx.validateScopeOrStudyParam(within);
        if (check.error) return check;
      }
      let queues = cloneJson(state.workQueues || []);
      if (queue) {
        queues = queues.filter((q) => q.code === queue);
      }
      return { data: { queues } };
    },

    getProposal(proposalRef) {
      const cleanRef = String(proposalRef || "").split("@")[0];
      return cloneJson(
        state.proposals.get(cleanRef) ||
          state.proposals.get(proposalRef) ||
          [...state.proposals.values()][0]
      );
    },

    rejectProposal(
      proposalRef,
      reason = "Rejected in demo review",
      caller = null
    ) {
      const cleanRef = String(proposalRef || "").split("@")[0];
      const existing =
        state.proposals.get(cleanRef) ||
        state.proposals.get(proposalRef) ||
        [...state.proposals.values()][0];
      const now = demoNowIso(nextCounter());
      const personaUsername = caller?.persona
        ? getDemoPersonaForWorkspace(caller.persona)?.username
        : null;
      const actor =
        personaUsername ||
        caller?.userId ||
        getDemoPersonaForWorkspace("sponsor_designer")?.username ||
        "designer_demo";
      existing.status = "Rejected";
      existing.decided_at = now;
      existing.decided_by = actor;
      existing.decision_reason = reason;
      ctx.recordActivity(
        "proposal.rejected",
        "governance",
        "proposal",
        existing.ref,
        actor
      );
      return {
        ref: existing.ref,
        status: existing.status,
        content_hash: existing.content_hash,
        decided_at: now,
        decided_by: existing.decided_by,
      };
    },

    upsertSoaArm(armId, props) {
      const soaState = state.soaProjection;
      const idx = soaState.arms.findIndex((a) => a.arm_id === armId);
      const entry = { arm_id: armId, arm_name: props?.name || armId };
      if (idx >= 0) soaState.arms[idx] = entry;
      else soaState.arms.push(entry);
      return entry;
    },

    upsertSoaEpoch(epochId, props) {
      const soaState = state.soaProjection;
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
    },

    upsertSoaVisit(visitId, props) {
      const soaState = state.soaProjection;
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
    },

    upsertSoaProcedure(procId, props) {
      const soaState = state.soaProjection;
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
    },

    linkSoaVisitProcedure(payload) {
      const soaState = state.soaProjection;
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
    },
  };

  return { seed, methods };
}
