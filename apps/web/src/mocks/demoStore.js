import {
  DEMO_PARTICIPANTS,
  DEMO_SITES,
  DEMO_SPONSOR,
  DEMO_STUDY,
  DEMO_TENANT_UUID as MANIFEST_DEMO_TENANT_UUID,
  getDemoPersonaForWorkspace,
} from "demo-fixtures";
import {
  DEFAULT_CATEGORIES,
  DEFAULT_MOCK_ARTICLES,
  getFallbackContextualHelp,
} from "../api/knowledge";
import {
  DEMO_ORGANIZATIONS,
  DEMO_PROTOCOL_TEMPLATES,
  buildImportedDraftDesign,
  createInitialDemoStudyDirectoryEntry,
} from "./demoStudyImport.js";
import {
  routeDemoTicketQueue,
  validateDemoTicketNoDirectPii,
} from "../api/ticketsApi";
import {
  findCommercialTier,
  PRE_RELEASE_ENVIRONMENT_NOTICE,
} from "../catalog/productCatalog.js";
import { demoNowIso, demoNowMs } from "../utils/demoMode.js";

const DEMO_TENANT_UUID =
  MANIFEST_DEMO_TENANT_UUID || "00000000-0000-0000-0000-000000000010"; // deid-ignore
const DEMO_SUBSCRIPTION_UUID = "00000000-0000-0000-0000-000000000011"; // deid-ignore

const STUDY_UUID = "00000000-0000-0000-0000-000000000001"; // deid-ignore
const STUDY_REF = `study:${STUDY_UUID}`;
const VERSION_V1_UUID = "00000000-0000-0000-0000-000000000101"; // deid-ignore
const VERSION_V2_UUID = "00000000-0000-0000-0000-000000000102"; // deid-ignore
const VERSION_V1_REF = `protocol_version:${VERSION_V1_UUID}`;
const VERSION_V2_REF = `protocol_version:${VERSION_V2_UUID}`;
const SITE_101_UUID = "00000000-0000-0000-0000-000000000201"; // deid-ignore
const SITE_102_UUID = "00000000-0000-0000-0000-000000000202"; // deid-ignore
const ENROLLMENT_1001_UUID = "00000000-0000-0000-0000-000000001001"; // deid-ignore
const ENROLLMENT_1002_UUID = "00000000-0000-0000-0000-000000001002"; // deid-ignore
const OBS_1_UUID = "00000000-0000-0000-0000-000000000001"; // deid-ignore
const OBS_2_UUID = "00000000-0000-0000-0000-000000000002"; // deid-ignore
const OBS_3_UUID = "00000000-0000-0000-0000-000000000003"; // deid-ignore
const OBS_4_UUID = "00000000-0000-0000-0000-000000000004"; // deid-ignore
const DEFAULT_FORM_UUID = "00000000-0000-0000-0000-000000002001"; // deid-ignore
const DEFAULT_QUERY_UUID = "00000000-0000-0000-0000-000000003001"; // deid-ignore
const PROPOSAL_1_REF = "proposal:00000000-0000-0000-0000-000000004001"; // deid-ignore

function cloneJson(value) {
  return JSON.parse(JSON.stringify(value));
}

function makeUuidFromIndex(prefixHex, index) {
  const suffix = String(index).padStart(12, "0").slice(-12);
  return `${prefixHex}-0000-4000-8000-${suffix}`; // deid-ignore
}

function computeContentHash(items) {
  const raw = JSON.stringify(
    items.map((item) => ({
      id: item.item_id,
      rev: item.revision,
      val: item.value,
    }))
  );
  let hash = 0x811c9dc5;
  for (let i = 0; i < raw.length; i += 1) {
    hash ^= raw.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return `sha256-demo-${(hash >>> 0).toString(16).padStart(8, "0")}`;
}

function buildDecision(capability, resource, permitted = true, reason = null) {
  return {
    capability,
    verb_class: capability.includes("read") ? "read" : "write",
    resource,
    permitted,
    reason,
    explanation: {
      subject: "usr-demo-operator",
      claim: permitted
        ? `Capability ${capability} permitted in isolated demo session.`
        : `Capability ${capability} restricted: ${reason || "policy"}.`,
      producer: "demo-kernel-authorizer",
      steps: [
        {
          claim: "Evaluated active demo session role bindings",
          evidence: [`resource=${resource}`, `capability=${capability}`],
        },
      ],
      conditions: [
        {
          code: "DEMO_SESSION_ACTIVE",
          description: "Synthetic demo workspace authorization active",
          met: permitted,
          evidence: ["mode=demo"],
        },
      ],
      next_actions: [],
    },
  };
}

function createInitialObservations() {
  const obs1Ref = `observation:${OBS_1_UUID}`;
  const obs2Ref = `observation:${OBS_2_UUID}`;
  const obs3Ref = `observation:${OBS_3_UUID}`;
  const enrollmentRef = `enrollment:${ENROLLMENT_1001_UUID}`;

  const makeObs = (ref, concept, value, recordedAt) => ({
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
        reason_for_change: "Initial baseline assessment entry",
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
        reason_for_change: "Initial baseline assessment entry",
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
        "2026-08-01T09:15:00Z"
      ),
    ],
    [
      obs2Ref,
      makeObs(
        obs2Ref,
        "DIABP",
        { type: "quantity", value: "82", unit: "mm[Hg]", comparator: null },
        "2026-08-01T09:16:00Z"
      ),
    ],
    [
      obs3Ref,
      makeObs(
        obs3Ref,
        "PULSE",
        { type: "quantity", value: "72", unit: "/min", comparator: null },
        "2026-08-01T09:17:00Z"
      ),
    ],
  ]);
}

function resolveCollectionTitle(collectionSpecId) {
  const normalized = String(collectionSpecId || "")
    .trim()
    .toLowerCase();
  if (normalized === "form-vital-signs" || normalized === "form-vitals") {
    return "Vital Signs (CDASH VS)";
  }
  if (normalized === "form-chem-panel") {
    return "Clinical Chemistry Panel (CDASH LB)";
  }
  if (normalized === "form-adverse-event") {
    return "Adverse Event (CDASH AE)";
  }
  return null;
}

function resolveSiteUuid(siteCode) {
  const normalized = String(siteCode || "SITE-101")
    .trim()
    .toUpperCase();
  const manifestSite = DEMO_SITES.find((s) => s.code === normalized);
  if (manifestSite && normalized !== "SITE-101" && normalized !== "SITE-102") {
    return manifestSite.studySiteUuid;
  }
  if (normalized === "SITE-101") return SITE_101_UUID;
  if (normalized === "SITE-102") return SITE_102_UUID;
  const digits = normalized.replace(/\D/g, "") || "299";
  return makeUuidFromIndex("00000200", Number(digits));
}

function resolveSiteCodeFromRef(nodeRef, subjects = []) {
  const raw = String(nodeRef || "").replace(/^study_site:/, "");
  const manifestSite = DEMO_SITES.find((s) => s.studySiteUuid === raw);
  if (manifestSite) return manifestSite.code;
  if (raw === SITE_101_UUID) return "SITE-101";
  if (raw === SITE_102_UUID) return "SITE-102";
  for (const subj of subjects) {
    if (subj.site_id && resolveSiteUuid(subj.site_id) === raw) {
      return subj.site_id;
    }
  }
  return raw.toUpperCase().startsWith("SITE-") ? raw.toUpperCase() : "SITE-101";
}

function createInitialForm() {
  const items = [
    {
      item_id: "item-sysbp",
      concept: "SYSBP",
      unit: "mm[Hg]",
      value_kind: "quantity",
      required: true,
      value: {
        type: "quantity",
        value: "128",
        unit: "mm[Hg]",
        comparator: null,
      },
      observation_id: OBS_1_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:15:00Z",
    },
    {
      item_id: "item-diabp",
      concept: "DIABP",
      unit: "mm[Hg]",
      value_kind: "quantity",
      required: true,
      value: {
        type: "quantity",
        value: "82",
        unit: "mm[Hg]",
        comparator: null,
      },
      observation_id: OBS_2_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:16:00Z",
    },
    {
      item_id: "item-pulse",
      concept: "PULSE",
      unit: "/min",
      value_kind: "quantity",
      required: true,
      value: {
        type: "quantity",
        value: "72",
        unit: "/min",
        comparator: null,
      },
      observation_id: OBS_3_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:17:00Z",
    },
    {
      item_id: "item-notes",
      concept: "VSCOMM",
      unit: null,
      value_kind: "text",
      required: false,
      value: { type: "text", value: "Seated rest 5 minutes prior to cuff." },
      observation_id: OBS_4_UUID,
      revision: 1,
      recorded_by: "usr-crc-01",
      recorded_at: "2026-08-01T09:18:00Z",
    },
  ];

  return {
    form_id: DEFAULT_FORM_UUID,
    enrollment_id: ENROLLMENT_1001_UUID,
    participant_id: "00000000-0000-0000-0000-000000005001", // deid-ignore
    study_id: STUDY_UUID,
    study_site_id: SITE_101_UUID,
    protocol_version_id: VERSION_V2_UUID,
    collection_spec_id: "form-vital-signs",
    collection_title: "Vital Signs (CDASH VS)",
    planned_activity_id: "visit-baseline",
    collected_at: "2026-08-01T09:15:00Z",
    opened_by: "usr-crc-01",
    opened_at: "2026-08-01T09:10:00Z",
    version: 1,
    complete: true,
    missing_required: [],
    items,
    locks: [],
    assertions: [],
    signatures: [],
    content_hash: computeContentHash(items),
  };
}

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

