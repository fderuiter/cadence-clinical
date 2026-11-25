import crypto from "crypto";
import fs from "fs";
import path from "path";

const VIRTUAL_DEMO_MANIFEST_SOURCE = "virtual:demo-fixtures-manifest";
const VIRTUAL_DEMO_MANIFEST_ID = "\0virtual:demo-fixtures-manifest";
const VIRTUAL_DEMO_FIXTURES_ID = "\0virtual:demo-fixtures-prod-stub";
const VIRTUAL_DEMO_RUNTIME_ID = "\0virtual:demo-fixtures-runtime-prod-stub";
const VIRTUAL_DEMO_MOCKS_ID = "\0virtual:demo-fixtures-mocks-prod-stub";
const VIRTUAL_DEMO_TLDTS_SHIM_ID = "\0virtual:demo-tldts-shim";
const VIRTUAL_DEMO_VIEW_PREFIX = "\0virtual:demo-view-prod-stub:";
const VIRTUAL_DEMO_STORE_PREFIX = "\0virtual:demo-store-prod-stub:";
const VIRTUAL_PORTAL_MOCK_DATA_ID = "\0virtual:portal-mock-data-prod-stub";

/**
 * The 22 view SFCs in `apps/web/src/views/` backing the 24 demo-only routes
 * (`DEMO_AVAILABLE_SCREENS` minus `AVAILABLE_SCREENS` in `router/availability.js`).
 * In non-demo builds, `router.beforeEach` redirects these route names to `UnavailableView`
 * before any route component resolves, so stubbing their dynamic imports drops their
 * unreachable component subgraphs from Rollup output while keeping route records intact.
 */
export const DEMO_ONLY_WEB_VIEW_FILES = new Set([
  "MdrView.vue",
  "ProtocolDigitizationView.vue",
  "ICFBuilderView.vue",
  "ConsentAuthoringView.vue",
  "AmendmentDiffView.vue",
  "ImagingView.vue",
  "DeviceInvestigationView.vue",
  "CecAdjudicationView.vue",
  "AuditView.vue",
  "AuditorView.vue",
  "RulesView.vue",
  "MedicalCodingView.vue",
  "LakehouseView.vue",
  "SensorTelemetryView.vue",
  "LabGovernanceView.vue",
  "TelehealthDtpView.vue",
  "AggregateSafetyView.vue",
  "QualityCockpitView.vue",
  "NotificationsView.vue",
  "DocumentsHubView.vue",
  "ParticipantWalletView.vue",
  "LaySummaryPortal.vue",
]);

const EMPTY_PORTAL_MOCK_DATA_STUB_SOURCE = `
const EMPTY_LIST = /* @__PURE__ */ Object.freeze([]);
const EMPTY_RECORD = /* @__PURE__ */ Object.freeze({});

export const MOCK_ASSIGNMENTS = EMPTY_LIST;
export const MOCK_INSTRUMENTS = EMPTY_RECORD;
export const MOCK_NOTIFICATIONS = EMPTY_LIST;
export const MOCK_APPROVED_CONTENT = EMPTY_RECORD;
export const MOCK_COMPREHENSION_CHECK = EMPTY_RECORD;
`;

const EMPTY_PROD_STUB_SOURCE = `
const EMPTY_LIST = /* @__PURE__ */ Object.freeze([]);
const EMPTY_RECORD = /* @__PURE__ */ Object.freeze({});

export const DEMO_MANIFEST = /* @__PURE__ */ Object.freeze({
  tenantCode: "",
  tenantUuid: "",
  epochIso: "",
  epochMs: 0,
  sponsor: /* @__PURE__ */ Object.freeze({ code: "", name: "", organizationUuid: "" }),
  study: /* @__PURE__ */ Object.freeze({
    code: "",
    title: "",
    phase: "",
    sponsorCode: "",
    uuid: "",
    ref: "",
  }),
  sites: EMPTY_LIST,
  participants: EMPTY_LIST,
  roles: EMPTY_LIST,
  personas: EMPTY_LIST,
});

export const DEMO_TENANT_CODE = "";
export const DEMO_TENANT_UUID = "";
export const DEMO_EPOCH_ISO = "";
export const DEMO_EPOCH_MS = 0;
export const DEMO_SPONSOR = DEMO_MANIFEST.sponsor;
export const DEMO_STUDY = DEMO_MANIFEST.study;
export const DEMO_SITES = EMPTY_LIST;
export const DEMO_PARTICIPANTS = EMPTY_LIST;
export const DEMO_ROLES = EMPTY_LIST;
export const DEMO_PERSONAS = EMPTY_LIST;
export const DEFAULT_DEMO_SITE = null;
export const DEFAULT_DEMO_PARTICIPANT = null;
export const DEMO_PERSONA_BY_WORKFLOW = EMPTY_RECORD;
export const DEMO_PERSONA_BY_WORKSPACE = EMPTY_RECORD;

export function getDemoSiteByCode() {
  return null;
}

export function getDemoParticipantByCode() {
  return null;
}

export function getDemoPersonaByUsername() {
  return null;
}

export function getDemoPersonaByWorkflow() {
  return null;
}

export function getDemoPersonaForWorkspace() {
  return null;
}

export function toDemoAuthUser() {
  return null;
}
`;

