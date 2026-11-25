import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import {
  createConstantFoldDemoModePlugin,
  createDemoFixturesPlugin,
  DEMO_ONLY_WEB_VIEW_FILES,
} from "../../../scripts/vite-strip-demo-fixtures.mjs";

const webAppDir = path.resolve(process.cwd());
const portalAppDir = path.resolve(webAppDir, "../subject-portal");
const viewsImporter = path.join(webAppDir, "src/views/CtmsView.vue");
const routerImporter = path.join(webAppDir, "src/router/index.js");
const drawerImporter = path.join(
  webAppDir,
  "src/components/tickets/TicketDetailDrawer.vue"
);
const storesImporter = path.join(webAppDir, "src/stores/clinical.ts");
const apiImporter = path.join(webAppDir, "src/api/knowledge.ts");
const portalFeatureImporter = path.join(
  portalAppDir,
  "src/features/site-support.js"
);

describe("vite-strip-demo-fixtures plugin", () => {
  const originalDemoEnv = process.env.VITE_DEMO_MODE;

  afterEach(() => {
    if (originalDemoEnv === undefined) {
      delete process.env.VITE_DEMO_MODE;
    } else {
      process.env.VITE_DEMO_MODE = originalDemoEnv;
    }
  });

  it("stubs demo-fixtures, demo-fixtures/mocks, views/demo/*, and stores/demo/* in non-demo production builds", () => {
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

    for (const [source, importer, expectedSuffix] of [
      ["./demo/CtmsDemoWorkspace.vue", viewsImporter, "CtmsDemoWorkspace.vue"],
      [
        "@/views/demo/EcrfDemoWorkspace.vue",
        viewsImporter,
        "EcrfDemoWorkspace.vue",
      ],
      [
        "./demo/KnowledgeDemoWorkspace.vue",
        viewsImporter,
        "KnowledgeDemoWorkspace.vue",
      ],
      [
        "./demo/TicketsDemoWorkspace.vue",
        viewsImporter,
        "TicketsDemoWorkspace.vue",
      ],
      [
        "../../views/demo/TicketAttachmentsDemoPanel.vue",
        drawerImporter,
        "TicketAttachmentsDemoPanel.vue",
      ],
    ] as const) {
      const demoViewId = resolveId(source, importer);
      expect(demoViewId).toContain(
        `\0virtual:demo-view-prod-stub:${expectedSuffix}`
      );
      expect(load(demoViewId!)).toContain("DemoWorkspaceProdStub");
    }

    for (const [source, importer, expectedSuffix] of [
      ["./demo/clinicalEcrf", storesImporter, "clinicalEcrf"],
      ["./demo/usdmSoaSchedule", storesImporter, "usdmSoaSchedule"],
      ["../stores/demo/knowledgeDemo", apiImporter, "knowledgeDemo"],
      ["./demo/ticketsDemo", storesImporter, "ticketsDemo"],
      ["./demo/etmfDemo.js", storesImporter, "etmfDemo.js"],
      ["./demo/notificationsDemo.js", storesImporter, "notificationsDemo.js"],
    ] as const) {
      const demoStoreId = resolveId(source, importer);
      expect(demoStoreId).toContain(
        `\0virtual:demo-store-prod-stub:${expectedSuffix}`
      );
      const storeStubSource = load(demoStoreId!)!;
      expect(storeStubSource).toContain("buildUsdmSoaSchedule");
      expect(storeStubSource).toContain("createClinicalUsdmState");
      expect(storeStubSource).toContain("createClinicalEcrfState");
      expect(storeStubSource).toContain("createClinicalLedgerState");
      expect(storeStubSource).toContain("createClinicalIngestionState");
      expect(storeStubSource).toContain("createKnowledgeDemoState");
      expect(storeStubSource).toContain("knowledgeDemoAuthoringService");
      expect(storeStubSource).toContain("initialMockTickets");
      expect(storeStubSource).toContain("fetchDemoEtmfDocuments");
      expect(storeStubSource).toContain("fetchDemoNotifications");
    }

    // Live views and live stores remain untouched.
    expect(resolveId("./CtmsLiveWorkspace.vue", viewsImporter)).toBeNull();
    expect(resolveId("../views/KnowledgeView.vue", routerImporter)).toBeNull();
    expect(resolveId("../views/TicketsView.vue", routerImporter)).toBeNull();
    expect(
      resolveId("../views/StudyCanvasView.vue", routerImporter)
    ).toBeNull();
    expect(
      resolveId("../views/UnavailableView.vue", routerImporter)
    ).toBeNull();
    expect(resolveId("./clinicalScope", storesImporter)).toBeNull();
  });

  it("stubs all 22 demo-only route views in non-demo web production builds", () => {
    delete process.env.VITE_DEMO_MODE;
    expect(DEMO_ONLY_WEB_VIEW_FILES.size).toBe(22);

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

    for (const viewFile of DEMO_ONLY_WEB_VIEW_FILES) {
      const resolved = resolveId(`../views/${viewFile}`, routerImporter);
      expect(resolved).toBe("\0virtual:demo-view-prod-stub:unavailable-route");
      expect(load(resolved!)).toContain("DemoWorkspaceProdStub");
    }
  });

  it("stubs subject-portal src/data/mock-data.js in non-demo production builds", () => {
    delete process.env.VITE_DEMO_MODE;
    const prodPlugin = createDemoFixturesPlugin(portalAppDir);
    prodPlugin.configResolved?.({
      command: "build",
      mode: "production",
      env: {},
    } as never);

    const resolveProd = prodPlugin.resolveId as (
      source: string,
      importer?: string
    ) => string | null;
    const loadProd = prodPlugin.load as (id: string) => string | null;

    const stubId = resolveProd("../data/mock-data.js", portalFeatureImporter);
    expect(stubId).toBe("\0virtual:portal-mock-data-prod-stub");
    const stubSource = loadProd(stubId!)!;
    expect(stubSource).toContain("export const MOCK_ASSIGNMENTS");
    expect(stubSource).toContain("export const MOCK_INSTRUMENTS");
    expect(stubSource).toContain("export const MOCK_NOTIFICATIONS");
    expect(stubSource).toContain("export const MOCK_APPROVED_CONTENT");
    expect(stubSource).toContain("export const MOCK_COMPREHENSION_CHECK");
    expect(stubSource).not.toContain("inst_daily_diary");

    const demoPlugin = createDemoFixturesPlugin(portalAppDir);
    demoPlugin.configResolved?.({
      command: "build",
      mode: "demo",
      env: {},
    } as never);
    const resolveDemo = demoPlugin.resolveId as (
      source: string,
      importer?: string
    ) => string | null;
    expect(
      resolveDemo("../data/mock-data.js", portalFeatureImporter)
    ).toBeNull();
  });

  it("preserves real demo views, demo-only route views, virtual manifest, and demo store slices in build:demo and dev serve modes", () => {
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
    expect(
      demoBuildResolve("./demo/KnowledgeDemoWorkspace.vue", viewsImporter)
    ).toBeNull();
    expect(demoBuildResolve("../views/MdrView.vue", routerImporter)).toBeNull();
    expect(demoBuildResolve("./demo/clinicalEcrf", storesImporter)).toBeNull();
    expect(
      demoBuildResolve("../stores/demo/knowledgeDemo", apiImporter)
    ).toBeNull();
    expect(demoBuildResolve("demo-fixtures")).toContain(
      "packages/demo-fixtures/src/index.js"
    );
    expect(demoBuildResolve("demo-fixtures/mocks")).toContain(
      "packages/demo-fixtures/src/mocks.js"
    );
    const manifestId = demoBuildResolve("virtual:demo-fixtures-manifest");
    expect(manifestId).toBe("\0virtual:demo-fixtures-manifest");
    const manifestSource = demoBuildLoad(manifestId!)!;
    expect(manifestSource).toContain("CADENCE-101");
    expect(manifestSource).toContain("export const __DEMO_MANIFEST_PERSONAS");
    const tldtsId = demoBuildResolve("tldts");
    expect(tldtsId).toBe("\0virtual:demo-tldts-shim");
    expect(demoBuildLoad(tldtsId!)).toContain("export function getDomain");

    const devServeResolve = resolveIdFor("serve", "development");
    expect(
      devServeResolve("./demo/EcrfDemoWorkspace.vue", viewsImporter)
    ).toBeNull();
    expect(
      devServeResolve("../views/LaySummaryPortal.vue", routerImporter)
    ).toBeNull();
    expect(devServeResolve("./demo/clinicalUsdm", storesImporter)).toBeNull();
    expect(devServeResolve("tldts")).toBeNull();
  });

  it("constant-folds zero-argument isDemoBuild(), isDemoModeActive(), and unbackedActionsEnabled() and rewrites demo-fixtures index for pure section tree-shaking", () => {
    delete process.env.VITE_DEMO_MODE;
    const sampleId = path.join(webAppDir, "src/views/TicketsView.vue");
    const demoModeId = path.join(webAppDir, "src/utils/demoMode.js");
    const sampleCode = [
      "const a = isDemoBuild();",
      "const b = isDemoModeActive();",
      "const c = unbackedActionsEnabled();",
      "const d = isDemoBuild(env);",
      "const e = isDemoModeActive(env);",
      "const f = unbackedActionsEnabled(env);",
      "const g = helper.isDemoBuild();",
      "function isDemoModeActive() { return false; }",
      "const __returned__ = { get isDemoModeActive() { return isDemoModeActive }, unbackedActionsEnabled() { return false; } };",
    ].join("\n");

    const prodFold = createConstantFoldDemoModePlugin(webAppDir);
    prodFold.configResolved?.({
      command: "build",
      mode: "production",
      env: {},
    } as never);
    const transformProd = prodFold.transform as (
      code: string,
      id: string
    ) => { code: string } | null;

    const prodResult = transformProd(sampleCode, sampleId);
    expect(prodResult?.code).toContain("const a = (false);");
    expect(prodResult?.code).toContain("const b = (false);");
    expect(prodResult?.code).toContain("const c = (false);");
    expect(prodResult?.code).toContain("const d = isDemoBuild(env);");
    expect(prodResult?.code).toContain("const e = isDemoModeActive(env);");
    expect(prodResult?.code).toContain(
      "const f = unbackedActionsEnabled(env);"
    );
    expect(prodResult?.code).toContain("const g = helper.isDemoBuild();");
    expect(prodResult?.code).toContain(
      "function isDemoModeActive() { return false; }"
    );
    expect(prodResult?.code).toContain(
      "const __returned__ = { get isDemoModeActive() { return isDemoModeActive }, unbackedActionsEnabled() { return false; } };"
    );
    expect(transformProd(sampleCode, demoModeId)).toBeNull();

    const demoFold = createConstantFoldDemoModePlugin(portalAppDir);
    demoFold.configResolved?.({
      command: "build",
      mode: "demo",
      env: {},
    } as never);
    const transformDemo = demoFold.transform as (
      code: string,
      id: string
    ) => { code: string } | null;
    const portalSampleId = path.join(
      portalAppDir,
      "src/features/site-support.js"
    );
    const demoResult = transformDemo(sampleCode, portalSampleId);
    expect(demoResult?.code).toContain("const a = (true);");
    expect(demoResult?.code).toContain("const b = (true);");
    expect(demoResult?.code).toContain("const c = (true);");
    expect(demoResult?.code).toContain("const e = isDemoModeActive(env);");

    const demoFixturesIndexId = path.resolve(
      webAppDir,
      "../../packages/demo-fixtures/src/index.js"
    );
    const indexSample = [
      'import { DEMO_MANIFEST } from "virtual:demo-fixtures-manifest";',
      "export const DEMO_STUDY = DEMO_MANIFEST.study;",
      "export const DEMO_PERSONAS = DEMO_MANIFEST.personas;",
    ].join("\n");
    const indexTransformed = transformDemo(indexSample, demoFixturesIndexId);
    expect(indexTransformed?.code).toContain("__DEMO_MANIFEST_STUDY");
    expect(indexTransformed?.code).toContain("__DEMO_MANIFEST_PERSONAS");
  });
});
