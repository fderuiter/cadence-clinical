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

function unpinRef(ref) {
  return String(ref || "")
    .trim()
    .split("@")[0];
}

const DEMO_CONCEPT_NODE_REFS = {
  SYSBP: "concept:00000000-0000-0000-0000-000000007001@1", // deid-ignore
  DIABP: "concept:00000000-0000-0000-0000-000000007002@1", // deid-ignore
  PULSE: "concept:00000000-0000-0000-0000-000000007003@1", // deid-ignore
  VSCOMM: "concept:00000000-0000-0000-0000-000000007004@1", // deid-ignore
  HBA1C: "concept:00000000-0000-0000-0000-000000007005@1", // deid-ignore
  EGFR: "concept:00000000-0000-0000-0000-000000007006@1", // deid-ignore
  AETERM: "concept:00000000-0000-0000-0000-000000007007@1", // deid-ignore
  AESEV: "concept:00000000-0000-0000-0000-000000007008@1", // deid-ignore
};

const DEMO_ELEMENT_BASE_UUIDS = {
  "epoch-screening": "00000000-0000-0000-0000-000000006101", // deid-ignore
  "epoch-treatment": "00000000-0000-0000-0000-000000006102", // deid-ignore
  "visit-screening": "00000000-0000-0000-0000-000000006201", // deid-ignore
  "visit-baseline": "00000000-0000-0000-0000-000000006202", // deid-ignore
  "visit-week4": "00000000-0000-0000-0000-000000006203", // deid-ignore
  "form-vital-signs": "00000000-0000-0000-0000-000000006301", // deid-ignore
  "form-chem-panel": "00000000-0000-0000-0000-000000006302", // deid-ignore
  "form-adverse-event": "00000000-0000-0000-0000-000000006303", // deid-ignore
};

function demoElementRef(elementId, versionNumber = 1) {
  const uuid = DEMO_ELEMENT_BASE_UUIDS[elementId];
  if (!uuid) return null;
  const ver =
    (elementId === "visit-week4" || elementId === "form-chem-panel") &&
    Number(versionNumber) >= 2
      ? 2
      : 1;
  return `protocol_element:${uuid}@${ver}`;
}

function buildSeededStudyVersions(studyRef) {
  const cleanStudyRef = unpinRef(studyRef) || STUDY_REF;
  return [
    {
      ref: VERSION_V2_REF,
      study: cleanStudyRef,
      number: 2,
      parent_version: VERSION_V1_REF,
      status: "Approved",
      content_hash: "sha256-demo-v2",
      amendment_intent: "Added Week 4 cardiometabolic biomarker panel",
      approval_attestation: "attestation:00000000-0000-0000-0000-000000000802", // deid-ignore
      withdrawal_reason: null,
      mask: null,
    },
    {
      ref: VERSION_V1_REF,
      study: cleanStudyRef,
      number: 1,
      parent_version: null,
      status: "Approved",
      content_hash: "sha256-demo-v1",
      amendment_intent: null,
      approval_attestation: "attestation:00000000-0000-0000-0000-000000000801", // deid-ignore
      withdrawal_reason: null,
      mask: null,
    },
  ];
}

