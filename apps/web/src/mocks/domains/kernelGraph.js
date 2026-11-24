import { DEMO_STUDY } from "demo-fixtures";
import { isCallerBlinded, isSitePermitted } from "../demoPolicy.js";
import {
  DEFAULT_FORM_UUID,
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

  function getNode(nodeRef, caller = null) {
    if (String(nodeRef).startsWith("study:")) {
      return cloneJson(ctx.getStudyByRefOrCode(nodeRef));
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
    if (String(nodeRef).startsWith("plan:")) {
      const unversionedPlan = String(nodeRef).split("@")[0];
      const matchingProposal = [...state.proposals.values()].find(
        (p) => String(p.plan?.ref || "").split("@")[0] === unversionedPlan
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
    if (String(nodeRef).startsWith("proposal:")) {
      const unversionedProp = String(nodeRef).split("@")[0];
      const matchingProposal =
        state.proposals.get(unversionedProp) ||
        [...state.proposals.values()].find(
          (p) => String(p.ref || "").split("@")[0] === unversionedProp
        );
      return applyNodeVisibilityMask(
        {
          ref: nodeRef,
          kind: "proposal",
          title: matchingProposal?.intent || "Protocol Amendment Proposal",
          code: String(unversionedProp).split(":")[1]?.slice(-4) || "4001",
          status: matchingProposal?.status || "Open",
          mask: null,
        },
        caller
      );
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
  }

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
          ctx.ensureStudyByCode(trimmed.toUpperCase());
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
          ref: subjectEnrollmentRef(subj),
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
          enrollment_ref: `${subjectEnrollmentRef(matched)}@1`,
          subject_code: matched.subject_id,
          status: matched.status || "SCREENED",
        },
      };
    },

    getNode,

    getZoomPath(nodeRef, caller = null) {
      const refStr = String(nodeRef || "");
      if (refStr.startsWith("enrollment:")) {
        const rawId = refStr.replace(/^enrollment:/, "").split("@")[0];
        const subj = resolveEnrollmentByIdOrCode(refStr);
        const study = subj
          ? ctx.getStudyByRefOrCode(subj.study_id)
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
        ? ctx.getStudyByRefOrCode(refStr)
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