const EMPTY_PROD_RUNTIME_STUB_SOURCE = `
const UINT32_MODULO = 2 ** 32;
const DEFAULT_UNBACKED_ACTION_DETAIL =
  "No Cadence service records this action yet, so nothing was saved.";

export function isDemoBuild(env) {
  return env !== undefined
    ? env?.MODE === "demo" || env?.VITE_DEMO_MODE === "true"
    : false;
}

function liveRandom() {
  if (typeof globalThis.crypto?.getRandomValues === "function") {
    const buf = new Uint32Array(1);
    globalThis.crypto.getRandomValues(buf);
    return buf[0] / UINT32_MODULO;
  }
  return 0.5;
}

export function createDemoRuntime({ defaultTokenLength = 7 } = {}) {
  function demoRandomToken(length = defaultTokenLength, radix = 36) {
    const alphabet = "0123456789abcdefghijklmnopqrstuvwxyz".slice(0, radix);
    let out = "";
    for (let i = 0; i < length; i++) {
      out += alphabet[Math.floor(liveRandom() * alphabet.length)];
    }
    return out;
  }

  return {
    createDemoPrng: () => liveRandom,
    resetDemoRandom() {},
    nextDemoStep: () => 0,
    demoNowMs: () => Date.now(),
    demoNowDate: () => new Date(),
    demoNowIso: () => new Date().toISOString(),
    demoRandom: liveRandom,
    demoRandomInt(min, maxInclusive) {
      const lower = Math.ceil(min);
      return Math.floor(lower + liveRandom() * (Math.floor(maxInclusive) - lower + 1));
    },
    demoRandomToken,
  };
}

export function createUnbackedActionGuard({
  isAllowed = (env) => isDemoBuild(env),
  defaultDetail = DEFAULT_UNBACKED_ACTION_DETAIL,
  onRefuse,
} = {}) {
  function refuseUnbackedAction(action, customDetail = null) {
    const title = \`\${action} isn't available yet\`;
    const detail = customDetail || defaultDetail;
    const message = \`\${title}. \${detail}\`;
    return typeof onRefuse === "function"
      ? onRefuse({ action, title, detail, message, customDetail })
      : message;
  }

  return {
    allowUnbackedAction(action, env) {
      if (isAllowed(env)) return true;
      refuseUnbackedAction(action);
      return false;
    },
    refuseUnbackedAction,
    unbackedActionsEnabled: (env) => Boolean(isAllowed(env)),
  };
}
`;

const EMPTY_PROD_MOCKS_STUB_SOURCE = `
const EMPTY_RECORD = /* @__PURE__ */ Object.freeze({});

export function buildDemoHealthPayload() {
  return EMPTY_RECORD;
}

export function buildSignatureVerificationOutcome() {
  return { status: 400, body: EMPTY_RECORD };
}

export function buildAmendmentReconsentPayload() {
  return EMPTY_RECORD;
}

export function computeMockReceiptDigest() {
  return null;
}

export function decodeReceiptImageText() {
  return "";
}

export function extractDemoReceiptOcr() {
  return EMPTY_RECORD;
}

export function evaluateDemoExpenseClaim() {
  return EMPTY_RECORD;
}
`;

const DEMO_TLDTS_SHIM_SOURCE = `
const MULTI_LABEL_PUBLIC_SUFFIXES = /* @__PURE__ */ new Set([
  "ac.uk",
  "co.jp",
  "co.nz",
  "co.uk",
  "com.au",
  "com.br",
  "github.io",
  "gov.uk",
  "org.uk",
]);

export function getDomain(input, options) {
  if (typeof input !== "string" || !input) return null;
  const host = input
    .trim()
    .toLowerCase()
    .replace(/\\.+$/, "");
  if (!host || host.includes(":") || /^[\\d.]+$/.test(host)) {
    return null;
  }
  const labels = host.split(".");
  if (labels.length < 2 || labels.some((part) => !part)) {
    return null;
  }
  const lastTwo = \`\${labels[labels.length - 2]}.\${labels[labels.length - 1]}\`;
  if (
    MULTI_LABEL_PUBLIC_SUFFIXES.has(lastTwo) &&
    (lastTwo !== "github.io" || options?.allowPrivateDomains !== false)
  ) {
    return labels.length >= 3 ? labels.slice(-3).join(".") : null;
  }
  return lastTwo;
}
`;

