import {
  DEMO_PARTICIPANTS,
  DEMO_SITES,
  DEMO_SPONSOR,
  DEMO_STUDY,
  getDemoPersonaForWorkspace,
} from "demo-fixtures";
import {
  createDemoHealthRoute,
  createDemoSignatureVerificationRoute,
} from "demo-fixtures/routes";
import { isValidSemanticAction, isValidStepUpAction } from "ui";
import {
  findCommercialTier,
  PRE_RELEASE_ENVIRONMENT_NOTICE,
} from "../../catalog/productCatalog.js";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";
import {
  evaluateCapabilityDecision,
  evaluateNodeCommands,
  isCallerBlinded,
  isSitePermitted,
  resolveDemoCaller,
} from "../demoPolicy.js";
import {
  DEMO_ORGANIZATIONS,
  createInitialDemoStudyDirectoryEntry,
} from "../demoStudyImport.js";
import {
  DEFAULT_FORM_UUID,
  DEFAULT_QUERY_UUID,
  DEMO_SUBSCRIPTION_UUID,
  DEMO_TENANT_UUID,
  ENROLLMENT_1001_UUID,
  FINDING_1_UUID,
  OBS_1_UUID,
  OBS_2_UUID,
  OBS_3_UUID,
  OBS_4_UUID,
  PROPOSAL_1_REF,
  RULE_PACK_SAFETY_UUID,
  RULE_PACK_VS_UUID,
  SITE_101_UUID,
  SITE_102_UUID,
  STUDY_REF,
  VERSION_V1_REF,
  VERSION_V2_REF,
  buildDecision,
  cloneJson,
  computeContentHash,
  decodeReasonHeader,
  evaluateVitalSignsOverlayWarnings,
  makeUuidFromIndex,
  readIdempotencyKey,
  readJsonBody,
  readRequiredReasonHeader,
  resolveCollectionTitle,
  resolveSiteCodeFromRef,
  resolveSiteUuid,
  validateWriteCommandHeaders,
} from "./shared.js";

function createInitialObservations() {
  const obs1Ref = `observation:${OBS_1_UUID}`;
  const obs2Ref = `observation:${OBS_2_UUID}`;
  const obs3Ref = `observation:${OBS_3_UUID}`;
  const obs4Ref = `observation:${OBS_4_UUID}`;
  const canonicalEnrollmentUuid =
    DEMO_PARTICIPANTS[0]?.enrollmentUuid || ENROLLMENT_1001_UUID;
  const enrollmentRef = `enrollment:${canonicalEnrollmentUuid}`;

  const makeObs = (ref, concept, value, recordedAt, reasonText) => ({
    ref,
    enrollment_ref: enrollmentRef,
    concept,
    value,
    observed_at: recordedAt,
    version: 1,
    locks: [],
    revisions: [
      {
        number: 1,
        value: cloneJson(value),
        reason_for_change: reasonText,
        recorded_by: "usr-crc-01",
        recorded_at: recordedAt,
        recorded_until: null,
      },
    ],
    assertions: [],
    audit: [
      {
        sequence: 1,
        recorded_at: recordedAt,
        actor_id: "usr-crc-01",
        action: "observation.record",
        subject: ref,
        version: 1,
        reason_for_change: reasonText,
      },
    ],
  });

  return new Map([
    [
      obs1Ref,
      makeObs(
        obs1Ref,
        "SYSBP",
        { type: "quantity", value: "128", unit: "mm[Hg]", comparator: null },
        "2026-08-01T09:15:00Z", // deid-ignore
        "Initial baseline assessment entry"
      ),
    ],
    [
      obs2Ref,
      makeObs(
        obs2Ref,
        "DIABP",
        { type: "quantity", value: "82", unit: "mm[Hg]", comparator: null },
        "2026-08-01T09:16:00Z", // deid-ignore
        "Initial baseline assessment entry"
      ),
    ],
    [
      obs3Ref,
      makeObs(
        obs3Ref,
        "PULSE",
        { type: "quantity", value: "72", unit: "/min", comparator: null },
        "2026-08-01T09:17:00Z", // deid-ignore
        "Initial baseline assessment entry"
      ),
    ],
    [
      obs4Ref,
      makeObs(
        obs4Ref,
        "VSCOMM",
        { type: "text", value: "Seated rest 5 minutes prior to cuff." },
        "2026-08-01T09:18:00Z", // deid-ignore
        "Initial baseline vital signs note"
      ),
    ],
  ]);
}

function createInitialRulePacks() {
  return [
    {
      ref: `library_item:${RULE_PACK_VS_UUID}`,
      key: "cdisc-cdash-vs-core",
      version: 1,
      title: "CDASH Vital Signs Physiological Bounds",
      description:
        "Baseline systolic, diastolic, and resting heart rate physiological edit checks.",
      status: "Approved",
      pinned: true,
      content_hash: "sha256-demo-rp-vs-01",
      rule_count: 3,
      rules: [
        {
          id: "RULE-SYSBP-RANGE",
          version: 1,
          stage: "Entry",
          dataset: "VS",
          severity: "Warning",
          explanation:
            "Systolic blood pressure outside expected 70-220 mmHg physiological window.",
          remediation_guidance:
            "Verify cuff size and confirm 5 minutes of quiet seated rest.",
          condition: "SYSBP < 70 || SYSBP > 220",
        },
        {
          id: "RULE-HR-RANGE",
          version: 1,
          stage: "Entry",
          dataset: "VS",
          severity: "Warning",
          explanation: "Resting heart rate outside expected 50-100 bpm window.",
          remediation_guidance: "Confirm pulse against bedside flowsheet.",
          condition: "PULSE < 50 || PULSE > 100",
        },
        {
          id: "RULE-BP-INVERTED",
          version: 1,
          stage: "Entry",
          dataset: "VS",
          severity: "Error",
          explanation:
            "Diastolic blood pressure must be strictly lower than systolic blood pressure.",
          remediation_guidance:
            "Verify systolic and diastolic values were not transposed.",
          condition: "DIABP >= SYSBP",
        },
      ],
    },
    {
      ref: `library_item:${RULE_PACK_SAFETY_UUID}`,
      key: "cadence-cardio-safety-v2",
      version: 1,
      title: "Cardiometabolic Hypertensive Crisis Escalation",
      description:
        "Flags severe systolic hypertension (>= 180 mmHg) for PI clinical review.",
      status: "Draft",
      pinned: false,
      content_hash: "sha256-demo-rp-cardio-02",
      rule_count: 1,
      rules: [
        {
          id: "RULE-SYSBP-CRISIS",
          version: 1,
          stage: "Commit",
          dataset: "VS",
          severity: "Error",
          explanation:
            "Systolic blood pressure >= 180 mmHg requires repeat measurement and PI notification.",
          remediation_guidance:
            "Repeat BP after 5 minutes and notify Principal Investigator.",
          condition: "SYSBP >= 180",
        },
      ],
    },
  ];
}

function createInitialValidationFindings(studyRef) {
  return [
    {
      ref: `finding:${FINDING_1_UUID}`,
      study_ref: studyRef,
      stage: "Entry",
      pack_key: "cdisc-cdash-vs-core",
      rule_pack_ref: `library_item:${RULE_PACK_VS_UUID}`,
      rule_id: "RULE-HR-RANGE",
      rule_version: 1,
      severity: "Warning",
      status: "Open",
      blocks: false,
      target_kind: "observation",
      dataset: "VS",
      record_key: "SUBJ-1002/visit-baseline/item-hr",
      variable: "PULSE",
      message:
        "[RULE-HR-RANGE] Resting heart rate outside expected 50-100 bpm window.",
      expected: "50 <= PULSE <= 100",
      observed: [{ variable: "PULSE", value: "108" }],
      first_detected_run_ref:
        "validation_run:00000000-0000-4000-8000-000000009301", // deid-ignore
      last_detected_run_ref:
        "validation_run:00000000-0000-4000-8000-000000009301", // deid-ignore
      resolved_by_run_ref: null,
      classification: null,
    },
  ];
}

