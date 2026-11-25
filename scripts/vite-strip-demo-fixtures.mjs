import crypto from "crypto";
import fs from "fs";
import path from "path";
import ts from "typescript";

const VIRTUAL_DEMO_MANIFEST_SOURCE = "virtual:demo-fixtures-manifest";
const VIRTUAL_DEMO_MANIFEST_ID = "\0virtual:demo-fixtures-manifest";
const VIRTUAL_DEMO_FIXTURES_ID = "\0virtual:demo-fixtures-prod-stub";
const VIRTUAL_DEMO_RUNTIME_ID = "\0virtual:demo-fixtures-runtime-prod-stub";
const VIRTUAL_DEMO_MOCKS_ID = "\0virtual:demo-fixtures-mocks-prod-stub";
const VIRTUAL_DEMO_TLDTS_SHIM_ID = "\0virtual:demo-tldts-shim";
const VIRTUAL_DEMO_VIEW_PREFIX = "\0virtual:demo-view-prod-stub:";
const VIRTUAL_DEMO_STORE_PREFIX = "\0virtual:demo-store-prod-stub:";

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

  const manifest = {
    tenantCode,
    tenantUuid,
    epochIso,
    epochMs,
    sponsor,
    study,
    sites,
    participants,
    roles,
    personas,
  };

  return `function deepFreeze(value) {
  if (!value || typeof value !== "object" || Object.isFrozen(value)) {
    return value;
  }
  for (const key of Object.keys(value)) {
    deepFreeze(value[key]);
  }
  return Object.freeze(value);
}

export const DEMO_MANIFEST = /* @__PURE__ */ deepFreeze(${JSON.stringify(manifest, null, 2)});
`;
}

/**
 * Excludes \`packages/demo-fixtures\`, \`src/views/demo/*\`, and \`src/stores/demo/*\`
 * from non-demo production Rollup bundles by resolving them to inert virtual modules
 * during production builds, while synthesizing \`DEMO_MANIFEST\` virtually from
 * \`packages/demo-fixtures/demo-seed.json\` and \`docker/cadence-realm.json\` in demo
 * builds (\`build:demo\`), dev servers, and Vitest runs.
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
  const viewsDemoPrefix = `${toPosixPath(path.resolve(appDir, "src/views/demo"))}/`;
  const storesDemoPrefix = `${toPosixPath(path.resolve(appDir, "src/stores/demo"))}/`;

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

/**
 * Constant-folds zero-argument `isDemoBuild()` calls during `vite build` so Rollup
 * dead-code-eliminates OIDC (`keycloak-js`) and streaming (`hls.js`) branches in demo
 * bundles while preserving `isDemoBuild(env)` overrides and `vi.stubEnv` in Vitest.
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function createConstantFoldDemoModePlugin(appDir) {
  let isDemo = false;
  const srcRoot = `${path.resolve(appDir, "src").replace(/\\/g, "/")}/`;

  return {
    name: "constant-fold-demo-mode",
    apply: "build",
    configResolved(resolved) {
      isDemo = isDemoBuildResolved(appDir, resolved);
    },
    transform(code, id) {
      if (!code.includes("isDemoBuild")) {
        return null;
      }
      const cleanId = id.split("?")[0].replace(/\\/g, "/");
      if (
        !cleanId.startsWith(srcRoot) ||
        cleanId.endsWith("/demoMode.js") ||
        !/\.(?:[cm]?js|ts|vue)$/.test(cleanId)
      ) {
        return null;
      }

      const literal = isDemo ? "(true)" : "(false)";
      const transformed = code.replace(
        /(?<!function\s+)(?<![.\w$])isDemoBuild\(\s*\)/g,
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

function walkUiVueComponents(dir) {
  const entries = [];
  if (!fs.existsSync(dir)) return entries;
  for (const item of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, item.name);
    if (item.isDirectory()) {
      entries.push(...walkUiVueComponents(fullPath));
    } else if (item.name.endsWith(".vue")) {
      entries.push({
        name: path.basename(item.name, ".vue"),
        fullPath,
      });
    }
  }
  return entries.sort((a, b) =>
    a.name === b.name
      ? a.fullPath.localeCompare(b.fullPath)
      : a.name < b.name
        ? -1
        : 1
  );
}

/**
 * Expands `packages/ui/src/index.js` with static named ESM re-exports for every `.vue`
 * component under `packages/ui/src/components/` so adding a shared component requires
 * zero edits to tracked barrel files while preserving Rollup tree-shaking.
 *
 * @param {string} [uiSrcDir] - Absolute path to `packages/ui/src`.
 * @returns {import("vite").Plugin}
 */