const EMPTY_DEMO_VIEW_STUB_SOURCE = `
import { defineComponent } from "vue";

export default defineComponent({
  name: "DemoWorkspaceProdStub",
  render() {
    return null;
  },
});
`;

const EMPTY_DEMO_STORE_STUB_SOURCE = `
const EMPTY_LIST = Object.freeze([]);
const EMPTY_RECORD = Object.freeze({});

export const DEFAULT_CLINICAL_USDM = Object.freeze({
  studyId: "",
  studyTitle: "",
  objectives: EMPTY_LIST,
  arms: EMPTY_LIST,
  epochs: EMPTY_LIST,
  encounters: EMPTY_LIST,
  rows: EMPTY_LIST,
});

export function cloneDefaultClinicalUsdm() {
  return {
    studyId: "",
    studyTitle: "",
    objectives: [],
    arms: [],
    epochs: [],
    encounters: [],
    rows: [],
  };
}

export function buildUsdmSoaSchedule() {
  return {
    arms: [],
    epochs: [],
    encounters: [],
    rows: [],
  };
}

export function formatZodIssues() {
  return [];
}

export function buildMutationValidationPayload() {
  return null;
}

export function registerUsdmSchemas() {}

export async function ensureUsdmSchemas() {
  return {};
}

export async function assertValidUsdmGraph() {}

export function createClinicalUsdmState() {
  return {
    currentUsdm: cloneDefaultClinicalUsdm(),
    activeStudyVersionId: "",
    soaLoading: false,
    soaError: null,
  };
}

export function createClinicalUsdmActions() {
  return {
    async fetchSoAProjection() {},
    validateModel() {
      return { success: true };
    },
    async pushSoAMutation() {},
    async pushSoALink() {},
  };
}

export function createClinicalCtmsState() {
  return {
    currentCtmsData: {
      milestones: [],
      visits: [],
      allocations: [],
      recruitment: [],
    },
  };
}

export const DEFAULT_DEMO_SUBJECTS = EMPTY_LIST;
export const DEFAULT_FORM_VALUES = EMPTY_RECORD;

export function createClinicalEcrfState() {
  return {
    ecrfFields: [],
    formValues: {},
    fieldVisibility: {},
    formQueries: {},
  };
}

export function createClinicalEcrfActions() {
  return {
    async evaluateRules() {},
    triggerValueChange() {},
    getEcrfFieldsForVersion() {
      return [];
    },
    async enrollSubject() {
      return null;
    },
  };
}

export function createClinicalLedgerState() {
  return {
    ledgerBlocks: [],
  };
}

export function createClinicalLedgerActions() {
  return {
    async addLedgerBlock() {
      return {
        index: 0,
        timestamp: "",
        action: "",
        details: {},
        reason: "",
        prevHash: "",
        hash: "",
        synced: false,
      };
    },
    clearLedger() {},
  };
}

export function createClinicalIngestionState() {
  return {
    candidateDraft: null,
    ingestionJobs: [],
    ingestionLoading: false,
    ingestionError: null,
  };
}

export function createClinicalIngestionActions() {
  return {
    async uploadProtocolDocument() {
      return { id: "", status: "EMPTY" };
    },
    async fetchCandidateDraft() {
      return null;
    },
    async transitionCandidateItemState() {
      return null;
    },
    async promoteCandidateDraft() {
      return null;
    },
  };
}

export const DEFAULT_CATEGORIES = EMPTY_LIST;
export const DEFAULT_CONTEXTUAL_SOPS = EMPTY_RECORD;
export const DEFAULT_MOCK_ARTICLES = EMPTY_LIST;

export function getFallbackArticles() {
  return [];
}

export function getFallbackCategories() {
  return [];
}

export function getFallbackContextualHelp() {
  return {
    matched_mapping: null,
    primary_article: null,
    primary_version: null,
    section_anchor: null,
    related_articles: [],
    article: null,
    version: null,
  };
}

export function createKnowledgeDemoState() {
  return {
    categories: [],
    articles: [],
    selectedArticle: null,
  };
}

export const knowledgeDemoAuthoringService = EMPTY_RECORD;

export const DEMO_TICKET_KPIS = EMPTY_RECORD;
export const initialMockTickets = EMPTY_LIST;

export function createInitialMockTickets() {
  return [];
}

export function mergeDemoEscalatedTickets(baseTickets = []) {
  return baseTickets;
}

export function buildDemoTicketDetailSeed() {
  return { comments: [], attachments: [], auditLogs: [] };
}

export function applyDemoTicketTransition() {
  return null;
}

export function applyDemoBatchStatusTransition() {
  return false;
}

export function applyDemoBatchSeverityUpdate() {
  return false;
}

export function buildDemoTicketComment() {
  return null;
}

export function buildDemoTicketAttachment() {
  return null;
}

export function applyDemoTicketSignature() {
  return null;
}

export function exportDemoAuditTrail() {
  return false;
}

export function buildDemoResolvePayload(ticketId = "") {
  return { ticketId, newStatus: "RESOLVED" };
}

export function resolveDemoEtmfStudyId() {
  return "";
}

export function buildDefaultDocuments() {
  return [];
}

export function getDefaultDocuments() {
  return [];
}

export function localPredictTaxonomy() {
  return {};
}

export function buildLocalIntelligenceReport() {
  return {};
}

export function fetchDemoEtmfDocuments() {
  return { handled: false, documentsList: [] };
}

export async function uploadDemoEtmfDocument() {
  return { handled: false, response: null };
}

export function fallbackSubmitDemoDraftForQc() {
  return { handled: false, result: null };
}

export function resetDemoEtmfStorage() {
  return [];
}

export function fallbackStageDemoQcDocument() {
  return { handled: false, item: null };
}

export function fallbackDeriveDemoQcQueue() {
  return { handled: false, qcQueue: [] };
}

export function fallbackReviewDemoQcDocument({ qcQueue = [] } = {}) {
  return { handled: false, qcQueue, result: null };
}

export function fetchDemoDocumentContent() {
  return { handled: false, result: null };
}

export function buildDefaultTemplates() {
  return [];
}

export function getDefaultTemplates() {
  return [];
}

export function getNotificationsStorageKey() {
  return "";
}

export function getInitialDemoNotifications() {
  return [];
}

export async function fetchDemoNotifications() {
  return { handled: false, notifications: [] };
}

export async function acknowledgeDemoNotification() {
  return { handled: false, updated: null };
}

export async function resolveDemoNotification() {
  return { handled: false, updated: null };
}

export function resetDemoNotificationsStorage() {
  return [];
}
`;

