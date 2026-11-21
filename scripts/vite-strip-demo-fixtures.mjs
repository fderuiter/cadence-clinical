import fs from "fs";
import path from "path";

const VIRTUAL_DEMO_FIXTURES_ID = "\0virtual:demo-fixtures-prod-stub";

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

/**
 * Excludes \`packages/demo-fixtures\` from non-demo production Rollup bundles by
 * resolving \`demo-fixtures\` to an inert virtual module during production builds,
 * while resolving directly to \`packages/demo-fixtures/src/index.js\` in demo builds,
 * dev servers, and test runs.
 *
 * @param {string} appDir - Absolute directory of the Vite app.
 * @returns {import("vite").Plugin}
 */
export function createDemoFixturesPlugin(appDir) {
  let stripDemoFixtures = false;
  const demoFixturesEntry = path.resolve(
    appDir,
    "../../packages/demo-fixtures/src/index.js"
  );

  return {
    name: "strip-demo-fixtures-in-prod",
    enforce: "pre",
    configResolved(resolved) {
      let fileFlag = false;
      const modeEnvPath = path.join(appDir, `.env.${resolved.mode}`);
      if (fs.existsSync(modeEnvPath)) {
        const content = fs.readFileSync(modeEnvPath, "utf8");
        fileFlag = /^\s*VITE_DEMO_MODE\s*=\s*true\s*$/m.test(content);
      }
      const isDemo =
        resolved.mode === "demo" ||
        resolved.env?.VITE_DEMO_MODE === "true" ||
        process.env.VITE_DEMO_MODE === "true" ||
        fileFlag;
      stripDemoFixtures = resolved.command === "build" && !isDemo;
    },
    resolveId(source) {
      if (source === "demo-fixtures") {
        return stripDemoFixtures ? VIRTUAL_DEMO_FIXTURES_ID : demoFixturesEntry;
      }
      return null;
    },
    load(id) {
      if (id === VIRTUAL_DEMO_FIXTURES_ID) {
        return EMPTY_PROD_STUB_SOURCE;
      }
      return null;
    },
  };
}