export function createUiAutoBarrelPlugin(uiSrcDir) {
  const resolvedUiSrcDir = toPosixPath(
    uiSrcDir
      ? path.resolve(uiSrcDir)
      : path.resolve(
          path.dirname(new URL(import.meta.url).pathname),
          "../packages/ui/src"
        )
  );
  const targetEntry = `${resolvedUiSrcDir}/index.js`;
  const componentsRoot = path.join(resolvedUiSrcDir, "components");

  return {
    name: "cadence-ui-auto-barrel",
    enforce: "pre",
    transform(code, id) {
      const cleanId = toPosixPath(id.split("?")[0]);
      if (cleanId !== targetEntry) {
        return null;
      }
      const discovered = walkUiVueComponents(componentsRoot);
      if (typeof this?.addWatchFile === "function") {
        for (const item of discovered) {
          this.addWatchFile(item.fullPath);
        }
      }
      const exportLines = discovered.map((item) => {
        const rel = `./${toPosixPath(path.relative(resolvedUiSrcDir, item.fullPath))}`;
        return `export { default as ${item.name} } from ${JSON.stringify(rel)};`;
      });
      return {
        code: `${code}\n${exportLines.join("\n")}\n`,
        map: null,
      };
    },
  };
}

function parseCanvasSectionMetaFile(filePath) {
  const sourceText = fs.readFileSync(filePath, "utf8");
  const sf = ts.createSourceFile(
    path.basename(filePath),
    sourceText,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS
  );
  const fields = { order: 9999 };

  function visit(node) {
    if (ts.isObjectLiteralExpression(node)) {
      let hasId = false;
      let hasStageId = false;
      for (const prop of node.properties) {
        if (!ts.isPropertyAssignment(prop) || !prop.name) continue;
        const name = ts.isIdentifier(prop.name)
          ? prop.name.text
          : ts.isStringLiteral(prop.name)
            ? prop.name.text
            : "";
        if (name === "id" && ts.isStringLiteralLike(prop.initializer)) {
          fields.id = prop.initializer.text;
          hasId = true;
        } else if (
          name === "stageId" &&
          ts.isStringLiteralLike(prop.initializer)
        ) {
          fields.stageId = prop.initializer.text;
          hasStageId = true;
        } else if (name === "order" && ts.isNumericLiteral(prop.initializer)) {
          fields.order = Number(prop.initializer.text);
        } else if (
          name === "label" &&
          ts.isStringLiteralLike(prop.initializer)
        ) {
          fields.label = prop.initializer.text;
        } else if (
          name === "description" &&
          ts.isStringLiteralLike(prop.initializer)
        ) {
          fields.description = prop.initializer.text;
        }
      }
      if (hasId && hasStageId) {
        return;
      }
    }
    ts.forEachChild(node, visit);
  }

  visit(sf);
  return fields.id && fields.stageId ? fields : null;
}

/**
 * Populates `apps/web/src/canvas/sections/meta.ts` from `*.section.ts` descriptors
 * at Vite transform time so cold-boot stage and palette navigation never pulls
 * section `.vue` components or route-only API modules into `index.html`, while
 * keeping `StudyCanvasView` section mounting synchronous.
 *
 * @param {string} appDir - Absolute directory of `apps/web`.
 * @returns {import("vite").Plugin}
 */
export function createCanvasSectionMetaPlugin(appDir) {
  const sectionsDir = path.resolve(appDir, "src/canvas/sections");
  const targetMetaFile = toPosixPath(path.join(sectionsDir, "meta.ts"));

  return {
    name: "cadence-canvas-section-meta",
    enforce: "pre",
    transform(_code, id) {
      const cleanId = toPosixPath(id.split("?")[0]);
      if (cleanId !== targetMetaFile || !fs.existsSync(sectionsDir)) {
        return null;
      }

      const items = [];
      for (const entry of fs.readdirSync(sectionsDir, {
        withFileTypes: true,
      })) {
        if (!entry.isFile() || !entry.name.endsWith(".section.ts")) {
          continue;
        }
        const fullPath = path.join(sectionsDir, entry.name);
        if (typeof this?.addWatchFile === "function") {
          this.addWatchFile(fullPath);
        }
        const parsed = parseCanvasSectionMetaFile(fullPath);
        if (parsed) {
          items.push(parsed);
        }
      }

      items.sort((a, b) =>
        a.order !== b.order ? a.order - b.order : a.id.localeCompare(b.id)
      );

      return {
        code: `export const SECTION_METADATA = Object.freeze(${JSON.stringify(items)});
export function sectionsForStage(stageId) {
  return SECTION_METADATA.filter((item) => item.stageId === stageId).map((item) => ({
    id: item.id,
    label: item.label,
    description: item.description,
  }));
}
`,
        map: null,
      };
    },
  };
}