export function createKernelSliceStore(ctx) {
  const { state, nextCounter } = ctx;

  function resolveEnrollmentByIdOrCode(rawId) {
    const cleanId = String(rawId || "")
      .replace(/^(enrollment|allocation|randomization|treatment_arm|arm):/, "")
      .split("@")[0];
    return (
      state.subjects.find(
        (s) =>
          s.id === cleanId ||
          String(s.subject_id || "").toUpperCase() === cleanId.toUpperCase()
      ) || null
    );
  }

  function resolveSiteCodeForNodeRef(nodeRef) {
    const refStr = String(nodeRef || "");
    if (refStr.startsWith("study_site:")) {
      return resolveSiteCodeFromRef(refStr, state.subjects, state.studySites);
    }
    if (
      refStr.startsWith("enrollment:") ||
      refStr.startsWith("allocation:") ||
      refStr.startsWith("randomization:")
    ) {
      const subj = resolveEnrollmentByIdOrCode(refStr);
      return subj?.site_id || null;
    }
    if (refStr.startsWith("collection_instance:")) {
      const rawFormId = refStr
        .replace(/^collection_instance:/, "")
        .split("@")[0];
      const form = state.forms.get(rawFormId);
      if (form?.enrollment_id) {
        const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
        return subj?.site_id || null;
      }
    }
    if (refStr.startsWith("observation:")) {
      const cleanObsId = refStr.replace(/^observation:/, "").split("@")[0];
      for (const form of state.forms.values()) {
        if (
          Array.isArray(form.items) &&
          form.items.some((it) => it.observation_id === cleanObsId)
        ) {
          const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
          return subj?.site_id || null;
        }
      }
    }
    return null;
  }

  function isAllocationNodeRef(nodeRef) {
    const kind = String(nodeRef || "")
      .split(":")[0]
      .toLowerCase();
    return (
      kind === "allocation" ||
      kind === "treatment_arm" ||
      kind === "arm" ||
      kind === "randomization"
    );
  }

  function applyNodeVisibilityMask(
    node,
    caller = null,
    { siteCode = null, isAllocation = false, subjectStatus = null } = {}
  ) {
    if (!caller || !node) {
      return node;
    }
    const effectiveSite = siteCode || resolveSiteCodeForNodeRef(node.ref);
    if (effectiveSite && !isSitePermitted(caller, effectiveSite)) {
      return {
        ...node,
        title: null,
        status: null,
        mask: "NotPermitted",
      };
    }
    if (
      (isAllocation || isAllocationNodeRef(node.ref)) &&
      isCallerBlinded(caller) &&
      String(subjectStatus || "").toUpperCase() !== "UNBLINDED"
    ) {
      return {
        ...node,
        title: null,
        code: null,
        status: null,
        mask: "Blinded",
      };
    }
    return node;
  }

  function reset() {
    state.observations = createInitialObservations();
    const demoSiteInstitutions = DEMO_SITES.map((site) => ({
      ref: `organization:${site.institutionUuid}@1`,
      code: site.institutionCode,
      name: `${site.name} Institution`,
      kind: "SiteInstitution",
    }));
    state.organizations = [
      ...cloneJson(DEMO_ORGANIZATIONS),
      ...demoSiteInstitutions,
    ];
    if (!state.organizations.some((org) => org.kind === "SiteInstitution")) {
      state.organizations.push({
        ref: "organization:00000000-0000-4000-8000-000000000310@1", // deid-ignore
        code: "US-CLINICAL-INST",
        name: "US Academic Clinical Research Network",
        kind: "SiteInstitution",
      });
    }
    state.idempotentStudyResponses = new Map();
    state.idempotentSiteResponses = new Map();
    state.idempotentStudySiteResponses = new Map();

    const canonicalStudyCode = DEMO_STUDY.code || "CADENCE-101";
    const baseCadenceStudy = createInitialDemoStudyDirectoryEntry();
    const canonicalStudyEntry = {
      ...baseCadenceStudy,
      ref: DEMO_STUDY.ref || baseCadenceStudy.ref,
      code: canonicalStudyCode,
      title: DEMO_STUDY.title || baseCadenceStudy.title,
      phase: DEMO_STUDY.phase || baseCadenceStudy.phase,
      sponsor: DEMO_SPONSOR.code || baseCadenceStudy.sponsor_code,
      sponsor_code: DEMO_SPONSOR.code || baseCadenceStudy.sponsor_code,
      sponsor_name: DEMO_SPONSOR.name || baseCadenceStudy.sponsor_name,
      site_count: DEMO_SITES.length || baseCadenceStudy.site_count,
    };

    state.studies = new Map([
      [canonicalStudyCode, canonicalStudyEntry],
      [
        "DEMO-01",
        {
          ref: STUDY_REF,
          kind: "study",
          code: "DEMO-01",
          title: "Phase II Synthetic Cardiometabolic Demonstration Protocol",
          phase: "Phase 2",
          sponsor_ref: DEMO_ORGANIZATIONS[0].ref,
          sponsor_code: DEMO_ORGANIZATIONS[0].code,
          sponsor_name: DEMO_ORGANIZATIONS[0].name,
          site_count: 2,
          status: "ACTIVE",
          mask: null,
        },
      ],
      [
        "STUDY-USDM-001",
        {
          ref: "study:00000000-0000-0000-0000-000000000002", // deid-ignore
          kind: "study",
          code: "STUDY-USDM-001",
          title: "USDM v3.0 Synthetic Oncology Reference Trial",
          phase: "Phase 3",
          sponsor_ref: DEMO_ORGANIZATIONS[0].ref,
          sponsor_code: DEMO_ORGANIZATIONS[0].code,
          sponsor_name: DEMO_ORGANIZATIONS[0].name,
          site_count: 2,
          status: "ACTIVE",
          mask: null,
        },
      ],
    ]);

    const initialSiteInst =
      state.organizations.find((o) => o.kind === "SiteInstitution") ||
      state.organizations[0];
    state.sites =
      DEMO_SITES.length > 0
        ? DEMO_SITES.map((demoSite, idx) => ({
            ref: demoSite.siteUuid
              ? `site:${demoSite.siteUuid}@1`
              : `site:${makeUuidFromIndex("00000210", idx + 1)}@1`,
            code: demoSite.code,
            name: demoSite.name || demoSite.code,
            country: demoSite.country || "US",
            is_active: true,
            institution_ref: demoSite.institutionUuid
              ? `organization:${demoSite.institutionUuid}@1`
              : initialSiteInst.ref,
            institution_code: demoSite.institutionCode || initialSiteInst.code,
            institution_name: demoSite.name
              ? `${demoSite.name} Institution`
              : initialSiteInst.name,
            linked_study_count: state.studies.size,
          }))
        : [
            {
              ref: `site:${SITE_101_UUID}@1`,
              code: "SITE-101",
              name: "US Academic Clinical Research Center (SITE-101)",
              country: "US",
              is_active: true,
              institution_ref: initialSiteInst.ref,
              institution_code: initialSiteInst.code,
              institution_name: initialSiteInst.name,
              linked_study_count: state.studies.size,
            },
            {
              ref: `site:${SITE_102_UUID}@1`,
              code: "SITE-102",
              name: "US Regional Trial Unit 2",
              country: "US",
              is_active: true,
              institution_ref: initialSiteInst.ref,
              institution_code: initialSiteInst.code,
              institution_name: initialSiteInst.name,
              linked_study_count: state.studies.size,
            },
          ];

    const buildStudySitesForStudy = (studyRef, studyCode) =>
      DEMO_SITES.length > 0
        ? DEMO_SITES.map((site, idx) => {
            const unversionedStudySiteRef =
              studyRef === canonicalStudyEntry.ref && site.studySiteRef
                ? String(site.studySiteRef).split("@")[0]
                : `study_site:${makeUuidFromIndex("00000400", idx + 1)}`;
            return {
              ref: `${unversionedStudySiteRef}@1`,
              study_site_id: unversionedStudySiteRef.replace(
                /^study_site:/,
                ""
              ),
              study_site_ref: unversionedStudySiteRef,
              study_ref: String(studyRef).split("@")[0],
              study_code: studyCode,
              site_ref: `site:${site.siteUuid}@1`,
              site_code: site.code,
              site_name: site.name,
              country: site.country,
              institution_ref: `organization:${site.institutionUuid}@1`,
              institution_code: site.institutionCode,
              institution_name: `${site.name} Institution`,
              status:
                site.complianceStage === "CONDUCT" ? "Active" : "Selected",
              is_active: true,
              effective_versions: [],
              mask: null,
            };
          })
        : state.sites.map((site) => {
            const siteUuid = resolveSiteUuid(site.code);
            return {
              ref: `study_site:${siteUuid}@1`,
              study_site_id: siteUuid,
              study_site_ref: `study_site:${siteUuid}`,
              study_ref: String(studyRef).split("@")[0],
              study_code: studyCode,
              site_ref: site.ref,
              site_code: site.code,
              site_name: site.name,
              country: site.country,
              institution_ref: site.institution_ref,
              institution_code: site.institution_code,
              institution_name: site.institution_name,
              status: "Active",
              is_active: true,
              effective_versions: [],
              mask: null,
            };
          });

    state.studySites = new Map([
      [
        canonicalStudyCode.toUpperCase(),
        buildStudySitesForStudy(canonicalStudyEntry.ref, canonicalStudyCode),
      ],
      ["DEMO-01", buildStudySitesForStudy(STUDY_REF, "DEMO-01")],
      [
        "STUDY-USDM-001",
        buildStudySitesForStudy(
          "study:00000000-0000-0000-0000-000000000002", // deid-ignore
          "STUDY-USDM-001"
        ),
      ],
    ]);

    state.proposals = new Map([
      [
        PROPOSAL_1_REF,
        {
          ref: PROPOSAL_1_REF,
          study: STUDY_REF,
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
          proposed_at: "2026-08-03T10:00:00Z", // deid-ignore
          expires_at: "2026-09-03T10:00:00Z", // deid-ignore
          proposed_by: "usr-designer-01",
          proposed_by_kind: "human",
          requested_by: "usr-designer-01",
          model_id: null,
          confidence_score: null,
          content_hash: "sha256-demo-prop-4001",
          status: "PENDING",
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

    state.activityEntries = [
      {
        event_id: "00000000-0000-4000-8000-000000000902", // deid-ignore
        type: "execution.query.raised",
        family: "execution",
        recorded: "2026-08-02T11:00:00Z", // deid-ignore
        occurred_valid: "2026-08-02T11:00:00Z", // deid-ignore
        actor_id: "usr-cra-01",
        subject_kind: "query",
        subject: `query:${DEFAULT_QUERY_UUID}`,
        mask: null,
        correlation_id: "00000000-0000-4000-8000-000000000992", // deid-ignore
      },
      {
        event_id: "00000000-0000-4000-8000-000000000901", // deid-ignore
        type: "observation.recorded",
        family: "collection",
        recorded: "2026-08-01T09:15:00Z", // deid-ignore
        occurred_valid: "2026-08-01T09:15:00Z", // deid-ignore
        actor_id: "usr-crc-01",
        subject_kind: "observation",
        subject: `observation:${OBS_1_UUID}`,
        mask: null,
        correlation_id: "00000000-0000-4000-8000-000000000991", // deid-ignore
      },
    ];

    state.rulePacks = createInitialRulePacks();
    state.studyPinnedRulePacks = new Map([
      [canonicalStudyCode, [`library_item:${RULE_PACK_VS_UUID}`]],
      ["DEMO-01", [`library_item:${RULE_PACK_VS_UUID}`]],
      ["STUDY-USDM-001", [`library_item:${RULE_PACK_VS_UUID}`]],
    ]);
    state.validationFindings = createInitialValidationFindings(STUDY_REF);

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

  function recordActivity(type, family, subjectKind, subjectRef, actorId) {
    const idx = nextCounter();
    const now = demoNowIso(idx);
    state.activityEntries.unshift({
      event_id: makeUuidFromIndex("00000900", idx),
      type,
      family,
      recorded: now,
      occurred_valid: now,
      actor_id:
        actorId ||
        getDemoPersonaForWorkspace("super_admin")?.username ||
        "designer.lead",
      subject_kind: subjectKind,
      subject: subjectRef,
      mask: null,
      correlation_id: makeUuidFromIndex("00000990", idx),
    });
  }

  function ensureStudyByCode(rawCode) {
    const code = (rawCode || "DEMO-01").trim() || "DEMO-01";
    for (const [existingCode, study] of state.studies.entries()) {
      if (existingCode.toUpperCase() === code.toUpperCase()) {
        return study;
      }
    }
    const idx = nextCounter();
    const created = {
      ref: `study:${makeUuidFromIndex("00000010", idx)}`,
      kind: "study",
      code,
      title: `Synthetic Clinical Study ${code}`,
      status: "ACTIVE",
      mask: null,
    };
    state.studies.set(code, created);
    return created;
  }

  function getStudyByRefOrCode(refOrCode) {
    const raw = String(refOrCode || "").trim();
    const unpinned = raw.split("@")[0];
    for (const study of state.studies.values()) {
      if (
        study.ref === raw ||
        study.ref.split("@")[0] === unpinned ||
        study.code.toUpperCase() === raw.toUpperCase()
      ) {
        return study;
      }
    }
    if (raw.startsWith("study:")) {
      return state.studies.get("DEMO-01");
    }
    return ensureStudyByCode(refOrCode);
  }

  function getPinnedRulePackRefsForStudy(studyCode) {
    return (
      state.studyPinnedRulePacks.get(studyCode) || [
        `library_item:${RULE_PACK_VS_UUID}`,
      ]
    );
  }

  ctx.recordActivity = recordActivity;
  ctx.ensureStudyByCode = ensureStudyByCode;
  ctx.getStudyByRefOrCode = getStudyByRefOrCode;
  ctx.getPinnedRulePackRefsForStudy = getPinnedRulePackRefsForStudy;

  const methods = {
    buildDecision,

    searchKernel({ q = "", kinds = [], limit = 25 } = {}) {
      const trimmed = String(q || "").trim();
      const lower = trimmed.toLowerCase();
      const kindFilter = new Set(
        (Array.isArray(kinds) ? kinds : [kinds]).filter(Boolean)
      );

      if (
        trimmed &&
        (kindFilter.size === 0 || kindFilter.has("study")) &&
        /^[A-Za-z0-9_-]{2,32}$/.test(trimmed)
      ) {
        const hasMatch = [...state.studies.values()].some(
          (s) =>
            s.code.toLowerCase().includes(lower) ||
            s.title.toLowerCase().includes(lower)
        );
        if (!hasMatch && trimmed.toUpperCase().startsWith("DEMO")) {
          ensureStudyByCode(trimmed.toUpperCase());
        }
      }

      const candidates = [];
      for (const study of state.studies.values()) {
        candidates.push({
          ref: study.ref,
          kind: "study",
          title: study.title,
          code: study.code,
          detail: `Status: ${study.status}`,
          exact_code: Boolean(trimmed && study.code.toLowerCase() === lower),
          rank:
            study.code === (DEMO_STUDY.code || "CADENCE-101") ||
            study.code === "DEMO-01"
              ? 1.0
              : 0.9,
        });
      }

      for (const subj of state.subjects) {
        candidates.push({
          ref: `enrollment:${subj.id}`,
          kind: "enrollment",
          title: `Participant ${subj.subject_id}`,
          code: subj.subject_id,
          detail: `${subj.study_id} · ${subj.site_id || "SITE-101"} (${subj.status})`,
          exact_code: Boolean(
            trimmed && subj.subject_id.toLowerCase() === lower
          ),
          rank: 0.8,
        });
      }

      for (const obs of state.observations.values()) {
        candidates.push({
          ref: obs.ref,
          kind: "observation",
          title: `Observation ${obs.concept}`,
          code: obs.concept,
          detail: `Version ${obs.version}`,
          exact_code: Boolean(
            trimmed &&
            (obs.concept.toLowerCase() === lower ||
              obs.ref.toLowerCase() === lower)
          ),
          rank: 0.7,
        });
      }

      return candidates
        .filter((hit) => {
          if (kindFilter.size > 0 && !kindFilter.has(hit.kind)) return false;
          if (!lower) return true;
          return (
            hit.ref.toLowerCase().includes(lower) ||
            (hit.code && hit.code.toLowerCase().includes(lower)) ||
            hit.title.toLowerCase().includes(lower)
          );
        })
        .slice(0, Number(limit) || 25);
    },

    lookupEnrollment({
      studyCode = "",
      subjectCode = "",
      siteCode = null,
    } = {}) {
      const trimmedStudy = String(studyCode || "").trim();
      const trimmedSubject = String(subjectCode || "").trim();
      const trimmedSite =
        siteCode !== null &&
        siteCode !== undefined &&
        String(siteCode).trim() !== ""
          ? String(siteCode).trim()
          : null;

      if (!trimmedStudy || !trimmedSubject) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Invalid lookup query",
              status: 400,
              detail:
                "Name a valid study_code, subject_code, and optional site_code.",
            },
          },
        };
      }

      const notFound = {
        error: {
          status: 404,
          problem: {
            title: "Not found",
            status: 404,
            detail:
              "No visible enrollment matches the requested codes in your tenant.",
          },
        },
      };

      let study = [...state.studies.values()].find(
        (s) => s.code.toUpperCase() === trimmedStudy.toUpperCase()
      );
      if (!study && trimmedStudy.toUpperCase().startsWith("DEMO")) {
        study = ensureStudyByCode(trimmedStudy.toUpperCase());
      }
      if (!study) {
        return notFound;
      }

      const studySubjects = state.subjects.filter(
        (s) =>
          String(s.study_id || "").toUpperCase() === study.code.toUpperCase()
      );
      const candidateSubjects =
        studySubjects.length > 0
          ? studySubjects
          : study.code.toUpperCase().startsWith("DEMO")
            ? state.subjects
            : [];

      const matches = candidateSubjects.filter((s) => {
        if (s.subject_id !== trimmedSubject) return false;
        if (trimmedSite === null) return true;
        return (
          String(s.site_id || "SITE-101").toUpperCase() ===
          trimmedSite.toUpperCase()
        );
      });

      if (matches.length !== 1) {
        return notFound;
      }

      const matched = matches[0];
      const effectiveSiteCode = matched.site_id || "SITE-101";
      const studyRef = study.ref.includes("@") ? study.ref : `${study.ref}@1`;

      return {
        data: {
          study_ref: studyRef,
          study_code: study.code,
          study_title: study.title,
          study_site_ref: `study_site:${resolveSiteUuid(effectiveSiteCode)}@1`,
          site_code: effectiveSiteCode,
          site_title:
            effectiveSiteCode === "SITE-102"
              ? "US Regional Trial Unit 2"
              : `US Academic Clinical Research Center (${effectiveSiteCode})`,
          enrollment_ref: `enrollment:${matched.id}@1`,
          subject_code: matched.subject_id,
          status: matched.status || "SCREENED",
        },
      };
    },

    getNode(nodeRef, caller = null) {
      if (String(nodeRef).startsWith("study:")) {
        return cloneJson(getStudyByRefOrCode(nodeRef));
      }
      if (isAllocationNodeRef(nodeRef)) {
        const subj =
          resolveEnrollmentByIdOrCode(nodeRef) || state.subjects[0] || null;
        const armCode = subj?.treatment_group || "ARM-A";
        const baseNode = {
          ref: nodeRef,
          kind: String(nodeRef).split(":")[0] || "allocation",
          title: `Treatment Allocation · ${armCode}`,
          code: armCode,
          status: "ALLOCATED",
          mask: null,
        };
        return applyNodeVisibilityMask(baseNode, caller, {
          siteCode: subj?.site_id || null,
          isAllocation: true,
          subjectStatus: subj?.status || null,
        });
      }
      if (String(nodeRef).startsWith("observation:")) {
        const obs = state.observations.get(nodeRef);
        if (obs) {
          return applyNodeVisibilityMask(
            {
              ref: obs.ref,
              kind: "observation",
              title: `Observation ${obs.concept}`,
              code: obs.concept,
              status: "RECORDED",
              mask: null,
            },
            caller
          );
        }
      }
      if (String(nodeRef).startsWith("protocol_version:")) {
        return {
          ref: nodeRef,
          kind: "protocol_version",
          title:
            nodeRef === VERSION_V1_REF
              ? "Protocol v1.0 Initial Release"
              : "Protocol v2.0 Active Release",
          code: nodeRef === VERSION_V1_REF ? "v1.0" : "v2.0",
          status: "PUBLISHED",
          mask: null,
        };
      }
      if (String(nodeRef).startsWith("study_site:")) {
        const siteCode = resolveSiteCodeFromRef(
          nodeRef,
          state.subjects,
          state.studySites
        );
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "study_site",
            title:
              siteCode === "SITE-102"
                ? "US Regional Trial Unit 2"
                : `US Academic Clinical Research Center (${siteCode})`,
            code: siteCode,
            status: "ACTIVE",
            mask: null,
          },
          caller,
          { siteCode }
        );
      }
      if (String(nodeRef).startsWith("enrollment:")) {
        const rawId = String(nodeRef)
          .replace(/^enrollment:/, "")
          .split("@")[0];
        const subj = state.subjects.find((s) => s.id === rawId);
        if (subj) {
          return applyNodeVisibilityMask(
            {
              ref: nodeRef,
              kind: "enrollment",
              title: `Participant ${subj.subject_id}`,
              code: subj.subject_id,
              status: subj.status,
              mask: null,
            },
            caller,
            { siteCode: subj.site_id }
          );
        }
      }
      if (String(nodeRef).startsWith("collection_instance:")) {
        const rawFormId = String(nodeRef)
          .replace(/^collection_instance:/, "")
          .split("@")[0];
        const form =
          state.forms.get(rawFormId) || state.forms.get(DEFAULT_FORM_UUID);
        if (form) {
          return applyNodeVisibilityMask(
            {
              ref: nodeRef,
              kind: "collection_instance",
              title: `${form.collection_title || resolveCollectionTitle(form.collection_spec_id) || form.collection_spec_id}${form.planned_activity_id ? ` (${form.planned_activity_id})` : ""}`,
              code: form.collection_spec_id,
              status: form.complete ? "COMPLETE" : "OPEN",
              mask: null,
            },
            caller
          );
        }
      }
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: String(nodeRef).split(":")[0] || "node",
          title: String(nodeRef),
          code: String(nodeRef).split(":")[1]?.slice(-6) || "DEMO",
          status: "ACTIVE",
          mask: null,
        },
        caller
      );
    },

    getZoomPath(nodeRef, caller = null) {
      const refStr = String(nodeRef || "");
      if (refStr.startsWith("enrollment:")) {
        const rawId = refStr.replace(/^enrollment:/, "").split("@")[0];
        const subj = state.subjects.find((s) => s.id === rawId);
        const study = subj
          ? getStudyByRefOrCode(subj.study_id)
          : state.studies.get("DEMO-01");
        const siteCode = subj?.site_id || "SITE-101";
        const sitePermitted = !caller || isSitePermitted(caller, siteCode);
        return [
          {
            level: "study",
            label: study.code || study.title,
            ref: study.ref,
            mask: null,
          },
          {
            level: "study_site",
            label: sitePermitted ? siteCode : null,
            ref: `study_site:${resolveSiteUuid(siteCode)}`,
            mask: sitePermitted ? null : "NotPermitted",
          },
          {
            level: "enrollment",
            label: sitePermitted ? subj?.subject_id || rawId.slice(-6) : null,
            ref: refStr,
            mask: sitePermitted ? null : "NotPermitted",
          },
        ];
      }
      if (refStr.startsWith("study_site:")) {
        const siteCode = resolveSiteCodeFromRef(
          refStr,
          state.subjects,
          state.studySites
        );
        const study = state.studies.get("DEMO-01");
        const sitePermitted = !caller || isSitePermitted(caller, siteCode);
        return [
          {
            level: "study",
            label: study.code || study.title,
            ref: study.ref,
            mask: null,
          },
          {
            level: "study_site",
            label: sitePermitted ? siteCode : null,
            ref: refStr,
            mask: sitePermitted ? null : "NotPermitted",
          },
        ];
      }
      const study = refStr.startsWith("study:")
        ? getStudyByRefOrCode(refStr)
        : state.studies.get("DEMO-01");
      const steps = [
        {
          level: "study",
          label: study.code || study.title,
          ref: study.ref,
          mask: null,
        },
      ];
      if (
        nodeRef !== study.ref &&
        refStr.split("@")[0] !== study.ref.split("@")[0]
      ) {
        const node = methods.getNode(nodeRef, caller);
        steps.push({
          level: node.kind,
          label: node.mask ? null : node.title || node.code,
          ref: node.ref,
          mask: node.mask ?? null,
        });
      }
      return steps;
    },

    getChildren(nodeRef, caller = null) {
      if (String(nodeRef).startsWith("study:")) {
        const study = getStudyByRefOrCode(nodeRef);
        const studySubjects = state.subjects.filter(
          (s) => s.study_id.toUpperCase() === study.code.toUpperCase()
        );
        const effectiveSubjects = study.isCustomCreated
          ? studySubjects
          : studySubjects.length > 0
            ? studySubjects
            : state.subjects;
        const linkedStudySites =
          state.studySites.get(study.code.toUpperCase()) || [];
        const linkedByCode = new Map(
          linkedStudySites
            .filter((item) => item.site_code)
            .map((item) => [item.site_code, item])
        );
        const defaultCodes = study.isCustomCreated
          ? []
          : ["SITE-101", "SITE-102"];
        const siteCodes = [
          ...new Set([
            ...defaultCodes,
            ...linkedByCode.keys(),
            ...effectiveSubjects.map((s) => s.site_id).filter(Boolean),
          ]),
        ];
        const siteNodes = siteCodes.map((siteCode) => {
          const siteSubjects = effectiveSubjects.filter(
            (s) => (s.site_id || "SITE-101") === siteCode
          );
          const linkedMeta = linkedByCode.get(siteCode);
          return {
            node: applyNodeVisibilityMask(
              {
                ref: linkedMeta?.ref
                  ? linkedMeta.ref.split("@")[0]
                  : `study_site:${resolveSiteUuid(siteCode)}`,
                kind: "study_site",
                title:
                  linkedMeta?.site_name ||
                  (siteCode === "SITE-102"
                    ? "US Regional Trial Unit 2"
                    : `US Academic Clinical Research Center (${siteCode})`),
                code: siteCode,
                status: linkedMeta?.status || "ACTIVE",
                mask: null,
              },
              caller,
              { siteCode }
            ),
            child_count: siteSubjects.length,
            group: "sites",
          };
        });
        return [
          {
            node: {
              ref: VERSION_V2_REF,
              kind: "protocol_version",
              title: "Protocol Version 2.0",
              code: "v2.0",
              status: "PUBLISHED",
              mask: null,
            },
            child_count: 4,
            group: "design",
          },
          ...siteNodes,
          ...effectiveSubjects.map((s) => ({
            node: applyNodeVisibilityMask(
              {
                ref: `enrollment:${s.id}`,
                kind: "enrollment",
                title: `Participant ${s.subject_id}`,
                code: s.subject_id,
                status: s.status,
                mask: null,
              },
              caller,
              { siteCode: s.site_id || "SITE-101" }
            ),
            child_count: 1,
            group: "enrollments",
          })),
        ];
      }
      if (String(nodeRef).startsWith("study_site:")) {
        const siteCode = resolveSiteCodeFromRef(
          nodeRef,
          state.subjects,
          state.studySites
        );
        const siteSubjects = state.subjects.filter(
          (s) =>
            String(s.site_id || "SITE-101").toUpperCase() ===
            siteCode.toUpperCase()
        );
        return siteSubjects.map((s) => ({
          node: applyNodeVisibilityMask(
            {
              ref: `enrollment:${s.id}`,
              kind: "enrollment",
              title: `Participant ${s.subject_id}`,
              code: s.subject_id,
              status: s.status,
              mask: null,
            },
            caller,
            { siteCode }
          ),
          child_count: 1,
          group: "enrollments",
        }));
      }
      if (String(nodeRef).startsWith("enrollment:")) {
        const enrollmentId = String(nodeRef)
          .replace(/^enrollment:/, "")
          .split("@")[0];
        const subj = state.subjects.find((s) => s.id === enrollmentId) || null;
        const siteCode = subj?.site_id || null;
        let forms = [...state.forms.values()].filter(
          (f) => f.enrollment_id === enrollmentId
        );
        if (forms.length === 0) {
          const fallback = state.forms.get(DEFAULT_FORM_UUID);
          if (fallback) forms = [fallback];
        }
        const formChildren = forms.map((f) => ({
          node: applyNodeVisibilityMask(
            {
              ref: `collection_instance:${f.form_id}`,
              kind: "collection_instance",
              title: `${f.collection_title || resolveCollectionTitle(f.collection_spec_id) || f.collection_spec_id}${f.planned_activity_id ? ` (${f.planned_activity_id})` : ""}`,
              code: f.collection_spec_id,
              status: f.complete ? "COMPLETE" : "OPEN",
              mask: null,
            },
            caller,
            { siteCode }
          ),
          child_count: Array.isArray(f.items) ? f.items.length : 0,
          group: f.planned_activity_id || "forms",
        }));
        if (subj?.treatment_group) {
          formChildren.push({
            node: applyNodeVisibilityMask(
              {
                ref: `allocation:${enrollmentId}`,
                kind: "allocation",
                title: `Treatment Allocation · ${subj.treatment_group}`,
                code: subj.treatment_group,
                status: "ALLOCATED",
                mask: null,
              },
              caller,
              {
                siteCode,
                isAllocation: true,
                subjectStatus: subj.status,
              }
            ),
            child_count: 0,
            group: "allocation",
          });
        }
        return formChildren;
      }
      if (String(nodeRef).startsWith("collection_instance:")) {
        const formId = String(nodeRef)
          .replace(/^collection_instance:/, "")
          .split("@")[0];
        const form =
          state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
        if (!form || !Array.isArray(form.items)) return [];
        const siteCode = resolveSiteCodeForNodeRef(nodeRef);
        return form.items
          .filter((it) => it.observation_id)
          .map((it) => ({
            node: applyNodeVisibilityMask(
              {
                ref: `observation:${it.observation_id}@${Math.max(1, Number(it.revision || 1))}`,
                kind: "observation",
                title: `${it.concept || it.item_id}: ${it.value?.value ?? "not recorded"}${it.unit ? ` ${it.unit}` : ""}`,
                code: it.item_id,
                status: `r${Math.max(1, Number(it.revision || 1))}`,
                mask: null,
              },
              caller,
              { siteCode }
            ),
            child_count: 0,
            group: null,
          }));
      }
      return [];
    },

    getCommands(nodeRef) {
      return [
        {
          command: "collection.correct",
          decision: buildDecision("collection.correct", nodeRef, true),
        },
        {
          command: "execution.site.activate",
          decision: buildDecision("execution.site.activate", nodeRef, true),
        },
      ];
    },

    listStudies() {
      return [...state.studies.values()].map((study) => ({
        ref: study.ref,
        code: study.code,
        title: study.title,
        phase: study.phase || "Phase 2",
        sponsor_ref: study.sponsor_ref || DEMO_ORGANIZATIONS[0].ref,
        sponsor_code: study.sponsor_code || DEMO_ORGANIZATIONS[0].code,
        sponsor_name: study.sponsor_name || DEMO_ORGANIZATIONS[0].name,
        site_count: study.site_count ?? 0,
      }));
    },

    listOrganizations(kind = "") {
      const filterKind = String(kind || "").trim();
      const orgs = filterKind
        ? state.organizations.filter(
            (org) =>
              String(org.kind || "").toLowerCase() === filterKind.toLowerCase()
          )
        : state.organizations;
      return cloneJson(orgs);
    },

    createOrganization(body = {}, { reason = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Missing X-Change-Reason",
              status: 422,
              code: "K.Audit.ReasonRequired",
              detail:
                "Header 'X-Change-Reason' is required when registering an organization.",
            },
          },
        };
      }
      const name = String(body.name || "").trim();
      if (!name) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Invalid organization request",
              status: 422,
              code: "K.Identity.NameRequired",
              detail: "Organization name is required.",
            },
          },
        };
      }
      const kind =
        String(body.kind || "SiteInstitution").trim() || "SiteInstitution";
      const derivedCode =
        String(body.code || "")
          .trim()
          .toUpperCase() ||
        `INST-${name
          .toUpperCase()
          .replace(/[^A-Z0-9]+/g, "-")
          .replace(/^-+|-+$/g, "")
          .slice(0, 16)}`;
      if (
        state.organizations.some(
          (o) => o.code.toUpperCase() === derivedCode.toUpperCase()
        )
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Duplicate organization code",
              status: 409,
              code: "K.Node.AlreadyExists",
              detail: `Organization '${derivedCode}' already exists.`,
            },
          },
        };
      }
      const idx = nextCounter();
      const created = {
        ref: `organization:${makeUuidFromIndex("00000310", idx)}@1`,
        code: derivedCode,
        name,
        kind,
      };
      state.organizations.push(created);
      return { data: cloneJson(created) };
    },

    listSites() {
      return cloneJson(
        [...state.sites].sort((a, b) => a.code.localeCompare(b.code))
      );
    },

    createSite(body = {}, { reason = "", idempotencyKey = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Missing X-Change-Reason",
              status: 422,
              code: "K.Audit.ReasonRequired",
              detail:
                "Header 'X-Change-Reason' is required when registering a site.",
            },
          },
        };
      }
      const trimmedKey = String(idempotencyKey || "").trim();
      if (trimmedKey && state.idempotentSiteResponses?.has(trimmedKey)) {
        return {
          data: cloneJson(state.idempotentSiteResponses.get(trimmedKey)),
          replayed: true,
        };
      }

      const siteCode = String(body.site_code || "")
        .trim()
        .toUpperCase();
      const name = String(body.name || "").trim();
      const country = String(body.country || "")
        .trim()
        .toUpperCase();
      if (!siteCode || !name || !/^[A-Z]{2}$/.test(country)) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Invalid site registration request",
              status: 422,
              code: "K.Study.CountryInvalid",
              detail:
                "Site code, name, and a two-letter ISO country code are required.",
            },
          },
        };
      }

      if (
        state.sites.some((s) => s.code.toUpperCase() === siteCode.toUpperCase())
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Duplicate site code",
              status: 409,
              code: "K.Study.SiteCodeTaken",
              detail: `A site with code '${siteCode}' already exists in this tenant.`,
            },
          },
        };
      }

      let org = state.organizations.find(
        (o) =>
          (body.institution_ref &&
            o.ref.split("@")[0] ===
              String(body.institution_ref).split("@")[0]) ||
          (body.institution_code &&
            o.code.toUpperCase() ===
              String(body.institution_code).trim().toUpperCase())
      );
      if (org && org.kind !== "SiteInstitution") {
        if (body.institution_ref || body.institution_code) {
          return {
            error: {
              status: 422,
              problem: {
                title: "Invalid site institution kind",
                status: 422,
                code: "K.Study.OrganizationKindMismatch",
                detail: `Organization '${org.code}' has kind '${org.kind}', expected 'SiteInstitution'.`,
              },
            },
          };
        }
        org = null;
      }

      if (!org) {
        const instName = String(body.institution_name || "").trim();
        if (!instName && !String(body.institution_code || "").trim()) {
          return {
            error: {
              status: 422,
              problem: {
                title: "Missing site institution",
                status: 422,
                code: "K.Study.MissingInstitution",
                detail:
                  "Select an existing SiteInstitution or provide institution_name.",
              },
            },
          };
        }
        const derivedBase = (
          String(body.institution_code || "").trim() ||
          instName.replace(/[^A-Za-z0-9]+/g, "-").replace(/^-+|-+$/g, "")
        ).toUpperCase();
        const codeTaken = state.organizations.some(
          (o) => o.code.toUpperCase() === derivedBase
        );
        const finalOrgCode = codeTaken ? `${derivedBase}-SITE` : derivedBase;
        const orgIdx = nextCounter();
        org = {
          ref: `organization:${makeUuidFromIndex("00000310", orgIdx)}@1`,
          code: finalOrgCode,
          name: instName || finalOrgCode,
          kind: "SiteInstitution",
        };
        state.organizations.push(org);
      }

      const idx = nextCounter();
      const created = {
        ref: `site:${makeUuidFromIndex("00000220", idx)}@1`,
        code: siteCode,
        name,
        country,
        is_active: true,
        institution_ref: org.ref,
        institution_code: org.code,
        institution_name: org.name,
        linked_study_count: 0,
      };
      state.sites.push(created);
      if (trimmedKey && state.idempotentSiteResponses) {
        state.idempotentSiteResponses.set(trimmedKey, cloneJson(created));
      }
      return { data: cloneJson(created) };
    },

    listStudySites(studyRefOrCode = "") {
      const clean = String(studyRefOrCode || "").trim();
      const unversioned = clean.split("@")[0];
      let matchedStudy = null;
      for (const candidate of state.studies.values()) {
        if (
          candidate.code.toUpperCase() === clean.toUpperCase() ||
          String(candidate.ref).split("@")[0] === unversioned
        ) {
          matchedStudy = candidate;
          break;
        }
      }
      if (!matchedStudy) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Study not found",
              status: 404,
              code: "K.Study.NotFound",
              detail: `No visible study '${clean}' exists in this tenant.`,
            },
          },
        };
      }
      const list =
        state.studySites.get(matchedStudy.code.toUpperCase()) ||
        state.studySites.get(matchedStudy.code) ||
        [];
      return {
        data: cloneJson(
          [...list].sort((a, b) =>
            String(a.site_code || "").localeCompare(String(b.site_code || ""))
          )
        ),
      };
    },

    linkSiteToStudy(
      studyRefOrCode = "",
      body = {},
      { reason = "", idempotencyKey = "" } = {}
    ) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 422,
            problem: {
              title: "Missing X-Change-Reason",
              status: 422,
              code: "K.Audit.ReasonRequired",
              detail:
                "Header 'X-Change-Reason' is required when linking a site to a study.",
            },
          },
        };
      }
      const trimmedKey = String(idempotencyKey || "").trim();
      if (trimmedKey && state.idempotentStudySiteResponses?.has(trimmedKey)) {
        return {
          data: cloneJson(state.idempotentStudySiteResponses.get(trimmedKey)),
          replayed: true,
        };
      }

      const clean = String(studyRefOrCode || "").trim();
      const unversioned = clean.split("@")[0];
      let matchedStudy = null;
      for (const candidate of state.studies.values()) {
        if (
          candidate.code.toUpperCase() === clean.toUpperCase() ||
          String(candidate.ref).split("@")[0] === unversioned
        ) {
          matchedStudy = candidate;
          break;
        }
      }
      if (!matchedStudy) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Study not found",
              status: 404,
              code: "K.Study.NotFound",
              detail: `No visible study '${clean}' exists in this tenant.`,
            },
          },
        };
      }

      const reqRef = String(body.site_ref || "")
        .trim()
        .split("@")[0];
      const reqCode = String(body.site_code || "").trim();
      let site = state.sites.find(
        (candidate) =>
          (reqRef && candidate.ref.split("@")[0] === reqRef) ||
          (reqCode && candidate.code.toUpperCase() === reqCode.toUpperCase())
      );
      if (
        !site &&
        !body.site_ref &&
        body.site_code &&
        (body.site_name || body.country || body.institution_ref)
      ) {
        const regIdx = nextCounter();
        const code = String(body.site_code).trim();
        site = {
          ref: `site:${makeUuidFromIndex("00000310", regIdx)}@1`,
          code,
          name: String(body.site_name || code).trim(),
          country: String(body.country || "US")
            .trim()
            .toUpperCase(),
          is_active: true,
          institution_ref:
            body.institution_ref ||
            matchedStudy.sponsor_ref ||
            DEMO_ORGANIZATIONS[0].ref,
          institution_code:
            matchedStudy.sponsor_code || DEMO_ORGANIZATIONS[0].code,
          institution_name:
            matchedStudy.sponsor_name || DEMO_ORGANIZATIONS[0].name,
          linked_study_count: 1,
        };
        state.sites.push(site);
      }
      if (!site) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Site not found",
              status: 404,
              code: "K.Study.SiteUnknown",
              detail:
                "The specified clinical site was not found in this tenant.",
            },
          },
        };
      }

      const key = matchedStudy.code.toUpperCase();
      const existingList =
        state.studySites.get(key) ||
        state.studySites.get(matchedStudy.code) ||
        [];
      if (
        existingList.some(
          (entry) => entry.site_code?.toUpperCase() === site.code.toUpperCase()
        )
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Site already linked",
              status: 409,
              code: "K.Study.AlreadyLinked",
              detail: `Site '${site.code}' is already linked to '${matchedStudy.code}'.`,
            },
          },
        };
      }

      const idx = nextCounter();
      const studySiteUuid = makeUuidFromIndex("00000410", idx);
      const studySite = {
        ref: `study_site:${studySiteUuid}@1`,
        study_site_id: studySiteUuid,
        study_site_ref: `study_site:${studySiteUuid}`,
        study_ref: String(matchedStudy.ref).split("@")[0],
        study_code: matchedStudy.code,
        site_ref: site.ref,
        site_code: site.code,
        site_name: site.name,
        country: site.country,
        institution_ref: site.institution_ref,
        institution_code: site.institution_code,
        institution_name: site.institution_name,
        status: "Selected",
        is_active: true,
        effective_versions: [],
        mask: null,
      };
      const nextLinks = [...existingList, studySite].sort((a, b) =>
        String(a.site_code || "").localeCompare(String(b.site_code || ""))
      );
      state.studySites.set(key, nextLinks);
      matchedStudy.site_count = nextLinks.length;
      if (trimmedKey && state.idempotentStudySiteResponses) {
        state.idempotentStudySiteResponses.set(
          trimmedKey,
          cloneJson(studySite)
        );
      }
      return { data: cloneJson(studySite) };
    },

    createStudy(body = {}, { reason = "", idempotencyKey = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Missing X-Change-Reason",
              status: 400,
              code: "Kernel.Audit.MissingReasonForChange",
              detail:
                "Header 'X-Change-Reason' is required when creating a study.",
            },
          },
        };
      }
      if (
        idempotencyKey &&
        state.idempotentStudyResponses.has(idempotencyKey)
      ) {
        return {
          data: cloneJson(state.idempotentStudyResponses.get(idempotencyKey)),
          replayed: true,
        };
      }

      const code = String(body.study_code || "").trim();
      const title = String(body.title || "").trim();
      const phase = String(body.phase || "").trim();
      if (!code || !title || !phase) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Invalid study creation request",
              status: 400,
              code: "Kernel.Study.InvalidRequest",
              detail: "Study code, title, and clinical phase are required.",
            },
          },
        };
      }

      for (const existing of state.studies.values()) {
        if (existing.code.toUpperCase() === code.toUpperCase()) {
          return {
            error: {
              status: 409,
              problem: {
                title: "Duplicate study code",
                status: 409,
                code: "Kernel.Study.DuplicateCode",
                detail: `A study with code '${code}' already exists in this tenant.`,
              },
            },
          };
        }
      }

      let org = state.organizations.find(
        (o) =>
          (body.sponsor_ref && o.ref === body.sponsor_ref) ||
          (body.sponsor_code &&
            o.code.toUpperCase() === String(body.sponsor_code).toUpperCase())
      );
      if (!org) {
        if (!String(body.sponsor_code || "").trim()) {
          return {
            error: {
              status: 400,
              problem: {
                title: "Missing sponsor",
                status: 400,
                code: "Kernel.Study.MissingSponsor",
                detail: "Select an existing sponsor or provide a sponsor code.",
              },
            },
          };
        }
        const orgIdx = nextCounter();
        org = {
          ref: `organization:${makeUuidFromIndex("00000300", orgIdx)}@1`,
          code: String(body.sponsor_code).trim().toUpperCase(),
          name:
            String(body.sponsor_name || "").trim() ||
            String(body.sponsor_code).trim(),
          kind: "Sponsor",
        };
        state.organizations.push(org);
      }

      const idx = nextCounter();
      const created = {
        ref: `study:${makeUuidFromIndex("00000020", idx)}@1`,
        kind: "study",
        code,
        title,
        phase,
        sponsor_ref: org.ref,
        sponsor_code: org.code,
        sponsor_name: org.name,
        site_count: 0,
        status: "ACTIVE",
        mask: null,
        isCustomCreated: true,
      };
      state.studies.set(code, created);
      state.studySites.set(code.toUpperCase(), []);
      let linkedSite = null;
      if (
        body.link_sandbox_site ||
        body.initial_site_ref ||
        body.initial_site_code
      ) {
        const candidateSite =
          state.sites.find(
            (s) =>
              (body.initial_site_ref &&
                s.ref.split("@")[0] ===
                  String(body.initial_site_ref).split("@")[0]) ||
              (body.initial_site_code &&
                s.code.toUpperCase() ===
                  String(body.initial_site_code).trim().toUpperCase())
          ) || state.sites[0];
        if (candidateSite) {
          const linkRes = methods.linkSiteToStudy(
            code,
            { site_ref: candidateSite.ref },
            { reason }
          );
          if (linkRes.data) {
            linkedSite = linkRes.data;
          }
        }
      }
      recordActivity(
        "kernel.study.create",
        "protocol",
        "study",
        created.ref,
        "usr-demo-operator"
      );

      const response = {
        ref: created.ref,
        code: created.code,
        title: created.title,
        phase: created.phase,
        sponsor_ref: created.sponsor_ref,
        sponsor_code: created.sponsor_code,
        sponsor_name: created.sponsor_name,
        site_count: created.site_count,
        linked_site: linkedSite,
      };
      if (idempotencyKey) {
        state.idempotentStudyResponses.set(idempotencyKey, cloneJson(response));
      }
      return { data: response };
    },

    getLineage(nodeRef, direction = "upstream", depth = 3, caller = null) {
      const now = new Date().toISOString();
      const startNode = methods.getNode(nodeRef, caller);
      return {
        start: nodeRef,
        direction,
        depth,
        valid_at: now,
        known_at: now,
        nodes: [
          {
            handle: "n0",
            kind: String(nodeRef).split(":")[0] || "node",
            ref: nodeRef,
            mask: startNode?.mask ?? null,
            distance: 0,
          },
          {
            handle: "n1",
            kind: "protocol_version",
            ref: VERSION_V2_REF,
            mask: null,
            distance: 1,
          },
          {
            handle: "n2",
            kind: "study",
            ref: STUDY_REF,
            mask: null,
            distance: 2,
          },
        ],
        edges: [
          {
            from: "n0",
            to: "n1",
            kind: "governed_by",
            activity: null,
            recorded_at: "2026-08-01T09:00:00Z", // deid-ignore
            valid_from: "2026-08-01T09:00:00Z", // deid-ignore
          },
          {
            from: "n1",
            to: "n2",
            kind: "belongs_to",
            activity: null,
            recorded_at: "2026-07-01T09:00:00Z", // deid-ignore
            valid_from: "2026-07-01T09:00:00Z", // deid-ignore
          },
        ],
        truncated: false,
      };
    },

    getDependents(nodeRef, depth = 3, caller = null) {
      const now = new Date().toISOString();
      const groups = [
        {
          kind: "enrollment",
          count: state.subjects.length,
          objects: state.subjects.map((subj, index) => {
            const sitePermitted =
              !caller || isSitePermitted(caller, subj.site_id || "SITE-101");
            return {
              handle: `dep-${index}`,
              kind: "enrollment",
              ref: `enrollment:${subj.id}`,
              mask: sitePermitted ? null : "NotPermitted",
              distance: 1,
            };
          }),
        },
      ];
      const allocatedSubjects = state.subjects.filter(
        (subj) => subj.treatment_group
      );
      if (allocatedSubjects.length > 0) {
        groups.push({
          kind: "allocation",
          count: allocatedSubjects.length,
          objects: allocatedSubjects.map((subj, index) => {
            const sitePermitted =
              !caller || isSitePermitted(caller, subj.site_id || "SITE-101");
            const blinded =
              caller &&
              isCallerBlinded(caller) &&
              String(subj.status || "").toUpperCase() !== "UNBLINDED";
            return {
              handle: `alloc-${index}`,
              kind: "allocation",
              ref: `allocation:${subj.id}`,
              mask: !sitePermitted
                ? "NotPermitted"
                : blinded
                  ? "Blinded"
                  : null,
              distance: 2,
            };
          }),
        });
      }
      return {
        start: nodeRef,
        depth,
        valid_at: now,
        known_at: now,
        groups,
        total: groups.reduce((sum, g) => sum + g.count, 0),
        truncated: false,
      };
    },

    listProposals() {
      return cloneJson([...state.proposals.values()]);
    },

    getProposal(proposalRef) {
      return cloneJson(
        state.proposals.get(proposalRef) || [...state.proposals.values()][0]
      );
    },

    rejectProposal(proposalRef, reason = "Rejected in demo review") {
      const existing =
        state.proposals.get(proposalRef) || [...state.proposals.values()][0];
      const now = new Date().toISOString();
      existing.status = "REJECTED";
      existing.decided_at = now;
      existing.decided_by = "usr-demo-operator";
      existing.decision_reason = reason;
      recordActivity(
        "proposal.rejected",
        "governance",
        "proposal",
        existing.ref,
        "usr-demo-operator"
      );
      return {
        ref: existing.ref,
        status: existing.status,
        content_hash: existing.content_hash,
        decided_at: now,
        decided_by: existing.decided_by,
      };
    },

    getActivityPage({ limit = 25 } = {}) {
      return {
        entries: cloneJson(state.activityEntries.slice(0, Number(limit) || 25)),
        next: null,
      };
    },

    getObservation(observationRef, scopeHint = {}) {
      const normalized = String(observationRef).startsWith("observation:")
        ? String(observationRef)
        : `observation:${observationRef}`;
      const canonicalEnrollmentUuid =
        DEMO_PARTICIPANTS[0]?.enrollmentUuid || ENROLLMENT_1001_UUID;
      let obs = state.observations.get(normalized);
      if (!obs) {
        obs = {
          ref: normalized,
          enrollment_ref: `enrollment:${canonicalEnrollmentUuid}`,
          concept: "SYSBP",
          value: {
            type: "quantity",
            value: "124",
            unit: "mm[Hg]",
            comparator: null,
          },
          observed_at: "2026-08-01T09:15:00Z", // deid-ignore
          version: 1,
          locks: [],
          revisions: [
            {
              number: 1,
              value: {
                type: "quantity",
                value: "124",
                unit: "mm[Hg]",
                comparator: null,
              },
              reason_for_change: "Initial observation capture",
              recorded_by: "usr-crc-01",
              recorded_at: "2026-08-01T09:15:00Z", // deid-ignore
              recorded_until: null,
            },
          ],
          assertions: [],
          audit: [
            {
              sequence: 1,
              recorded_at: "2026-08-01T09:15:00Z", // deid-ignore
              actor_id: "usr-crc-01",
              action: "observation.record",
              subject: normalized,
              version: 1,
              reason_for_change: "Initial observation capture",
            },
          ],
        };
        state.observations.set(normalized, obs);
      }
      const result = cloneJson(obs);
      const hintStudy = String(scopeHint?.studyCode || "").trim();
      const hintSite = String(scopeHint?.siteCode || "").trim();
      const hintSubject = String(scopeHint?.subjectCode || "").trim();
      if (hintStudy || hintSite || hintSubject) {
        const matchedSubject = (state.subjects || []).find((subj) => {
          if (
            hintStudy &&
            String(subj.study_id || "").toUpperCase() !==
              hintStudy.toUpperCase()
          ) {
            return false;
          }
          if (
            hintSite &&
            String(subj.site_id || "").toUpperCase() !== hintSite.toUpperCase()
          ) {
            return false;
          }
          if (hintSubject && subj.subject_id !== hintSubject) {
            return false;
          }
          return true;
        });
        if (matchedSubject) {
          result.enrollment_ref = `enrollment:${matchedSubject.id}`;
        }
      }
      return result;
    },

    correctObservation(observationRef, body, reasonForChange) {
      const normalized = String(observationRef).startsWith("observation:")
        ? String(observationRef)
        : `observation:${observationRef}`;
      const existing = state.observations.get(normalized);
      if (!existing) {
        methods.getObservation(normalized);
      }
      const target = state.observations.get(normalized);
      const expectedVersion = Number(body?.expected_version ?? target.version);
      if (expectedVersion !== Number(target.version)) {
        return {
          conflict: true,
          problem: {
            title: "Observation version conflict",
            status: 409,
            code: "K.Collection.RevisionStale",
            detail: `Expected version ${expectedVersion} but observation is at version ${target.version}.`,
          },
        };
      }

      const now = new Date().toISOString();
      const nextRevision = target.revisions.length + 1;
      const nextVersion = Number(target.version) + 1;
      const prevRev = target.revisions[target.revisions.length - 1];
      if (prevRev && !prevRev.recorded_until) {
        prevRev.recorded_until = now;
      }

      const nextValue = cloneJson(body.value);
      target.value = nextValue;
      target.version = nextVersion;
      target.revisions.push({
        number: nextRevision,
        value: cloneJson(nextValue),
        reason_for_change: reasonForChange || "Clinical observation correction",
        recorded_by: "usr-crc-01",
        recorded_at: now,
        recorded_until: null,
      });
      target.audit.push({
        sequence: target.audit.length + 1,
        recorded_at: now,
        actor_id: "usr-crc-01",
        action: "observation.correct",
        subject: normalized,
        version: nextVersion,
        reason_for_change: reasonForChange || "Clinical observation correction",
      });

      const rawObsUuid = normalized.replace(/^observation:/, "");
      for (const form of state.forms.values()) {
        let touched = false;
        for (const item of form.items) {
          if (item.observation_id === rawObsUuid) {
            item.value = cloneJson(nextValue);
            item.revision = nextRevision;
            item.recorded_at = now;
            item.recorded_by = "usr-crc-01";
            touched = true;
          }
        }
        if (touched) {
          form.version = Number(form.version) + 1;
          form.content_hash = computeContentHash(form.items);
          for (const sig of form.signatures) {
            sig.superseded = true;
          }
        }
      }

      for (const query of state.queries.values()) {
        if (query.observation_id === rawObsUuid) {
          query.current_revision = nextRevision;
          query.value_changed = nextRevision > Number(query.revision);
        }
      }

      recordActivity(
        "observation.corrected",
        "collection",
        "observation",
        normalized,
        "usr-crc-01"
      );

      return {
        conflict: false,
        data: {
          ref: normalized,
          enrollment_ref: body?.enrollment_ref || target.enrollment_ref,
          value: cloneJson(nextValue),
          version: nextVersion,
          revision: nextRevision,
        },
      };
    },

    verifyObservation(
      observationRef,
      body,
      reason = "Source data verified in demo session"
    ) {
      const rawObsRef = String(observationRef).split("@")[0];
      const normalized = rawObsRef.startsWith("observation:")
        ? rawObsRef
        : `observation:${rawObsRef}`;
      if (!state.observations.has(normalized)) {
        methods.getObservation(normalized);
      }
      const target = state.observations.get(normalized);
      const currentRev =
        target.revisions[target.revisions.length - 1]?.number ?? 1;
      const requestedRev = Number(body?.revision ?? currentRev);
      if (requestedRev !== Number(currentRev)) {
        return {
          conflict: true,
          problem: {
            title: "Stale observation revision",
            status: 409,
            code: "K.Assertion.RevisionStale",
            detail: `Observation is at revision ${currentRev}, requested ${requestedRev}.`,
          },
        };
      }
      const idx = nextCounter();
      const assertionUuid =
        body?.assertion_id || makeUuidFromIndex("00008000", idx);
      const assertionRef = `assertion:${assertionUuid}`;
      const now = new Date().toISOString();
      const assertionEntry = {
        assertion_id: assertionUuid,
        kind: "Verified",
        target: normalized,
        revision: requestedRev,
        text: null,
        asserted_by: "usr-cra-01",
        asserted_at: now,
        reason,
        retracted_at: null,
      };
      target.assertions.push({
        ref: assertionRef,
        kind: "Verified",
        target: normalized,
        revision: requestedRev,
        retracts: null,
        asserted_at: now,
        asserted_by: "usr-cra-01",
        retracted_at: null,
      });
      const rawObsUuid = normalized.replace(/^observation:/, "");
      for (const form of state.forms.values()) {
        if (form.items.some((it) => it.observation_id === rawObsUuid)) {
          form.assertions.push(cloneJson(assertionEntry));
        }
      }
      recordActivity(
        "observation.verified",
        "quality",
        "assertion",
        assertionRef,
        "usr-cra-01"
      );
      return {
        conflict: false,
        data: {
          ref: assertionRef,
          kind: "Verified",
          target: normalized,
          revision: requestedRev,
          retracts: null,
          asserted_at: now,
          asserted_by: "usr-cra-01",
        },
      };
    },

    retractAssertion(
      assertionRef,
      body,
      reason = "Verification withdrawn in demo session"
    ) {
      const normalized = String(assertionRef).startsWith("assertion:")
        ? String(assertionRef)
        : `assertion:${assertionRef}`;
      const rawUuid = normalized.replace(/^assertion:/, "");
      const now = new Date().toISOString();
      let targetObsRef = `observation:${OBS_1_UUID}`;

      for (const obs of state.observations.values()) {
        for (const a of obs.assertions) {
          if (a.ref === normalized || a.assertion_id === rawUuid) {
            if (a.retracted_at) {
              return {
                conflict: true,
                problem: {
                  title: "Assertion already retracted",
                  status: 409,
                  code: "K.Assertion.AlreadyRetracted",
                  detail: `Assertion ${normalized} was already retracted.`,
                },
              };
            }
            a.retracted_at = now;
            targetObsRef = a.target || obs.ref;
          }
        }
      }

      for (const form of state.forms.values()) {
        for (const a of form.assertions) {
          if (a.assertion_id === rawUuid) {
            a.retracted_at = now;
            targetObsRef = a.target || targetObsRef;
          }
        }
      }

      const idx = nextCounter();
      const retractionId =
        body?.assertion_id || makeUuidFromIndex("00008100", idx);
      const retractionRef = `assertion:${retractionId}`;
      recordActivity(
        "assertion.retracted",
        "quality",
        "assertion",
        retractionRef,
        "usr-cra-01",
        reason
      );
      return {
        conflict: false,
        data: {
          ref: retractionRef,
          kind: "Verified",
          target: targetObsRef,
          revision: null,
          retracts: normalized,
          asserted_at: now,
          asserted_by: "usr-cra-01",
        },
      };
    },

    listStudyRulePacks(studyRef) {
      const study = getStudyByRefOrCode(studyRef);
      const cleanStudyRef = String(study?.ref || studyRef || STUDY_REF).split(
        "@"
      )[0];
      const pinnedSet = new Set(getPinnedRulePackRefsForStudy(study.code));
      const packs = state.rulePacks.map((pack) => ({
        ...cloneJson(pack),
        pinned: pinnedSet.has(pack.ref),
      }));
      return {
        study_ref: cleanStudyRef,
        profile_ref: `standards_profile:${cleanStudyRef.replace(/^study:/, "")}`,
        pinned_rule_pack_refs: [...pinnedSet],
        rule_packs: packs,
      };
    },

    previewStudyRulePack(studyRef, itemRef) {
      const cleanItemRef = String(itemRef || "").split("@")[0];
      const normalizedRef = cleanItemRef.startsWith("library_item:")
        ? cleanItemRef
        : `library_item:${cleanItemRef}`;
      const pack = state.rulePacks.find(
        (p) => p.ref === normalizedRef || p.key === cleanItemRef
      );
      if (!pack) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Rule pack not found",
              status: 404,
              code: "K.Node.NotFound",
              detail: `Library item '${itemRef}' was not found.`,
            },
          },
        };
      }
      const formsList = [...state.forms.values()];
      const violations = [];
      for (const form of formsList) {
        const warnings = evaluateVitalSignsOverlayWarnings(
          form.items || [],
          new Set([pack.key])
        );
        for (const w of warnings) {
          violations.push({
            form_id: form.form_id,
            pack_key: w.pack_key,
            rule_id: w.rule_id,
            rule_version: w.rule_version,
            severity: w.severity,
            dataset: w.dataset,
            item_id: w.item_id,
            message: w.message,
            remediation_guidance: w.remediation_guidance ?? null,
          });
        }
      }
      return {
        data: {
          item_ref: pack.ref,
          forms_evaluated: formsList.length,
          rules_evaluated: formsList.length * Number(pack.rule_count || 0),
          violation_count: violations.length,
          violations,
        },
      };
    },

    approveLibraryItem(
      itemRef,
      _body = {},
      { reason = "", sigToken = "" } = {}
    ) {
      if (!String(sigToken || "").trim()) {
        return {
          error: {
            status: 401,
            problem: {
              title: "Missing step-up signature token",
              status: 401,
              code: "K.Trust.SignatureRequired",
              detail:
                "Header 'X-Sig-Token' is required to approve a library item.",
            },
          },
        };
      }
      const cleanRef = String(itemRef || "").split("@")[0];
      const normalizedRef = cleanRef.startsWith("library_item:")
        ? cleanRef
        : `library_item:${cleanRef}`;
      const target = state.rulePacks.find(
        (p) => p.ref === normalizedRef || p.key === cleanRef
      );
      if (!target) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Library item not found",
              status: 404,
              code: "K.Node.NotFound",
              detail: `Library item '${itemRef}' was not found.`,
            },
          },
        };
      }
      target.status = "Approved";
      recordActivity(
        "kernel.library_item.approved",
        "governance",
        "library_item",
        target.ref,
        "usr-dm-01",
        reason
      );
      return { data: cloneJson(target) };
    },

    declareStudyRulePacks(studyRef, body = {}, { reason = "" } = {}) {
      const study = getStudyByRefOrCode(studyRef);
      const rawRefs = Array.isArray(body?.rule_pack_refs)
        ? body.rule_pack_refs
        : [];
      const normalized = rawRefs.map((r) => {
        const clean = String(r || "")
          .trim()
          .split("@")[0];
        return clean.startsWith("library_item:")
          ? clean
          : `library_item:${clean}`;
      });
      for (const ref of normalized) {
        const pack = state.rulePacks.find((p) => p.ref === ref);
        if (!pack) {
          return {
            error: {
              status: 404,
              problem: {
                title: "Rule pack not found",
                status: 404,
                code: "K.Node.NotFound",
                detail: `Rule pack '${ref}' does not exist in this tenant.`,
              },
            },
          };
        }
        if (String(pack.status).toLowerCase() !== "approved") {
          return {
            error: {
              status: 409,
              problem: {
                title: "Rule pack not approved",
                status: 409,
                code: "K.Standards.RulePackNotApproved",
                detail: `Rule pack '${pack.key}' must be Approved before pinning on study '${study.code}'.`,
              },
            },
          };
        }
      }
      state.studyPinnedRulePacks.set(study.code, normalized);
      for (const pack of state.rulePacks) {
        pack.pinned = normalized.includes(pack.ref);
      }
      recordActivity(
        "kernel.study.standards_declared",
        "protocol",
        "study",
        study.ref,
        "usr-dm-01",
        reason
      );
      return { data: methods.listStudyRulePacks(study.ref) };
    },

    listStudyValidationFindings(studyRef, query = {}) {
      const study = getStudyByRefOrCode(studyRef);
      const cleanStudyRef = String(study?.ref || studyRef || STUDY_REF).split(
        "@"
      )[0];
      let list = state.validationFindings.map((f) => ({
        ...cloneJson(f),
        study_ref: cleanStudyRef,
      }));
      if (query.stage) {
        list = list.filter(
          (f) => f.stage.toLowerCase() === String(query.stage).toLowerCase()
        );
      }
      if (query.status) {
        list = list.filter(
          (f) => f.status.toLowerCase() === String(query.status).toLowerCase()
        );
      }
      if (query.severity) {
        list = list.filter(
          (f) =>
            f.severity.toLowerCase() === String(query.severity).toLowerCase()
        );
      }
      if (query.dataset) {
        list = list.filter(
          (f) => f.dataset.toLowerCase() === String(query.dataset).toLowerCase()
        );
      }
      if (query.packKey) {
        list = list.filter(
          (f) =>
            f.pack_key.toLowerCase() === String(query.packKey).toLowerCase()
        );
      }
      const total = list.length;
      if (query.limit) {
        list = list.slice(0, Number(query.limit));
      }
      return {
        total,
        findings: list,
      };
    },

    getValidationFinding(findingRef) {
      const cleanRef = String(findingRef || "").split("@")[0];
      const normalizedRef = cleanRef.startsWith("finding:")
        ? cleanRef
        : `finding:${cleanRef}`;
      const found = state.validationFindings.find(
        (f) => f.ref === normalizedRef
      );
      if (!found) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Validation finding not found",
              status: 404,
              code: "K.Node.NotFound",
              detail: `Validation finding '${findingRef}' was not found.`,
            },
          },
        };
      }
      return {
        data: {
          finding: cloneJson(found),
          explanation: {
            subject: found.ref,
            claim: found.message,
            producer: "kernel/validation",
            steps: [
              {
                claim: `Evaluated ${found.pack_key} rule ${found.rule_id}@${found.rule_version} at stage ${found.stage}.`,
                evidence: [found.record_key],
              },
            ],
            conditions: [
              {
                code: found.rule_id,
                description: found.expected || found.message,
                met: false,
                evidence: found.observed.map(
                  (o) => `${o.variable}=${o.value ?? "null"}`
                ),
              },
            ],
            next_actions: [
              {
                action:
                  "Review source vitals flowsheet and correct observation or classify discrepancy.",
                capability: "collection.correct",
                target: found.ref,
              },
            ],
          },
        },
      };
    },

    getEntitlements() {
      return cloneJson(state.entitlements);
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
      recordActivity(
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
      recordActivity(
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

  return {
    reset,
    methods,
  };
}

export function createKernelSliceHandlers(store, { http, HttpResponse }) {
  return [
    createDemoHealthRoute(
      { http, HttpResponse },
      { status: "healthy", mode: "demo" }
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
    createDemoSignatureVerificationRoute(
      { http, HttpResponse },
      {
        validatePayload: (body) =>
          isValidStepUpAction(body?.action) &&
          isValidSemanticAction(body?.semantic_action),
        createToken: () => `demo_sig_tok_${Date.now()}`,
      }
    ),

    http.get("*/api/v1/kernel/entitlements", () =>
      HttpResponse.json(store.getEntitlements())
    ),
    http.post("*/api/v1/kernel/entitlements/tier", async ({ request }) => {
      const headerErr = validateWriteCommandHeaders(request, HttpResponse);
      if (headerErr) return headerErr;
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
      const headerErr = validateWriteCommandHeaders(request, HttpResponse);
      if (headerErr) return headerErr;
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
      const headerErr = validateWriteCommandHeaders(request, HttpResponse);
      if (headerErr) return headerErr;
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
    http.get("*/api/v1/kernel/nodes/:nodeRef", ({ params, request }) =>
      HttpResponse.json(
        store.getNode(
          decodeURIComponent(String(params.nodeRef)),
          resolveDemoCaller(request)
        )
      )
    ),
    http.get("*/api/v1/kernel/studies", () =>
      HttpResponse.json(store.listStudies())
    ),
    http.post("*/api/v1/kernel/studies", async ({ request }) => {
      const body = await readJsonBody(request);
      const reason = readRequiredReasonHeader(request);
      const idempotencyKey = readIdempotencyKey(request);
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
      const idempotencyKey = readIdempotencyKey(request);
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
      const idempotencyKey = readIdempotencyKey(request);
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
        const idempotencyKey = readIdempotencyKey(request);
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
    http.get("*/api/v1/kernel/studies/:studyRef/proposals", () =>
      HttpResponse.json(store.listProposals())
    ),
    http.get("*/api/v1/kernel/studies/:studyRef/rule-packs", ({ params }) =>
      HttpResponse.json(
        store.listStudyRulePacks(decodeURIComponent(String(params.studyRef)))
      )
    ),
    http.post(
      "*/api/v1/kernel/studies/:studyRef/rule-packs/:itemRef/preview",
      ({ params }) => {
        const result = store.previewStudyRulePack(
          decodeURIComponent(String(params.studyRef)),
          decodeURIComponent(String(params.itemRef))
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.post(
      "*/api/v1/kernel/library-items/:itemRef/approve",
      async ({ params, request }) => {
        const headerErr = validateWriteCommandHeaders(request, HttpResponse);
        if (headerErr) return headerErr;
        const body = await readJsonBody(request);
        const result = store.approveLibraryItem(
          decodeURIComponent(String(params.itemRef)),
          body,
          {
            reason: readRequiredReasonHeader(request),
            sigToken:
              request.headers.get("X-Sig-Token") ||
              request.headers.get("x-sig-token") ||
              "",
          }
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.put(
      "*/api/v1/kernel/studies/:studyRef/standards",
      async ({ params, request }) => {
        const headerErr = validateWriteCommandHeaders(request, HttpResponse);
        if (headerErr) return headerErr;
        const body = await readJsonBody(request);
        const result = store.declareStudyRulePacks(
          decodeURIComponent(String(params.studyRef)),
          body,
          { reason: readRequiredReasonHeader(request) }
        );
        if (result.error) {
          return HttpResponse.json(result.error.problem, {
            status: result.error.status,
          });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get("*/api/v1/kernel/work-queues", () =>
      HttpResponse.json({
        queues: [],
      })
    ),
    http.post("*/api/v1/kernel/work-queues", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(
        {
          definition:
            "work_queue_definition:01960000-0000-7000-8000-000000000900", // deid-ignore
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
        return HttpResponse.json(
          store.listStudyValidationFindings(
            decodeURIComponent(String(params.studyRef)),
            {
              stage: url.searchParams.get("stage") || undefined,
              status: url.searchParams.get("status") || undefined,
              severity: url.searchParams.get("severity") || undefined,
              dataset: url.searchParams.get("dataset") || undefined,
              packKey: url.searchParams.get("pack_key") || undefined,
              limit: url.searchParams.get("limit") || undefined,
            }
          )
        );
      }
    ),
    http.get(
      "*/api/v1/kernel/validation/findings/:findingRef",
      ({ params }) => {
        const result = store.getValidationFinding(
          decodeURIComponent(String(params.findingRef))
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
      "*/api/v1/kernel/studies/:studyRef/exports",
      ({ params, request }) => {
        const url = new URL(request.url);
        const format = (url.searchParams.get("format") || "csv").toLowerCase();
        const studyRef = decodeURIComponent(String(params.studyRef));
        const digestHeader = {
          "X-Content-Sha256":
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
        };
        if (format === "odm_xml" || format === "odm" || format === "xml") {
          return new HttpResponse(
            `<?xml version="1.0" encoding="UTF-8"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="DEMO" CreationDateTime="${new Date().toISOString()}" ODMVersion="1.3.2"><Study OID="${studyRef}"/></ODM>`,
            {
              headers: {
                "Content-Type": "application/xml",
                ...digestHeader,
              },
            }
          );
        }
        if (
          format === "dataset_json" ||
          format === "dataset-json" ||
          format === "json"
        ) {
          return HttpResponse.json(
            { study_ref: studyRef, records: [] },
            { headers: digestHeader }
          );
        }
        if (
          format === "sas_bundle" ||
          format === "r_bundle" ||
          format === "study_archive"
        ) {
          const eocd = new Uint8Array([
            0x50, 0x4b, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
          ]);
          return new HttpResponse(eocd, {
            headers: {
              "Content-Type": "application/zip",
              "Content-Disposition": `attachment; filename="CAD-ONC-2026.${format}.zip"`,
              ...digestHeader,
            },
          });
        }
        return new HttpResponse("STUDYID,USUBJID\nCAD-ONC-2026,SUBJ-1001\n", {
          headers: {
            "Content-Type": "text/csv",
            ...digestHeader,
          },
        });
      }
    ),
    http.get(
      "*/api/v1/kernel/enrollments/:enrollmentRef/casebook",
      ({ params, request }) => {
        const url = new URL(request.url);
        const format = (url.searchParams.get("format") || "json").toLowerCase();
        const enrollmentRef = decodeURIComponent(String(params.enrollmentRef));
        const digestHeader = {
          "X-Content-Sha256":
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
        };
        if (format === "odm_xml" || format === "odm" || format === "xml") {
          return new HttpResponse(
            `<?xml version="1.0" encoding="UTF-8"?><ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="CASEBOOK-DEMO" CreationDateTime="${new Date().toISOString()}" ODMVersion="1.3.2"><ClinicalData StudyOID="CAD-ONC-2026" MetaDataVersionOID="MDV.1"><SubjectData SubjectKey="${enrollmentRef}"/></ClinicalData></ODM>`,
            {
              headers: {
                "Content-Type": "application/xml",
                ...digestHeader,
              },
            }
          );
        }
        if (format === "html") {
          return new HttpResponse(
            `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Casebook ${enrollmentRef}</title></head><body><h1>Participant Casebook</h1><p>${enrollmentRef}</p></body></html>`,
            {
              headers: {
                "Content-Type": "text/html; charset=utf-8",
                ...digestHeader,
              },
            }
          );
        }
        if (format === "pdf") {
          // deid-ignore: PDF-1.7 10-digit xref byte offsets are structural PDF syntax, not telephone numbers
          const pdfText = `%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\nxref\n0 3\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \ntrailer << /Size 3 /Root 1 0 R >>\nstartxref\n115\n%%EOF\n`; // deid-ignore
          return new HttpResponse(pdfText, {
            headers: {
              "Content-Type": "application/pdf",
              "Content-Disposition": `attachment; filename="CAD-ONC-2026.${enrollmentRef.replace(/[^a-zA-Z0-9_-]/g, "_")}.casebook.pdf"`,
              ...digestHeader,
            },
          });
        }
        return HttpResponse.json(
          { enrollment_ref: enrollmentRef, observations: [] },
          { headers: digestHeader }
        );
      }
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
        body?.stored_version_ref || body?.base_version_ref || STUDY_REF;
      return HttpResponse.json({
        scope: STUDY_REF,
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
  ];
}
