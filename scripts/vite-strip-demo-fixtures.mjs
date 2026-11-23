import fs from "fs";
import path from "path";
import ts from "typescript";

const VIRTUAL_DEMO_FIXTURES_ID = "\0virtual:demo-fixtures-prod-stub";
const VIRTUAL_DEMO_RUNTIME_ID = "\0virtual:demo-fixtures-runtime-prod-stub";
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
  const demoFixturesRuntimeEntry = path.resolve(
    appDir,
    "../../packages/demo-fixtures/src/runtime.js"
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
      if (source === "demo-fixtures/runtime") {
        return stripDemoFixtures
          ? VIRTUAL_DEMO_RUNTIME_ID
          : demoFixturesRuntimeEntry;
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
      if (id === VIRTUAL_DEMO_RUNTIME_ID) {
        return EMPTY_PROD_RUNTIME_STUB_SOURCE;
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
