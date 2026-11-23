import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { createDemoFixturesPlugin } from "../../../scripts/vite-strip-demo-fixtures.mjs";

const webAppDir = path.resolve(process.cwd());
const viewsImporter = path.join(webAppDir, "src/views/CtmsView.vue");
const storesImporter = path.join(webAppDir, "src/stores/clinical.ts");

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

    const demoBuildResolve = resolveIdFor("build", "demo");
    expect(
      demoBuildResolve("./demo/CtmsDemoWorkspace.vue", viewsImporter)
    ).toBeNull();
    expect(demoBuildResolve("./demo/clinicalEcrf", storesImporter)).toBeNull();
    expect(demoBuildResolve("demo-fixtures")).toContain(
      "packages/demo-fixtures/src/index.js"
    );

    const devServeResolve = resolveIdFor("serve", "development");
    expect(
      devServeResolve("./demo/EcrfDemoWorkspace.vue", viewsImporter)
    ).toBeNull();
    expect(devServeResolve("./demo/clinicalUsdm", storesImporter)).toBeNull();
  });
});
