import fs from "node:fs";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import {
  AVAILABLE_SCREENS as WEB_AVAILABLE_SCREENS,
  DEMO_AVAILABLE_SCREENS as WEB_DEMO_AVAILABLE_SCREENS,
} from "../src/router/availability.js";
import {
  SUBJECT_PORTAL_NON_DEMO_STUBBED_VIEWS,
  WEB_NON_DEMO_STUBBED_VIEWS,
  createDemoFixturesPlugin,
} from "../../../scripts/vite-strip-demo-fixtures.mjs";

const webAppDir = path.resolve(process.cwd());
const subjectPortalDir = path.resolve(webAppDir, "../subject-portal");
const viewsImporter = path.join(webAppDir, "src/views/CtmsView.vue");
const routerImporter = path.join(webAppDir, "src/router/index.js");
const storesImporter = path.join(webAppDir, "src/stores/clinical.ts");
const portalRouterImporter = path.join(
  subjectPortalDir,
  "src/router/routes.js"
);
const portalAvailabilityPath = path.join(
  subjectPortalDir,
  "src/router/availability.js"
);
const INFRASTRUCTURE_ROUTE_NAMES = new Set([
  "overview",
  "plans",
  "trial-onboarding",
  "login",
  "forbidden",
  "unavailable",
  "observation",
]);

function createResolver(
  appDir: string,
  command: "build" | "serve",
  mode: string
) {
  const plugin = createDemoFixturesPlugin(appDir);
  plugin.configResolved?.({ command, mode, env: {} } as never);
  return {
    resolveId: plugin.resolveId as (
      source: string,
      importer?: string
    ) => string | null,
    load: plugin.load as (id: string) => string | null,
  };
}

function parseWebRouteViews(routerSource: string) {
  const routeBlocks = routerSource.split(/\n {2}\{\n/).slice(1);
  return routeBlocks
    .map((block) => {
      const nameMatch = block.match(/\bname:\s*"([^"]+)"/);
      const viewMatch = block.match(
        /component:\s*\(\)\s*=>\s*import\("\.\.\/views\/([^"]+)"\)/
      );
      if (!nameMatch || !viewMatch) return null;
      const redirectsInProd =
        /beforeEnter:\s*redirectToCanvasSection\(/.test(block) ||
        /buildCanvasLocationFromEcrfQuery/.test(block);
      return {
        name: nameMatch[1],
        viewFile: viewMatch[1],
        redirectsInProd,
      };
    })
    .filter(
      (
        entry
      ): entry is {
        name: string;
        viewFile: string;
        redirectsInProd: boolean;
      } => entry !== null
    );
}

function parseSubjectPortalRouteViews(routesSource: string) {
  const importMap = new Map<string, string>();
  for (const match of routesSource.matchAll(
    /import\s+(\w+)\s+from\s+"\.\.\/views\/([^"]+)";/g
  )) {
    importMap.set(match[1], match[2]);
  }
  const entries: Array<{ name: string; viewFile: string }> = [];
  for (const match of routesSource.matchAll(
    /name:\s*"([^"]+)",\s*component:\s*(\w+)/g
  )) {
    const viewFile = importMap.get(match[2]);
    if (viewFile) {
      entries.push({ name: match[1], viewFile });
    }
  }
  return entries;
}