function toPosixPath(value) {
  return value.split(path.sep).join("/");
}

function deriveUuidV8(tenantScope, kind, name) {
  const digest = crypto
    .createHash("sha256")
    .update(`cadence-development-demo:${tenantScope}:${kind}:${name}`, "utf8")
    .digest();
  const bytes = Uint8Array.from(digest.subarray(0, 16));
  bytes[6] = (bytes[6] & 0x0f) | 0x80;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Buffer.from(bytes).toString("hex");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}

function formatUtcIsoSeconds(epochMs) {
  return new Date(epochMs).toISOString().replace(".000Z", "Z");
}

function resolveWorkflowAndWorkspace(username, kernelRoleCode, realmRoles) {
  switch (kernelRoleCode) {
    case "DEMO_CRC":
      return ["coordinator", "site_crc"];
    case "DEMO_INVESTIGATOR":
      return ["investigator", "site_investigator"];
    case "DEMO_MONITOR":
      return ["monitor", "cra_monitor"];
    case "DEMO_DATA_MANAGER":
      return ["data_manager", "data_manager"];
    case "DEMO_DESIGNER":
      return realmRoles.includes("Sponsor Admin")
        ? ["designer_lead", "super_admin"]
        : ["designer", "sponsor_designer"];
    case "DEMO_SAFETY":
      return ["safety", "safety_officer"];
    case "DEMO_AUDITOR":
      return ["auditor", "auditor"];
    case "DEMO_STATISTICIAN":
      return username === "stat.approver"
        ? ["statistician_approver", "unblinded_statistician"]
        : ["statistician", "unblinded_statistician"];
    case "DEMO_PHARMACIST":
      return ["pharmacist", "pharmacist"];
    default:
      return [String(kernelRoleCode || "").toLowerCase(), "super_admin"];
  }
}