export function createWebDemoStore() {
  let sequenceCounter = 10;

  const nextCounter = () => {
    sequenceCounter += 1;
    return sequenceCounter;
  };

  const state = {
    studies: new Map(),
    organizations: [],
    sites: [],
    studySites: new Map(),
    importedProtocolVersions: new Map(),
    protocolDesignSummaries: new Map(),
    idempotentStudyResponses: new Map(),
    idempotentImportResponses: new Map(),
    subjects: [],
    observations: createInitialObservations(),
    forms: new Map(),
    queries: new Map(),
    proposals: new Map(),
    activityEntries: [],
    siteCompliance: new Map(),
    startupMatrices: new Map(),
    milestones: [],
    monitoringVisits: [],
    craWorkload: [],
    recruitment: [],
    doaLogs: new Map(),
    grants: [],
    financialTemplates: [],
    financialAdjustments: [],
    procedureGrids: [],
    grantAmendments: [],
    rules: [],
    knowledgeCategories: cloneJson(DEFAULT_CATEGORIES),
    knowledgeArticles: cloneJson(DEFAULT_MOCK_ARTICLES),
    tickets: [],
    ipKits: [],
    locks: [],
    participantWallets: [],
    stipendRules: [],
    expenseClaims: [],
    walletTransactions: [],
  };

  function seedInitialState() {
    sequenceCounter = 10;
    state.observations = createInitialObservations();
    state.observations.set(`observation:${OBS_4_UUID}`, {
      ref: `observation:${OBS_4_UUID}`,
      enrollment_ref: `enrollment:${ENROLLMENT_1001_UUID}`,
      concept: "VSCOMM",
      value: { type: "text", value: "Seated rest 5 minutes prior to cuff." },
      observed_at: "2026-08-01T09:18:00Z",
      version: 1,
      locks: [],
      revisions: [
        {
          number: 1,
          value: {
            type: "text",
            value: "Seated rest 5 minutes prior to cuff.",
          },
          reason_for_change: "Initial baseline vital signs note",
          recorded_by: "usr-crc-01",
          recorded_at: "2026-08-01T09:18:00Z",
          recorded_until: null,
        },
      ],
      assertions: [],
      audit: [
        {
          sequence: 1,
          recorded_at: "2026-08-01T09:18:00Z",
          actor_id: "usr-crc-01",
          action: "observation.record",
          subject: `observation:${OBS_4_UUID}`,
          version: 1,
          reason_for_change: "Initial baseline vital signs note",
        },
      ],
    });

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
    state.sites = DEMO_SITES.map((site) => ({
      ref: `site:${site.siteUuid}@1`,
      code: site.code,
      name: site.name,
      country: site.country,
      institution_ref: `organization:${site.institutionUuid}@1`,
      institution_code: site.institutionCode,
      institution_name: `${site.name} Institution`,
    }));
    state.importedProtocolVersions = new Map();
    state.protocolDesignSummaries = new Map();
    state.idempotentStudyResponses = new Map();
    state.idempotentImportResponses = new Map();

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

    const buildStudySitesForStudy = (studyRef) =>
      DEMO_SITES.map((site, idx) => ({
        ref:
          studyRef === canonicalStudyEntry.ref
            ? `${site.studySiteRef}@1`
            : `study_site:${makeUuidFromIndex("00000400", idx + 1)}@1`,
        study_ref: String(studyRef).split("@")[0],
        site_ref: `site:${site.siteUuid}@1`,
        site_code: site.code,
        site_name: site.name,
        country: site.country,
        institution_ref: `organization:${site.institutionUuid}@1`,
        institution_code: site.institutionCode,
        institution_name: `${site.name} Institution`,
        status: site.complianceStage === "CONDUCT" ? "Active" : "Selected",
      }));

    state.studySites = new Map([
      [canonicalStudyCode, buildStudySitesForStudy(canonicalStudyEntry.ref)],
      ["DEMO-01", buildStudySitesForStudy(STUDY_REF)],
      [
        "STUDY-USDM-001",
        buildStudySitesForStudy("study:00000000-0000-0000-0000-000000000002"), // deid-ignore
      ],
    ]);

    const canonicalSubjects = DEMO_PARTICIPANTS.map((participant, idx) => ({
      id: participant.enrollmentUuid,
      subject_id: participant.subjectCode,
      study_id: canonicalStudyCode,
      site_id: participant.siteCode,
      status: idx === 2 ? "ENROLLED" : "ACTIVE",
      active_protocol_version: idx === 1 ? "1.0" : "2.0",
      enrollment_index: idx + 1,
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
        treatment_group: null,
      },
    ];

    const initialForm = createInitialForm();
    state.forms = new Map([[initialForm.form_id, initialForm]]);

    state.queries = new Map([
      [
        DEFAULT_QUERY_UUID,
        {
          query_id: DEFAULT_QUERY_UUID,
          form_id: DEFAULT_FORM_UUID,
          enrollment_id: ENROLLMENT_1001_UUID,
          item_id: "item-sysbp",
          observation_id: OBS_1_UUID,
          revision: 1,
          current_revision: 1,
          value_changed: false,
          status: "open",
          origin: "manual",
          owner_role: "site",
          assignee: null,
          priority: "high",
          opened_at: "2026-08-02T11:00:00Z",
          status_since: "2026-08-02T11:00:00Z",
          due_at: "2026-08-16T11:00:00Z",
          escalated_at: "2026-08-17T00:05:00Z",
          last_answered_by: null,
          text: "Confirm seated rest duration prior to baseline systolic BP.",
          messages: [
            {
              kind: "raised",
              text: "Confirm seated rest duration prior to baseline systolic BP.",
              by: "usr-cra-01",
              at: "2026-08-02T11:00:00Z",
              reason_for_change: "Source chart audit discrepancy",
            },
            {
              kind: "escalated",
              text: "14-day SLA reached; escalated to High priority.",
              by: "svc:execution-edit-checks",
              at: "2026-08-17T00:05:00Z",
              reason_for_change: "14-day SLA escalation",
            },
          ],
          raised_by: "usr-cra-01",
          raised_at: "2026-08-02T11:00:00Z",
          version: 2,
          study_id: STUDY_UUID,
          study_site_id: SITE_101_UUID,
          site_code: "SITE-101",
          subject_code: "SUBJ-1001",
          collection_spec_id: "form-vital-signs",
          planned_activity_id: "visit-baseline",
        },
      ],
      [
        "00000000-0000-0000-0000-000000003002", // deid-ignore
        {
          query_id: "00000000-0000-0000-0000-000000003002", // deid-ignore
          form_id: "00000000-0000-0000-0000-000000002002", // deid-ignore
          enrollment_id: ENROLLMENT_1002_UUID,
          item_id: "item-diabp",
          observation_id: OBS_2_UUID,
          revision: 1,
          current_revision: 2,
          value_changed: true,
          status: "answered",
          origin: "manual",
          owner_role: "monitor",
          assignee: null,
          priority: "normal",
          opened_at: "2026-08-04T09:15:00Z",
          status_since: "2026-08-05T14:20:00Z",
          due_at: null,
          escalated_at: null,
          last_answered_by: "usr-crc-01",
          text: "Verify baseline diastolic blood pressure against bedside strip.",
          messages: [
            {
              kind: "raised",
              text: "Verify baseline diastolic blood pressure against bedside strip.",
              by: "usr-cra-01",
              at: "2026-08-04T09:15:00Z",
              reason_for_change: "Routine SDV review",
            },
            {
              kind: "answered",
              text: "Observation updated to revision 2.",
              by: "usr-crc-01",
              at: "2026-08-05T14:20:00Z",
              reason_for_change: "Corrected from signed vitals flowsheet",
            },
          ],
          raised_by: "usr-cra-01",
          raised_at: "2026-08-04T09:15:00Z",
          version: 2,
          study_id: STUDY_UUID,
          study_site_id: SITE_102_UUID,
          site_code: "SITE-102",
          subject_code: "SUBJ-1002",
          collection_spec_id: "form-vital-signs",
          planned_activity_id: "visit-baseline",
        },
      ],
      [
        "00000000-0000-0000-0000-000000003003", // deid-ignore
        {
          query_id: "00000000-0000-0000-0000-000000003003", // deid-ignore
          form_id: "00000000-0000-0000-0000-000000002002", // deid-ignore
          enrollment_id: ENROLLMENT_1002_UUID,
          item_id: "item-hr",
          observation_id: OBS_3_UUID,
          revision: 1,
          current_revision: 1,
          value_changed: false,
          status: "open",
          origin: "system",
          owner_role: "site",
          assignee: null,
          priority: "normal",
          opened_at: "2026-08-10T08:00:00Z",
          status_since: "2026-08-10T08:00:00Z",
          due_at: "2026-08-24T08:00:00Z",
          escalated_at: null,
          last_answered_by: null,
          text: "[RULE-HR-RANGE] Resting heart rate outside expected 50-100 bpm window.",
          messages: [
            {
              kind: "raised",
              text: "[RULE-HR-RANGE] Resting heart rate outside expected 50-100 bpm window.",
              by: "svc:execution-edit-checks",
              at: "2026-08-10T08:00:00Z",
              reason_for_change: "Post-commit RulePack evaluation",
            },
          ],
          raised_by: "svc:execution-edit-checks",
          raised_at: "2026-08-10T08:00:00Z",
          version: 1,
          study_id: STUDY_UUID,
          study_site_id: SITE_102_UUID,
          site_code: "SITE-102",
          subject_code: "SUBJ-1002",
          collection_spec_id: "form-vital-signs",
          planned_activity_id: "visit-baseline",
        },
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
          proposed_at: "2026-08-03T10:00:00Z",
          expires_at: "2026-09-03T10:00:00Z",
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
        recorded: "2026-08-02T11:00:00Z",
        occurred_valid: "2026-08-02T11:00:00Z",
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
        recorded: "2026-08-01T09:15:00Z",
        occurred_valid: "2026-08-01T09:15:00Z",
        actor_id: "usr-crc-01",
        subject_kind: "observation",
        subject: `observation:${OBS_1_UUID}`,
        mask: null,
        correlation_id: "00000000-0000-4000-8000-000000000991", // deid-ignore
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

    state.milestones = [
      {
        id: "ms-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        milestone_name: "IRB Favorable Opinion",
        status: "COMPLETED",
        planned_date: "2026-07-01", // deid-ignore
        actual_date: "2026-06-28", // deid-ignore
      },
      {
        id: "ms-102",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        milestone_name: "Site Initiation Visit (SIV)",
        status: "IN_PROGRESS",
        planned_date: "2026-08-10", // deid-ignore
        actual_date: null,
      },
    ];

    state.monitoringVisits = [
      {
        id: "mv-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        visit_type: "INTERIM_MONITORING",
        status: "SCHEDULED",
        scheduled_date: "2026-08-25", // deid-ignore
        monitor_id: "usr-cra-01",
        sdv_percentage: 100,
      },
    ];

    state.craWorkload = [
      {
        cra_id: "usr-cra-01",
        cra_name: "Lead Clinical Monitor 01",
        assigned_sites: 2,
        pending_sdv_items: 4,
        utilization_pct: 68,
      },
    ];

    state.recruitment = [
      {
        id: "rec-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        target_enrollment: 15,
        screened_count: 6,
        enrolled_count: 4,
        screen_failure_count: 2,
      },
    ];

    state.doaLogs = new Map([
      [
        "SITE-101",
        {
          site_id: "SITE-101",
          pi_name: "Investigator Lead 01, MD",
          active_delegations: [
            {
              record_id: "doa-staff-01",
              site_id: "SITE-101",
              staff_user_id: "usr-crc-01",
              staff_name: "Site Coordinator 01",
              staff_role: "CRC",
              task_codes: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
              status: "ACTIVE",
              start_date: "2026-07-15", // deid-ignore
              pi_signed_off_by: "usr-pi-01",
              pi_signed_off_at: "2026-07-15T10:00:00Z", // deid-ignore
            },
          ],
          revoked_delegations: [],
          delegated_staff: [
            {
              id: "doa-staff-01",
              staff_id: "usr-crc-01",
              staff_name: "Site Coordinator 01",
              role: "CRC",
              tasks: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
              status: "ACTIVE",
              signed_at: "2026-07-15T10:00:00Z",
            },
          ],
          audit_history: [
            {
              id: "doa-aud-01",
              action: "DELEGATION_APPROVED",
              actor: "Investigator Lead 01, MD",
              timestamp: "2026-07-15T10:00:00Z",
              reason: "Initial site delegation sign-off",
            },
          ],
        },
      ],
    ]);

    state.grants = [
      {
        id: "grant-101",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        currency: "USD",
        total_budget: 145000,
        disbursed_amount: 32500, // deid-ignore
        holdback_percentage: 10,
        overhead_percentage: 28,
        version_index: 1,
        status: "ACTIVE",
      },
    ];

    state.financialTemplates = [
      {
        id: "ftmpl-01",
        template_name: "Academic Medical Center Tier 1",
        tier_level: "TIER_1",
        currency: "USD",
        overhead_percentage: 28,
        withholding_percentage: 10,
        fixed_startup_fee: 8500,
        fixed_closeout_fee: 3500,
        irb_fee: 2500,
        patient_stipend_amount: 75,
        travel_reimbursement_rate: 50,
      },
    ];

    state.financialAdjustments = [
      {
        id: "fadj-01",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        description: "Unscheduled safety lab handling fee",
        amount: 450,
        currency: "USD",
        status: "APPROVED",
      },
    ];

    state.procedureGrids = [
      {
        id: "pgrid-01",
        grant_id: "grant-101",
        procedure_code: "PROC-VS",
        procedure_name: "Vital Signs & 12-Lead ECG",
        visit_name: "Baseline",
        unit_cost: 320,
        currency: "USD",
        version_index: 1,
      },
    ];

    state.grantAmendments = [
      {
        id: "gamd-01",
        grant_id: "grant-101",
        version_index: 1,
        effective_date: "2026-07-01", // deid-ignore
        summary: "Initial executed clinical trial agreement budget",
      },
    ];

    state.rules = [
      {
        id: "rule-sysbp-range",
        study_id: "study_1",
        name: "Systolic Blood Pressure Physiological Range",
        target_field: "SYSBP",
        expression: "SYSBP >= 70 && SYSBP <= 220",
        severity: "WARNING",
        message: "Systolic blood pressure outside expected 70-220 mmHg window.",
        is_active: true,
      },
    ];

    state.knowledgeCategories = cloneJson(DEFAULT_CATEGORIES);
    state.knowledgeArticles = cloneJson(DEFAULT_MOCK_ARTICLES);
    state.tickets = [
      {
        id: "tk-8801",
        reference: "SIM-2026-0041",
        title: "Investigational Product Cold-Chain Excursion at Site 101",
        description:
          "Cold-chain continuous temperature logger logged 9.4C during weekend storage.",
        category: "SUPPLY_EXCURSION",
        gxp_severity: "CRITICAL",
        priority: "CRITICAL",
        status: "OPEN",
        assigned_queue: "SUBJECT_MANAGEMENT_RTSM",
        assignee_role: "SUBJECT_MANAGEMENT_RTSM",
        assignee_user: null,
        reporter: "cra_monitor",
        created_by: "cra_monitor",
        study_id: "STUDY-ONC-202",
        site_id: "SITE-101",
        subject_id: null,
        related_entity_type: "SUPPLY_KIT",
        related_entity_id: "KIT-9941",
        source_route: "/ssu",
        acknowledge_target_at: "2026-08-04T10:30:00Z",
        sla_target_at: "2026-08-06T09:30:00Z",
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change: "Initial cold-chain deviation triage",
        created_at: "2026-08-04T09:30:00Z",
        version_index: 1,
      },
      {
        id: "tk-8802",
        reference: "SIM-2026-0042",
        title: "Missed Scheduled Blood Draw at Cycle 2 Day 1",
        description:
          "Subject SUBJ-8812 PK blood sample collection missed due to scheduling conflict.",
        category: "PROTOCOL_DEVIATION",
        gxp_severity: "MAJOR",
        priority: "HIGH",
        status: "IN_PROGRESS",
        assigned_queue: "DATA_CAPTURE_ECRF",
        assignee_role: "DATA_CAPTURE_ECRF",
        assignee_user: null,
        reporter: "site_crc",
        created_by: "site_crc",
        study_id: "STUDY-ONC-202",
        site_id: "SITE-102",
        subject_id: "SUBJ-8812",
        related_entity_type: "SUBJECT",
        related_entity_id: "SUBJ-8812",
        source_route: "/ecrf",
        acknowledge_target_at: "2026-08-03T13:30:00Z",
        sla_target_at: "2026-08-05T09:30:00Z",
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change: "Logged protocol window deviation",
        created_at: "2026-08-03T09:30:00Z",
        version_index: 2,
      },
    ];

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

    state.participantWallets = [
      {
        id: "wallet-1001",
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        current_balance: 150,
        lifetime_stipends: 150,
        lifetime_reimbursements: 0,
        lifetime_disbursements: 0,
        taxable_ytd: 150,
        is_1099_threshold_exceeded: false,
        w9_status: "VERIFIED",
        currency: "USD",
        status: "ACTIVE",
        card_type: "RELOADABLE_DEBIT",
        card_status: "ACTIVE",
        virtual_card_mask: "**** **** **** 4821",
        virtual_card_expiry: "12/29",
        ach_routing_mask: "*****0021",
        ach_account_mask: "ACH-***4321",
        ach_account_holder: "Participant SUBJ-1001",
        anti_coercion_cap: 200,
      },
      {
        id: "wallet-1002",
        subject_id: "SUBJ-1002",
        study_id: "DEMO-01",
        site_id: "SITE-102",
        current_balance: 175,
        lifetime_stipends: 625,
        lifetime_reimbursements: 0,
        lifetime_disbursements: 450,
        taxable_ytd: 625,
        is_1099_threshold_exceeded: true,
        w9_status: "REQUIRED",
        currency: "USD",
        status: "ACTIVE",
        card_type: "RELOADABLE_DEBIT",
        card_status: "ACTIVE",
        virtual_card_mask: "**** **** **** 9012",
        virtual_card_expiry: "06/28",
        ach_routing_mask: "*****0021",
        ach_account_mask: "ACH-***9012",
        ach_account_holder: "Participant SUBJ-1002",
        anti_coercion_cap: 200,
      },
    ];

    state.stipendRules = [
      {
        id: "srule-01",
        visit_code: "V1_SCREENING",
        visit_name: "Screening",
        amount: 75,
        currency: "USD",
        taxable: true,
        anti_coercion_cap: 150,
        is_milestone: false,
      },
      {
        id: "srule-02",
        visit_code: "VISIT_BASELINE",
        visit_name: "Baseline (Day 1)",
        amount: 75,
        currency: "USD",
        taxable: true,
        anti_coercion_cap: 200,
        is_milestone: false,
      },
    ];

    state.expenseClaims = [];

    state.walletTransactions = [
      {
        id: "wtx-1001",
        created_at: "2026-08-01T10:00:00Z",
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        transaction_type: "VISIT_STIPEND",
        amount: 150,
        balance_after: 150,
        description: "Baseline visit stipend credit",
      },
    ];

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

  seedInitialState();

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

  return {
    reset: seedInitialState,
    state,
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

    getNode(nodeRef) {
      if (String(nodeRef).startsWith("study:")) {
        return cloneJson(getStudyByRefOrCode(nodeRef));
      }
      if (String(nodeRef).startsWith("observation:")) {
        const obs = state.observations.get(nodeRef);
        if (obs) {
          return {
            ref: obs.ref,
            kind: "observation",
            title: `Observation ${obs.concept}`,
            code: obs.concept,
            status: "RECORDED",
            mask: null,
          };
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
        const siteCode = resolveSiteCodeFromRef(nodeRef, state.subjects);
        return {
          ref: nodeRef,
          kind: "study_site",
          title:
            siteCode === "SITE-102"
              ? "US Regional Trial Unit 2"
              : `US Academic Clinical Research Center (${siteCode})`,
          code: siteCode,
          status: "ACTIVE",
          mask: null,
        };
      }
      if (String(nodeRef).startsWith("enrollment:")) {
        const rawId = String(nodeRef)
          .replace(/^enrollment:/, "")
          .split("@")[0];
        const subj = state.subjects.find((s) => s.id === rawId);
        if (subj) {
          return {
            ref: nodeRef,
            kind: "enrollment",
            title: `Participant ${subj.subject_id}`,
            code: subj.subject_id,
            status: subj.status,
            mask: null,
          };
        }
      }
      if (String(nodeRef).startsWith("collection_instance:")) {
        const rawFormId = String(nodeRef)
          .replace(/^collection_instance:/, "")
          .split("@")[0];
        const form =
          state.forms.get(rawFormId) || state.forms.get(DEFAULT_FORM_UUID);
        if (form) {
          return {
            ref: nodeRef,
            kind: "collection_instance",
            title: `${form.collection_title || resolveCollectionTitle(form.collection_spec_id) || form.collection_spec_id}${form.planned_activity_id ? ` (${form.planned_activity_id})` : ""}`,
            code: form.collection_spec_id,
            status: form.complete ? "COMPLETE" : "OPEN",
            mask: null,
          };
        }
      }
      return {
        ref: nodeRef,
        kind: String(nodeRef).split(":")[0] || "node",
        title: String(nodeRef),
        code: String(nodeRef).split(":")[1]?.slice(-6) || "DEMO",
        status: "ACTIVE",
        mask: null,
      };
    },

    getZoomPath(nodeRef) {
      const refStr = String(nodeRef || "");
      if (refStr.startsWith("enrollment:")) {
        const rawId = refStr.replace(/^enrollment:/, "");
        const subj = state.subjects.find((s) => s.id === rawId);
        const study = subj
          ? getStudyByRefOrCode(subj.study_id)
          : state.studies.get("DEMO-01");
        const siteCode = subj?.site_id || "SITE-101";
        return [
          {
            level: "study",
            label: study.code || study.title,
            ref: study.ref,
            mask: null,
          },
          {
            level: "study_site",
            label: siteCode,
            ref: `study_site:${resolveSiteUuid(siteCode)}`,
            mask: null,
          },
          {
            level: "enrollment",
            label: subj?.subject_id || rawId.slice(-6),
            ref: refStr,
            mask: null,
          },
        ];
      }
      if (refStr.startsWith("study_site:")) {
        const siteCode = resolveSiteCodeFromRef(refStr, state.subjects);
        const study = state.studies.get("DEMO-01");
        return [
          {
            level: "study",
            label: study.code || study.title,
            ref: study.ref,
            mask: null,
          },
          {
            level: "study_site",
            label: siteCode,
            ref: refStr,
            mask: null,
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
        const node = this.getNode(nodeRef);
        steps.push({
          level: node.kind,
          label: node.title || node.code,
          ref: node.ref,
          mask: null,
        });
      }
      return steps;
    },

    getChildren(nodeRef) {
      if (String(nodeRef).startsWith("study:")) {
        const study = getStudyByRefOrCode(nodeRef);
        const studySubjects = state.subjects.filter(
          (s) => s.study_id.toUpperCase() === study.code.toUpperCase()
        );
        const effectiveSubjects =
          studySubjects.length > 0 ? studySubjects : state.subjects;
        const siteCodes = [
          ...new Set([
            "SITE-101",
            "SITE-102",
            ...effectiveSubjects.map((s) => s.site_id).filter(Boolean),
          ]),
        ];
        const siteNodes = siteCodes.map((siteCode) => {
          const siteSubjects = effectiveSubjects.filter(
            (s) => (s.site_id || "SITE-101") === siteCode
          );
          return {
            node: {
              ref: `study_site:${resolveSiteUuid(siteCode)}`,
              kind: "study_site",
              title:
                siteCode === "SITE-102"
                  ? "US Regional Trial Unit 2"
                  : `US Academic Clinical Research Center (${siteCode})`,
              code: siteCode,
              status: "ACTIVE",
              mask: null,
            },
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
            node: {
              ref: `enrollment:${s.id}`,
              kind: "enrollment",
              title: `Participant ${s.subject_id}`,
              code: s.subject_id,
              status: s.status,
              mask: null,
            },
            child_count: 1,
            group: "enrollments",
          })),
        ];
      }
      if (String(nodeRef).startsWith("study_site:")) {
        const siteCode = resolveSiteCodeFromRef(nodeRef, state.subjects);
        const siteSubjects = state.subjects.filter(
          (s) =>
            String(s.site_id || "SITE-101").toUpperCase() ===
            siteCode.toUpperCase()
        );
        return siteSubjects.map((s) => ({
          node: {
            ref: `enrollment:${s.id}`,
            kind: "enrollment",
            title: `Participant ${s.subject_id}`,
            code: s.subject_id,
            status: s.status,
            mask: null,
          },
          child_count: 1,
          group: "enrollments",
        }));
      }
      if (String(nodeRef).startsWith("enrollment:")) {
        const enrollmentId = String(nodeRef)
          .replace(/^enrollment:/, "")
          .split("@")[0];
        let forms = [...state.forms.values()].filter(
          (f) => f.enrollment_id === enrollmentId
        );
        if (forms.length === 0) {
          const fallback = state.forms.get(DEFAULT_FORM_UUID);
          if (fallback) forms = [fallback];
        }
        return forms.map((f) => ({
          node: {
            ref: `collection_instance:${f.form_id}`,
            kind: "collection_instance",
            title: `${f.collection_title || resolveCollectionTitle(f.collection_spec_id) || f.collection_spec_id}${f.planned_activity_id ? ` (${f.planned_activity_id})` : ""}`,
            code: f.collection_spec_id,
            status: f.complete ? "COMPLETE" : "OPEN",
            mask: null,
          },
          child_count: Array.isArray(f.items) ? f.items.length : 0,
          group: f.planned_activity_id || "forms",
        }));
      }
      if (String(nodeRef).startsWith("collection_instance:")) {
        const formId = String(nodeRef)
          .replace(/^collection_instance:/, "")
          .split("@")[0];
        const form =
          state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
        if (!form || !Array.isArray(form.items)) return [];
        return form.items
          .filter((it) => it.observation_id)
          .map((it) => ({
            node: {
              ref: `observation:${it.observation_id}@${Math.max(1, Number(it.revision || 1))}`,
              kind: "observation",
              title: `${it.concept || it.item_id}: ${it.value?.value ?? "not recorded"}${it.unit ? ` ${it.unit}` : ""}`,
              code: it.item_id,
              status: `r${Math.max(1, Number(it.revision || 1))}`,
              mask: null,
            },
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

    listProtocolVersions(studyRef) {
      const study = getStudyByRefOrCode(studyRef);
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
    },

    getProtocolVersion(versionRef) {
      for (const versions of state.importedProtocolVersions.values()) {
        const found = versions.find(
          (v) =>
            v.ref === versionRef ||
            v.ref.split("@")[0] === String(versionRef || "").split("@")[0]
        );
        if (found) return cloneJson(found);
      }
      const versions = this.listProtocolVersions(STUDY_REF);
      return cloneJson(
        versions.find((v) => v.ref === versionRef) || versions[0]
      );
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

    listOrganizations(kind) {
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
            status: 400,
            problem: {
              title: "Missing X-Change-Reason",
              status: 400,
              code: "Kernel.Audit.MissingReasonForChange",
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
            status: 400,
            problem: {
              title: "Invalid organization request",
              status: 400,
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

    createSite(body = {}, { reason = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Missing X-Change-Reason",
              status: 400,
              code: "Kernel.Audit.MissingReasonForChange",
              detail:
                "Header 'X-Change-Reason' is required when registering a site.",
            },
          },
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
              code: "K.Site.CountryInvalid",
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
              code: "K.Node.AlreadyExists",
              detail: `Site '${siteCode}' already exists.`,
            },
          },
        };
      }
      let inst = state.organizations.find(
        (o) =>
          (body.institution_ref &&
            o.ref.split("@")[0] ===
              String(body.institution_ref).split("@")[0]) ||
          (body.institution_code &&
            o.code.toUpperCase() ===
              String(body.institution_code).trim().toUpperCase())
      );
      if (!inst) {
        const instRes = this.createOrganization(
          {
            name: String(body.institution_name || `${name} Institution`).trim(),
            code: body.institution_code || `INST-${siteCode}`,
            kind: "SiteInstitution",
          },
          { reason }
        );
        if (instRes.error) return instRes;
        inst = instRes.data;
      }
      const idx = nextCounter();
      const created = {
        ref: `site:${makeUuidFromIndex("00000320", idx)}@1`,
        code: siteCode,
        name,
        country,
        institution_ref: inst.ref,
        institution_code: inst.code,
        institution_name: inst.name,
      };
      state.sites.push(created);
      return { data: cloneJson(created) };
    },

    listStudySites(studyRef) {
      const normalized = String(studyRef || "").trim();
      const unpinned = normalized.split("@")[0];
      let matchedCode = null;
      for (const [code, study] of state.studies.entries()) {
        if (
          code.toUpperCase() === normalized.toUpperCase() ||
          String(study.ref).split("@")[0] === unpinned
        ) {
          matchedCode = code;
          break;
        }
      }
      if (!matchedCode) return [];
      return cloneJson(state.studySites.get(matchedCode) || []);
    },

    linkSiteToStudy(studyRef, body = {}, { reason = "" } = {}) {
      if (!String(reason || "").trim()) {
        return {
          error: {
            status: 400,
            problem: {
              title: "Missing X-Change-Reason",
              status: 400,
              code: "Kernel.Audit.MissingReasonForChange",
              detail:
                "Header 'X-Change-Reason' is required when linking a site.",
            },
          },
        };
      }
      const normalized = String(studyRef || "").trim();
      const unpinned = normalized.split("@")[0];
      let studyEntry = null;
      for (const study of state.studies.values()) {
        if (
          study.code.toUpperCase() === normalized.toUpperCase() ||
          String(study.ref).split("@")[0] === unpinned
        ) {
          studyEntry = study;
          break;
        }
      }
      if (!studyEntry) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Study not found",
              status: 404,
              code: "K.Node.NotFound",
              detail: `Study '${studyRef}' was not found.`,
            },
          },
        };
      }
      const site = state.sites.find(
        (s) =>
          (body.site_ref &&
            s.ref.split("@")[0] === String(body.site_ref).split("@")[0]) ||
          (body.site_code &&
            s.code.toUpperCase() ===
              String(body.site_code).trim().toUpperCase())
      );
      if (!site) {
        return {
          error: {
            status: 404,
            problem: {
              title: "Site not found",
              status: 404,
              code: "K.Node.NotFound",
              detail: "Requested investigational site was not found.",
            },
          },
        };
      }
      const existingLinks = state.studySites.get(studyEntry.code) || [];
      if (
        existingLinks.some(
          (l) => l.site_code.toUpperCase() === site.code.toUpperCase()
        )
      ) {
        return {
          error: {
            status: 409,
            problem: {
              title: "Site already linked",
              status: 409,
              code: "K.StudySite.DuplicateSite",
              detail: `Site '${site.code}' is already linked to '${studyEntry.code}'.`,
            },
          },
        };
      }
      const idx = nextCounter();
      const linked = {
        ref: `study_site:${makeUuidFromIndex("00000410", idx)}@1`,
        study_ref: String(studyEntry.ref).split("@")[0],
        site_ref: site.ref,
        site_code: site.code,
        site_name: site.name,
        country: site.country,
        institution_ref: site.institution_ref,
        institution_code: site.institution_code,
        institution_name: site.institution_name,
        status: "Selected",
      };
      const nextLinks = [...existingLinks, linked].sort((a, b) =>
        a.site_code.localeCompare(b.site_code)
      );
      state.studySites.set(studyEntry.code, nextLinks);
      studyEntry.site_count = nextLinks.length;
      return { data: cloneJson(linked) };
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
      state.studySites.set(code, []);
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
          const linkRes = this.linkSiteToStudy(
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

    listProtocolTemplates() {
      return DEMO_PROTOCOL_TEMPLATES.map((tpl) => ({
        id: tpl.id,
        category: tpl.category,
        form_oid: tpl.form_oid,
        form_name: tpl.form_name,
        file_name: tpl.file_name,
        item_count: tpl.item_count,
      }));
    },

    getProtocolDesignSummary(versionRef) {
      const unpinned = String(versionRef || "").split("@")[0];
      for (const [key, summary] of state.protocolDesignSummaries.entries()) {
        if (key === versionRef || key.split("@")[0] === unpinned) {
          return cloneJson(summary);
        }
      }
      const version = this.getProtocolVersion(versionRef);
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

      const existingVersions = this.listProtocolVersions(foundStudy.ref);
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
        const parent = existingVersions.find(
          (v) => String(v.status).toUpperCase() === "PUBLISHED"
        );
        const vIdx = nextCounter();
        targetVersion = {
          ref: `protocol_version:${makeUuidFromIndex("00000150", vIdx)}@1`,
          study: foundStudy.ref,
          number: maxNum + 1,
          parent_version: parent ? parent.ref : null,
          status: "Draft",
          content_hash: null,
          amendment_intent: reason,
          approval_attestation: null,
          withdrawal_reason: null,
          mask: null,
        };
        const list = state.importedProtocolVersions.get(foundStudy.code) || [];
        list.unshift(targetVersion);
        state.importedProtocolVersions.set(foundStudy.code, list);
      }

      const summary = {
        version: cloneJson(targetVersion),
        title: built.title,
        form_count: mergedForms.length,
        item_count: mergedForms.reduce((acc, f) => acc + f.item_count, 0),
        forms: cloneJson(mergedForms),
        import_notes: cloneJson(mergedNotes),
      };
      state.protocolDesignSummaries.set(targetVersion.ref, summary);
      recordActivity(
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
            footnotes: [1],
          },
          {
            collection_spec_id: "form-chem-panel",
            name: "Clinical Chemistry Panel (CDASH LB)",
            item_ids: ["item-hba1c", "item-egfr"],
            concepts: ["HBA1C", "EGFR"],
            columns: [0, 2],
            frequency: 2,
            footnotes: [],
          },
          {
            collection_spec_id: "form-adverse-event",
            name: "Adverse Event (CDASH AE)",
            item_ids: ["item-aeterm", "item-aesev"],
            concepts: ["AETERM", "AESEV"],
            columns: [],
            frequency: 0,
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

    getAmendmentImpact(versionRef) {
      return {
        amended: versionRef || VERSION_V2_REF,
        parent: VERSION_V1_REF,
        impacted_elements: ["visit-week4", "form-chem-panel"],
        dependents: this.getDependents(versionRef || VERSION_V2_REF),
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

    getLineage(nodeRef, direction = "upstream", depth = 3) {
      const now = new Date().toISOString();
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
            mask: null,
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

    getDependents(nodeRef, depth = 3) {
      const now = new Date().toISOString();
      return {
        start: nodeRef,
        depth,
        valid_at: now,
        known_at: now,
        groups: [
          {
            kind: "enrollment",
            count: state.subjects.length,
            objects: state.subjects.map((subj, index) => ({
              handle: `dep-${index}`,
              kind: "enrollment",
              ref: `enrollment:${subj.id}`,
              mask: null,
              distance: 1,
            })),
          },
        ],
        total: state.subjects.length,
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

    getObservation(observationRef) {
      const normalized = String(observationRef).startsWith("observation:")
        ? String(observationRef)
        : `observation:${observationRef}`;
      let obs = state.observations.get(normalized);
      if (!obs) {
        obs = {
          ref: normalized,
          enrollment_ref: `enrollment:${ENROLLMENT_1001_UUID}`,
          concept: "SYSBP",
          value: {
            type: "quantity",
            value: "124",
            unit: "mm[Hg]",
            comparator: null,
          },
          observed_at: "2026-08-01T09:15:00Z",
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
              recorded_at: "2026-08-01T09:15:00Z",
              recorded_until: null,
            },
          ],
          assertions: [],
          audit: [
            {
              sequence: 1,
              recorded_at: "2026-08-01T09:15:00Z",
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
      return cloneJson(obs);
    },

    correctObservation(observationRef, body, reasonForChange) {
      const normalized = String(observationRef).startsWith("observation:")
        ? String(observationRef)
        : `observation:${observationRef}`;
      const existing = state.observations.get(normalized);
      if (!existing) {
        this.getObservation(normalized);
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
        this.getObservation(normalized);
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

    createSubject(body) {
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
        };
      }

      ensureStudyByCode(studyCode);
      const idx = nextCounter();
      const created = {
        id: makeUuidFromIndex("00001000", idx),
        subject_id: subjectCode,
        study_id: studyCode,
        site_id: siteCode,
        status: "SCREENED",
        active_protocol_version: "2.0",
        enrollment_index: state.subjects.length + 1,
        treatment_group: null,
      };
      state.subjects.push(created);
      recordActivity(
        "execution.subject.registered",
        "execution",
        "enrollment",
        `enrollment:${created.id}`,
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
      };
    },

    getSubjectDetail(subjectId) {
      const found =
        state.subjects.find(
          (s) =>
            s.subject_id.toUpperCase() === String(subjectId).toUpperCase() ||
            s.id === subjectId
        ) || state.subjects[0];
      return {
        id: found.id,
        subject_id: found.subject_id,
        study_id: found.study_id,
        status: found.status,
        site_id: found.site_id,
        active_protocol_version: found.active_protocol_version || "2.0",
        enrollment_index: found.enrollment_index ?? 0,
        treatment_group: found.treatment_group,
        randomization_seed: null,
        investigational_product_id: null,
      };
    },

    updateSubjectState(subjectId, body) {
      const nextStatus = String(
        body?.status || body?.state || body?.target || "ENROLLED"
      ).toUpperCase();
      let found = state.subjects.find(
        (s) =>
          s.subject_id.toUpperCase() === String(subjectId).toUpperCase() ||
          s.id === subjectId
      );
      if (!found) {
        const created = this.createSubject({
          subject_id: subjectId,
          study_id: "DEMO-01",
          site_id: "SITE-101",
        });
        found = state.subjects.find((s) => s.id === created.id);
      }
      found.status = nextStatus;
      recordActivity(
        "execution.subject.state_changed",
        "execution",
        "enrollment",
        `enrollment:${found.id}`,
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
      };
    },

    getSiteCompliance(
      siteId,
      studyId = "DEMO-01",
      milestone = "SITE_ACTIVATION"
    ) {
      const existing = state.siteCompliance.get(siteId);
      if (existing) {
        const isActivated = Boolean(
          existing.is_activated ?? existing.activated
        );
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
    },

    activateSite(siteId, studyId = "DEMO-01") {
      const compliance = this.getSiteCompliance(siteId, studyId);
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
      recordActivity(
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

    openForm(body) {
      const enrollmentId = body?.enrollment_id || ENROLLMENT_1001_UUID;
      const collectionSpecId = body?.collection_spec_id || "form-vital-signs";
      const plannedActivityId =
        body &&
        Object.prototype.hasOwnProperty.call(body, "planned_activity_id")
          ? (body.planned_activity_id ?? null)
          : "visit-baseline";

      for (const existing of state.forms.values()) {
        if (
          (body?.form_id && existing.form_id === body.form_id) ||
          (existing.enrollment_id === enrollmentId &&
            existing.collection_spec_id === collectionSpecId &&
            existing.planned_activity_id === plannedActivityId &&
            (!body?.collected_at ||
              existing.collected_at === body.collected_at))
        ) {
          return {
            form_id: existing.form_id,
            enrollment_id: existing.enrollment_id,
            protocol_version_id: existing.protocol_version_id,
            collection_spec_id: existing.collection_spec_id,
            collection_title:
              existing.collection_title ??
              resolveCollectionTitle(existing.collection_spec_id),
            planned_activity_id: existing.planned_activity_id,
            collected_at: existing.collected_at,
            version: existing.version,
          };
        }
      }

      const idx = nextCounter();
      const formId = body?.form_id || makeUuidFromIndex("00002000", idx);
      const obsSysId = makeUuidFromIndex("00006001", idx);
      const obsDiaId = makeUuidFromIndex("00006002", idx);
      const now = body?.collected_at || new Date().toISOString();

      const items = [
        {
          item_id: "item-sysbp",
          concept: "SYSBP",
          unit: "mm[Hg]",
          value_kind: "quantity",
          required: true,
          value: null,
          observation_id: obsSysId,
          revision: 0,
          recorded_by: null,
          recorded_at: null,
        },
        {
          item_id: "item-diabp",
          concept: "DIABP",
          unit: "mm[Hg]",
          value_kind: "quantity",
          required: true,
          value: null,
          observation_id: obsDiaId,
          revision: 0,
          recorded_by: null,
          recorded_at: null,
        },
      ];

      const subjMatch = state.subjects.find((s) => s.id === enrollmentId);
      const siteUuid = resolveSiteUuid(subjMatch?.site_id || "SITE-101");
      const collectionTitle = resolveCollectionTitle(collectionSpecId);

      const created = {
        form_id: formId,
        enrollment_id: enrollmentId,
        participant_id: makeUuidFromIndex("00005000", idx),
        study_id: STUDY_UUID,
        study_site_id: siteUuid,
        protocol_version_id: VERSION_V2_UUID,
        collection_spec_id: collectionSpecId,
        collection_title: collectionTitle,
        planned_activity_id: plannedActivityId,
        collected_at: now,
        opened_by: "usr-crc-01",
        opened_at: now,
        version: 1,
        complete: false,
        missing_required: ["item-sysbp", "item-diabp"],
        items,
        locks: [],
        assertions: [],
        signatures: [],
        content_hash: computeContentHash(items),
      };
      state.forms.set(formId, created);

      return {
        form_id: created.form_id,
        enrollment_id: created.enrollment_id,
        protocol_version_id: created.protocol_version_id,
        collection_spec_id: created.collection_spec_id,
        collection_title: created.collection_title,
        planned_activity_id: created.planned_activity_id,
        collected_at: created.collected_at,
        version: created.version,
      };
    },

    listEnrollmentForms(enrollmentId) {
      const targetId = String(enrollmentId || "").trim();
      if (!targetId) return [];
      const matches = [...state.forms.values()]
        .filter((f) => f.enrollment_id === targetId)
        .sort((a, b) =>
          String(b.collected_at || "").localeCompare(
            String(a.collected_at || "")
          )
        );
      return matches.map((f) => {
        const itemsList = Array.isArray(f.items) ? f.items : [];
        const recordedItems = itemsList.filter(
          (it) =>
            it.value !== null &&
            it.value !== undefined &&
            !(it.value.type === "missing" && it.value.reason === "not_asked")
        );
        const recordedCount = recordedItems.length;
        const missingRequiredCount = Array.isArray(f.missing_required)
          ? f.missing_required.length
          : itemsList.filter(
              (it) =>
                it.required &&
                (!it.value ||
                  (it.value.type === "missing" &&
                    it.value.reason === "not_asked"))
            ).length;

        const formQueries = [...state.queries.values()].filter(
          (q) => q.form_id === f.form_id
        );
        const openQueryCount = formQueries.filter(
          (q) => String(q.status).toLowerCase() === "open"
        ).length;
        const answeredQueryCount = formQueries.filter(
          (q) => String(q.status).toLowerCase() === "answered"
        ).length;

        const formAssertions = Array.isArray(f.assertions) ? f.assertions : [];
        const activeFormVerified = formAssertions.some(
          (a) =>
            !a.retracted_at &&
            String(a.kind).toLowerCase() === "verified" &&
            !String(a.target || "").startsWith("observation:")
        );
        let verifiedCount = 0;
        for (const it of recordedItems) {
          if (activeFormVerified) {
            verifiedCount += 1;
            continue;
          }
          const obsRef = `observation:${it.observation_id}`;
          const obs = state.observations.get(obsRef);
          const obsAssertions = Array.isArray(obs?.assertions)
            ? obs.assertions
            : [];
          const isObsVerified =
            obsAssertions.some(
              (a) =>
                !a.retracted_at &&
                String(a.kind).toLowerCase() === "verified" &&
                (a.revision === undefined ||
                  Number(a.revision) === Number(it.revision))
            ) ||
            formAssertions.some(
              (a) =>
                !a.retracted_at &&
                String(a.kind).toLowerCase() === "verified" &&
                a.target === obsRef &&
                (a.revision === undefined ||
                  Number(a.revision) === Number(it.revision))
            );
          if (isObsVerified) {
            verifiedCount += 1;
          }
        }
        const unverifiedCount = Math.max(0, recordedCount - verifiedCount);

        const formLocks = Array.isArray(f.locks) ? f.locks : [];
        const studyLocked = state.locks.some(
          (lk) => String(lk.status || "").toUpperCase() === "LOCKED"
        );
        const hasHardLock =
          studyLocked ||
          formLocks.some(
            (lk) =>
              !lk.released_at &&
              String(lk.level || lk.kind || "locked").toLowerCase() !== "frozen"
          );
        const hasFreeze =
          formLocks.some(
            (lk) =>
              !lk.released_at &&
              String(lk.level || lk.kind || "").toLowerCase() === "frozen"
          ) ||
          formAssertions.some(
            (a) => !a.retracted_at && String(a.kind).toLowerCase() === "frozen"
          );
        const activeLockCount =
          formLocks.filter((lk) => !lk.released_at).length +
          (studyLocked ? 1 : 0);

        const sigs = Array.isArray(f.signatures) ? f.signatures : [];
        const signatureCount = sigs.length;
        const signed =
          signatureCount > 0 &&
          sigs.some((s) => !s.superseded && s.content_hash === f.content_hash);
        const signatureSuperseded = signatureCount > 0 && !signed;

        return {
          form_id: f.form_id,
          enrollment_id: f.enrollment_id,
          participant_id: f.participant_id,
          study_id: f.study_id,
          study_site_id: f.study_site_id,
          protocol_version_id: f.protocol_version_id,
          collection_spec_id: f.collection_spec_id,
          planned_activity_id: f.planned_activity_id,
          collected_at: f.collected_at,
          opened_by: f.opened_by,
          opened_at: f.opened_at,
          version: f.version,
          complete: Boolean(f.complete),
          item_count: itemsList.length,
          recorded_count: recordedCount,
          missing_required_count: missingRequiredCount,
          open_query_count: openQueryCount,
          answered_query_count: answeredQueryCount,
          verified_count: verifiedCount,
          unverified_count: unverifiedCount,
          locked: hasHardLock,
          frozen: !hasHardLock && hasFreeze,
          lock_count: activeLockCount,
          signed,
          signature_superseded: signatureSuperseded,
          signature_count: signatureCount,
          collection_title:
            f.collection_title ?? resolveCollectionTitle(f.collection_spec_id),
        };
      });
    },

    getForm(formId) {
      let form = state.forms.get(formId);
      if (!form) {
        form = state.forms.get(DEFAULT_FORM_UUID);
      }
      return cloneJson(form);
    },

    saveFormValues(formId, body, reason = "Saved eCRF form values") {
      const form =
        state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
      const incoming = Array.isArray(body?.values) ? body.values : [];
      const now = new Date().toISOString();
      const savedValues = [];
      let anyChanged = false;

      for (const entry of incoming) {
        let item = form.items.find((it) => it.item_id === entry.item_id);
        if (!item) {
          const idx = nextCounter();
          item = {
            item_id: entry.item_id,
            concept: entry.item_id.toUpperCase(),
            unit: entry.value?.unit ?? null,
            value_kind: entry.value?.type ?? "text",
            required: false,
            value: null,
            observation_id:
              entry.observation_id || makeUuidFromIndex("00006100", idx),
            revision: 0,
            recorded_by: null,
            recorded_at: null,
          };
          form.items.push(item);
        }

        const expectedRev = Number(entry.expected_revision ?? item.revision);
        if (
          entry.expected_revision !== undefined &&
          expectedRev !== Number(item.revision)
        ) {
          return {
            conflict: true,
            problem: {
              title: "Stale item revision",
              status: 409,
              code: "K.Collection.RevisionStale",
              detail: `Item ${item.item_id} is at revision ${item.revision}, expected ${expectedRev}.`,
            },
          };
        }

        const prevJson = JSON.stringify(item.value);
        const nextJson = JSON.stringify(entry.value);
        const changed = prevJson !== nextJson;
        if (changed) {
          anyChanged = true;
          item.revision = Number(item.revision) + 1;
          item.value = cloneJson(entry.value);
          item.recorded_by = "usr-crc-01";
          item.recorded_at = entry.observed_at || now;

          const obsId =
            item.observation_id ||
            entry.observation_id ||
            makeUuidFromIndex("00006200", nextCounter());
          item.observation_id = obsId;
          const obsRef = `observation:${obsId}`;
          const existingObs = state.observations.get(obsRef);
          if (existingObs) {
            const lastRev =
              existingObs.revisions[existingObs.revisions.length - 1];
            if (lastRev && !lastRev.recorded_until) {
              lastRev.recorded_until = now;
            }
            existingObs.value = cloneJson(entry.value);
            existingObs.version = Number(existingObs.version) + 1;
            existingObs.revisions.push({
              number: item.revision,
              value: cloneJson(entry.value),
              reason_for_change: reason,
              recorded_by: "usr-crc-01",
              recorded_at: now,
              recorded_until: null,
            });
            existingObs.audit.push({
              sequence: existingObs.audit.length + 1,
              recorded_at: now,
              actor_id: "usr-crc-01",
              action: "observation.correct",
              subject: obsRef,
              version: existingObs.version,
              reason_for_change: reason,
            });
          } else {
            state.observations.set(obsRef, {
              ref: obsRef,
              enrollment_ref: `enrollment:${form.enrollment_id}`,
              concept: item.concept || item.item_id,
              value: cloneJson(entry.value),
              observed_at: now,
              version: 1,
              locks: [],
              revisions: [
                {
                  number: item.revision,
                  value: cloneJson(entry.value),
                  reason_for_change: reason,
                  recorded_by: "usr-crc-01",
                  recorded_at: now,
                  recorded_until: null,
                },
              ],
              assertions: [],
              audit: [
                {
                  sequence: 1,
                  recorded_at: now,
                  actor_id: "usr-crc-01",
                  action: "observation.record",
                  subject: obsRef,
                  version: 1,
                  reason_for_change: reason,
                },
              ],
            });
          }

          for (const q of state.queries.values()) {
            if (q.observation_id === obsId) {
              q.current_revision = item.revision;
              q.value_changed = item.revision > Number(q.revision);
            }
          }
        }

        savedValues.push({
          item_id: item.item_id,
          observation_id: item.observation_id,
          value: cloneJson(item.value),
          revision: item.revision,
          changed,
        });
      }

      const missingRequired = form.items
        .filter(
          (it) =>
            it.required &&
            (!it.value ||
              (it.value.type === "missing" && it.value.reason === "not_asked"))
        )
        .map((it) => it.item_id);
      form.missing_required = missingRequired;
      form.complete = missingRequired.length === 0;
      if (anyChanged) {
        form.version = Number(form.version) + 1;
        form.content_hash = computeContentHash(form.items);
        for (const sig of form.signatures) {
          sig.superseded = true;
        }
        recordActivity(
          "execution.form.saved",
          "collection",
          "form",
          `form:${form.form_id}`,
          "usr-crc-01"
        );
      }

      return {
        conflict: false,
        data: {
          form_id: form.form_id,
          version: form.version,
          values: savedValues,
          form: cloneJson(form),
        },
      };
    },

    signForm(formId, body, reason = "Investigator eCRF sign-off") {
      const form =
        state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
      if (body?.content_hash && body.content_hash !== form.content_hash) {
        return {
          conflict: true,
          problem: {
            title: "Form content changed since read",
            status: 409,
            code: "K.Trust.TargetContentChanged",
            detail: "Reload the form and review updated values before signing.",
          },
        };
      }
      const idx = nextCounter();
      const now = new Date().toISOString();
      const signature = {
        attestation_id:
          body?.signature_id || makeUuidFromIndex("00007000", idx),
        meaning: body?.meaning || "approved",
        signed_by: "usr-pi-01",
        signed_at: now,
        reason,
        content_hash: form.content_hash,
        superseded: false,
        signed_values: form.items
          .filter((it) => it.value)
          .map((it) => ({
            item_id: it.item_id,
            observation_id: it.observation_id,
            revision: it.revision,
            value: cloneJson(it.value),
          })),
      };
      form.signatures.push(signature);
      recordActivity(
        "execution.form.signed",
        "trust",
        "form",
        `form:${form.form_id}`,
        "usr-pi-01"
      );
      return { conflict: false, data: cloneJson(signature) };
    },

    listFormQueries(formId) {
      return cloneJson(
        [...state.queries.values()].filter((q) => q.form_id === formId)
      );
    },

    listEnrollmentQueries(enrollmentIdOrOptions) {
      const opts =
        enrollmentIdOrOptions && typeof enrollmentIdOrOptions === "object"
          ? enrollmentIdOrOptions
          : { enrollmentId: enrollmentIdOrOptions };
      let results = [...state.queries.values()];
      if (opts.enrollmentId) {
        const matches = results.filter(
          (q) => q.enrollment_id === opts.enrollmentId
        );
        if (matches.length > 0) {
          results = matches;
        }
      } else if (opts.studyId) {
        const matches = results.filter(
          (q) => !q.study_id || q.study_id === opts.studyId
        );
        results = matches;
      }
      if (opts.status) {
        const wanted = String(opts.status).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.status).toLowerCase() === wanted
        );
      }
      if (opts.owner) {
        const wanted = String(opts.owner).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.owner_role || "site").toLowerCase() === wanted
        );
      }
      if (opts.origin) {
        const wanted = String(opts.origin).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.origin || "manual").toLowerCase() === wanted
        );
      }
      if (opts.priority) {
        const wanted = String(opts.priority).trim().toLowerCase();
        results = results.filter(
          (q) => String(q.priority || "normal").toLowerCase() === wanted
        );
      }
      if (
        opts.escalated !== undefined &&
        opts.escalated !== null &&
        opts.escalated !== ""
      ) {
        const wantedEsc =
          opts.escalated === true || String(opts.escalated) === "true";
        results = results.filter((q) => Boolean(q.escalated_at) === wantedEsc);
      }
      if (opts.site) {
        const site = String(opts.site).trim().toLowerCase();
        results = results.filter(
          (q) =>
            String(q.site_code || "").toLowerCase() === site ||
            String(q.study_site_id || "").toLowerCase() === site
        );
      }
      if (opts.participant) {
        const participant = String(opts.participant).trim().toLowerCase();
        results = results.filter(
          (q) =>
            String(q.subject_code || "")
              .toLowerCase()
              .includes(participant) ||
            String(q.enrollment_id || "").toLowerCase() === participant
        );
      }
      if (
        opts.valueChanged !== undefined &&
        opts.valueChanged !== null &&
        opts.valueChanged !== ""
      ) {
        const wantedBool =
          opts.valueChanged === true || String(opts.valueChanged) === "true";
        results = results.filter(
          (q) => Boolean(q.value_changed) === wantedBool
        );
      }
      return cloneJson(results);
    },

    raiseQuery(formId, body, reasonHeader) {
      const form =
        state.forms.get(formId) || state.forms.get(DEFAULT_FORM_UUID);
      let item =
        (body?.observation_id &&
          form.items.find((it) => it.observation_id === body.observation_id)) ||
        (body?.item_id &&
          form.items.find((it) => it.item_id === body.item_id)) ||
        form.items[0];
      const idx = nextCounter();
      const now = new Date().toISOString();
      const dueAt = new Date(Date.now() + 14 * 86400 * 1000).toISOString();
      if (!body?.observation_id && body?.item_id && item && !item.value) {
        const unaskedObsId =
          item.observation_id || makeUuidFromIndex("00006300", idx);
        item.observation_id = unaskedObsId;
        item.revision = Math.max(1, Number(item.revision || 0));
        item.value = { type: "missing", reason: "not_asked" };
        item.recorded_by = "usr-cra-01";
        item.recorded_at = now;
      }
      const queryId = body?.query_id || makeUuidFromIndex("00003000", idx);
      const text = String(
        body?.text || "Verify recorded value against source."
      );
      const rev = Number(body?.revision ?? item?.revision ?? 1);
      const subjectMatch = state.subjects.find(
        (s) => s.id === form.enrollment_id
      );

      const created = {
        query_id: queryId,
        form_id: form.form_id,
        enrollment_id: form.enrollment_id,
        item_id: body?.item_id || item?.item_id || "item-sysbp",
        observation_id:
          body?.observation_id || item?.observation_id || OBS_1_UUID,
        revision: rev,
        current_revision: item?.revision ?? rev,
        value_changed: false,
        status: "open",
        origin: "manual",
        owner_role: body?.owner_role || "site",
        assignee: body?.assignee || null,
        priority: body?.priority || "normal",
        opened_at: now,
        status_since: now,
        due_at: dueAt,
        escalated_at: null,
        last_answered_by: null,
        text,
        messages: [
          {
            kind: "raised",
            text,
            by: "usr-cra-01",
            at: now,
            reason_for_change: reasonHeader || text,
          },
        ],
        raised_by: "usr-cra-01",
        raised_at: now,
        version: 1,
        study_id: form.study_id || STUDY_UUID,
        study_site_id: form.study_site_id || SITE_101_UUID,
        site_code: subjectMatch?.site_id || "SITE-101",
        subject_code: subjectMatch?.subject_id || "SUBJ-1001",
        collection_spec_id: form.collection_spec_id || "form-vital-signs",
        planned_activity_id: form.planned_activity_id || "visit-baseline",
      };
      state.queries.set(queryId, created);
      recordActivity(
        "execution.query.raised",
        "execution",
        "query",
        `query:${queryId}`,
        "usr-cra-01"
      );
      return cloneJson(created);
    },

    transitionQuery(formId, queryId, action, body, reason) {
      const existing = state.queries.get(queryId);
      if (!existing) {
        return {
          notFound: true,
          problem: {
            title: "Query not found",
            status: 404,
            detail: `Query ${queryId} does not exist on form ${formId}.`,
          },
        };
      }
      const statusMap = {
        answer: "answered",
        reopen: "open",
        close: "closed",
        cancel: "cancelled",
      };
      const kindMap = {
        answer: "answered",
        reopen: "reopened",
        close: "closed",
        cancel: "cancelled",
      };
      const nextStatus = statusMap[action];
      if (!nextStatus) {
        return {
          invalid: true,
          problem: {
            title: "Invalid query transition",
            status: 409,
            code: "M.Execution.Query.InvalidTransition",
            detail: `Unsupported transition ${action}.`,
          },
        };
      }
      const now = new Date().toISOString();
      if (
        action === "close" &&
        existing.status === "open" &&
        (existing.origin || "manual") !== "system"
      ) {
        return {
          invalid: true,
          problem: {
            title: "Invalid query transition",
            status: 409,
            code: "M.Execution.Query.InvalidTransition",
            detail: "Manual queries must be answered before closure.",
          },
        };
      }
      const actor = action === "answer" ? "usr-crc-01" : "usr-dm-01";
      const messageText = String(
        body?.text || reason || `Query ${kindMap[action]}.`
      );
      existing.status = nextStatus;
      existing.status_since = now;
      if (action === "answer") {
        existing.owner_role = "monitor";
        existing.due_at = null;
        existing.escalated_at = null;
        existing.last_answered_by = actor;
      } else if (action === "reopen") {
        existing.owner_role = "site";
        existing.due_at = new Date(
          Date.now() + 14 * 86400 * 1000
        ).toISOString();
        existing.escalated_at = null;
      } else {
        existing.due_at = null;
        existing.escalated_at = null;
      }
      existing.version = Number(existing.version) + 1;
      existing.messages.push({
        kind: kindMap[action],
        text: messageText,
        by: actor,
        at: now,
        reason_for_change: reason || messageText,
      });
      recordActivity(
        `execution.query.${kindMap[action]}`,
        "execution",
        "query",
        `query:${queryId}`,
        action === "answer" ? "usr-crc-01" : "usr-dm-01"
      );
      return { data: cloneJson(existing) };
    },

    getStartupMatrix(countryCode = "US") {
      const key = (countryCode || "US").toUpperCase();
      if (!state.startupMatrices.has(key)) {
        state.startupMatrices.set(key, createStartupMatrixForCountry(key));
      }
      return cloneJson(state.startupMatrices.get(key));
    },

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
      const matrix = this.getStartupMatrix(countryCode);
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

    resolveContextualHelp(route, persona) {
      return cloneJson(getFallbackContextualHelp(route, persona));
    },

    listTickets(filters = {}) {
      let results = [...state.tickets];
      if (filters.status) {
        results = results.filter((t) => t.status === filters.status);
      }
      if (filters.category) {
        results = results.filter((t) => t.category === filters.category);
      }
      if (filters.assigned_queue) {
        results = results.filter(
          (t) => t.assigned_queue === filters.assigned_queue
        );
      }
      if (filters.study_id) {
        results = results.filter((t) => t.study_id === filters.study_id);
      }
      if (filters.site_id) {
        results = results.filter((t) => t.site_id === filters.site_id);
      }
      if (filters.search) {
        const q = String(filters.search).toLowerCase();
        results = results.filter(
          (t) =>
            String(t.title || "")
              .toLowerCase()
              .includes(q) ||
            String(t.reference || "")
              .toLowerCase()
              .includes(q)
        );
      }
      return cloneJson(results);
    },

    getTicket(ticketId) {
      const found = state.tickets.find(
        (t) => t.id === ticketId || t.reference === ticketId
      );
      return found ? cloneJson(found) : null;
    },

    createTicket(body, reasonForChange) {
      const title = String(body?.title || "").trim();
      const description = String(body?.description || "").trim();
      const reason = String(reasonForChange || "").trim();
      if (!title || !description) {
        return {
          invalid: true,
          problem: {
            title: "Invalid ticket request",
            status: 400,
            detail: "Ticket title and description are required.",
          },
        };
      }
      if (!reason) {
        return {
          invalid: true,
          problem: {
            title: "Missing X-Change-Reason",
            status: 400,
            detail: "X-Change-Reason header is required for ticket creation.",
          },
        };
      }
      const piiViolation = validateDemoTicketNoDirectPii(body || {});
      if (piiViolation) {
        return {
          invalid: true,
          problem: {
            title: "Direct participant PII is prohibited",
            status: 400,
            detail: piiViolation,
          },
        };
      }

      const idx = nextCounter();
      const nowMs = demoNowMs(idx);
      const nowIso = new Date(nowMs).toISOString();
      const year = new Date(nowMs).getUTCFullYear();
      const seq = String(idx).padStart(4, "0");
      const category = String(body?.category || "SYSTEM_SUPPORT").toUpperCase();
      const priority = String(body?.priority || "MEDIUM").toUpperCase();
      const severity = String(body?.gxp_severity || "MINOR").toUpperCase();
      const assignedQueue = routeDemoTicketQueue({
        category,
        source_route: body?.source_route,
        persona: body?.persona,
      });

      const created = {
        id: makeUuidFromIndex("00007100", idx),
        reference: `SIM-${year}-${seq}`,
        title,
        description,
        category,
        priority,
        gxp_severity: severity,
        status: "OPEN",
        assigned_queue: assignedQueue,
        assignee_role: assignedQueue,
        assignee_user: null,
        reporter: body?.persona || "usr-demo-operator",
        created_by: body?.persona || "usr-demo-operator",
        study_id: body?.study_id || null,
        site_id: body?.site_id || null,
        subject_id: body?.subject_id || null,
        related_entity_type: body?.entity_type || null,
        related_entity_id: body?.entity_id || null,
        source_route: body?.source_route || null,
        acknowledge_target_at: new Date(nowMs + 4 * 3600_000).toISOString(),
        sla_target_at: new Date(nowMs + 24 * 3600_000).toISOString(),
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change: reason,
        created_at: nowIso,
        version_index: 1,
      };
      state.tickets.unshift(created);
      recordActivity(
        "tickets.created",
        "tickets",
        "ticket",
        `ticket:${created.reference}`,
        created.created_by
      );
      return { data: cloneJson(created) };
    },

    getEntitlements() {
      return cloneJson(state.entitlements);
    },

    getLockReadiness(studyRef, rawScopeRef) {
      const study = getStudyByRefOrCode(studyRef);
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
}

export const webDemoStore = createWebDemoStore();
