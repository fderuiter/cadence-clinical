import "fake-indexeddb/auto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";
import { build } from "vite";
import { describe, expect, it, vi } from "vitest";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PORTAL_ROOT = path.resolve(__dirname, "..");
const VITE_CONFIG_PATH = path.resolve(PORTAL_ROOT, "vite.config.js");

const FORBIDDEN_BOOT_PATTERNS = [
  { label: "src/data/mock-data.js", pattern: /src\/data\/mock-data\.js$/ },
  { label: "ClinicalDataTable.vue", pattern: /ClinicalDataTable\.vue/ },
  { label: "packages/usdm-schemas", pattern: /packages\/usdm-schemas\// },
  { label: "zod", pattern: /node_modules\/zod\// },
  { label: "keycloak-js", pattern: /(?:^|\/)keycloak-js(?:\/|$)/ },
  { label: "TasksView.vue", pattern: /src\/views\/TasksView\.vue/ },
  {
    label: "QuestionnaireView.vue",
    pattern: /src\/views\/QuestionnaireView\.vue/,
  },
  { label: "ComplianceView.vue", pattern: /src\/views\/ComplianceView\.vue/ },
  { label: "InboxView.vue", pattern: /src\/views\/InboxView\.vue/ },
  { label: "ConsentView.vue", pattern: /src\/views\/ConsentView\.vue/ },
  {
    label: "LaySummariesView.vue",
    pattern: /src\/views\/LaySummariesView\.vue/,
  },
  { label: "SyncDrawer.vue", pattern: /src\/components\/SyncDrawer\.vue/ },
  {
    label: "PortalPeekSheet.vue",
    pattern: /src\/components\/PortalPeekSheet\.vue/,
  },
  {
    label: "SignatureModal.vue",
    pattern: /src\/components\/SignatureModal\.vue/,
  },
  {
    label: "ReconsentModal.vue",
    pattern: /src\/components\/ReconsentModal\.vue/,
  },
  {
    label: "PinSetupModal.vue",
    pattern: /src\/components\/PinSetupModal\.vue/,
  },
  {
    label: "PinUnlockModal.vue",
    pattern: /src\/components\/PinUnlockModal\.vue/,
  },
];

function extractInitialJsAssetsFromHtml(html, knownChunkNames) {
  const assetRefs = new Set();
  const scriptRe = /<script\b[^>]*\bsrc=["']([^"']+\.js)["'][^>]*>/gi;
  const preloadRe =
    /<link\b[^>]*\brel=["']modulepreload["'][^>]*\bhref=["']([^"']+\.js)["'][^>]*>/gi;
  const preloadHrefFirstRe =
    /<link\b[^>]*\bhref=["']([^"']+\.js)["'][^>]*\brel=["']modulepreload["'][^>]*>/gi;

  for (const re of [scriptRe, preloadRe, preloadHrefFirstRe]) {
    let match;
    while ((match = re.exec(html)) !== null) {
      const rawRef = match[1].replace(/^\.?\//, "");
      if (knownChunkNames.has(rawRef)) {
        assetRefs.add(rawRef);
        continue;
      }
      const matchedName = Array.from(knownChunkNames).find((fileName) =>
        rawRef.endsWith(`/${fileName}`)
      );
      assetRefs.add(matchedName || rawRef);
    }
  }
  return Array.from(assetRefs);
}

async function runPortalBuild(mode) {
  const tempOutDir = fs.mkdtempSync(
    path.join(os.tmpdir(), `subject-portal-${mode}-bundle-`)
  );
  const warnings = [];
  const previousNodeEnv = process.env.NODE_ENV;
  const previousDemoMode = process.env.VITE_DEMO_MODE;
  process.env.NODE_ENV = "production";
  if (mode === "demo") {
    process.env.VITE_DEMO_MODE = "true";
  } else {
    delete process.env.VITE_DEMO_MODE;
  }

  try {
    const result = await build({
      root: PORTAL_ROOT,
      configFile: VITE_CONFIG_PATH,
      mode,
      logLevel: "silent",
      define: {
        "process.env.NODE_ENV": JSON.stringify("production"),
      },
      build: {
        outDir: tempOutDir,
        emptyOutDir: true,
        rollupOptions: {
          onwarn(warning, defaultHandler) {
            warnings.push(warning);
            if (warning.code !== "EMPTY_BUNDLE") {
              defaultHandler(warning);
            }
          },
        },
      },
    });

    const outputs = Array.isArray(result) ? result : [result];
    const bundleItems = outputs.flatMap((entry) =>
      "output" in entry ? entry.output : []
    );
    const swPath = path.join(tempOutDir, "sw.js");
    const swSource = fs.existsSync(swPath)
      ? fs.readFileSync(swPath, "utf8")
      : "";
    const assetsDir = path.join(tempOutDir, "assets");
    const totalAssetsBytes = fs.existsSync(assetsDir)
      ? fs
          .readdirSync(assetsDir)
          .reduce(
            (sum, name) => sum + fs.statSync(path.join(assetsDir, name)).size,
            0
          )
      : 0;

    return {
      bundleItems,
      warnings,
      swSource,
      totalAssetsBytes,
    };
  } finally {
    fs.rmSync(tempOutDir, { recursive: true, force: true });
    if (previousNodeEnv === undefined) {
      delete process.env.NODE_ENV;
    } else {
      process.env.NODE_ENV = previousNodeEnv;
    }
    if (previousDemoMode === undefined) {
      delete process.env.VITE_DEMO_MODE;
    } else {
      process.env.VITE_DEMO_MODE = previousDemoMode;
    }
  }
}

describe("apps/subject-portal cold-boot bundle budget, chunk isolation, and signed-out DOM", () => {
  it("keeps production index.html initial JS <= 250 KiB raw / <= 90 KiB gzip with zero forbidden modules, zero INEFFECTIVE_DYNAMIC_IMPORT warnings, and scoped SW precache", async () => {
    const { bundleItems, warnings, swSource, totalAssetsBytes } =
      await runPortalBuild("production");

    const ineffectiveWarnings = warnings.filter(
      (w) =>
        w.code === "INEFFECTIVE_DYNAMIC_IMPORT" ||
        String(w.message || "").includes("dynamically imported by")
    );
    expect(ineffectiveWarnings).toEqual([]);

    const htmlAsset = bundleItems.find(
      (item) => item.type === "asset" && item.fileName === "index.html"
    );
    expect(htmlAsset).toBeDefined();

    const htmlSource =
      typeof htmlAsset.source === "string"
        ? htmlAsset.source
        : Buffer.from(htmlAsset.source).toString("utf8");

    const cspMetaMatches = htmlSource.match(
      /<meta\b[^>]*http-equiv=["']Content-Security-Policy["'][^>]*>/gi
    );
    expect(cspMetaMatches).toHaveLength(1);

    const chunksByFile = new Map();
    const allModuleIds = [];
    for (const item of bundleItems) {
      if (item.type === "chunk") {
        chunksByFile.set(item.fileName, item);
        for (const modId of Object.keys(item.modules)) {
          allModuleIds.push(modId.replace(/\\/g, "/"));
        }
      }
    }

    expect(
      allModuleIds.filter((id) => /(?:^|\/)axe-core(?:\/|$)/.test(id))
    ).toEqual([]);

    const initialJsFiles = extractInitialJsAssetsFromHtml(
      htmlSource,
      new Set(chunksByFile.keys())
    );
    expect(initialJsFiles.length).toBeGreaterThan(0);

    const bootChunkFiles = new Set(initialJsFiles);
    const queue = [...initialJsFiles];
    while (queue.length > 0) {
      const current = queue.shift();
      const chunk = chunksByFile.get(current);
      expect(chunk, `Expected output chunk for ${current}`).toBeDefined();
      for (const imported of chunk.imports) {
        if (imported.endsWith(".js") && !bootChunkFiles.has(imported)) {
          bootChunkFiles.add(imported);
          queue.push(imported);
        }
      }
    }

    let rawBytes = 0;
    let gzipBytes = 0;
    const bootModuleIds = [];

    for (const fileName of bootChunkFiles) {
      const chunk = chunksByFile.get(fileName);
      const codeBuffer = Buffer.from(chunk.code, "utf8");
      rawBytes += codeBuffer.byteLength;
      gzipBytes += zlib.gzipSync(codeBuffer).byteLength;
      for (const moduleId of Object.keys(chunk.modules)) {
        bootModuleIds.push(moduleId.replace(/\\/g, "/"));
      }
    }

    // Matching apps/web/tests/cold_boot_bundle.spec.ts:L186-L197:
    // <= 250 KiB (256,000 B) raw JS and <= 90 KiB (92,160 B) gzip JS.
    expect(rawBytes).toBeLessThanOrEqual(250 * 1024);
    expect(gzipBytes).toBeLessThanOrEqual(90 * 1024);

    for (const { label, pattern } of FORBIDDEN_BOOT_PATTERNS) {
      const offending = bootModuleIds.filter((id) => pattern.test(id));
      expect(
        offending,
        `Cold-boot chunks must contain 0 B of ${label}, found in: ${offending.join(", ")}`
      ).toEqual([]);
    }

    // Staged /subject-portal/assets/ stays within PublicBundleValidatorService.cs budget (942,080 B).
    expect(totalAssetsBytes).toBeGreaterThan(0);
    expect(totalAssetsBytes).toBeLessThanOrEqual(942_080);

    // Built sw.js precache omits lazy OIDC keycloak chunks while keeping offline shell assets.
    const injectedMatch = swSource.match(
      /const INJECTED_BUNDLE_ASSETS = \[([\s\S]*?)\];/
    );
    expect(injectedMatch).not.toBeNull();
    expect(injectedMatch[1]).not.toMatch(/keycloak/i);
    expect(injectedMatch[1]).toMatch(/assets\/index-/);
  }, 60_000);

  it("emits zero keycloak-js chunks, zero axe-core chunks, and zero INEFFECTIVE_DYNAMIC_IMPORT warnings in demo builds (ADR-2537)", async () => {
    const { bundleItems, warnings } = await runPortalBuild("demo");

    const ineffectiveWarnings = warnings.filter(
      (w) =>
        w.code === "INEFFECTIVE_DYNAMIC_IMPORT" ||
        String(w.message || "").includes("dynamically imported by")
    );
    expect(ineffectiveWarnings).toEqual([]);

    const demoChunkFiles = [];
    const demoModuleIds = [];
    for (const item of bundleItems) {
      if (item.type === "chunk") {
        demoChunkFiles.push(item.fileName);
        for (const modId of Object.keys(item.modules)) {
          demoModuleIds.push(modId.replace(/\\/g, "/"));
        }
      }
    }

    expect(demoChunkFiles.filter((f) => /keycloak/i.test(f))).toEqual([]);
    expect(
      demoModuleIds.filter((id) => /(?:^|\/)keycloak-js(?:\/|$)/.test(id))
    ).toEqual([]);
    expect(
      demoModuleIds.filter((id) => /(?:^|\/)axe-core(?:\/|$)/.test(id))
    ).toEqual([]);
  }, 60_000);

  it("mounts only #view-unavailable with zero hidden .portal-view sections or closed overlays on signed-out /unavailable navigation", async () => {
    const {
      initializeApp,
      resetPortalRouterForTests,
      state,
      unmountApp,
      clearAllSubmissions,
    } = await import("../src/index.js");

    window.__MOCK_TEST_ENV__ = true;
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([]),
    });

    unmountApp();
    resetPortalRouterForTests();
    await clearAllSubmissions();
    document.body.innerHTML = '<div id="app"></div>';
    window.history.replaceState({}, "", "/subject-portal/unavailable");

    state.session.userId = null;
    state.session.token = null;
    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.signatureModalPrimed = false;
    state.reconsentModalOpen = false;
    state.pinSetup.isOpen = false;
    state.pinUnlock.isOpen = false;
    state.peekRecord = null;
    state.mountedViewIds = [];
    state.currentView = "view-unavailable";

    try {
      await initializeApp();
      await new Promise((resolve) => setTimeout(resolve, 25));

      const unavailableEl = document.getElementById("view-unavailable");
      expect(unavailableEl).not.toBeNull();
      expect(unavailableEl.hasAttribute("hidden")).toBe(false);

      const portalViews = Array.from(document.querySelectorAll(".portal-view"));
      expect(portalViews.map((el) => el.id)).toEqual(["view-unavailable"]);

      for (const closedOverlaySelector of [
        "#global-sync-drawer",
        ".global-drawer-overlay",
        "#portal-peek-drawer",
        "#portal-sign-modal",
        "#portal-reconsent-modal",
        "#portal-pin-setup-modal",
        "#portal-pin-unlock-modal",
      ]) {
        expect(
          document.querySelector(closedOverlaySelector),
          `Expected closed overlay ${closedOverlaySelector} to remain unmounted`
        ).toBeNull();
      }
    } finally {
      unmountApp();
      resetPortalRouterForTests();
      window.history.replaceState({}, "", "/");
    }
  });
});