export function synthesizeDemoManifestModuleSource(demoSeedPath, realmPath) {
  const seed = JSON.parse(fs.readFileSync(demoSeedPath, "utf8"));
  const realm = JSON.parse(fs.readFileSync(realmPath, "utf8"));
  const realmUsersByName = new Map();
  let defaultTenantCode = "cadence-local";

  for (const user of realm.users || []) {
    if (!user?.username) continue;
    const attrs = user.attributes || {};
    const tenantId =
      Array.isArray(attrs.tenant_id) && attrs.tenant_id.length > 0
        ? String(attrs.tenant_id[0])
        : "";
    if (tenantId && defaultTenantCode === "cadence-local") {
      defaultTenantCode = tenantId;
    }
    realmUsersByName.set(user.username, {
      id: String(user.id || ""),
      email: String(user.email || ""),
      firstName: String(user.firstName || ""),
      lastName: String(user.lastName || ""),
      tenantCode: tenantId || "cadence-local",
      realmRoles: Array.isArray(user.realmRoles)
        ? user.realmRoles.map((r) => String(r))
        : [],
    });
  }

  const tenantCode = defaultTenantCode;
  const tenantUuid = deriveUuidV8(tenantCode, "tenant", tenantCode);
  const epochMs = Date.parse(seed.epoch);
  const epochIso = formatUtcIsoSeconds(epochMs);
  const sponsor = {
    code: seed.sponsor.code,
    name: seed.sponsor.name,
    organizationUuid: deriveUuidV8(tenantUuid, "organization", "sponsor"),
  };
  const studyUuid = deriveUuidV8(tenantUuid, "study", seed.study.code);
  const study = {
    code: seed.study.code,
    title: seed.study.title,
    phase: seed.study.phase,
    sponsorCode: seed.sponsor.code,
    uuid: studyUuid,
    ref: `study:${studyUuid}`,
  };

  const sites = (seed.sites || []).map((site) => {
    const institutionUuid = deriveUuidV8(tenantUuid, "organization", site.code);
    const siteUuid = deriveUuidV8(tenantUuid, "site", site.code);
    const studySiteUuid = deriveUuidV8(tenantUuid, "study-site", site.code);
    return {
      code: site.code,
      name: site.name,
      country: site.country,
      institutionCode: site.institutionCode,
      complianceStage: site.complianceStage || "CONDUCT",
      institutionUuid,
      siteUuid,
      studySiteUuid,
      studySiteRef: `study_site:${studySiteUuid}`,
    };
  });

  const participants = (seed.participants || []).map((participant, index) => {
    const participantUuid = deriveUuidV8(
      tenantUuid,
      "participant",
      participant.subjectCode
    );
    const enrollmentUuid = deriveUuidV8(
      tenantUuid,
      "enrollment",
      participant.subjectCode
    );
    return {
      subjectCode: participant.subjectCode,
      siteCode: participant.siteCode,
      enrollmentIndex: index,
      status: "SCREENED",
      screenedAt: formatUtcIsoSeconds(epochMs + (7 + index) * 86_400_000),
      participantUuid,
      enrollmentUuid,
      enrollmentRef: `enrollment:${enrollmentUuid}`,
    };
  });

  const roles = (seed.roles || []).map((role) => ({
    code: role.code,
    name: role.name,
    roleUuid: deriveUuidV8(tenantUuid, "role", role.code),
    capabilities: [...(role.capabilities || [])],
  }));

  const personas = (seed.users || []).map((user) => {
    const realmUser = realmUsersByName.get(user.username) || {
      id: user.subject,
      email: "",
      firstName: "",
      lastName: "",
      tenantCode,
      realmRoles: [],
    };
    const [workflow, workspacePersona] = resolveWorkflowAndWorkspace(
      user.username,
      user.role,
      realmUser.realmRoles
    );
    return {
      username: user.username,
      id: user.subject,
      email: realmUser.email,
      firstName: realmUser.firstName,
      lastName: realmUser.lastName,
      tenantId: realmUser.tenantCode || tenantCode,
      kernelRole: user.role,
      workflow,
      workspacePersona,
      siteCode: user.site ?? null,
      grantUuid: deriveUuidV8(tenantUuid, "grant", user.username),
      realmRoles: [...realmUser.realmRoles],
    };
  });

  return `function deepFreeze(value) {
  if (!value || typeof value !== "object" || Object.isFrozen(value)) {
    return value;
  }
  for (const key of Object.keys(value)) {
    deepFreeze(value[key]);
  }
  return Object.freeze(value);
}

export const __DEMO_MANIFEST_TENANTCODE = ${JSON.stringify(tenantCode)};
export const __DEMO_MANIFEST_TENANTUUID = ${JSON.stringify(tenantUuid)};
export const __DEMO_MANIFEST_EPOCHISO = ${JSON.stringify(epochIso)};
export const __DEMO_MANIFEST_EPOCHMS = ${JSON.stringify(epochMs)};
export const __DEMO_MANIFEST_SPONSOR = /* @__PURE__ */ deepFreeze(${JSON.stringify(sponsor, null, 2)});
export const __DEMO_MANIFEST_STUDY = /* @__PURE__ */ deepFreeze(${JSON.stringify(study, null, 2)});
export const __DEMO_MANIFEST_SITES = /* @__PURE__ */ deepFreeze(${JSON.stringify(sites, null, 2)});
export const __DEMO_MANIFEST_PARTICIPANTS = /* @__PURE__ */ deepFreeze(${JSON.stringify(participants, null, 2)});
export const __DEMO_MANIFEST_ROLES = /* @__PURE__ */ deepFreeze(${JSON.stringify(roles, null, 2)});
export const __DEMO_MANIFEST_PERSONAS = /* @__PURE__ */ deepFreeze(${JSON.stringify(personas, null, 2)});

export const DEMO_MANIFEST = /* @__PURE__ */ Object.freeze({
  tenantCode: __DEMO_MANIFEST_TENANTCODE,
  tenantUuid: __DEMO_MANIFEST_TENANTUUID,
  epochIso: __DEMO_MANIFEST_EPOCHISO,
  epochMs: __DEMO_MANIFEST_EPOCHMS,
  sponsor: __DEMO_MANIFEST_SPONSOR,
  study: __DEMO_MANIFEST_STUDY,
  sites: __DEMO_MANIFEST_SITES,
  participants: __DEMO_MANIFEST_PARTICIPANTS,
  roles: __DEMO_MANIFEST_ROLES,
  personas: __DEMO_MANIFEST_PERSONAS,
});
`;
}

