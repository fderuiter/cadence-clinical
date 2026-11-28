import { DEMO_PARTICIPANTS, DEMO_STUDY } from "demo-fixtures";
import { isCallerBlinded, isSitePermitted } from "../demoPolicy.js";
import {
  DEFAULT_FORM_UUID,
  STUDY_REF,
  VERSION_V1_REF,
  VERSION_V2_REF,
  buildDecision,
  cloneJson,
  resolveCollectionTitle,
  resolveEnrollmentByIdOrCode as resolveSubjectByEnrollmentOrCode,
  resolveSiteCodeFromRef,
  resolveSiteUuid,
  subjectEnrollmentRef,
  subjectEnrollmentUuid,
} from "./shared.js";

const CADENCE_101_FORM_UUID = "00000000-0000-0000-0000-000000002101"; // deid-ignore
const CADENCE_101_OBS_UUID = "00000000-0000-0000-0000-000000006101"; // deid-ignore
const BLINDED_OBS_UUID = "00000000-0000-0000-0000-000000006999"; // deid-ignore
const CADENCE_101_QUERY_UUID = "00000000-0000-0000-0000-000000003104"; // deid-ignore
const BLINDED_QUERY_UUID = "00000000-0000-0000-0000-000000003999"; // deid-ignore
const CADENCE_101_FINDING_UUID = "00000000-0000-0000-0000-000000007101"; // deid-ignore
const BLINDED_FINDING_UUID = "00000000-0000-0000-0000-000000007999"; // deid-ignore

const CONCEPT_LABELS = {
  SYSBP: "Systolic Blood Pressure",
  DIABP: "Diastolic Blood Pressure",
  PULSE: "Pulse Rate",
  HR: "Heart Rate",
  VSCOMM: "Vital Signs Comment",
  RANDARM: "Randomization Treatment Arm",
};

const SEEDED_GOVERNANCE_RECORDS = [
  {
    ref: "qualification:00000000-0000-0000-0000-000000008101@1", // deid-ignore
    kind: "qualification",
    code: "GCP-ICH-E6R3",
    title: "Good Clinical Practice ICH E6(R3) Qualification",
    detail: "Training · Active qualification",
    status: "ACTIVE",
    rank: 0.72,
  },
  {
    ref: "artifact:00000000-0000-0000-0000-000000008201@1", // deid-ignore
    kind: "artifact",
    code: "DEV-2026-014",
    title: "DEV-2026-014 Protocol Deviation Report — Visit Window",
    detail: "CADENCE-101 · Site SITE-101 · Protocol Deviation",
    status: "APPROVED",
    studyCode: "CADENCE-101",
    siteCode: "SITE-101",
    rank: 0.78,
  },
  {
    ref: "artifact:00000000-0000-0000-0000-000000008202@1", // deid-ignore
    kind: "artifact",
    code: "IRB-2026-101",
    title: "Central IRB Initial Approval Letter",
    detail: "CADENCE-101 · Regulatory Document",
    status: "APPROVED",
    studyCode: "CADENCE-101",
    rank: 0.73,
  },
  {
    ref: "concept:00000000-0000-0000-0000-000000008301@1", // deid-ignore
    kind: "concept",
    code: "SYSBP",
    title: "Systolic Blood Pressure",
    detail: "Vital Signs · mm[Hg] · LOINC 8480-6",
    status: "ACTIVE",
    rank: 0.74,
  },
  {
    ref: "concept:00000000-0000-0000-0000-000000008302@1", // deid-ignore
    kind: "concept",
    code: "DIABP",
    title: "Diastolic Blood Pressure",
    detail: "Vital Signs · mm[Hg] · LOINC 8462-4",
    status: "ACTIVE",
    rank: 0.73,
  },
  {
    ref: "code_system:00000000-0000-0000-0000-000000008401@1", // deid-ignore
    kind: "code_system",
    code: "http://loinc.org",
    title: "LOINC Logical Observation Identifiers Names and Codes",
    detail: "Terminology · Open",
    status: "ACTIVE",
    rank: 0.68,
  },
  {
    ref: "code_system_version:00000000-0000-0000-0000-000000008501@1", // deid-ignore
    kind: "code_system_version",
    code: "8480-6",
    title: "Systolic blood pressure · LOINC 2.78",
    detail: "Code system version 2.78",
    status: "ACTIVE",
    rank: 0.67,
  },
  {
    ref: "standard_version:00000000-0000-0000-0000-000000008601@1", // deid-ignore
    kind: "standard_version",
    code: "SDTMIG 3.4",
    title: "CDISC SDTM Implementation Guide 3.4",
    detail: "Active standard",
    status: "ACTIVE",
    rank: 0.69,
  },
  {
    ref: "library_item:00000000-0000-0000-0000-000000008701@1", // deid-ignore
    kind: "library_item",
    code: "VS-VITAL-SIGNS",
    title: "Vital Signs CDASH Collection & Edit Check Pack",
    detail: "Approved library item · v1",
    status: "APPROVED",
    rank: 0.71,
  },
];

function formatQueryShortCode(queryId) {
  const hex = String(queryId || "")
    .replace(/[^0-9a-f]/gi, "")
    .toUpperCase();
  return `QRY-${hex.slice(-8).padStart(8, "0")}`;
}

function normalizeSearchBag(text) {
  return String(text || "")
    .toLowerCase()
    .replace(/[_-]+/g, " ");
}