describe("vite-strip-demo-fixtures plugin", () => {
  const originalDemoEnv = process.env.VITE_DEMO_MODE;

  afterEach(() => {
    if (originalDemoEnv === undefined) {
      delete process.env.VITE_DEMO_MODE;
    } else {
      process.env.VITE_DEMO_MODE = originalDemoEnv;
    }
  });

  it("stubs demo-fixtures, views/demo/*, and stores/demo/* in non-demo production builds", () => {
    delete process.env.VITE_DEMO_MODE;
    const plugin = createDemoFixturesPlugin(webAppDir);
    plugin.configResolved?.({
      command: "build",
      mode: "production",
      env: {},
    } as never);

    const resolveId = plugin.resolveId as (
      source: string,
      importer?: string
    ) => string | null;
    const load = plugin.load as (id: string) => string | null;

    const fixturesId = resolveId("demo-fixtures");
    expect(fixturesId).toContain("\0virtual:demo-fixtures-prod-stub");
    expect(load(fixturesId!)).toContain("export const DEMO_MANIFEST");

    const runtimeId = resolveId("demo-fixtures/runtime");
    expect(runtimeId).toContain("\0virtual:demo-fixtures-runtime-prod-stub");
    const runtimeStub = load(runtimeId!)!;
    expect(runtimeStub).toContain("export function createDemoRuntime");
    expect(runtimeStub).toContain("export function createUnbackedActionGuard");
    expect(runtimeStub).not.toContain("2026-01-05T09:00:00Z");

    const mocksId = resolveId("demo-fixtures/mocks");
    expect(mocksId).toContain("\0virtual:demo-fixtures-mocks-prod-stub");
    const mocksStub = load(mocksId!)!;
    expect(mocksStub).toContain("export function buildDemoHealthPayload");
    expect(mocksStub).toContain(
      "export function buildSignatureVerificationOutcome"
    );
    expect(mocksStub).toContain(
      "export function buildAmendmentReconsentPayload"
    );
    expect(mocksStub).toContain("export function extractDemoReceiptOcr");
    expect(mocksStub).toContain("export function evaluateDemoExpenseClaim");
    expect(mocksStub).not.toContain("CADENCE-101");

    const demoViewId = resolveId("./demo/CtmsDemoWorkspace.vue", viewsImporter);
    expect(demoViewId).toContain(
      "\0virtual:demo-view-prod-stub:CtmsDemoWorkspace.vue"
    );
    expect(load(demoViewId!)).toContain("DemoWorkspaceProdStub");

    const aliasViewId = resolveId(
      "@/views/demo/EcrfDemoWorkspace.vue",
      viewsImporter
    );
    expect(aliasViewId).toContain(
      "\0virtual:demo-view-prod-stub:EcrfDemoWorkspace.vue"
    );

    const demoStoreId = resolveId("./demo/clinicalEcrf", storesImporter);
    expect(demoStoreId).toContain(
      "\0virtual:demo-store-prod-stub:clinicalEcrf"
    );
    const storeStubSource = load(demoStoreId!)!;
    expect(storeStubSource).toContain("createClinicalUsdmState");
    expect(storeStubSource).toContain("createClinicalEcrfState");
    expect(storeStubSource).toContain("createClinicalLedgerState");
    expect(storeStubSource).toContain("createClinicalIngestionState");

    // Live views and live stores remain untouched.
    expect(resolveId("./CtmsLiveWorkspace.vue", viewsImporter)).toBeNull();
    expect(resolveId("./clinicalScope", storesImporter)).toBeNull();
  });

  it("preserves real demo views and demo store slices in build:demo and dev serve modes", () => {
    delete process.env.VITE_DEMO_MODE;
    const resolveIdFor = (command: "build" | "serve", mode: string) => {
      const plugin = createDemoFixturesPlugin(webAppDir);
      plugin.configResolved?.({ command, mode, env: {} } as never);
      return plugin.resolveId as (
        source: string,
        importer?: string
      ) => string | null;
    };

    const demoBuildPlugin = createDemoFixturesPlugin(webAppDir);
    demoBuildPlugin.configResolved?.({
      command: "build",
      mode: "demo",
      env: {},
    } as never);
    const demoBuildResolve = demoBuildPlugin.resolveId as (
      source: string,
      importer?: string
    ) => string | null;
    const demoBuildLoad = demoBuildPlugin.load as (id: string) => string | null;
    expect(
      demoBuildResolve("./demo/CtmsDemoWorkspace.vue", viewsImporter)
    ).toBeNull();
    expect(demoBuildResolve("./demo/clinicalEcrf", storesImporter)).toBeNull();
    expect(demoBuildResolve("demo-fixtures")).toContain(
      "packages/demo-fixtures/src/index.js"
    );
    expect(demoBuildResolve("demo-fixtures/mocks")).toContain(
      "packages/demo-fixtures/src/mocks.js"
    );
    const manifestId = demoBuildResolve("virtual:demo-fixtures-manifest");
    expect(manifestId).toBe("\0virtual:demo-fixtures-manifest");
    expect(demoBuildLoad(manifestId!)).toContain("CADENCE-101");
    const tldtsId = demoBuildResolve("tldts");
    expect(tldtsId).toBe("\0virtual:demo-tldts-shim");
    expect(demoBuildLoad(tldtsId!)).toContain("export function getDomain");

    const devServeResolve = resolveIdFor("serve", "development");
    expect(
      devServeResolve("./demo/EcrfDemoWorkspace.vue", viewsImporter)
    ).toBeNull();
    expect(devServeResolve("./demo/clinicalUsdm", storesImporter)).toBeNull();
    expect(devServeResolve("tldts")).toBeNull();
  });

  it("maintains route parity across apps/web and stubs every unbacked and redirected view only in non-demo builds", () => {
    delete process.env.VITE_DEMO_MODE;
    const routerSource = fs.readFileSync(routerImporter, "utf8");
    const routeViews = parseWebRouteViews(routerSource);
    expect(routeViews.length).toBeGreaterThanOrEqual(35);

    const expectedStubbed = new Set<string>();
    const expectedLive = new Set<string>();
    for (const { name, viewFile, redirectsInProd } of routeViews) {
      const isLiveAvailable =
        (WEB_AVAILABLE_SCREENS.has(name) ||
          INFRASTRUCTURE_ROUTE_NAMES.has(name)) &&
        !redirectsInProd;
      if (isLiveAvailable) {
        expectedLive.add(viewFile);
      } else {
        expect(
          WEB_DEMO_AVAILABLE_SCREENS.has(name) || redirectsInProd,
          `Unexpected unreachable route ${name} (${viewFile})`
        ).toBe(true);
        expectedStubbed.add(viewFile);
      }
    }

    expect(Array.from(expectedStubbed).sort()).toEqual(
      [...WEB_NON_DEMO_STUBBED_VIEWS].sort()
    );
    for (const liveFile of expectedLive) {
      expect(expectedStubbed.has(liveFile)).toBe(false);
    }

    const prodBuild = createResolver(webAppDir, "build", "production");
    const demoBuild = createResolver(webAppDir, "build", "demo");
    const devServe = createResolver(webAppDir, "serve", "development");

    for (const viewFile of expectedStubbed) {
      const stubId = prodBuild.resolveId(
        `../views/${viewFile}`,
        routerImporter
      );
      expect(stubId).toBe(`\0virtual:demo-view-prod-stub:${viewFile}`);
      expect(prodBuild.load(stubId!)).toContain("DemoWorkspaceProdStub");
      expect(
        demoBuild.resolveId(`../views/${viewFile}`, routerImporter)
      ).toBeNull();
      expect(
        devServe.resolveId(`../views/${viewFile}`, routerImporter)
      ).toBeNull();
    }

    for (const liveFile of expectedLive) {
      expect(
        prodBuild.resolveId(`../views/${liveFile}`, routerImporter)
      ).toBeNull();
    }
  });

  it("maintains route parity across apps/subject-portal and stubs all six demo views only in non-demo builds", () => {
    delete process.env.VITE_DEMO_MODE;
    const availabilitySource = fs.readFileSync(portalAvailabilityPath, "utf8");
    const parseSet = (exportName: string) => {
      const blockMatch = availabilitySource.match(
        new RegExp(
          `export const ${exportName}\\s*=\\s*new Set\\(([\\s\\S]*?)\\);`
        )
      );
      expect(blockMatch).not.toBeNull();
      return new Set(
        Array.from(blockMatch![1].matchAll(/"([^"]+)"/g), (m) => m[1])
      );
    };
    const portalLiveScreens = new Set([
      ...parseSet("AVAILABLE_SCREENS"),
      ...parseSet("AUTHENTICATED_SCREENS"),
    ]);
    const portalDemoScreens = parseSet("DEMO_AVAILABLE_SCREENS");

    const routesSource = fs.readFileSync(portalRouterImporter, "utf8");
    const portalRouteViews = parseSubjectPortalRouteViews(routesSource);
    expect(portalRouteViews).toHaveLength(7);

    const expectedPortalStubbed = portalRouteViews
      .filter(
        ({ name }) =>
          portalDemoScreens.has(name) && !portalLiveScreens.has(name)
      )
      .map(({ viewFile }) => viewFile);
    const expectedPortalLive = [
      ...portalRouteViews
        .filter(({ name }) => portalLiveScreens.has(name))
        .map(({ viewFile }) => viewFile),
      "UnavailableView.vue",
    ];

    expect([...expectedPortalStubbed].sort()).toEqual(
      [...SUBJECT_PORTAL_NON_DEMO_STUBBED_VIEWS].sort()
    );
    expect(expectedPortalStubbed).toHaveLength(6);

    const prodBuild = createResolver(subjectPortalDir, "build", "production");
    const demoBuild = createResolver(subjectPortalDir, "build", "demo");
    const devServe = createResolver(subjectPortalDir, "serve", "development");

    for (const viewFile of expectedPortalStubbed) {
      const stubId = prodBuild.resolveId(
        `../views/${viewFile}`,
        portalRouterImporter
      );
      expect(stubId).toBe(`\0virtual:demo-view-prod-stub:${viewFile}`);
      expect(prodBuild.load(stubId!)).toContain("DemoWorkspaceProdStub");
      expect(
        demoBuild.resolveId(`../views/${viewFile}`, portalRouterImporter)
      ).toBeNull();
      expect(
        devServe.resolveId(`../views/${viewFile}`, portalRouterImporter)
      ).toBeNull();
    }

    for (const liveFile of expectedPortalLive) {
      expect(
        prodBuild.resolveId(`../views/${liveFile}`, portalRouterImporter)
      ).toBeNull();
    }
  });
});