function getPropNameText(nameNode) {
  if (!nameNode) return "";
  if (ts.isIdentifier(nameNode) || ts.isStringLiteralLike(nameNode)) {
    return nameNode.text;
  }
  return "";
}

function parsePlainObjectLiteral(objNode) {
  const out = {};
  for (const prop of objNode.properties) {
    if (!ts.isPropertyAssignment(prop)) continue;
    const key = getPropNameText(prop.name);
    if (!key) continue;
    const init = prop.initializer;
    if (ts.isStringLiteralLike(init)) {
      out[key] = init.text;
    } else if (init.kind === ts.SyntaxKind.TrueKeyword) {
      out[key] = true;
    } else if (init.kind === ts.SyntaxKind.FalseKeyword) {
      out[key] = false;
    } else if (ts.isNumericLiteral(init)) {
      out[key] = Number(init.text);
    }
  }
  return out;
}

function findHelperRouteCatalog(helpersPath) {
  if (!fs.existsSync(helpersPath)) return null;
  const src = fs.readFileSync(helpersPath, "utf8");
  const sf = ts.createSourceFile(
    path.basename(helpersPath),
    src,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS
  );
  let found = null;
  function visit(node) {
    if (found) return;
    if (ts.isObjectLiteralExpression(node)) {
      const hasRoute = node.properties.some(
        (p) => ts.isPropertyAssignment(p) && getPropNameText(p.name) === "route"
      );
      const catalogProp = node.properties.find(
        (p) =>
          ts.isPropertyAssignment(p) &&
          getPropNameText(p.name) === "catalog" &&
          ts.isObjectLiteralExpression(p.initializer)
      );
      if (hasRoute && catalogProp && ts.isPropertyAssignment(catalogProp)) {
        found = parsePlainObjectLiteral(catalogProp.initializer);
        return;
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sf);
  return found;
}

function extractRouteFileCatalogEntry(filePath, helperCatalog) {
  const src = fs.readFileSync(filePath, "utf8");
  const sf = ts.createSourceFile(
    path.basename(filePath),
    src,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS
  );
  for (const stmt of sf.statements) {
    if (!ts.isExportAssignment(stmt) || stmt.isExportEquals) continue;
    const expr = stmt.expression;
    if (
      ts.isCallExpression(expr) &&
      expr.arguments.length > 0 &&
      ts.isObjectLiteralExpression(expr.arguments[0]) &&
      helperCatalog
    ) {
      const argObj = parsePlainObjectLiteral(expr.arguments[0]);
      if (typeof argObj.name === "string" && argObj.name) {
        return [argObj.name, { ...helperCatalog }];
      }
    }
    if (ts.isObjectLiteralExpression(expr)) {
      let routeName = "";
      let catalog = null;
      for (const prop of expr.properties) {
        if (!ts.isPropertyAssignment(prop)) continue;
        const propName = getPropNameText(prop.name);
        if (
          propName === "route" &&
          ts.isObjectLiteralExpression(prop.initializer)
        ) {
          const routeFields = parsePlainObjectLiteral(prop.initializer);
          if (typeof routeFields.name === "string") {
            routeName = routeFields.name;
          }
        } else if (
          propName === "catalog" &&
          ts.isObjectLiteralExpression(prop.initializer)
        ) {
          catalog = parsePlainObjectLiteral(prop.initializer);
        }
      }
      if (routeName && catalog) {
        return [routeName, catalog];
      }
    }
  }
  return null;
}

function stripRouteDescriptorCatalogProperty(code, filePath) {
  const sf = ts.createSourceFile(
    path.basename(filePath),
    code,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS
  );
  const removals = [];
  function visit(node) {
    if (ts.isObjectLiteralExpression(node)) {
      const hasRoute = node.properties.some(
        (p) => ts.isPropertyAssignment(p) && getPropNameText(p.name) === "route"
      );
      if (hasRoute) {
        for (const prop of node.properties) {
          if (
            ts.isPropertyAssignment(prop) &&
            getPropNameText(prop.name) === "catalog" &&
            ts.isObjectLiteralExpression(prop.initializer)
          ) {
            const start = prop.getStart(sf);
            let end = prop.getEnd();
            while (end < code.length && /[ \t]/.test(code[end])) end++;
            if (code[end] === ",") end++;
            removals.push([start, end]);
          }
        }
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sf);
  if (removals.length === 0) return null;
  removals.sort((a, b) => b[0] - a[0]);
  let updated = code;
  for (const [start, end] of removals) {
    updated = `${updated.slice(0, start)}${updated.slice(end)}`;
  }
  return updated;
}

/**
 * Isolates colocated `*.route.js` `catalog` metadata into lazy `screenCatalog.js`
 * during production builds so `/unavailable` roadmap copy never inflates cold-boot JS.
 *
 * @param {string} appDir - Absolute directory of `apps/web`.
 * @returns {import("vite").Plugin}
 */
export function createWebRouteCatalogPlugin(appDir) {
  const routerDir = path.resolve(appDir, "src/router");
  const routesDir = toPosixPath(path.join(routerDir, "routes"));
  const helpersFile = toPosixPath(path.join(routerDir, "helpers.js"));
  const availabilityFile = toPosixPath(path.join(routerDir, "availability.js"));
  const screenCatalogFile = toPosixPath(
    path.join(routerDir, "screenCatalog.js")
  );
  let isProdBuild = false;

  return {
    name: "cadence-web-route-catalog",
    enforce: "pre",
    configResolved(resolved) {
      isProdBuild = resolved.command === "build";
    },
    transform(code, id) {
      if (!isProdBuild) return null;
      const cleanId = toPosixPath(id.split("?")[0]);
      if (cleanId === availabilityFile) {
        const cleaned = code.replace(
          /export\s*\{[\s\S]*?\}\s*from\s*["']\.\/screenCatalog(?:\.js)?["'];?/,
          ""
        );
        return cleaned !== code ? { code: cleaned, map: null } : null;
      }

      if (cleanId === screenCatalogFile && fs.existsSync(routesDir)) {
        if (
          typeof this?.addWatchFile === "function" &&
          fs.existsSync(helpersFile)
        ) {
          this.addWatchFile(helpersFile);
        }
        const helperCatalog = findHelperRouteCatalog(helpersFile);
        const entries = [];
        const files = fs
          .readdirSync(routesDir, { withFileTypes: true })
          .filter((e) => e.isFile() && e.name.endsWith(".route.js"))
          .map((e) => e.name)
          .sort();
        for (const name of files) {
          const fullPath = path.join(routesDir, name);
          if (typeof this?.addWatchFile === "function") {
            this.addWatchFile(fullPath);
          }
          const entry = extractRouteFileCatalogEntry(fullPath, helperCatalog);
          if (entry) {
            entries.push(entry);
          }
        }
        const catalogMap = Object.fromEntries(entries);
        return {
          code: `export const SCREEN_CATALOG = /* @__PURE__ */ Object.freeze(${JSON.stringify(catalogMap)});
const DEFAULT_SCREEN_METADATA = Object.freeze({
  title: "This screen",
  family: "Blueprint",
  minimumTier: "Individual",
  isBaselineCapability: false,
  roadmapStage: "Pre-release module roadmap",
  summary:
    "This workspace module is progressing through pre-release Clinical Kernel integration.",
});
export function screenMetadata(routeName) {
  if (!routeName) return DEFAULT_SCREEN_METADATA;
  return SCREEN_CATALOG[routeName] || DEFAULT_SCREEN_METADATA;
}
export function screenTitle(routeName) {
  return SCREEN_CATALOG[routeName]?.title || "This screen";
}
`,
          map: null,
        };
      }

      if (
        cleanId === helpersFile ||
        (cleanId.startsWith(`${routesDir}/`) && cleanId.endsWith(".route.js"))
      ) {
        const stripped = stripRouteDescriptorCatalogProperty(code, cleanId);
        if (stripped !== null) {
          return { code: stripped, map: null };
        }
      }

      return null;
    },
  };
}