export function createKernelGraphDomain(ctx) {
  const { state } = ctx;

  function resolveEnrollmentByIdOrCode(rawId) {
    return resolveSubjectByEnrollmentOrCode(state.subjects, rawId);
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

  function findLinkedStudySiteByRef(nodeRef) {
    const unpinned = String(nodeRef || "").split("@")[0];
    for (const list of state.studySites.values()) {
      const matched = (list || []).find(
        (item) =>
          String(item.ref || "").split("@")[0] === unpinned ||
          String(item.study_site_ref || "").split("@")[0] === unpinned
      );
      if (matched) return matched;
    }
    return null;
  }

  function findCanonicalCadenceParticipant() {
    return (
      state.subjects.find((s) => s.subject_id === "SUBJ-101-001") ||
      (DEMO_PARTICIPANTS[0]
        ? {
            id: DEMO_PARTICIPANTS[0].participantUuid,
            enrollment_ref: `enrollment:${DEMO_PARTICIPANTS[0].enrollmentUuid}`,
            subject_id: DEMO_PARTICIPANTS[0].subjectCode,
            study_id: DEMO_STUDY.code || "CADENCE-101",
            site_id: DEMO_PARTICIPANTS[0].siteCode || "SITE-101",
            status: "ACTIVE",
          }
        : state.subjects[0] || null)
    );
  }

  function resolveStudySiteRefForStudy(studyCode, siteCode) {
    const list = state.studySites.get(String(studyCode || "").toUpperCase());
    const matched = (list || []).find(
      (item) =>
        String(item.site_code || "").toUpperCase() ===
        String(siteCode || "").toUpperCase()
    );
    if (matched?.study_site_ref) {
      return String(matched.study_site_ref).split("@")[0];
    }
    if (matched?.ref) {
      return String(matched.ref).split("@")[0];
    }
    return `study_site:${resolveSiteUuid(siteCode || "SITE-101")}`;
  }

  function findGovernanceRecord(nodeRef) {
    const unpinned = String(nodeRef || "").split("@")[0];
    return (
      SEEDED_GOVERNANCE_RECORDS.find(
        (rec) => rec.ref.split("@")[0] === unpinned
      ) || null
    );
  }

  function getNode(nodeRef, caller = null) {
    const refStr = String(nodeRef || "");
    const unpinned = refStr.split("@")[0];

    if (refStr.startsWith("study:")) {
      return cloneJson(ctx.getStudyByRefOrCode(nodeRef));
    }
    if (refStr.startsWith("organization:")) {
      const matchedOrg = (state.organizations || []).find(
        (org) => String(org.ref || "").split("@")[0] === unpinned
      );
      return {
        ref: nodeRef,
        kind: "organization",
        title: matchedOrg?.name || "Clinical Organization",
        code: matchedOrg?.code || null,
        status: "ACTIVE",
        mask: null,
      };
    }
    if (refStr.startsWith("site:")) {
      const matchedSite = (state.sites || []).find(
        (site) => String(site.ref || "").split("@")[0] === unpinned
      );
      const siteCode = matchedSite?.code || "SITE-101";
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: "site",
          title: matchedSite?.name || `Clinical Site ${siteCode}`,
          code: siteCode,
          status: matchedSite?.is_active === false ? "INACTIVE" : "ACTIVE",
          mask: null,
        },
        caller,
        { siteCode }
      );
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
    if (refStr.startsWith("observation:")) {
      const cleanObsId = unpinned.replace(/^observation:/, "");
      if (cleanObsId === BLINDED_OBS_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "observation",
            title: "Randomization Treatment Arm · ACTIVE_DRUG_200MG",
            code: "RANDARM",
            status: "RECORDED",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101", isAllocation: true }
        );
      }
      if (cleanObsId === CADENCE_101_OBS_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "observation",
            title: "Systolic Blood Pressure: 128 mm[Hg]",
            code: "SYSBP",
            status: "RECORDED",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101" }
        );
      }
      const obs =
        state.observations.get(nodeRef) || state.observations.get(unpinned);
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
      for (const form of state.forms.values()) {
        const item = (form.items || []).find(
          (it) => it.observation_id === cleanObsId
        );
        if (item) {
          const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
          const conceptLabel =
            CONCEPT_LABELS[item.concept] || item.concept || item.item_id;
          return applyNodeVisibilityMask(
            {
              ref: nodeRef,
              kind: "observation",
              title: `${conceptLabel}: ${item.value?.value ?? "recorded"}${item.unit ? ` ${item.unit}` : ""}`,
              code: item.concept || item.item_id,
              status: "RECORDED",
              mask: null,
            },
            caller,
            { siteCode: subj?.site_id || null }
          );
        }
      }
    }
    if (refStr.startsWith("protocol_version:")) {
      const isV1 =
        nodeRef === VERSION_V1_REF || unpinned === VERSION_V1_REF.split("@")[0];
      return {
        ref: nodeRef,
        kind: "protocol_version",
        title: isV1
          ? "Protocol v1.0 Initial Release"
          : "Protocol v2.0 Active Release",
        code: isV1 ? "v1.0" : "v2.0",
        status: "PUBLISHED",
        mask: null,
      };
    }
    if (refStr.startsWith("plan:")) {
      const matchingProposal = [...state.proposals.values()].find(
        (p) => String(p.plan?.ref || "").split("@")[0] === unpinned
      );
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: "plan",
          title:
            matchingProposal?.plan?.title ||
            "Protocol Amendment 2 Visit Window Adjustment",
          code: `v${matchingProposal?.plan?.number ?? 2}`,
          status: "PROPOSED",
          mask: null,
        },
        caller
      );
    }
    if (refStr.startsWith("proposal:")) {
      const matchingProposal =
        state.proposals.get(nodeRef) ||
        state.proposals.get(unpinned) ||
        [...state.proposals.values()].find(
          (p) => String(p.ref || "").split("@")[0] === unpinned
        );
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: "proposal",
          title: matchingProposal?.intent || "Protocol Amendment Proposal",
          code: String(unpinned).split(":")[1]?.slice(-4) || "4001",
          status: matchingProposal?.status || "Open",
          mask: null,
        },
        caller
      );
    }
    if (refStr.startsWith("study_site:")) {
      const linked = findLinkedStudySiteByRef(nodeRef);
      const siteCode =
        linked?.site_code ||
        resolveSiteCodeFromRef(nodeRef, state.subjects, state.studySites);
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: "study_site",
          title:
            linked?.site_name ||
            (siteCode === "SITE-102"
              ? "US Regional Trial Unit 2"
              : `US Academic Clinical Research Center (${siteCode})`),
          code: siteCode,
          status: linked?.status || "ACTIVE",
          mask: null,
        },
        caller,
        { siteCode }
      );
    }
    if (refStr.startsWith("enrollment:")) {
      const subj = resolveEnrollmentByIdOrCode(nodeRef);
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
    if (refStr.startsWith("collection_instance:")) {
      const rawFormId = unpinned.replace(/^collection_instance:/, "");
      if (rawFormId === CADENCE_101_FORM_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "collection_instance",
            title: "Vital Signs (Week 4)",
            code: "form-vital-signs",
            status: "COMPLETE",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101" }
        );
      }
      const form =
        state.forms.get(rawFormId) || state.forms.get(DEFAULT_FORM_UUID);
      if (form) {
        const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "collection_instance",
            title: `${form.collection_title || resolveCollectionTitle(form.collection_spec_id) || form.collection_spec_id}${form.planned_activity_id ? ` (${form.planned_activity_id})` : ""}`,
            code: form.collection_spec_id,
            status: form.complete ? "COMPLETE" : "OPEN",
            mask: null,
          },
          caller,
          { siteCode: subj?.site_id || null }
        );
      }
    }
    if (refStr.startsWith("form_query:") || refStr.startsWith("query:")) {
      const kind = refStr.startsWith("form_query:") ? "form_query" : "query";
      const rawQueryId = unpinned.replace(/^(form_query|query):/, "");
      if (rawQueryId === BLINDED_QUERY_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind,
            title: "Verify unblinded randomization arm assignment",
            code: formatQueryShortCode(BLINDED_QUERY_UUID),
            status: "OPEN",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101", isAllocation: true }
        );
      }
      if (rawQueryId === CADENCE_101_QUERY_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind,
            title: "Systolic blood pressure unit check",
            code: formatQueryShortCode(CADENCE_101_QUERY_UUID),
            status: "OPEN",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101" }
        );
      }
      const q = state.queries.get(rawQueryId);
      if (q) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind,
            title: q.text || `Discrepancy query (${q.item_id || "item"})`,
            code:
              kind === "form_query"
                ? formatQueryShortCode(q.query_id)
                : q.subject_code
                  ? `${q.subject_code} · ${q.item_id || "query"}`
                  : q.item_id || null,
            status: String(q.status || "OPEN").toUpperCase(),
            mask: null,
          },
          caller,
          { siteCode: q.site_code || null }
        );
      }
    }
    if (refStr.startsWith("validation_finding:")) {
      const rawFindingId = unpinned.replace(/^validation_finding:/, "");
      if (rawFindingId === BLINDED_FINDING_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "validation_finding",
            title: "Randomization stratum imbalance on ACTIVE_DRUG_200MG",
            code: "RAND-BLIND-01",
            status: "OPEN",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101", isAllocation: true }
        );
      }
      if (rawFindingId === CADENCE_101_FINDING_UUID) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "validation_finding",
            title: "Systolic blood pressure out of protocol window",
            code: "DEV-2026-01",
            status: "OPEN",
            mask: null,
          },
          caller,
          { siteCode: "SITE-101" }
        );
      }
      const finding = (state.validationFindings || []).find(
        (f) =>
          String(f.ref || "").split("@")[0] === unpinned ||
          String(f.finding_id || f.id || "") === rawFindingId
      );
      if (finding) {
        return applyNodeVisibilityMask(
          {
            ref: nodeRef,
            kind: "validation_finding",
            title:
              finding.message || finding.title || "Validation rule finding",
            code: finding.rule_id || finding.code || "RULE-01",
            status: String(finding.status || "OPEN").toUpperCase(),
            mask: null,
          },
          caller,
          {
            siteCode: finding.site_code || null,
            isAllocation: Boolean(finding.is_blinded),
          }
        );
      }
    }
    const govRecord = findGovernanceRecord(nodeRef);
    if (govRecord) {
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: govRecord.kind,
          title: govRecord.title,
          code: govRecord.code,
          status: govRecord.status,
          mask: null,
        },
        caller,
        { siteCode: govRecord.siteCode || null }
      );
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
  }

  function getNodes(refs = [], caller = null) {
    const list = Array.isArray(refs) ? refs : [refs];
    if (list.length > 100) {
      return {
        error: {
          status: 400,
          problem: {
            title: "Batch size limit exceeded",
            status: 400,
            code: "K.Nodes.BatchLimitExceeded",
            detail: "At most 100 node references may be requested per batch.",
          },
        },
      };
    }
    const nodes = {};
    for (const raw of list) {
      const key = String(raw || "").trim();
      if (!key) continue;
      if (!ctx.DEMO_NODE_REF_SYNTAX.test(key)) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Invalid node reference",
              status: 400,
              code: "K.NodeRef.Invalid",
              detail: `'${key}' is not a valid NodeRef.`,
            },
          },
        };
      }
      const unpinned = key.split("@")[0];
      if (
        state.missingNodeRefs?.has(key) ||
        state.missingNodeRefs?.has(unpinned)
      ) {
        continue;
      }
      const resolved = getNode(key, caller);
      if (resolved) {
        nodes[key] = resolved;
      }
    }
    return { data: { nodes } };
  }

  ctx.getNodes = getNodes;

  function getDependents(nodeRef, depth = 3, caller = null) {
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
            ref: subjectEnrollmentRef(subj),
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
            ref: `allocation:${subjectEnrollmentUuid(subj)}`,
            mask: !sitePermitted ? "NotPermitted" : blinded ? "Blinded" : null,
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
  }

  ctx.getNode = getNode;
  ctx.getDependents = getDependents;

  const methods = {
    buildDecision,

    searchKernel({ q = "", kinds = [], limit = 25, caller = null } = {}) {
      const trimmed = String(q || "").trim();
      const lower = trimmed.toLowerCase();
      const normalizedTokens = normalizeSearchBag(trimmed)
        .split(/\s+/)
        .filter(Boolean);
      const kindFilter = new Set(
        (Array.isArray(kinds) ? kinds : [kinds]).filter(Boolean)
      );
      const rawRoles = caller?.backendRoles || caller?.roles || [];
      const rawSites = caller?.siteIds || caller?.permittedSites || [];
      const effectiveCaller = caller
        ? {
            ...caller,
            isAuthenticated: caller.isAuthenticated ?? true,
            backendRoles:
              rawRoles instanceof Set
                ? rawRoles
                : new Set(
                    Array.from(rawRoles).map((r) =>
                      String(r).trim().toLowerCase()
                    )
                  ),
            siteIds: Array.from(rawSites).map((s) =>
              String(s).trim().toUpperCase()
            ),
          }
        : null;
      const blindedCaller = Boolean(
        effectiveCaller &&
        (typeof caller.blinded === "boolean"
          ? caller.blinded
          : isCallerBlinded(effectiveCaller))
      );
      const canSeeSite = (siteCode) =>
        !siteCode ||
        !effectiveCaller ||
        isSitePermitted(effectiveCaller, siteCode);

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
          ctx.ensureStudyByCode(trimmed.toUpperCase());
        }
      }

      const rawCandidates = [];
      const pushCandidate = ({
        ref,
        kind,
        title,
        code = null,
        detail = null,
        rank = 0.7,
        searchExtra = "",
      }) => {
        if (!ref || !kind || !title) return;
        if (kindFilter.size > 0 && !kindFilter.has(kind)) return;
        const exactCode = Boolean(
          trimmed &&
          ((code && String(code).toLowerCase() === lower) ||
            String(ref).toLowerCase() === lower ||
            String(ref).split("@")[0].toLowerCase() === lower)
        );
        rawCandidates.push({
          ref,
          kind,
          title,
          code,
          detail,
          exact_code: exactCode,
          rank,
          _bag: `${String(ref).toLowerCase()} ${String(code || "").toLowerCase()} ${String(title).toLowerCase()} ${String(detail || "").toLowerCase()} ${normalizeSearchBag(`${code || ""} ${title} ${detail || ""} ${searchExtra}`)}`,
        });
      };

      // 1. study
      for (const study of state.studies.values()) {
        pushCandidate({
          ref: study.ref,
          kind: "study",
          title: study.title,
          code: study.code,
          detail: `Status: ${study.status}`,
          rank:
            study.code === (DEMO_STUDY.code || "CADENCE-101") ||
            study.code === "DEMO-01"
              ? 1.0
              : 0.9,
        });
      }

      // 2. organization
      for (const org of state.organizations || []) {
        pushCandidate({
          ref: org.ref,
          kind: "organization",
          title: org.name,
          code: org.code || null,
          detail: org.kind || "Organization",
          rank: 0.66,
        });
      }

      // 3. site
      for (const site of state.sites || []) {
        if (!canSeeSite(site.code)) continue;
        pushCandidate({
          ref: site.ref,
          kind: "site",
          title: site.name,
          code: site.code,
          detail: `${site.country || "US"} · ${site.institution_name || "Investigational site"}`,
          rank: 0.86,
        });
      }

      // 4. study_site
      const canonicalStudyCode = DEMO_STUDY.code || "CADENCE-101";
      for (const [studyKey, list] of state.studySites.entries()) {
        for (const ss of list || []) {
          const siteCode = ss.site_code || "SITE-101";
          if (!canSeeSite(siteCode)) continue;
          const studyCode = ss.study_code || studyKey;
          pushCandidate({
            ref: ss.ref || `${ss.study_site_ref}@1`,
            kind: "study_site",
            title: `${siteCode} · ${ss.site_name || siteCode}`,
            code: siteCode,
            detail: `${studyCode} · ${ss.country || "US"} · ${ss.status || "Active"}`,
            rank: studyCode === canonicalStudyCode ? 0.95 : 0.85,
            searchExtra: `${ss.institution_code || ""} ${ss.institution_name || ""}`,
          });
        }
      }

      // 5. enrollment
      for (const subj of state.subjects) {
        const siteCode = subj.site_id || "SITE-101";
        if (!canSeeSite(siteCode)) continue;
        pushCandidate({
          ref: subjectEnrollmentRef(subj),
          kind: "enrollment",
          title: `Participant ${subj.subject_id}`,
          code: subj.subject_id,
          detail: `${subj.status || "ACTIVE"} · ${subj.study_id} · Site ${siteCode}`,
          rank: subj.study_id === canonicalStudyCode ? 0.9 : 0.82,
        });
      }

      // 6. protocol_version
      pushCandidate({
        ref: `${VERSION_V2_REF}@1`,
        kind: "protocol_version",
        title: "Protocol v2.0 Active Release",
        code: "v2.0",
        detail: `${canonicalStudyCode} · Published`,
        rank: 0.77,
        searchExtra: "protocol version 2 amendment",
      });
      pushCandidate({
        ref: `${VERSION_V1_REF}@1`,
        kind: "protocol_version",
        title: "Protocol v1.0 Initial Release",
        code: "v1.0",
        detail: `${canonicalStudyCode} · Superseded`,
        rank: 0.7,
        searchExtra: "protocol version 1 initial",
      });

      // 7. collection_instance
      const cadenceSubj = findCanonicalCadenceParticipant();
      if (canSeeSite("SITE-101")) {
        pushCandidate({
          ref: `collection_instance:${CADENCE_101_FORM_UUID}@1`,
          kind: "collection_instance",
          title: "Vital Signs (Week 4)",
          code: "form-vital-signs",
          detail: `${cadenceSubj?.subject_id || "SUBJ-101-001"} · ${canonicalStudyCode} · Site SITE-101`,
          rank: 0.84,
          searchExtra: "CDASH VS eCRF week 4",
        });
      }
      for (const form of state.forms.values()) {
        const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
        const siteCode = subj?.site_id || "SITE-101";
        if (!canSeeSite(siteCode)) continue;
        const studyCode = subj?.study_id || "DEMO-01";
        const formTitle = `${form.collection_title || resolveCollectionTitle(form.collection_spec_id) || form.collection_spec_id}${form.planned_activity_id ? ` (${form.planned_activity_id})` : ""}`;
        pushCandidate({
          ref: `collection_instance:${form.form_id}@${Math.max(1, Number(form.version || 1))}`,
          kind: "collection_instance",
          title: formTitle,
          code: form.collection_spec_id,
          detail: `${subj?.subject_id || "SUBJ-1001"} · ${studyCode} · Site ${siteCode}`,
          rank: 0.79,
        });
      }

      // 8. observation
      if (canSeeSite("SITE-101")) {
        pushCandidate({
          ref: `observation:${CADENCE_101_OBS_UUID}@1`,
          kind: "observation",
          title: "Systolic Blood Pressure · 128 mm[Hg]",
          code: "SYSBP",
          detail: `${cadenceSubj?.subject_id || "SUBJ-101-001"} · ${canonicalStudyCode} · Site SITE-101`,
          rank: 0.83,
          searchExtra: "vital signs item-sysbp 128 mmHg",
        });
        if (!blindedCaller) {
          pushCandidate({
            ref: `observation:${BLINDED_OBS_UUID}@1`,
            kind: "observation",
            title: "Randomization Treatment Arm · ACTIVE_DRUG_200MG",
            code: "RANDARM",
            detail: `${cadenceSubj?.subject_id || "SUBJ-101-001"} · ${canonicalStudyCode} · Site SITE-101 · Unblinded`,
            rank: 0.75,
            searchExtra: "ACTIVE_DRUG_200MG allocation arm",
          });
        }
      }
      for (const form of state.forms.values()) {
        const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
        const siteCode = subj?.site_id || "SITE-101";
        if (!canSeeSite(siteCode)) continue;
        const studyCode = subj?.study_id || "DEMO-01";
        for (const item of form.items || []) {
          if (!item.observation_id) continue;
          const label =
            CONCEPT_LABELS[item.concept] ||
            `Observation ${item.concept || item.item_id}`;
          const valText =
            item.value?.value !== undefined && item.value?.value !== null
              ? `${item.value.value}${item.unit ? ` ${item.unit}` : ""}`
              : "";
          pushCandidate({
            ref: `observation:${item.observation_id}@${Math.max(1, Number(item.revision || 1))}`,
            kind: "observation",
            title: valText ? `${label} · ${valText}` : label,
            code: item.concept || item.item_id,
            detail: `${subj?.subject_id || "SUBJ-1001"} · ${studyCode} · Site ${siteCode}`,
            rank: 0.76,
            searchExtra: `${item.item_id || ""} ${form.collection_spec_id || ""}`,
          });
        }
      }
      for (const obs of state.observations.values()) {
        pushCandidate({
          ref: obs.ref,
          kind: "observation",
          title: `${CONCEPT_LABELS[obs.concept] || `Observation ${obs.concept}`}`,
          code: obs.concept,
          detail: `Version ${obs.version}`,
          rank: 0.7,
        });
      }

      // 9. form_query
      if (canSeeSite("SITE-101")) {
        const cadenceQueryCode = formatQueryShortCode(CADENCE_101_QUERY_UUID);
        pushCandidate({
          ref: `form_query:${CADENCE_101_QUERY_UUID}`,
          kind: "form_query",
          title: "Systolic blood pressure unit check",
          code: cadenceQueryCode,
          detail: `Open · ${cadenceSubj?.subject_id || "SUBJ-101-001"} · ${canonicalStudyCode} · Site SITE-101`,
          rank: 0.82,
          searchExtra: "QRY-104 item-sysbp vital signs discrepancy",
        });
        if (!blindedCaller) {
          pushCandidate({
            ref: `form_query:${BLINDED_QUERY_UUID}`,
            kind: "form_query",
            title:
              "Confirm unblinded kit dispensation against ACTIVE_DRUG_200MG",
            code: formatQueryShortCode(BLINDED_QUERY_UUID),
            detail: `Open · ${cadenceSubj?.subject_id || "SUBJ-101-001"} · ${canonicalStudyCode} · Site SITE-101`,
            rank: 0.74,
            searchExtra: "unblinded allocation query",
          });
        }
      }
      for (const query of state.queries.values()) {
        const siteCode = query.site_code || "SITE-101";
        if (!canSeeSite(siteCode)) continue;
        if (blindedCaller && query.is_blinded) continue;
        const shortCode = formatQueryShortCode(query.query_id);
        const messageBag = (query.messages || [])
          .map((m) => `${m.text || ""} ${m.reason_for_change || ""}`)
          .join(" ");
        const statusLabel =
          String(query.status || "open")
            .charAt(0)
            .toUpperCase() + String(query.status || "open").slice(1);
        pushCandidate({
          ref: `form_query:${query.query_id}`,
          kind: "form_query",
          title: query.text || `Discrepancy query (${query.item_id || "item"})`,
          code: shortCode,
          detail: `${statusLabel} · ${query.subject_code || "SUBJ-1001"} · Site ${siteCode}`,
          rank: 0.8,
          searchExtra: `${query.item_id || ""} ${query.collection_spec_id || ""} ${messageBag}`,
        });
      }

      // 10. validation_finding
      if (canSeeSite("SITE-101")) {
        pushCandidate({
          ref: `validation_finding:${CADENCE_101_FINDING_UUID}@1`,
          kind: "validation_finding",
          title: "Systolic blood pressure out of protocol window",
          code: "DEV-2026-01",
          detail: `${canonicalStudyCode} · Site SITE-101 · ${cadenceSubj?.subject_id || "SUBJ-101-001"} · Warning`,
          rank: 0.81,
          searchExtra: "SYSBP edit check rule finding",
        });
        if (!blindedCaller) {
          pushCandidate({
            ref: `validation_finding:${BLINDED_FINDING_UUID}@1`,
            kind: "validation_finding",
            title: "Randomization stratum imbalance on ACTIVE_DRUG_200MG",
            code: "RAND-BLIND-01",
            detail: `${canonicalStudyCode} · Site SITE-101 · ${cadenceSubj?.subject_id || "SUBJ-101-001"} · Error`,
            rank: 0.74,
            searchExtra: "unblinded allocation finding",
          });
        }
      }
      for (const finding of state.validationFindings || []) {
        const siteCode = finding.site_code || null;
        if (!canSeeSite(siteCode)) continue;
        if (blindedCaller && finding.is_blinded) continue;
        const findingRef =
          finding.ref ||
          `validation_finding:${finding.finding_id || finding.id}@1`;
        pushCandidate({
          ref: findingRef,
          kind: "validation_finding",
          title: finding.message || finding.title || "Validation rule finding",
          code: finding.rule_id || finding.code || null,
          detail: `${finding.study_code || canonicalStudyCode}${siteCode ? ` · Site ${siteCode}` : ""} · ${finding.severity || "Warning"}`,
          rank: 0.78,
        });
      }

      // 11-12 & 14-18. Seeded governance/terminology/artifact/qualification records
      for (const rec of SEEDED_GOVERNANCE_RECORDS) {
        if (!canSeeSite(rec.siteCode)) continue;
        pushCandidate({
          ref: rec.ref,
          kind: rec.kind,
          title: rec.title,
          code: rec.code,
          detail: rec.detail,
          rank: rec.rank,
        });
      }

      // 13. proposal
      for (const proposal of state.proposals.values()) {
        const propStudy = ctx.getStudyByRefOrCode(
          proposal.study || canonicalStudyCode
        );
        const propCode = `PROP-${String(proposal.ref || "")
          .split("@")[0]
          .slice(-4)
          .toUpperCase()}`;
        pushCandidate({
          ref: proposal.ref.includes("@") ? proposal.ref : `${proposal.ref}@1`,
          kind: "proposal",
          title: proposal.intent || "Protocol Amendment Proposal",
          code: propCode,
          detail: `${propStudy?.code || canonicalStudyCode} · ${proposal.status || "Open"}`,
          rank: 0.77,
          searchExtra: `${proposal.rationale || ""} ${proposal.simulated_impact || ""} ${proposal.plan?.title || ""}`,
        });
      }

      const deduped = new Map();
      for (const candidate of rawCandidates) {
        if (lower) {
          const matchesDirect = candidate._bag.includes(lower);
          const matchesAllTokens =
            normalizedTokens.length > 0 &&
            normalizedTokens.every((tok) => candidate._bag.includes(tok));
          if (!matchesDirect && !matchesAllTokens) continue;
        }
        const dedupeKey = `${candidate.kind}:${String(candidate.ref).split("@")[0]}`;
        const existing = deduped.get(dedupeKey);
        if (
          !existing ||
          Number(candidate.exact_code) > Number(existing.exact_code) ||
          (candidate.exact_code === existing.exact_code &&
            candidate.rank > existing.rank)
        ) {
          deduped.set(dedupeKey, candidate);
        }
      }

      return [...deduped.values()]
        .sort((a, b) => {
          if (a.exact_code !== b.exact_code) {
            return Number(b.exact_code) - Number(a.exact_code);
          }
          if (b.rank !== a.rank) {
            return b.rank - a.rank;
          }
          return a.title.localeCompare(b.title);
        })
        .slice(0, Number(limit) || 25)
        .map(({ _bag, ...hit }) => hit);
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
        study = ctx.ensureStudyByCode(trimmedStudy.toUpperCase());
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
      const studySiteRef = resolveStudySiteRefForStudy(
        study.code,
        effectiveSiteCode
      );

      return {
        data: {
          study_ref: studyRef,
          study_code: study.code,
          study_title: study.title,
          study_site_ref: `${studySiteRef}@1`,
          site_code: effectiveSiteCode,
          site_title:
            effectiveSiteCode === "SITE-102"
              ? "US Regional Trial Unit 2"
              : `US Academic Clinical Research Center (${effectiveSiteCode})`,
          enrollment_ref: `${subjectEnrollmentRef(matched)}@1`,
          subject_code: matched.subject_id,
          status: matched.status || "SCREENED",
        },
      };
    },

    getNode,

    getNodes,

    getZoomPath(nodeRef, caller = null) {
      const refStr = String(nodeRef || "");
      const unpinned = refStr.split("@")[0];
      const canonicalStudy =
        state.studies.get(DEMO_STUDY.code || "CADENCE-101") ||
        state.studies.get("DEMO-01");

      const buildStudyStep = (study) => ({
        level: "study",
        label: study.code || study.title,
        ref: study.ref,
        mask: null,
      });

      const buildStudySiteStep = (studyCode, siteCode, explicitRef = null) => {
        const sitePermitted = !caller || isSitePermitted(caller, siteCode);
        return {
          level: "study_site",
          label: sitePermitted ? siteCode : null,
          ref:
            explicitRef ||
            resolveStudySiteRefForStudy(studyCode, siteCode || "SITE-101"),
          mask: sitePermitted ? null : "NotPermitted",
        };
      };

      const buildEnrollmentStep = (subj, fallbackRef = null) => {
        const siteCode = subj?.site_id || "SITE-101";
        const sitePermitted = !caller || isSitePermitted(caller, siteCode);
        return {
          level: "enrollment",
          label: sitePermitted ? subj?.subject_id || "SUBJ-101-001" : null,
          ref: fallbackRef || (subj ? subjectEnrollmentRef(subj) : null),
          mask: sitePermitted ? null : "NotPermitted",
        };
      };

      if (refStr.startsWith("organization:")) {
        const node = getNode(nodeRef, caller);
        return [
          {
            level: "organization",
            label: node.code || node.title,
            ref: node.ref,
            mask: node.mask ?? null,
          },
        ];
      }
      if (refStr.startsWith("site:")) {
        const node = getNode(nodeRef, caller);
        return [
          {
            level: "site",
            label: node.mask ? null : node.code || node.title,
            ref: node.ref,
            mask: node.mask ?? null,
          },
        ];
      }
      if (refStr.startsWith("enrollment:")) {
        const rawId = unpinned.replace(/^enrollment:/, "");
        const subj = resolveEnrollmentByIdOrCode(refStr);
        const study = subj
          ? ctx.getStudyByRefOrCode(subj.study_id)
          : state.studies.get("DEMO-01");
        const siteCode = subj?.site_id || "SITE-101";
        const sitePermitted = !caller || isSitePermitted(caller, siteCode);
        return [
          buildStudyStep(study),
          buildStudySiteStep(study.code, siteCode),
          {
            level: "enrollment",
            label: sitePermitted ? subj?.subject_id || rawId.slice(-6) : null,
            ref: refStr,
            mask: sitePermitted ? null : "NotPermitted",
          },
        ];
      }
      if (refStr.startsWith("study_site:")) {
        const linked = findLinkedStudySiteByRef(refStr);
        const siteCode =
          linked?.site_code ||
          resolveSiteCodeFromRef(refStr, state.subjects, state.studySites);
        const study = linked?.study_code
          ? ctx.getStudyByRefOrCode(linked.study_code)
          : state.studies.get("DEMO-01");
        return [
          buildStudyStep(study),
          buildStudySiteStep(study.code, siteCode, refStr),
        ];
      }
      if (refStr.startsWith("collection_instance:")) {
        const rawFormId = unpinned.replace(/^collection_instance:/, "");
        if (rawFormId === CADENCE_101_FORM_UUID) {
          const subj = findCanonicalCadenceParticipant();
          const node = getNode(nodeRef, caller);
          return [
            buildStudyStep(canonicalStudy),
            buildStudySiteStep(canonicalStudy.code, "SITE-101"),
            buildEnrollmentStep(subj),
            {
              level: "collection_instance",
              label: node.mask ? null : node.code || node.title,
              ref: nodeRef,
              mask: node.mask ?? null,
            },
          ];
        }
        const form =
          state.forms.get(rawFormId) || state.forms.get(DEFAULT_FORM_UUID);
        const subj = form
          ? resolveEnrollmentByIdOrCode(form.enrollment_id)
          : null;
        const study = subj
          ? ctx.getStudyByRefOrCode(subj.study_id)
          : state.studies.get("DEMO-01");
        const siteCode = subj?.site_id || "SITE-101";
        const node = getNode(nodeRef, caller);
        return [
          buildStudyStep(study),
          buildStudySiteStep(study.code, siteCode),
          buildEnrollmentStep(subj),
          {
            level: "collection_instance",
            label: node.mask ? null : node.code || node.title,
            ref: nodeRef,
            mask: node.mask ?? null,
          },
        ];
      }
      if (refStr.startsWith("observation:")) {
        const cleanObsId = unpinned.replace(/^observation:/, "");
        const node = getNode(nodeRef, caller);
        if (
          cleanObsId === CADENCE_101_OBS_UUID ||
          cleanObsId === BLINDED_OBS_UUID
        ) {
          const subj = findCanonicalCadenceParticipant();
          return [
            buildStudyStep(canonicalStudy),
            buildStudySiteStep(canonicalStudy.code, "SITE-101"),
            buildEnrollmentStep(subj),
            {
              level: "collection_instance",
              label: node.mask ? null : "form-vital-signs",
              ref: `collection_instance:${CADENCE_101_FORM_UUID}@1`,
              mask: node.mask === "NotPermitted" ? "NotPermitted" : null,
            },
            {
              level: "observation",
              label: node.mask ? null : node.code || node.title,
              ref: nodeRef,
              mask: node.mask ?? null,
            },
          ];
        }
        for (const form of state.forms.values()) {
          if (
            (form.items || []).some((it) => it.observation_id === cleanObsId)
          ) {
            const subj = resolveEnrollmentByIdOrCode(form.enrollment_id);
            const study = subj
              ? ctx.getStudyByRefOrCode(subj.study_id)
              : state.studies.get("DEMO-01");
            const siteCode = subj?.site_id || "SITE-101";
            return [
              buildStudyStep(study),
              buildStudySiteStep(study.code, siteCode),
              buildEnrollmentStep(subj),
              {
                level: "collection_instance",
                label: node.mask ? null : form.collection_spec_id,
                ref: `collection_instance:${form.form_id}@1`,
                mask: node.mask === "NotPermitted" ? "NotPermitted" : null,
              },
              {
                level: "observation",
                label: node.mask ? null : node.code || node.title,
                ref: nodeRef,
                mask: node.mask ?? null,
              },
            ];
          }
        }
      }
      if (refStr.startsWith("form_query:") || refStr.startsWith("query:")) {
        const rawQueryId = unpinned.replace(/^(form_query|query):/, "");
        const node = getNode(nodeRef, caller);
        if (
          rawQueryId === CADENCE_101_QUERY_UUID ||
          rawQueryId === BLINDED_QUERY_UUID
        ) {
          const subj = findCanonicalCadenceParticipant();
          return [
            buildStudyStep(canonicalStudy),
            buildStudySiteStep(canonicalStudy.code, "SITE-101"),
            buildEnrollmentStep(subj),
            {
              level: "collection_instance",
              label: node.mask ? null : "form-vital-signs",
              ref: `collection_instance:${CADENCE_101_FORM_UUID}@1`,
              mask: node.mask === "NotPermitted" ? "NotPermitted" : null,
            },
            {
              level: "observation",
              label: node.mask ? null : "SYSBP",
              ref: `observation:${CADENCE_101_OBS_UUID}@1`,
              mask: node.mask ?? null,
            },
            {
              level: node.kind,
              label: node.mask ? null : node.code || node.title,
              ref: nodeRef,
              mask: node.mask ?? null,
            },
          ];
        }
        const q = state.queries.get(rawQueryId);
        if (q) {
          const subj = resolveEnrollmentByIdOrCode(q.enrollment_id);
          const study = subj
            ? ctx.getStudyByRefOrCode(subj.study_id)
            : state.studies.get("DEMO-01");
          const siteCode = q.site_code || subj?.site_id || "SITE-101";
          const steps = [
            buildStudyStep(study),
            buildStudySiteStep(study.code, siteCode),
            buildEnrollmentStep(subj),
          ];
          if (q.form_id) {
            steps.push({
              level: "collection_instance",
              label: node.mask ? null : q.collection_spec_id || "form",
              ref: `collection_instance:${q.form_id}@1`,
              mask: node.mask === "NotPermitted" ? "NotPermitted" : null,
            });
          }
          if (q.observation_id) {
            steps.push({
              level: "observation",
              label: node.mask ? null : q.item_id || "observation",
              ref: `observation:${q.observation_id}@1`,
              mask: node.mask ?? null,
            });
          }
          steps.push({
            level: node.kind,
            label: node.mask ? null : node.code || node.title,
            ref: nodeRef,
            mask: node.mask ?? null,
          });
          return steps;
        }
      }
      if (refStr.startsWith("validation_finding:")) {
        const rawFindingId = unpinned.replace(/^validation_finding:/, "");
        const node = getNode(nodeRef, caller);
        if (
          rawFindingId === CADENCE_101_FINDING_UUID ||
          rawFindingId === BLINDED_FINDING_UUID
        ) {
          return [
            buildStudyStep(canonicalStudy),
            buildStudySiteStep(canonicalStudy.code, "SITE-101"),
            {
              level: "validation_finding",
              label: node.mask ? null : node.code || node.title,
              ref: nodeRef,
              mask: node.mask ?? null,
            },
          ];
        }
        const finding = (state.validationFindings || []).find(
          (f) =>
            String(f.ref || "").split("@")[0] === unpinned ||
            String(f.finding_id || f.id || "") === rawFindingId
        );
        const study = finding?.study_code
          ? ctx.getStudyByRefOrCode(finding.study_code)
          : canonicalStudy;
        const steps = [buildStudyStep(study)];
        if (finding?.site_code) {
          steps.push(buildStudySiteStep(study.code, finding.site_code));
        }
        steps.push({
          level: "validation_finding",
          label: node.mask ? null : node.code || node.title,
          ref: nodeRef,
          mask: node.mask ?? null,
        });
        return steps;
      }
      if (refStr.startsWith("proposal:")) {
        const matchingProposal =
          state.proposals.get(nodeRef) ||
          state.proposals.get(unpinned) ||
          [...state.proposals.values()].find(
            (p) => String(p.ref || "").split("@")[0] === unpinned
          );
        const study = matchingProposal?.study
          ? ctx.getStudyByRefOrCode(matchingProposal.study)
          : canonicalStudy;
        const node = getNode(nodeRef, caller);
        return [
          buildStudyStep(study),
          {
            level: "proposal",
            label: node.mask ? null : node.title || node.code,
            ref: nodeRef,
            mask: node.mask ?? null,
          },
        ];
      }
      if (refStr.startsWith("protocol_version:")) {
        const node = getNode(nodeRef, caller);
        return [
          buildStudyStep(canonicalStudy),
          {
            level: "protocol_version",
            label: node.code || node.title,
            ref: nodeRef,
            mask: null,
          },
        ];
      }
      const govRecord = findGovernanceRecord(nodeRef);
      if (govRecord) {
        const node = getNode(nodeRef, caller);
        const steps = [];
        if (govRecord.studyCode) {
          const study = ctx.getStudyByRefOrCode(govRecord.studyCode);
          steps.push(buildStudyStep(study));
          if (govRecord.siteCode) {
            steps.push(buildStudySiteStep(study.code, govRecord.siteCode));
          }
        }
        steps.push({
          level: govRecord.kind,
          label: node.mask ? null : node.code || node.title,
          ref: nodeRef,
          mask: node.mask ?? null,
        });
        return steps;
      }

      const study = refStr.startsWith("study:")
        ? ctx.getStudyByRefOrCode(refStr)
        : state.studies.get("DEMO-01");
      const steps = [buildStudyStep(study)];
      if (
        nodeRef !== study.ref &&
        refStr.split("@")[0] !== study.ref.split("@")[0]
      ) {
        const node = getNode(nodeRef, caller);
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
        const study = ctx.getStudyByRefOrCode(nodeRef);
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
                ref: subjectEnrollmentRef(s),
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
              ref: subjectEnrollmentRef(s),
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
        const subj = resolveEnrollmentByIdOrCode(nodeRef);
        const canonicalEnrollmentUuid = subj
          ? subjectEnrollmentUuid(subj)
          : enrollmentId;
        const siteCode = subj?.site_id || null;
        let forms = [...state.forms.values()].filter(
          (f) =>
            f.enrollment_id === enrollmentId ||
            (subj &&
              (f.enrollment_id === subj.id ||
                f.enrollment_id === canonicalEnrollmentUuid))
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
                ref: `allocation:${canonicalEnrollmentUuid}`,
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

    getLineage(nodeRef, direction = "upstream", depth = 3, caller = null) {
      const now = new Date().toISOString();
      const startNode = getNode(nodeRef, caller);
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
            recorded_at: "2026-08-01T09:00:00Z",
            valid_from: "2026-08-01T09:00:00Z",
          },
          {
            from: "n1",
            to: "n2",
            kind: "belongs_to",
            activity: null,
            recorded_at: "2026-07-01T09:00:00Z",
            valid_from: "2026-07-01T09:00:00Z",
          },
        ],
        truncated: false,
      };
    },

    getDependents,
  };

  return { seed() {}, methods };
}
