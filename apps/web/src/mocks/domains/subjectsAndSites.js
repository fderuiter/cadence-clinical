import { DEMO_PARTICIPANTS, DEMO_SITES, DEMO_STUDY } from "demo-fixtures";
import { demoNowIso } from "../../utils/demoMode.js";
import { isCallerBlinded } from "../demoPolicy.js";
import {
  ENROLLMENT_1001_UUID,
  ENROLLMENT_1002_UUID,
  cloneJson,
  makeUuidFromIndex,
} from "./shared.js";

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
        greenlight_certified_at: idx === 0 ? "2026-01-11T09:00:00Z" : null,
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
        greenlight_certified_at: "2026-07-20T14:00:00Z",
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

export function createSubjectsAndSitesDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.subjects = [];
  state.siteCompliance = new Map();
  state.startupMatrices = new Map();
  state.ipKits = [];

  function seed() {
    const canonicalStudyCode = DEMO_STUDY.code || "CADENCE-101";
    const canonicalSubjects = DEMO_PARTICIPANTS.map((participant, idx) => ({
      id: participant.participantUuid || participant.enrollmentUuid,
      subject_id: participant.subjectCode,
      study_id: canonicalStudyCode,
      site_id: participant.siteCode,
      status: idx === 2 ? "ENROLLED" : "ACTIVE",
      active_protocol_version: idx === 1 ? "1.0" : "2.0",
      enrollment_index: idx + 1,
      enrollment_ref: participant.enrollmentRef,
      treatment_group: idx === 1 ? "ARM-B" : "ARM-A",
    }));

    state.subjects = [
      ...canonicalSubjects,
      {
        id: ENROLLMENT_1001_UUID,
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        status: "ENROLLED",
        active_protocol_version: "2.0",
        enrollment_index: 1,
        enrollment_ref: `enrollment:${ENROLLMENT_1001_UUID}`,
        treatment_group: "ARM-A",
      },
      {
        id: ENROLLMENT_1002_UUID,
        subject_id: "SUBJ-1002",
        study_id: "DEMO-01",
        site_id: "SITE-102",
        status: "SCREENED",
        active_protocol_version: "2.0",
        enrollment_index: 2,
        enrollment_ref: `enrollment:${ENROLLMENT_1002_UUID}`,
        treatment_group: null,
      },
    ];

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

    state.ipKits = [
      {
        id: "kit-0001",
        kit_number: "KIT-1001",
        lot_number: "LOT-2026A",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        status: "AVAILABLE",
        expiration_date: "2027-12-31T00:00:00Z",
      },
      {
        id: "kit-0002",
        kit_number: "KIT-1002",
        lot_number: "LOT-2026A",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        status: "DISPENSED",
        expiration_date: "2027-12-31T00:00:00Z",
      },
    ];
  }

  function createSubject(body) {
    const subjectCode = String(
      body?.subject_id || `SUBJ-${1000 + state.subjects.length + 1}`
    ).trim();
    const studyCode = String(body?.study_id || "DEMO-01").trim();
    const siteCode = body?.site_id || "SITE-101";

    const existing = state.subjects.find(
      (s) =>
        s.subject_id.toUpperCase() === subjectCode.toUpperCase() &&
        s.study_id.toUpperCase() === studyCode.toUpperCase()
    );
    if (existing) {
      return {
        id: existing.id,
        subject_id: existing.subject_id,
        study_id: existing.study_id,
        site_id: existing.site_id,
        status: existing.status,
        active_protocol_version: existing.active_protocol_version,
        encrypted_demographics: null,
        enrollment_ref: existing.enrollment_ref || `enrollment:${existing.id}`,
      };
    }

    ctx.ensureStudyByCode(studyCode);
    const idx = nextCounter();
    const createdId = makeUuidFromIndex("00001000", idx);
    const created = {
      id: createdId,
      subject_id: subjectCode,
      study_id: studyCode,
      site_id: siteCode,
      status: "SCREENED",
      active_protocol_version: "2.0",
      enrollment_index: state.subjects.length + 1,
      enrollment_ref: `enrollment:${createdId}`,
      treatment_group: null,
    };
    state.subjects.push(created);
    ctx.recordActivity(
      "execution.subject.registered",
      "execution",
      "enrollment",
      created.enrollment_ref,
      "usr-crc-01"
    );

    return {
      id: created.id,
      subject_id: created.subject_id,
      study_id: created.study_id,
      site_id: created.site_id,
      status: created.status,
      active_protocol_version: created.active_protocol_version,
      encrypted_demographics: null,
      enrollment_ref: created.enrollment_ref,
    };
  }

  function getSiteCompliance(
    siteId,
    studyId = "DEMO-01",
    milestone = "SITE_ACTIVATION"
  ) {
    const existing = state.siteCompliance.get(siteId);
    if (existing) {
      const isActivated = Boolean(existing.is_activated ?? existing.activated);
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
  }

  function getStartupMatrix(countryCode = "US") {
    const key = (countryCode || "US").toUpperCase();
    if (!state.startupMatrices.has(key)) {
      state.startupMatrices.set(key, createStartupMatrixForCountry(key));
    }
    return cloneJson(state.startupMatrices.get(key));
  }

  const methods = {
    listSubjects({ studyId, siteId, status, q, limit = 50, offset = 0 } = {}) {
      let filtered = [...state.subjects];
      if (studyId) {
        const byStudy = filtered.filter(
          (s) => s.study_id.toUpperCase() === String(studyId).toUpperCase()
        );
        if (byStudy.length > 0 || studyId !== "DEMO-01") {
          filtered =
            byStudy.length > 0
              ? byStudy
              : filtered.filter((s) => s.study_id === "DEMO-01");
        }
      }
      if (siteId) {
        filtered = filtered.filter((s) => s.site_id === siteId);
      }
      if (status) {
        filtered = filtered.filter(
          (s) => s.status.toUpperCase() === String(status).toUpperCase()
        );
      }
      if (q && String(q).trim()) {
        const needle = String(q).trim().toLowerCase();
        filtered = filtered.filter((s) =>
          s.subject_id.toLowerCase().includes(needle)
        );
      }
      const numOffset = Number(offset) || 0;
      const numLimit = Number(limit) || 50;
      return {
        items: cloneJson(
          filtered.slice(numOffset, numOffset + numLimit).map((s) => ({
            id: s.id,
            subject_id: s.subject_id,
            study_id: s.study_id,
            site_id: s.site_id,
            status: s.status,
            active_protocol_version: s.active_protocol_version || "2.0",
            enrollment_index: s.enrollment_index,
            enrollment_ref: s.enrollment_ref || `enrollment:${s.id}`,
          }))
        ),
        total: filtered.length,
        limit: numLimit,
        offset: numOffset,
      };
    },

    getSubjectCounts({ studyId, siteId } = {}) {
      let filtered = [...state.subjects];
      if (studyId) {
        const byStudy = filtered.filter(
          (s) => s.study_id.toUpperCase() === String(studyId).toUpperCase()
        );
        if (byStudy.length > 0 || studyId !== "DEMO-01") {
          filtered =
            byStudy.length > 0
              ? byStudy
              : filtered.filter((s) => s.study_id === "DEMO-01");
        }
      }
      if (siteId) {
        filtered = filtered.filter((s) => s.site_id === siteId);
      }
      const counts = {};
      for (const subj of filtered) {
        const key = String(subj.status || "").toUpperCase();
        if (!key) continue;
        counts[key] = (counts[key] || 0) + 1;
      }
      return {
        total: filtered.length,
        counts,
      };
    },

    createSubject,

    getSubjectDetail(subjectId, caller = null) {
      const normalizedKey = String(subjectId || "")
        .trim()
        .replace(/^(?:enrollment|participant):/i, "")
        .split("@")[0];
      const found =
        state.subjects.find(
          (s) =>
            s.subject_id.toUpperCase() === normalizedKey.toUpperCase() ||
            s.id === normalizedKey ||
            s.enrollment_ref === `enrollment:${normalizedKey}`
        ) || state.subjects[0];
      const shouldBlind =
        Boolean(caller) &&
        isCallerBlinded(caller) &&
        String(found.status || "").toUpperCase() !== "UNBLINDED";
      return {
        id: found.id,
        subject_id: found.subject_id,
        study_id: found.study_id,
        status: found.status,
        site_id: found.site_id,
        active_protocol_version: found.active_protocol_version || "2.0",
        enrollment_index: found.enrollment_index ?? 0,
        treatment_group: shouldBlind
          ? "BLINDED"
          : (found.treatment_group ?? null),
        randomization_seed: null,
        investigational_product_id: shouldBlind
          ? found.investigational_product_id
            ? "BLINDED"
            : null
          : (found.investigational_product_id ?? null),
        enrollment_ref: found.enrollment_ref || `enrollment:${found.id}`,
      };
    },

    updateSubjectState(subjectId, body) {
      const nextStatus = String(
        body?.status || body?.state || body?.target || "ENROLLED"
      ).toUpperCase();
      const normalizedKey = String(subjectId || "")
        .trim()
        .replace(/^(?:enrollment|participant):/i, "")
        .split("@")[0];
      let found = state.subjects.find(
        (s) =>
          s.subject_id.toUpperCase() === normalizedKey.toUpperCase() ||
          s.id === normalizedKey ||
          s.enrollment_ref === `enrollment:${normalizedKey}`
      );
      if (!found) {
        const created = createSubject({
          subject_id: subjectId,
          study_id: "DEMO-01",
          site_id: "SITE-101",
        });
        found = state.subjects.find((s) => s.id === created.id);
      }
      found.status = nextStatus;
      if (nextStatus === "UNBLINDED") {
        found.treatment_group =
          found.unblinded_treatment_group || found.treatment_group || "ACTIVE";
        found.investigational_product_id =
          found.investigational_product_id || "KIT-1001";
      }
      ctx.recordActivity(
        "execution.subject.state_changed",
        "execution",
        "enrollment",
        found.enrollment_ref || `enrollment:${found.id}`,
        "usr-crc-01"
      );
      return {
        id: found.id,
        subject_id: found.subject_id,
        study_id: found.study_id,
        site_id: found.site_id,
        status: found.status,
        active_protocol_version: found.active_protocol_version,
        encrypted_demographics: null,
        enrollment_ref: found.enrollment_ref || `enrollment:${found.id}`,
      };
    },

    getSiteCompliance,

    activateSite(siteId, studyId = "DEMO-01") {
      const compliance = getSiteCompliance(siteId, studyId);
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

    getStartupMatrix,

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
      const matrix = getStartupMatrix(countryCode);
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

  return { seed, methods };
}