export function createProtocolDesignDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.importedProtocolVersions = new Map();
  state.protocolVersionsByStudy = new Map();
  state.protocolDesignSummaries = new Map();
  state.idempotentImportResponses = new Map();
  state.idempotentGovernanceResponses = new Map();
  state.simulationReportsByVersion = new Map();
  state.artifactReviews = new Map();
  state.studyArtifacts = new Map();
  state.studyEffectivities = new Map();
  state.proposals = new Map();
  state.soaProjection = createInitialSoaProjection();

  function seed() {
    state.importedProtocolVersions = new Map();
    state.protocolVersionsByStudy = new Map();
    state.protocolDesignSummaries = new Map();
    state.idempotentImportResponses = new Map();
    state.idempotentGovernanceResponses = new Map();
    state.simulationReportsByVersion = new Map();
    state.artifactReviews = new Map();
    state.studyArtifacts = new Map();
    state.studyEffectivities = new Map();
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

  function ensureStudyVersionRecords(study) {
    const codeKey = String(study?.code || "DEMO-01").toUpperCase();
    if (!state.protocolVersionsByStudy.has(codeKey)) {
      const imported = state.importedProtocolVersions.get(study.code) || [];
      const initial = study.isCustomCreated
        ? [...imported]
        : [...imported, ...buildSeededStudyVersions(study.ref)];
      state.protocolVersionsByStudy.set(codeKey, initial);
    }
    return state.protocolVersionsByStudy.get(codeKey);
  }

  function listProtocolVersions(studyRef) {
    const study = ctx.getStudyByRefOrCode(studyRef);
    return cloneJson(ensureStudyVersionRecords(study));
  }

  function findMutableProtocolVersion(versionRef) {
    const targetUnpinned = unpinRef(versionRef);
    if (!targetUnpinned) return null;
    for (const study of state.studies.values()) {
      const records = ensureStudyVersionRecords(study);
      const found = records.find((v) => unpinRef(v.ref) === targetUnpinned);
      if (found) {
        return { study, records, version: found };
      }
    }
    for (const [codeKey, records] of state.protocolVersionsByStudy.entries()) {
      const found = records.find((v) => unpinRef(v.ref) === targetUnpinned);
      if (found) {
        const study = ctx.getStudyByRefOrCode(codeKey);
        return { study, records, version: found };
      }
    }
    return null;
  }

  function requireMutableProtocolVersionInStatus(
    versionRef,
    allowedStatuses,
    detail
  ) {
    const found = findMutableProtocolVersion(versionRef);
    if (!found) {
      return {
        error: {
          status: 404,
          problem: {
            title: "Protocol version not found",
            status: 404,
            code: "K.Protocol.VersionNotFound",
          },
        },
      };
    }
    const lower = String(found.version.status).toLowerCase();
    if (!allowedStatuses.includes(lower)) {
      return {
        error: {
          status: 409,
          problem: {
            title: "Invalid protocol version transition",
            status: 409,
            code: "K.Protocol.InvalidTransition",
            detail,
          },
        },
      };
    }
    return found;
  }

  function getProtocolVersion(versionRef) {
    const found = findMutableProtocolVersion(versionRef);
    if (found) return cloneJson(found.version);
    const versions = listProtocolVersions(STUDY_REF);
    return cloneJson(
      versions.find((v) => unpinRef(v.ref) === unpinRef(versionRef)) ||
        versions[0]
    );
  }

  function syncSummaryVersionSnapshot(version) {
    const unpinned = unpinRef(version.ref);
    for (const [key, summary] of state.protocolDesignSummaries.entries()) {
      if (unpinRef(key) === unpinned) {
        summary.version = cloneJson(version);
      }
    }
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
      const isGoverned =
        String(version?.status || "").toLowerCase() === "approved";
      const vitalsTpl = DEMO_PROTOCOL_TEMPLATES[0];
      const defaultItems = cloneJson(vitalsTpl.items).map((item, idx) => ({
        ...item,
        protocol_element: isGoverned
          ? `protocol_element:${makeUuidFromIndex("00000650", idx + 1)}@1`
          : null,
        concept_ref: DEMO_CONCEPT_NODE_REFS[item.concept] || null,
      }));
      const defaultForm = {
        id: vitalsTpl.form_oid,
        display_order: 1,
        name: vitalsTpl.form_name,
        item_count: defaultItems.length,
        required_item_count: defaultItems.filter((i) => i.required).length,
        items: defaultItems,
        protocol_element: isGoverned
          ? "protocol_element:00000000-0000-0000-0000-000000006301@1" // deid-ignore
          : null,
        form_version_snapshot:
          "form_version_snapshot:00000000-0000-0000-0000-000000006401@1", // deid-ignore
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

      const records = ensureStudyVersionRecords(foundStudy);
      const existingDraft = records.find(
        (v) => String(v.status).toLowerCase() === "draft"
      );

      let mergedForms = built.forms;
      let mergedNotes = built.importNotes;
      let targetVersion = existingDraft;

      if (existingDraft && !parsedRequest.replaceForms) {
        const prevSummary =
          state.protocolDesignSummaries.get(existingDraft.ref) ||
          state.protocolDesignSummaries.get(unpinRef(existingDraft.ref));
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
        const maxNum = records.reduce(
          (acc, v) => Math.max(acc, Number(v.number || 0)),
          0
        );
        const parent = records.find((v) => {
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
        records.unshift(targetVersion);
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

    forkProtocolVersion(
      parentVersionRef,
      body = {},
      { reason = "", idempotencyKey = "" } = {}
    ) {
      if (
        idempotencyKey &&
        state.idempotentGovernanceResponses.has(`fork:${idempotencyKey}`)
      ) {
        return {
          data: cloneJson(
            state.idempotentGovernanceResponses.get(`fork:${idempotencyKey}`)
          ),
          replayed: true,
        };
      }
      const found = findMutableProtocolVersion(parentVersionRef);
      if (!found) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Protocol version not found",
              status: 404,
              code: "K.Protocol.VersionNotFound",
              detail: `Protocol version '${parentVersionRef}' was not found.`,
            },
          },
        };
      }
      const { study, records, version: parent } = found;
      if (String(parent.status).toLowerCase() !== "approved") {
        return {
          error: {
            status: 409,
            problem: {
              title: "Fork requires an Approved parent version",
              status: 409,
              code: "K.Protocol.ForkRequiresApprovedParent",
              detail:
                "Only an Approved protocol version may be forked into an amendment draft.",
            },
          },
        };
      }
      if (records.some((v) => String(v.status).toLowerCase() === "draft")) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Active Draft already exists",
              status: 409,
              code: "K.Protocol.ActiveDraftExists",
              detail:
                "Submit or withdraw the active Draft protocol version before forking another amendment.",
            },
          },
        };
      }
      const intent = String(body?.intent || "").trim();
      const rationale = String(body?.rationale || "").trim();
      if (!intent || !rationale) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Amendment intent and rationale required",
              status: 400,
              code: "K.Protocol.AmendmentDetailsRequired",
              detail:
                "Forking an amendment requires non-empty intent and rationale.",
            },
          },
        };
      }
      const classification = body?.classification || "Substantial";
      const transitionPolicy = body?.transition_policy || "RequiresReconsent";
      const maxNum = records.reduce(
        (acc, v) => Math.max(acc, Number(v.number || 0)),
        0
      );
      const vIdx = nextCounter();
      const customId = String(body?.protocol_version_id || "").trim();
      const createdRef = customId
        ? `protocol_version:${customId}@1`
        : `protocol_version:${makeUuidFromIndex("00000150", vIdx)}@1`;
      const created = {
        ref: createdRef,
        study: unpinRef(study.ref),
        number: maxNum + 1,
        parent_version: unpinRef(parent.ref),
        status: "Draft",
        content_hash: null,
        amendment_intent: intent,
        approval_attestation: null,
        withdrawal_reason: null,
        mask: null,
      };
      records.unshift(created);

      const parentSummary = methods.getProtocolDesignSummary(parent.ref);
      const impactedElements =
        Array.isArray(body?.impacted_elements) &&
        body.impacted_elements.length > 0
          ? body.impacted_elements.map((item) => String(item))
          : (parentSummary.forms || []).map((f) => f.id);
      const summaryOfChanges =
        String(body?.summary_of_changes || "").trim() ||
        `Forked from Version ${parent.number} (${intent}).`;
      const forkedSummary = {
        version: cloneJson(created),
        title: `Protocol Version ${created.number} Amendment Draft`,
        form_count: parentSummary.form_count,
        item_count: parentSummary.item_count,
        forms: cloneJson(parentSummary.forms || []),
        import_notes: cloneJson(parentSummary.import_notes || []),
        amendment: {
          intent,
          rationale: rationale || reason,
          classification,
          summary_of_changes: summaryOfChanges,
          impacted_elements: impactedElements,
          transition_policy: transitionPolicy,
        },
      };
      state.protocolDesignSummaries.set(created.ref, forkedSummary);
      state.protocolDesignSummaries.set(unpinRef(created.ref), forkedSummary);
      ctx.recordActivity(
        "kernel.protocol.fork",
        "protocol",
        "protocol_version",
        created.ref,
        "usr-demo-operator"
      );
      if (idempotencyKey) {
        state.idempotentGovernanceResponses.set(
          `fork:${idempotencyKey}`,
          cloneJson(created)
        );
      }
      return { data: cloneJson(created) };
    },

    submitProtocolVersionForReview(versionRef) {
      const checked = requireMutableProtocolVersionInStatus(
        versionRef,
        ["draft"],
        "Only a Draft protocol version may be submitted for review."
      );
      if (checked.error) return checked;
      const { version } = checked;
      version.status = "InReview";
      version.content_hash =
        version.content_hash || `sha256-demo-v${version.number}-frozen`;
      syncSummaryVersionSnapshot(version);
      ctx.recordActivity(
        "kernel.protocol.submit_for_review",
        "protocol",
        "protocol_version",
        version.ref,
        "usr-demo-operator"
      );
      return { data: cloneJson(version) };
    },

    returnProtocolVersionToDraft(versionRef) {
      const checked = requireMutableProtocolVersionInStatus(
        versionRef,
        ["inreview"],
        "Only an InReview protocol version may be returned to Draft."
      );
      if (checked.error) return checked;
      const { version } = checked;
      version.status = "Draft";
      version.content_hash = null;
      syncSummaryVersionSnapshot(version);
      ctx.recordActivity(
        "kernel.protocol.return_to_draft",
        "protocol",
        "protocol_version",
        version.ref,
        "usr-demo-operator"
      );
      return { data: cloneJson(version) };
    },

    simulateProtocolCandidate(body = {}) {
      const now = demoNowIso();
      const simKind = body?.kind || "ProtocolVersion";
      const rawCandidateRef = body?.stored_version_ref || null;
      const rawLibraryRef = body?.library_item_ref || null;
      const rawBaseLibraryRef = body?.base_library_item_ref || null;
      const candidateFound = rawCandidateRef
        ? findMutableProtocolVersion(rawCandidateRef)
        : null;
      const candidate = candidateFound?.version || null;
      const rawBaseRef =
        body?.base_version_ref || candidate?.parent_version || null;
      const baseFound = rawBaseRef
        ? findMutableProtocolVersion(rawBaseRef)
        : null;
      const base = baseFound?.version || null;
      const scopeRef =
        unpinRef(candidate?.study) || unpinRef(body?.study_ref) || STUDY_REF;
      const candHash =
        candidate?.content_hash ||
        (candidate
          ? `sha256-demo-v${candidate.number}-draft`
          : `sha256:demo-sim-${simKind.toLowerCase()}`);
      const baseHash = base
        ? base.content_hash || `sha256-demo-v${base.number}`
        : rawBaseRef || rawBaseLibraryRef
          ? "sha256:demo-sim-base"
          : null;
      const isAmendmentSim = Boolean(
        rawBaseRef || rawBaseLibraryRef || body?.candidate_content
      );
      const targetRef =
        rawCandidateRef || rawLibraryRef || rawBaseRef || scopeRef;
      const candidateVersionRef =
        simKind === "ProtocolVersion"
          ? rawCandidateRef
            ? unpinRef(rawCandidateRef)
            : null
          : rawLibraryRef
            ? unpinRef(rawLibraryRef)
            : null;
      const baseVersionRefOut =
        simKind === "ProtocolVersion"
          ? rawBaseRef
            ? unpinRef(rawBaseRef)
            : null
          : rawBaseLibraryRef
            ? unpinRef(rawBaseLibraryRef)
            : null;
      const governedVerNum = Number(candidate?.number || 2);
      const week4ElementRef = demoElementRef("visit-week4", governedVerNum);
      const subjectsList = state.subjects || [];
      const simBaseMs = demoNowMs();
      const simDate = (offsetDays) =>
        new Date(simBaseMs + offsetDays * 86_400_000)
          .toISOString()
          .slice(0, 10);

      return {
        scope: scopeRef,
        as_of: { valid_at: now, known_at: now },
        candidate: {
          kind: simKind,
          version: candidateVersionRef,
          base_version: baseVersionRefOut,
          content_hash: candHash,
          base_content_hash: baseHash,
        },
        engine_versions: [
          { name: "protocol.expected_schedule", version: "1" },
          { name: "protocol.matrix_gap", version: "1" },
          { name: "protocol.structural_diff", version: "1" },
        ],
        input_hash: `sha256-sim-in-${candHash}`,
        result_hash: `sha256-sim-out-${candHash}-${baseHash || "root"}`,
        structural_diff:
          simKind === "ProtocolVersion" && isAmendmentSim
            ? [
                {
                  element_id: "visit-week4",
                  instance_type: "PlannedVisit",
                  change: "Modified",
                  properties: ["window_before", "window_after"],
                  protocol_element: week4ElementRef,
                },
              ]
            : [],
        schedule_changes:
          simKind === "ProtocolVersion" && isAmendmentSim
            ? [
                {
                  activity_id: "visit-week4",
                  name: "Week 4",
                  change: "Modified",
                  protocol_element: week4ElementRef,
                  before: {
                    activity_id: "visit-week4",
                    name: "Week 4",
                    epoch_id: "epoch-treatment",
                    epoch_order: 2,
                    study_day: 28,
                    window_before: 2,
                    window_after: 2,
                    repeating: false,
                    locations: ["CLINIC"],
                    protocol_element: demoElementRef("visit-week4", 1),
                    epoch_protocol_element: demoElementRef(
                      "epoch-treatment",
                      1
                    ),
                  },
                  after: {
                    activity_id: "visit-week4",
                    name: "Week 4",
                    epoch_id: "epoch-treatment",
                    epoch_order: 2,
                    study_day: 28,
                    window_before: 3,
                    window_after: 3,
                    repeating: false,
                    locations: ["CLINIC"],
                    protocol_element: week4ElementRef,
                    epoch_protocol_element: demoElementRef(
                      "epoch-treatment",
                      1
                    ),
                  },
                },
              ]
            : [],
        masked: [],
        dependents: ctx.getDependents(targetRef),
        matrix_gaps: [],
        operational_impact: {
          active_site_count: 2,
          pending_site_count: 1,
          total_participant_count: subjectsList.length,
          affected_participant_count: isAmendmentSim
            ? Math.min(2, subjectsList.length)
            : 0,
          reconsent_participant_count: isAmendmentSim ? 1 : 0,
          participant_impacts: isAmendmentSim
            ? subjectsList.slice(0, 2).map((subj, idx) => ({
                enrollment: `enrollment:${ENROLLMENT_1001_UUID}@1`,
                subject_code: subj.subject_id || `SUBJ-100${idx + 1}`,
                status: subj.status || "ENROLLED",
                cohort: idx === 0 ? "ActiveOnStudy" : "FollowUp",
                requires_reconsent: idx === 0,
                schedule_shifts: [
                  {
                    activity_id: "visit-week4",
                    change: "Modified",
                    protocol_element: week4ElementRef,
                    before: {
                      visit_id: "visit-week4",
                      target_date: simDate(28),
                      window_start: simDate(26),
                      window_end: simDate(30),
                      protocol_element: demoElementRef("visit-week4", 1),
                    },
                    after: {
                      visit_id: "visit-week4",
                      target_date: simDate(29),
                      window_start: simDate(26),
                      window_end: simDate(32),
                      protocol_element: week4ElementRef,
                    },
                  },
                ],
              }))
            : [],
          hidden: { more_exist: false, reasons: [] },
        },
        projection_changes:
          isAmendmentSim || simKind === "MappingSpecification"
            ? [
                {
                  dataset: "LB",
                  added_count: 2,
                  removed_count: 0,
                  changed_count: 1,
                  samples: [
                    {
                      record_key: "SUBJ-1001:LB:VISIT-WEEK4:HBA1C",
                      change: "Modified",
                      before: { VISITDY: "28", LBSTAT: "" },
                      after: { VISITDY: "28", LBSTAT: "SCHEDULED" },
                    },
                  ],
                },
              ]
            : [],
        validation_delta: {
          opening:
            simKind === "RulePack"
              ? [
                  {
                    rule_id: "SDTM-VS-004",
                    rule_version: 1,
                    severity: "Warning",
                    stage: "Submission",
                    dataset: "VS",
                    subject: `enrollment:${ENROLLMENT_1001_UUID}@1`,
                    explanation:
                      "Seated systolic blood pressure exceeds protocol review threshold.",
                  },
                ]
              : [],
          resolving: isAmendmentSim
            ? [
                {
                  rule_id: "VISIT-WIN-002",
                  rule_version: 1,
                  severity: "Warning",
                  stage: "Capture",
                  dataset: "SV",
                  subject: `enrollment:${ENROLLMENT_1001_UUID}@1`,
                  explanation:
                    "Expanded +/-3 day Week 4 visit window resolves prior out-of-window warning.",
                },
              ]
            : [],
          unchanged_count: 14,
        },
        is_empty: !isAmendmentSim && simKind === "ProtocolVersion",
      };
    },

    saveSimulationReport(studyRef, body = {}) {
      const study = ctx.getStudyByRefOrCode(studyRef);
      const cleanStudyRef = unpinRef(study?.ref || studyRef || STUDY_REF);
      const simResult = body?.result || {};
      if (Array.isArray(simResult?.masked) && simResult.masked.length > 0) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Masked simulation cannot be saved",
              status: 409,
              code: "K.Simulation.MaskedNotAttestable",
              detail:
                "Simulation results containing withheld parts cannot be persisted as attestable evidence.",
            },
          },
        };
      }
      const idx = nextCounter();
      const artifactId =
        String(body?.artifact_id || "").trim() ||
        makeUuidFromIndex("00000910", idx);
      const artifactVersionId =
        String(body?.artifact_version_id || "").trim() ||
        makeUuidFromIndex("00000920", idx);
      const artifactRef = `artifact:${artifactId}`;
      const artifactVersionRef = `artifact_version:${artifactVersionId}`;
      const now = demoNowIso(idx);
      const serialized = JSON.stringify(simResult);
      const contentHash = simResult?.result_hash || `sha256-sim-report-${idx}`;
      const record = {
        artifact_ref: artifactRef,
        artifact_id: artifactId,
        artifact_version_ref: artifactVersionRef,
        artifact_version_id: artifactVersionId,
        version_number: 1,
        kind: "SimulationReport",
        study_ref: cleanStudyRef,
        title: String(body?.title || "Protocol Simulation Report"),
        content_hash: contentHash,
        size_bytes: serialized.length,
        media_type: "application/vnd.cadence.simulation-report+json",
        storage_ref: `urn:cadence:simulation-report:${artifactVersionId}`,
        recorded_at: now,
      };
      const studyCodeKey = String(study?.code || "DEMO-01").toUpperCase();
      const artifacts = state.studyArtifacts.get(studyCodeKey) || [];
      artifacts.unshift(record);
      state.studyArtifacts.set(studyCodeKey, artifacts);

      const candRef = unpinRef(simResult?.candidate?.version || "");
      if (candRef) {
        state.simulationReportsByVersion.set(candRef, {
          artifact: record,
          candidateContentHash: simResult?.candidate?.content_hash || null,
          baseVersionRef: unpinRef(simResult?.candidate?.base_version || ""),
          baseContentHash: simResult?.candidate?.base_content_hash || null,
        });
      }
      ctx.recordActivity(
        "kernel.simulation_report.save",
        "evidence",
        "artifact_version",
        artifactVersionRef,
        "usr-demo-operator"
      );
      return { data: cloneJson(record) };
    },

    reviewArtifactVersion(
      artifactVersionRef,
      body = {},
      { sigToken = "" } = {}
    ) {
      if (!String(sigToken || "").trim()) {
        return {
          error: {
            status: 401,
            problem: {
              title: "Step-up signature token required",
              status: 401,
              code: "Auth.SignatureTokenRequired",
              detail:
                "Header 'X-Sig-Token' is required to sign a 21 CFR Part 11 Reviewed attestation.",
            },
          },
        };
      }
      const cleanArtifactVerRef = unpinRef(artifactVersionRef);
      let matchedArtifact = null;
      for (const list of state.studyArtifacts.values()) {
        const hit = list.find(
          (a) => unpinRef(a.artifact_version_ref) === cleanArtifactVerRef
        );
        if (hit) {
          matchedArtifact = hit;
          break;
        }
      }
      if (!matchedArtifact) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Artifact version not found",
              status: 404,
              code: "K.Artifact.VersionNotFound",
              detail: `Artifact version '${artifactVersionRef}' was not found.`,
            },
          },
        };
      }
      if (
        body?.expected_content_hash &&
        body.expected_content_hash !== matchedArtifact.content_hash
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Artifact content hash mismatch",
              status: 409,
              code: "K.Evidence.ContentHashMismatch",
              detail:
                "Artifact content digest does not match expected_content_hash.",
            },
          },
        };
      }
      const idx = nextCounter();
      const attestationId =
        String(body?.attestation_id || "").trim() ||
        makeUuidFromIndex("00000930", idx);
      const attestationRef = `attestation:${attestationId}`;
      const now = demoNowIso(idx);
      const attestation = {
        attestation_ref: attestationRef,
        attestation_id: attestationId,
        artifact_ref: matchedArtifact.artifact_ref,
        artifact_version_ref: matchedArtifact.artifact_version_ref,
        study_ref: matchedArtifact.study_ref,
        meaning: body?.meaning || "Reviewed",
        content_hash: matchedArtifact.content_hash,
        recorded_at: now,
      };
      state.artifactReviews.set(cleanArtifactVerRef, attestation);
      ctx.recordActivity(
        "kernel.artifact_version.review",
        "evidence",
        "attestation",
        attestationRef,
        "usr-demo-operator"
      );
      return { data: cloneJson(attestation) };
    },

    getProtocolApprovalReadiness(versionRef) {
      const found = findMutableProtocolVersion(versionRef);
      if (!found) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Protocol version not found",
              status: 404,
              code: "K.Protocol.VersionNotFound",
              detail: `Protocol version '${versionRef}' was not found.`,
            },
          },
        };
      }
      const { version } = found;
      const cleanVersionRef = unpinRef(version.ref);
      const parentRef = version.parent_version
        ? unpinRef(version.parent_version)
        : null;
      const isAmendment = Boolean(parentRef);
      const isInReview = String(version.status).toLowerCase() === "inreview";

      if (!isAmendment) {
        const ready = isInReview;
        return {
          data: {
            protocol_version_ref: cleanVersionRef,
            status: version.status,
            is_amendment: false,
            parent_version_ref: null,
            ready,
            simulation_report_saved: true,
            review_signed: true,
            simulation_report_artifact_version_ref: null,
            review_attestation_ref: null,
            blocking_code: ready ? null : "K.Protocol.InvalidTransition",
            explanation: {
              subject: cleanVersionRef,
              claim: ready
                ? `Baseline Protocol Version ${version.number} is ready for Part 11 approval.`
                : `Protocol Version ${version.number} must be in InReview status before approval.`,
              producer: "kernel.protocol.approval_readiness",
              steps: [],
              conditions: [
                {
                  code: "protocol.status.in_review",
                  description:
                    "Protocol version is submitted for review (InReview) with a frozen content digest.",
                  met: isInReview,
                  evidence: [cleanVersionRef],
                },
              ],
              next_actions: ready
                ? []
                : [
                    {
                      description: `Submit Protocol Version ${version.number} for review before signing approval.`,
                      capability: "protocol.submit_for_review",
                      target: cleanVersionRef,
                    },
                  ],
            },
          },
        };
      }

      const parentFound = findMutableProtocolVersion(parentRef);
      const expectedParentHash = parentFound?.version?.content_hash || null;
      const savedSimEntry =
        state.simulationReportsByVersion.get(cleanVersionRef);
      const hashesMatch = Boolean(
        savedSimEntry &&
        (!version.content_hash ||
          savedSimEntry.candidateContentHash === version.content_hash) &&
        (!parentRef || savedSimEntry.baseVersionRef === parentRef) &&
        (!expectedParentHash ||
          !savedSimEntry.baseContentHash ||
          savedSimEntry.baseContentHash === expectedParentHash)
      );
      const reportArtifactVersionRef = hashesMatch
        ? savedSimEntry.artifact.artifact_version_ref
        : null;
      const reviewEntry = reportArtifactVersionRef
        ? state.artifactReviews.get(unpinRef(reportArtifactVersionRef)) || null
        : null;
      const simulationReportSaved = Boolean(reportArtifactVersionRef);
      const reviewSigned = Boolean(reviewEntry?.attestation_ref);
      const ready = isInReview && simulationReportSaved && reviewSigned;

      let blockingCode = null;
      if (!isInReview) {
        blockingCode = "K.Protocol.InvalidTransition";
      } else if (!simulationReportSaved || !reviewSigned) {
        blockingCode = "K.Protocol.SimulationReviewRequired";
      }

      const nextActions = [];
      const steps = [];
      if (!isInReview) {
        const msg = `Submit Protocol Version ${version.number} for review to freeze its content digest.`;
        steps.push({ claim: msg, evidence: [cleanVersionRef] });
        nextActions.push({
          description: msg,
          capability: "protocol.submit_for_review",
          target: cleanVersionRef,
        });
      }
      if (!simulationReportSaved) {
        const msg = `Run Simulate on Protocol Version ${version.number} against parent Version ${parentFound?.version?.number ?? 1} and save an unmasked SimulationReport.`;
        steps.push({
          claim: msg,
          evidence: [cleanVersionRef, parentRef].filter(Boolean),
        });
        nextActions.push({
          description: msg,
          capability: "simulation.save_report",
          target: cleanVersionRef,
        });
      } else if (!reviewSigned) {
        const msg = `Sign a 21 CFR Part 11 Reviewed attestation on ${reportArtifactVersionRef} before approving Protocol Version ${version.number}.`;
        steps.push({
          claim: msg,
          evidence: [reportArtifactVersionRef],
        });
        nextActions.push({
          description: msg,
          capability: "kernel.artifact_version.review",
          target: reportArtifactVersionRef,
        });
      }

      const claim = ready
        ? `Amendment Protocol Version ${version.number} has a verified SimulationReport and signed Reviewed attestation and is ready for Part 11 approval.`
        : !simulationReportSaved
          ? `Protocol Version ${version.number} requires a saved SimulationReport comparing it with parent Version ${parentFound?.version?.number ?? 1} before Part 11 approval.`
          : !reviewSigned
            ? `Saved SimulationReport ${reportArtifactVersionRef} requires a signed 21 CFR Part 11 Reviewed attestation before Protocol Version ${version.number} can be approved.`
            : `Protocol Version ${version.number} must be InReview before Part 11 approval.`;

      return {
        data: {
          protocol_version_ref: cleanVersionRef,
          status: version.status,
          is_amendment: true,
          parent_version_ref: parentRef,
          ready,
          simulation_report_saved: simulationReportSaved,
          review_signed: reviewSigned,
          simulation_report_artifact_version_ref: reportArtifactVersionRef,
          review_attestation_ref: reviewEntry?.attestation_ref || null,
          blocking_code: blockingCode,
          explanation: {
            subject: cleanVersionRef,
            claim,
            producer: "kernel.protocol.approval_readiness",
            steps,
            conditions: [
              {
                code: "protocol.status.in_review",
                description:
                  "Protocol version is submitted for review (InReview) with a frozen content digest.",
                met: isInReview,
                evidence: [cleanVersionRef],
              },
              {
                code: "protocol.amendment.simulation_report_saved",
                description:
                  "Unmasked SimulationReport artifact saved for parent and candidate protocol hashes.",
                met: simulationReportSaved,
                evidence: reportArtifactVersionRef
                  ? [reportArtifactVersionRef]
                  : [cleanVersionRef],
              },
              {
                code: "protocol.amendment.review_signed",
                description:
                  "21 CFR Part 11 Reviewed attestation signed on the saved SimulationReport.",
                met: reviewSigned,
                evidence: reviewEntry?.attestation_ref
                  ? [reviewEntry.attestation_ref]
                  : reportArtifactVersionRef
                    ? [reportArtifactVersionRef]
                    : [cleanVersionRef],
              },
            ],
            next_actions: nextActions,
          },
        },
      };
    },

    approveProtocolVersion(versionRef, body = {}, { sigToken = "" } = {}) {
      if (!String(sigToken || "").trim()) {
        return {
          error: {
            status: 401,
            problem: {
              title: "Step-up signature token required",
              status: 401,
              code: "Auth.SignatureTokenRequired",
              detail:
                "Header 'X-Sig-Token' is required to approve a protocol version.",
            },
          },
        };
      }
      const checked = requireMutableProtocolVersionInStatus(
        versionRef,
        ["inreview"],
        "Only an InReview protocol version may be approved."
      );
      if (checked.error) return checked;
      const { version } = checked;
      const readinessRes = methods.getProtocolApprovalReadiness(versionRef);
      if (!readinessRes.data?.ready) {
        return {
          error: {
            status: 409,
            problem: {
              title:
                "Simulation impact review required before amendment approval",
              status: 409,
              code:
                readinessRes.data?.blocking_code ||
                "K.Protocol.SimulationReviewRequired",
              detail: readinessRes.data?.explanation?.claim,
              explanation: readinessRes.data?.explanation,
            },
          },
        };
      }
      const idx = nextCounter();
      const attestationId =
        String(body?.attestation_id || "").trim() ||
        makeUuidFromIndex("00000800", idx);
      version.status = "Approved";
      version.approval_attestation = `attestation:${attestationId}`;
      syncSummaryVersionSnapshot(version);
      ctx.recordActivity(
        "kernel.protocol.approve",
        "protocol",
        "protocol_version",
        version.ref,
        "usr-demo-operator"
      );
      return { data: cloneJson(version) };
    },

    withdrawProtocolVersion(
      versionRef,
      _body = {},
      { reason = "", sigToken = "" } = {}
    ) {
      if (!String(sigToken || "").trim()) {
        return {
          error: {
            status: 401,
            problem: {
              title: "Step-up signature token required",
              status: 401,
              code: "Auth.SignatureTokenRequired",
              detail:
                "Header 'X-Sig-Token' is required to withdraw an InReview or Approved protocol version.",
            },
          },
        };
      }
      const checked = requireMutableProtocolVersionInStatus(
        versionRef,
        ["inreview", "approved"],
        "Only an InReview or Approved protocol version may be withdrawn."
      );
      if (checked.error) return checked;
      const { version } = checked;
      version.status = "Withdrawn";
      version.withdrawal_reason = reason || "Withdrawn in demo session";
      syncSummaryVersionSnapshot(version);
      ctx.recordActivity(
        "kernel.protocol.withdraw",
        "protocol",
        "protocol_version",
        version.ref,
        "usr-demo-operator"
      );
      return { data: cloneJson(version) };
    },

    listStudyArtifacts(studyRef) {
      const study = ctx.getStudyByRefOrCode(studyRef);
      const codeKey = String(study?.code || "DEMO-01").toUpperCase();
      return cloneJson(state.studyArtifacts.get(codeKey) || []);
    },

    registerStudyArtifact(studyRef, body = {}) {
      const study = ctx.getStudyByRefOrCode(studyRef);
      const codeKey = String(study?.code || "DEMO-01").toUpperCase();
      const cleanStudyRef = unpinRef(study?.ref || studyRef || STUDY_REF);
      const idx = nextCounter();
      const artifactId =
        String(body?.artifact_id || "").trim() ||
        makeUuidFromIndex("00000940", idx);
      const artifactVersionId =
        String(body?.artifact_version_id || "").trim() ||
        makeUuidFromIndex("00000950", idx);
      const record = {
        artifact_ref: `artifact:${artifactId}`,
        artifact_id: artifactId,
        artifact_version_ref: `artifact_version:${artifactVersionId}`,
        artifact_version_id: artifactVersionId,
        version_number: 1,
        kind: String(body?.kind || "EthicsApproval"),
        study_ref: cleanStudyRef,
        title: String(body?.title || "Regulatory Evidence Artifact"),
        content_hash: String(body?.content_hash || `sha256-artifact-${idx}`),
        size_bytes: Number(body?.size_bytes || 1024),
        media_type: String(body?.media_type || "application/pdf"),
        storage_ref: String(
          body?.storage_ref || `urn:cadence:artifact:${artifactVersionId}`
        ),
        recorded_at: demoNowIso(idx),
      };
      const list = state.studyArtifacts.get(codeKey) || [];
      list.unshift(record);
      state.studyArtifacts.set(codeKey, list);
      return { data: cloneJson(record) };
    },

    listStudyEffectivities(studyRef) {
      const study = ctx.getStudyByRefOrCode(studyRef);
      const codeKey = String(study?.code || "DEMO-01").toUpperCase();
      return cloneJson(state.studyEffectivities.get(codeKey) || []);
    },

    declareProtocolEffectivity(versionRef, body = {}) {
      const found = findMutableProtocolVersion(versionRef);
      if (!found) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Protocol version not found",
              status: 404,
              code: "K.Protocol.VersionNotFound",
            },
          },
        };
      }
      const { study, version } = found;
      const codeKey = String(study?.code || "DEMO-01").toUpperCase();
      const idx = nextCounter();
      const effectivityId =
        String(body?.effectivity_id || "").trim() ||
        makeUuidFromIndex("00000960", idx);
      const record = {
        effectivity_ref: `effectivity:${effectivityId}`,
        effectivity_id: effectivityId,
        study_ref: unpinRef(study.ref),
        protocol_version_ref: unpinRef(version.ref),
        protocol_version_number: version.number,
        scope_level: String(body?.scope_level || "Study"),
        country: body?.country || null,
        study_site_ref: body?.study_site_ref
          ? unpinRef(body.study_site_ref)
          : null,
        status: "Pending",
        status_since: body?.declared_at || demoNowIso(idx),
        evidence_artifact_version_refs: [],
      };
      const list = state.studyEffectivities.get(codeKey) || [];
      list.unshift(record);
      state.studyEffectivities.set(codeKey, list);
      return { data: cloneJson(record) };
    },

    changeEffectivityStatus(effectivityRef, body = {}) {
      const cleanRef = unpinRef(effectivityRef);
      for (const list of state.studyEffectivities.values()) {
        const hit = list.find((e) => unpinRef(e.effectivity_ref) === cleanRef);
        if (hit) {
          hit.status = String(body?.status || "Effective");
          hit.status_since = body?.effective_at || demoNowIso(nextCounter());
          if (Array.isArray(body?.evidence_artifact_version_refs)) {
            hit.evidence_artifact_version_refs =
              body.evidence_artifact_version_refs.map((r) => unpinRef(r));
          }
          return { data: cloneJson(hit) };
        }
      }
      return {
        error: {
          status: 404,
          problem: {
            title: "Effectivity not found",
            status: 404,
            code: "K.Effectivity.NotFound",
          },
        },
      };
    },

    activateKernelStudySite(studySiteRef) {
      const cleanRef = unpinRef(studySiteRef);
      if (state.studySites) {
        for (const links of state.studySites.values()) {
          const hit = links.find(
            (s) => unpinRef(s.study_site_ref) === cleanRef
          );
          if (hit) {
            hit.status = "Active";
            hit.is_active = true;
            return { data: cloneJson(hit) };
          }
        }
      }
      return {
        error: {
          status: 404,
          problem: {
            title: "Study site not found",
            status: 404,
            code: "K.StudySite.NotFound",
          },
        },
      };
    },

    getScheduleOfActivities(versionRef) {
      const version = getProtocolVersion(versionRef);
      const isGoverned =
        String(version?.status || "Approved").toLowerCase() === "approved";
      const verNum = Number(version?.number || 2);
      return {
        version: versionRef || VERSION_V2_REF,
        content_hash: "sha256-demo-soa-v2",
        hash: "sha256-demo-soa-matrix",
        title: `DEMO-01 Schedule of Activities (Protocol v${verNum}.0)`,
        epochs: [
          {
            epoch_id: "epoch-screening",
            name: "Screening",
            order: 1,
            first_column: 0,
            column_count: 1,
            protocol_element: isGoverned
              ? demoElementRef("epoch-screening", verNum)
              : null,
          },
          {
            epoch_id: "epoch-treatment",
            name: "Treatment",
            order: 2,
            first_column: 1,
            column_count: 2,
            protocol_element: isGoverned
              ? demoElementRef("epoch-treatment", verNum)
              : null,
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
            protocol_element: isGoverned
              ? demoElementRef("visit-screening", verNum)
              : null,
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
            protocol_element: isGoverned
              ? demoElementRef("visit-baseline", verNum)
              : null,
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
            protocol_element: isGoverned
              ? demoElementRef("visit-week4", verNum)
              : null,
          },
        ],
        rows: [
          {
            collection_spec_id: "form-vital-signs",
            name: "Vital Signs (CDASH VS)",
            item_ids: ["item-sysbp", "item-diabp", "item-pulse", "item-notes"],
            concepts: [
              DEMO_CONCEPT_NODE_REFS.SYSBP,
              DEMO_CONCEPT_NODE_REFS.DIABP,
              DEMO_CONCEPT_NODE_REFS.PULSE,
              DEMO_CONCEPT_NODE_REFS.VSCOMM,
            ],
            columns: [0, 1, 2],
            frequency: 3,
            repeating: false,
            footnotes: [1],
            protocol_element: isGoverned
              ? demoElementRef("form-vital-signs", verNum)
              : null,
            form_version_snapshot:
              "form_version_snapshot:00000000-0000-0000-0000-000000006401@1", // deid-ignore
          },
          {
            collection_spec_id: "form-chem-panel",
            name: "Clinical Chemistry Panel (CDASH LB)",
            item_ids: ["item-hba1c", "item-egfr"],
            concepts: [
              DEMO_CONCEPT_NODE_REFS.HBA1C,
              DEMO_CONCEPT_NODE_REFS.EGFR,
            ],
            columns: [0, 2],
            frequency: 2,
            repeating: false,
            footnotes: [],
            protocol_element: isGoverned
              ? demoElementRef("form-chem-panel", verNum)
              : null,
            form_version_snapshot:
              "form_version_snapshot:00000000-0000-0000-0000-000000006402@1", // deid-ignore
          },
          {
            collection_spec_id: "form-adverse-event",
            name: "Adverse Event (CDASH AE)",
            item_ids: ["item-aeterm", "item-aesev"],
            concepts: [
              DEMO_CONCEPT_NODE_REFS.AETERM,
              DEMO_CONCEPT_NODE_REFS.AESEV,
            ],
            columns: [],
            frequency: 0,
            repeating: true,
            footnotes: [],
            protocol_element: isGoverned
              ? demoElementRef("form-adverse-event", verNum)
              : null,
            form_version_snapshot:
              "form_version_snapshot:00000000-0000-0000-0000-000000006403@1", // deid-ignore
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
      const subjectsList = state.subjects || [];
      const week4ElementRef = demoElementRef("visit-week4", 2);
      const chemElementRef = demoElementRef("form-chem-panel", 2);
      return {
        amended: versionRef || VERSION_V2_REF,
        parent: VERSION_V1_REF,
        impacted_elements: ["visit-week4", "form-chem-panel"],
        protocol_elements: {
          "visit-week4": week4ElementRef,
          "form-chem-panel": chemElementRef,
        },
        dependents: ctx.getDependents(versionRef || VERSION_V2_REF),
        changed_elements: [
          {
            element_id: "visit-week4",
            instance_type: "PlannedVisit",
            change: "Modified",
            properties: ["window_before", "window_after"],
            protocol_element: week4ElementRef,
          },
        ],
        schedule: {
          directives: [],
          impacted_elements: ["visit-week4"],
          impacted_concepts: [DEMO_CONCEPT_NODE_REFS.HBA1C],
          undeclared: [],
          changes_schedule: true,
          changes_eligibility: false,
          protocol_elements: {
            "visit-week4": week4ElementRef,
          },
        },
        active_site_count: 2,
        pending_site_count: 1,
        total_participant_count: subjectsList.length,
        affected_participant_count: Math.min(2, subjectsList.length),
        reconsent_participant_count: 1,
        hidden: { more_exist: false, reasons: [] },
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

    getStudyAttention(studyRefOrCode = "", { limit = 20 } = {}, caller = null) {
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
      const studyRef = String(resolved.study.ref).split("@")[0];
      const studyId = studyRef.startsWith("study:")
        ? studyRef.slice("study:".length)
        : "";
      const sdvAlerts =
        typeof ctx.listSdvAlerts === "function"
          ? ctx.listSdvAlerts(
              {
                studyId: studyId || undefined,
                studyCode: resolved.study.code || undefined,
              },
              caller
            )
          : [];
      return {
        data: {
          study_ref: studyRef,
          proposals: proposalsRes.data || [],
          findings: findingsRes.data?.findings || [],
          sdv_alerts: Array.isArray(sdvAlerts) ? sdvAlerts : [],
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