/**
 * Excludes `packages/demo-fixtures`, `src/views/demo/*`, `src/stores/demo/*`,
 * unreachable demo-only route views in `apps/web`, and `src/data/mock-data.js`
 * in `apps/subject-portal` from non-demo production Rollup bundles by resolving
 * them to inert virtual modules during production builds, while synthesizing
 * `DEMO_MANIFEST` virtually from `packages/demo-fixtures/demo-seed.json` and
 * `docker/cadence-realm.json` in demo builds (`build:demo`), dev servers, and
 * Vitest runs.
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function isDemoBuildResolved(appDir, resolved) {
  let fileFlag = false;
  const modeEnvPath = path.join(appDir, `.env.${resolved.mode}`);
  if (fs.existsSync(modeEnvPath)) {
    const content = fs.readFileSync(modeEnvPath, "utf8");
    fileFlag = /^\s*VITE_DEMO_MODE\s*=\s*true\s*$/m.test(content);
  }
  return (
    resolved.mode === "demo" ||
    resolved.env?.VITE_DEMO_MODE === "true" ||
    process.env.VITE_DEMO_MODE === "true" ||
    fileFlag
  );
}

export function createDemoFixturesPlugin(appDir) {
  let stripDemoFixtures = false;
  let isDemoStaticBuild = false;
  const repoRoot = path.resolve(appDir, "../..");
  const demoSeedPath = path.join(
    repoRoot,
    "packages/demo-fixtures/demo-seed.json"
  );
  const realmPath = path.join(repoRoot, "docker/cadence-realm.json");
  const demoFixturesEntry = path.join(
    repoRoot,
    "packages/demo-fixtures/src/index.js"
  );
  const demoFixturesRuntimeEntry = path.join(
    repoRoot,
    "packages/demo-fixtures/src/runtime.js"
  );
  const demoFixturesMocksEntry = path.join(
    repoRoot,
    "packages/demo-fixtures/src/mocks.js"
  );
  const viewsRoot = toPosixPath(path.resolve(appDir, "src/views"));
  const viewsDemoPrefix = `${toPosixPath(path.resolve(appDir, "src/views/demo"))}/`;
  const storesDemoPrefix = `${toPosixPath(path.resolve(appDir, "src/stores/demo"))}/`;
  const portalMockDataBase = toPosixPath(
    path.resolve(appDir, "src/data/mock-data")
  );

  function resolveCandidateSource(source, importer) {
    if (!source || source.startsWith("\0")) return null;
    const cleanSource = source.split("?")[0];
    if (cleanSource.startsWith("@/")) {
      return toPosixPath(path.resolve(appDir, "src", cleanSource.slice(2)));
    }
    if (cleanSource.startsWith(".")) {
      if (!importer || importer.startsWith("\0")) return null;
      const cleanImporter = importer.split("?")[0];
      return toPosixPath(
        path.resolve(path.dirname(cleanImporter), cleanSource)
      );
    }
    if (path.isAbsolute(cleanSource)) {
      return toPosixPath(path.resolve(cleanSource));
    }
    return null;
  }

  return {
    name: "strip-demo-fixtures-in-prod",
    enforce: "pre",
    configResolved(resolved) {
      const isDemo = isDemoBuildResolved(appDir, resolved);
      stripDemoFixtures = resolved.command === "build" && !isDemo;
      isDemoStaticBuild = resolved.command === "build" && isDemo;
    },
    resolveId(source, importer) {
      if (isDemoStaticBuild && source === "tldts") {
        return VIRTUAL_DEMO_TLDTS_SHIM_ID;
      }
      if (source === VIRTUAL_DEMO_MANIFEST_SOURCE) {
        return stripDemoFixtures
          ? VIRTUAL_DEMO_FIXTURES_ID
          : VIRTUAL_DEMO_MANIFEST_ID;
      }
      if (source === "demo-fixtures") {
        return stripDemoFixtures ? VIRTUAL_DEMO_FIXTURES_ID : demoFixturesEntry;
      }
      if (source === "demo-fixtures/runtime") {
        return stripDemoFixtures
          ? VIRTUAL_DEMO_RUNTIME_ID
          : demoFixturesRuntimeEntry;
      }
      if (source === "demo-fixtures/mocks") {
        return stripDemoFixtures
          ? VIRTUAL_DEMO_MOCKS_ID
          : demoFixturesMocksEntry;
      }
      if (!stripDemoFixtures) {
        return null;
      }
      const resolvedPath = resolveCandidateSource(source, importer);
      if (!resolvedPath) {
        return null;
      }
      if (resolvedPath.startsWith(viewsDemoPrefix)) {
        return `${VIRTUAL_DEMO_VIEW_PREFIX}${path.basename(resolvedPath)}`;
      }
      if (resolvedPath.startsWith(storesDemoPrefix)) {
        return `${VIRTUAL_DEMO_STORE_PREFIX}${path.basename(resolvedPath)}`;
      }
      const candidateBaseName = path.posix.basename(resolvedPath);
      if (
        path.posix.dirname(resolvedPath) === viewsRoot &&
        DEMO_ONLY_WEB_VIEW_FILES.has(candidateBaseName)
      ) {
        return `${VIRTUAL_DEMO_VIEW_PREFIX}unavailable-route`;
      }
      if (
        resolvedPath === `${portalMockDataBase}.js` ||
        resolvedPath === portalMockDataBase
      ) {
        return VIRTUAL_PORTAL_MOCK_DATA_ID;
      }
      return null;
    },
    load(id) {
      if (id === VIRTUAL_DEMO_MANIFEST_ID) {
        this?.addWatchFile?.(demoSeedPath);
        this?.addWatchFile?.(realmPath);
        return synthesizeDemoManifestModuleSource(demoSeedPath, realmPath);
      }
      if (id === VIRTUAL_DEMO_FIXTURES_ID) {
        return EMPTY_PROD_STUB_SOURCE;
      }
      if (id === VIRTUAL_DEMO_RUNTIME_ID) {
        return EMPTY_PROD_RUNTIME_STUB_SOURCE;
      }
      if (id === VIRTUAL_DEMO_MOCKS_ID) {
        return EMPTY_PROD_MOCKS_STUB_SOURCE;
      }
      if (id === VIRTUAL_DEMO_TLDTS_SHIM_ID) {
        return DEMO_TLDTS_SHIM_SOURCE;
      }
      if (id === VIRTUAL_PORTAL_MOCK_DATA_ID) {
        return EMPTY_PORTAL_MOCK_DATA_STUB_SOURCE;
      }
      if (id.startsWith(VIRTUAL_DEMO_VIEW_PREFIX)) {
        return EMPTY_DEMO_VIEW_STUB_SOURCE;
      }
      if (id.startsWith(VIRTUAL_DEMO_STORE_PREFIX)) {
        return EMPTY_DEMO_STORE_STUB_SOURCE;
      }
      return null;
    },
  };
}

function rewriteDemoFixturesIndexForTreeShaking(code) {
  if (
    !code.includes(
      'import { DEMO_MANIFEST } from "virtual:demo-fixtures-manifest";'
    )
  ) {
    return null;
  }
  return code
    .replace(
      'import { DEMO_MANIFEST } from "virtual:demo-fixtures-manifest";',
      'import { DEMO_MANIFEST, __DEMO_MANIFEST_TENANTCODE, __DEMO_MANIFEST_TENANTUUID, __DEMO_MANIFEST_EPOCHISO, __DEMO_MANIFEST_EPOCHMS, __DEMO_MANIFEST_SPONSOR, __DEMO_MANIFEST_STUDY, __DEMO_MANIFEST_SITES, __DEMO_MANIFEST_PARTICIPANTS, __DEMO_MANIFEST_ROLES, __DEMO_MANIFEST_PERSONAS } from "virtual:demo-fixtures-manifest";'
    )
    .replace(
      "export const DEMO_TENANT_CODE = DEMO_MANIFEST.tenantCode;",
      "export const DEMO_TENANT_CODE = __DEMO_MANIFEST_TENANTCODE;"
    )
    .replace(
      "export const DEMO_TENANT_UUID = DEMO_MANIFEST.tenantUuid;",
      "export const DEMO_TENANT_UUID = __DEMO_MANIFEST_TENANTUUID;"
    )
    .replace(
      "export const DEMO_EPOCH_ISO = DEMO_MANIFEST.epochIso;",
      "export const DEMO_EPOCH_ISO = __DEMO_MANIFEST_EPOCHISO;"
    )
    .replace(
      "export const DEMO_EPOCH_MS = DEMO_MANIFEST.epochMs;",
      "export const DEMO_EPOCH_MS = __DEMO_MANIFEST_EPOCHMS;"
    )
    .replace(
      "export const DEMO_SPONSOR = DEMO_MANIFEST.sponsor;",
      "export const DEMO_SPONSOR = __DEMO_MANIFEST_SPONSOR;"
    )
    .replace(
      "export const DEMO_STUDY = DEMO_MANIFEST.study;",
      "export const DEMO_STUDY = __DEMO_MANIFEST_STUDY;"
    )
    .replace(
      "export const DEMO_SITES = DEMO_MANIFEST.sites;",
      "export const DEMO_SITES = __DEMO_MANIFEST_SITES;"
    )
    .replace(
      "export const DEMO_PARTICIPANTS = DEMO_MANIFEST.participants;",
      "export const DEMO_PARTICIPANTS = __DEMO_MANIFEST_PARTICIPANTS;"
    )
    .replace(
      "export const DEMO_ROLES = DEMO_MANIFEST.roles;",
      "export const DEMO_ROLES = __DEMO_MANIFEST_ROLES;"
    )
    .replace(
      "export const DEMO_PERSONAS = DEMO_MANIFEST.personas;",
      "export const DEMO_PERSONAS = __DEMO_MANIFEST_PERSONAS;"
    );
}

/**
 * Constant-folds zero-argument `isDemoBuild()`, `isDemoModeActive()`, and
 * `unbackedActionsEnabled()` calls during `vite build` so Rollup dead-code-eliminates
 * unreachable OIDC/streaming branches in demo bundles and synthetic/unbacked branches
 * in live bundles while preserving explicit `(env)` overrides and `vi.stubEnv` in Vitest.
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function createConstantFoldDemoModePlugin(appDir) {
  let isDemo = false;
  const srcRoot = `${path.resolve(appDir, "src").replace(/\\/g, "/")}/`;
  const demoFixturesIndexPath = path
    .resolve(appDir, "../../packages/demo-fixtures/src/index.js")
    .replace(/\\/g, "/");

  return {
    name: "constant-fold-demo-mode",
    apply: "build",
    configResolved(resolved) {
      isDemo = isDemoBuildResolved(appDir, resolved);
    },
    transform(code, id) {
      const cleanId = id.split("?")[0].replace(/\\/g, "/");
      if (isDemo && cleanId === demoFixturesIndexPath) {
        const rewritten = rewriteDemoFixturesIndexForTreeShaking(code);
        return rewritten ? { code: rewritten, map: null } : null;
      }
      if (
        !code.includes("isDemoBuild") &&
        !code.includes("isDemoModeActive") &&
        !code.includes("unbackedActionsEnabled")
      ) {
        return null;
      }
      if (
        !cleanId.startsWith(srcRoot) ||
        cleanId.endsWith("/demoMode.js") ||
        !/\.(?:[cm]?js|ts|vue)$/.test(cleanId)
      ) {
        return null;
      }

      const literal = isDemo ? "(true)" : "(false)";
      const transformed = code.replace(
        /(?<!(?:function|get|set)\s+)(?<![.\w$])(?:isDemoBuild|isDemoModeActive|unbackedActionsEnabled)\(\s*\)(?!\s*\{)/g,
        literal
      );
      if (transformed === code) {
        return null;
      }
      return {
        code: transformed,
        map: null,
      };
    },
  };
}

/**
 * Builds the shared `packages/ui` subpath aliases (`ui/testing`, `ui/vitest`, and `ui`)
 * so `apps/web` and `apps/subject-portal` keep mock-gateway and Vitest-only entrypoints
 * isolated from the library root export (#5335, ADR-2537, ADR-2538).
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @param {{ uiEntry?: string }} [options] - Relative entry inside `packages/ui`.
 * @returns {import("vite").Alias[]}
 */
export function createUiWorkspaceAliases(
  appDir,
  { uiEntry = "index.js" } = {}
) {
  const uiRoot = path.resolve(appDir, "../../packages/ui");
  return [
    {
      find: /^ui\/testing$/,
      replacement: path.resolve(uiRoot, "mockGateway.js"),
    },
    {
      find: /^ui\/vitest$/,
      replacement: path.resolve(uiRoot, "accessibility-matcher.js"),
    },
    {
      find: /^ui$/,
      replacement: path.resolve(uiRoot, uiEntry),
    },
  ];
}
