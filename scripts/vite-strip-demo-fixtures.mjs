import fs from "fs";
import path from "path";

const VIRTUAL_DEMO_FIXTURES_ID = "\0virtual:demo-fixtures-prod-stub";
const VIRTUAL_DEMO_VIEW_PREFIX = "\0virtual:demo-view-prod-stub:";
const VIRTUAL_DEMO_STORE_PREFIX = "\0virtual:demo-store-prod-stub:";

const EMPTY_PROD_STUB_SOURCE = `
const EMPTY_LIST = Object.freeze([]);
const EMPTY_RECORD = Object.freeze({});

export const DEMO_MANIFEST = Object.freeze({
  tenantCode: "",
  tenantUuid: "",
  epochIso: "",
  epochMs: 0,
  sponsor: Object.freeze({ code: "", name: "", organizationUuid: "" }),
  study: Object.freeze({
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

/**
 * Excludes \`packages/demo-fixtures\`, \`src/views/demo/*\`, and \`src/stores/demo/*\`
 * from non-demo production Rollup bundles by resolving them to inert virtual modules
 * during production builds, while preserving real modules in demo builds (\`build:demo\`),
 * dev servers, and test runs.
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
  const demoFixturesEntry = path.resolve(
    appDir,
    "../../packages/demo-fixtures/src/index.js"
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
    },
    resolveId(source, importer) {
      if (source === "demo-fixtures") {
        return stripDemoFixtures ? VIRTUAL_DEMO_FIXTURES_ID : demoFixturesEntry;
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
      if (id === VIRTUAL_DEMO_FIXTURES_ID) {
        return EMPTY_PROD_STUB_SOURCE;
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
