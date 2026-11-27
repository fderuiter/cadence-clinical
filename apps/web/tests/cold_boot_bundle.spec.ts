import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";
import { build, type Rollup } from "vite";
import { beforeAll, describe, expect, it } from "vitest";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const WEB_ROOT = path.resolve(__dirname, "..");
const VITE_CONFIG_PATH = path.resolve(WEB_ROOT, "vite.config.js");

const FORBIDDEN_BOOT_PATTERNS: ReadonlyArray<{
  label: string;
  pattern: RegExp;
}> = [
  { label: "vuedraggable", pattern: /(?:^|\/)vuedraggable(?:\/|$)/ },
  { label: "sortablejs", pattern: /(?:^|\/)sortablejs(?:\/|$)/ },
  {
    label: "packages/usdm-schemas",
    pattern: /packages\/usdm-schemas\//,
  },
  { label: "zod", pattern: /node_modules\/zod\// },
  { label: "stores/clinical.ts", pattern: /src\/stores\/clinical\.ts$/ },
  { label: "api/ctmsApi.ts", pattern: /src\/api\/ctmsApi\.ts$/ },
  { label: "api/etmf.ts", pattern: /src\/api\/etmf\.ts$/ },
  { label: "api/training.ts", pattern: /src\/api\/training\.ts$/ },
  { label: "api/soaClient.ts", pattern: /src\/api\/soaClient\.ts$/ },
  {
    label: "api/ingestionClient.ts",
    pattern: /src\/api\/ingestionClient\.ts$/,
  },
  { label: "api/designer.ts", pattern: /src\/api\/designer\.ts$/ },
  { label: "api/auditor.ts", pattern: /src\/api\/auditor\.ts$/ },
  { label: "api/econsent.ts", pattern: /src\/api\/econsent\.ts$/ },
  { label: "api/fileshare.ts", pattern: /src\/api\/fileshare\.ts$/ },
  {
    label: "api/terminologyClient.ts",
    pattern: /src\/api\/terminologyClient\.ts$/,
  },
  {
    label: "api/observationValue.ts",
    pattern: /src\/api\/observationValue\.ts$/,
  },
  { label: "api/ecrfFormsApi.ts", pattern: /src\/api\/ecrfFormsApi\.ts$/ },
  {
    label: "api/ecrfQueriesApi.ts",
    pattern: /src\/api\/ecrfQueriesApi\.ts$/,
  },
  { label: "api/interop.ts", pattern: /src\/api\/interop\.ts$/ },
  {
    label: "api/statisticalComputing.ts",
    pattern: /src\/api\/statisticalComputing\.ts$/,
  },
  {
    label: "api/kernelGovernanceApi.ts",
    pattern: /src\/api\/kernelGovernanceApi\.ts$/,
  },
  {
    label: "canvas/ecrfRedirect.ts",
    pattern: /src\/canvas\/ecrfRedirect\.ts$/,
  },
  { label: "api/apiClient.js", pattern: /src\/api\/apiClient\.js$/ },
  { label: "vue-i18n", pattern: /(?:^|\/)vue-i18n(?:\/|$)/ },
  { label: "@intlify", pattern: /(?:^|\/)@intlify(?:\/|$)/ },
  { label: "src/locales/", pattern: /src\/locales\// },
  { label: "ContextualHelpDrawer", pattern: /ContextualHelpDrawer/ },
  { label: "TicketCreateModal", pattern: /TicketCreateModal/ },
  { label: "CanvasPalette", pattern: /CanvasPalette/ },
  { label: "studyCanvas", pattern: /studyCanvas/ },
  { label: "kernelApi", pattern: /src\/api\/kernelApi\.ts$/ },
  { label: "keycloak-js", pattern: /(?:^|\/)keycloak-js(?:\/|$)/ },
];

const STUDY_HOME_EAGER_COMPONENTS: ReadonlyArray<string> = [
  "src/components/canvas/StudyPulse.vue",
  "src/components/canvas/StudySitesPanel.vue",
  "src/components/ai/NeedsAttention.vue",
  "src/components/canvas/WorkQueuesPanel.vue",
  "src/components/canvas/StudyActivity.vue",
  "src/components/canvas/CanvasInspector.vue",
];

const STUDY_CANVAS_DEFERRED_BRANCHES_AND_OVERLAYS: ReadonlyArray<string> = [
  "src/components/ai/ProposalReview.vue",
  "src/views/ObservationView.vue",
  "src/components/canvas/CanvasFormWorkspace.vue",
  "src/components/canvas/ParticipantRecord.vue",
  "src/components/canvas/ParticipantsSection.vue",
  "src/components/canvas/SitesSection.vue",
  "src/components/canvas/ProtocolVersionsSection.vue",
  "src/components/clinical/SiteActivationPanel.vue",
  "src/components/clinical/CtmsGreenlightSection.vue",
  "src/components/clinical/CtmsMilestonesSection.vue",
  "src/components/clinical/CtmsDelegationSection.vue",
  "src/components/clinical/CtmsMonitoringSection.vue",
  "src/components/clinical/RtsmSupplySection.vue",
  "src/views/DocumentManagerView.vue",
  "src/views/StudyTrainingView.vue",
  "src/components/canvas/QueriesSection.vue",
  "src/components/canvas/LocksSection.vue",
  "src/components/canvas/ValidationSection.vue",
  "src/components/canvas/ExportsSection.vue",
  "src/components/canvas/CanvasPalette.vue",
  "SignOutConfirmModal.vue",
  "SignatureCaptureModal.vue",
];

const CTMS_TAB_PANEL_MODULES: ReadonlyArray<string> = [
  "src/components/clinical/SiteActivationPanel.vue",
  "src/components/clinical/CtmsGreenlightSection.vue",
  "src/components/clinical/CtmsMilestonesSection.vue",
  "src/components/clinical/CtmsMonitoringSection.vue",
  "src/components/clinical/CtmsDelegationSection.vue",
  "src/components/clinical/RtsmSupplySection.vue",
];

const DOCUMENT_MANAGER_DEFERRED_OVERLAYS: ReadonlyArray<string> = [
  "src/components/etmf/CraQcQueue.vue",
  "src/components/etmf/PdfPreviewModal.vue",
  "src/components/etmf/ExportJobDrawer.vue",
  "src/components/etmf/BatchUploadDrawer.vue",
];

interface BundleSnapshot {
  htmlSource: string;
  chunksByFile: Map<string, Rollup.OutputChunk>;
  assetsByFile: Map<string, Rollup.OutputAsset>;
  bootChunkFiles: Set<string>;
  bootCssFiles: Set<string>;
}

function getChunkImportedCss(
  chunk: Rollup.OutputChunk
): ReadonlySet<string> | undefined {
  return (
    chunk as Rollup.OutputChunk & {
      viteMetadata?: { importedCss?: ReadonlySet<string> };
    }
  ).viteMetadata?.importedCss;
}

function extractInitialJsAssetsFromHtml(
  html: string,
  knownChunkNames: ReadonlySet<string>
): string[] {
  const assetRefs = new Set<string>();
  const scriptRe = /<script\b[^>]*\bsrc=["']([^"']+\.js)["'][^>]*>/gi;
  const preloadRe =
    /<link\b[^>]*\brel=["']modulepreload["'][^>]*\bhref=["']([^"']+\.js)["'][^>]*>/gi;
  const preloadHrefFirstRe =
    /<link\b[^>]*\bhref=["']([^"']+\.js)["'][^>]*\brel=["']modulepreload["'][^>]*>/gi;

  for (const re of [scriptRe, preloadRe, preloadHrefFirstRe]) {
    let match: RegExpExecArray | null;
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

function collectStaticClosure(
  seedFiles: ReadonlyArray<string>,
  chunksByFile: ReadonlyMap<string, Rollup.OutputChunk>
): { jsFiles: Set<string>; cssFiles: Set<string>; moduleIds: string[] } {
  const jsFiles = new Set<string>(seedFiles);
  const queue = [...seedFiles];
  while (queue.length > 0) {
    const current = queue.shift()!;
    const chunk = chunksByFile.get(current);
    expect(chunk, `Expected output chunk for ${current}`).toBeDefined();
    for (const imported of chunk!.imports) {
      if (imported.endsWith(".js") && !jsFiles.has(imported)) {
        jsFiles.add(imported);
        queue.push(imported);
      }
    }
  }

  const cssFiles = new Set<string>();
  const moduleIds: string[] = [];
  for (const fileName of jsFiles) {
    const chunk = chunksByFile.get(fileName)!;
    const importedCss = getChunkImportedCss(chunk);
    if (importedCss) {
      for (const cssFile of importedCss) {
        cssFiles.add(cssFile);
      }
    }
    for (const moduleId of Object.keys(chunk.modules)) {
      moduleIds.push(moduleId.replace(/\\/g, "/"));
    }
  }
  return { jsFiles, cssFiles, moduleIds };
}

function measureJsBytes(
  fileNames: Iterable<string>,
  chunksByFile: ReadonlyMap<string, Rollup.OutputChunk>
): { rawBytes: number; gzipBytes: number } {
  let rawBytes = 0;
  let gzipBytes = 0;
  for (const fileName of fileNames) {
    const chunk = chunksByFile.get(fileName)!;
    const codeBuffer = Buffer.from(chunk.code, "utf8");
    rawBytes += codeBuffer.byteLength;
    gzipBytes += zlib.gzipSync(codeBuffer).byteLength;
  }
  return { rawBytes, gzipBytes };
}

function measureCssBytes(
  fileNames: Iterable<string>,
  assetsByFile: ReadonlyMap<string, Rollup.OutputAsset>
): number {
  let rawBytes = 0;
  for (const fileName of fileNames) {
    const asset = assetsByFile.get(fileName);
    expect(asset, `Expected CSS output asset for ${fileName}`).toBeDefined();
    const buf =
      typeof asset!.source === "string"
        ? Buffer.from(asset!.source, "utf8")
        : Buffer.from(asset!.source);
    rawBytes += buf.byteLength;
  }
  return rawBytes;
}

function findEntryChunkBySuffix(
  suffix: string,
  chunksByFile: ReadonlyMap<string, Rollup.OutputChunk>
): string {
  for (const [fileName, chunk] of chunksByFile.entries()) {
    if (
      chunk.facadeModuleId &&
      chunk.facadeModuleId.replace(/\\/g, "/").endsWith(suffix)
    ) {
      return fileName;
    }
  }
  for (const [fileName, chunk] of chunksByFile.entries()) {
    const hasModule = Object.keys(chunk.modules).some((modId) =>
      modId.replace(/\\/g, "/").split("?", 1)[0].endsWith(suffix)
    );
    if (hasModule) return fileName;
  }
  throw new Error(`Unable to locate output chunk for ${suffix}`);
}

function assertZeroMatchingModules(
  closureLabel: string,
  moduleIds: ReadonlyArray<string>,
  forbiddenSuffixes: ReadonlyArray<string>
): void {
  for (const suffix of forbiddenSuffixes) {
    const offending = moduleIds.filter((id) =>
      id.split("?", 1)[0].endsWith(suffix)
    );
    expect(
      offending,
      `${closureLabel} static closure must contain 0 B of ${suffix}, found in: ${offending.join(", ")}`
    ).toEqual([]);
  }
}

describe("apps/web cold-boot bundle budget & chunk isolation", () => {
  let snapshot: BundleSnapshot;

  beforeAll(async () => {
    const previousNodeEnv = process.env.NODE_ENV;
    process.env.NODE_ENV = "production";
    let result: Awaited<ReturnType<typeof build>>;
    try {
      result = await build({
        root: WEB_ROOT,
        configFile: VITE_CONFIG_PATH,
        mode: "production",
        logLevel: "silent",
        define: {
          "process.env.NODE_ENV": JSON.stringify("production"),
        },
        build: {
          write: false,
        },
      });
    } finally {
      if (previousNodeEnv === undefined) {
        delete process.env.NODE_ENV;
      } else {
        process.env.NODE_ENV = previousNodeEnv;
      }
    }

    const outputs = Array.isArray(result) ? result : [result];
    const bundleItems = outputs.flatMap((entry) =>
      "output" in entry ? entry.output : []
    );

    const chunksByFile = new Map<string, Rollup.OutputChunk>();
    const assetsByFile = new Map<string, Rollup.OutputAsset>();
    for (const item of bundleItems) {
      if (item.type === "chunk") {
        chunksByFile.set(item.fileName, item);
      } else {
        assetsByFile.set(item.fileName, item);
      }
    }

    const htmlAsset = assetsByFile.get("index.html");
    expect(htmlAsset).toBeDefined();
    const htmlSource =
      typeof htmlAsset!.source === "string"
        ? htmlAsset!.source
        : Buffer.from(htmlAsset!.source).toString("utf8");

    const initialJsFiles = extractInitialJsAssetsFromHtml(
      htmlSource,
      new Set(chunksByFile.keys())
    );
    expect(initialJsFiles.length).toBeGreaterThan(0);

    const bootClosure = collectStaticClosure(initialJsFiles, chunksByFile);
    snapshot = {
      htmlSource,
      chunksByFile,
      assetsByFile,
      bootChunkFiles: bootClosure.jsFiles,
      bootCssFiles: bootClosure.cssFiles,
    };
  }, 60_000);

  it("keeps index.html initial JS within budget and excludes authoring, USDM/zod, and route-only API modules", () => {
    const { chunksByFile, bootChunkFiles } = snapshot;
    const bootClosure = collectStaticClosure(
      Array.from(bootChunkFiles),
      chunksByFile
    );
    const { rawBytes, gzipBytes } = measureJsBytes(
      bootClosure.jsFiles,
      chunksByFile
    );

    // Cold-boot budget after deferring i18n, closed overlays, canvas/onboarding stores, and kernelApi barrel:
    // <= 250 kB raw JS and <= 90 kB gzip JS.
    expect(rawBytes).toBeLessThanOrEqual(Math.floor(250 * 1024));
    expect(gzipBytes).toBeLessThanOrEqual(Math.floor(90 * 1024));

    for (const { label, pattern } of FORBIDDEN_BOOT_PATTERNS) {
      const offending = bootClosure.moduleIds.filter((id) => pattern.test(id));
      expect(
        offending,
        `Cold-boot chunks must contain 0 B of ${label}, found in: ${offending.join(", ")}`
      ).toEqual([]);
    }

    // Verify preserved vendor splits, lazy auth chunk, and zero unreachable third-party vendor chunks in non-demo production builds.
    const allChunkNames = Array.from(chunksByFile.keys());
    expect(allChunkNames.some((f) => f.includes("vendor-vue"))).toBe(true);
    const vendorAuthChunks = allChunkNames.filter((f) =>
      f.includes("vendor-auth")
    );
    expect(vendorAuthChunks.length).toBeGreaterThan(0);
    for (const vendorAuthChunk of vendorAuthChunks) {
      expect(bootChunkFiles.has(vendorAuthChunk)).toBe(false);
    }
    expect(allChunkNames.some((f) => f.includes("vendor-media"))).toBe(false);
    expect(allChunkNames.some((f) => f.includes("vendor-vuedraggable"))).toBe(
      false
    );

    const allBundleModuleIds = Array.from(chunksByFile.values()).flatMap(
      (chunk) => Object.keys(chunk.modules).map((m) => m.replace(/\\/g, "/"))
    );
    const forbiddenProdBundlePatterns: ReadonlyArray<{
      label: string;
      pattern: RegExp;
    }> = [
      { label: "hls.js", pattern: /(?:^|\/)hls\.js(?:\/|$)/ },
      { label: "zod", pattern: /node_modules\/zod\// },
      {
        label: "packages/usdm-schemas",
        pattern: /packages\/usdm-schemas\//,
      },
      { label: "sortablejs", pattern: /(?:^|\/)sortablejs(?:\/|$)/ },
      { label: "vuedraggable", pattern: /(?:^|\/)vuedraggable(?:\/|$)/ },
      { label: "vue-i18n", pattern: /(?:^|\/)vue-i18n(?:\/|$)/ },
      { label: "@intlify", pattern: /(?:^|\/)@intlify(?:\/|$)/ },
      { label: "src/locales/", pattern: /src\/locales\// },
    ];
    for (const { label, pattern } of forbiddenProdBundlePatterns) {
      const offending = allBundleModuleIds.filter((id) => pattern.test(id));
      expect(
        offending,
        `Non-demo production bundle must contain 0 B of ${label}, found in: ${offending.join(", ")}`
      ).toEqual([]);
    }
  });

  it("enforces static closure budgets and async branch/overlay splits for StudyCanvasView, CtmsView, and DocumentManagerView", () => {
    const { chunksByFile, assetsByFile, bootChunkFiles, bootCssFiles } =
      snapshot;

    const incrementalMetrics = (entrySuffixes: ReadonlyArray<string>) => {
      const entryFiles = entrySuffixes.map((suffix) =>
        findEntryChunkBySuffix(suffix, chunksByFile)
      );
      const closure = collectStaticClosure(entryFiles, chunksByFile);
      const incJsFiles = Array.from(closure.jsFiles).filter(
        (f) => !bootChunkFiles.has(f)
      );
      const incCssFiles = Array.from(closure.cssFiles).filter(
        (f) => !bootCssFiles.has(f)
      );
      const { rawBytes: incJsBytes } = measureJsBytes(incJsFiles, chunksByFile);
      const incCssBytes = measureCssBytes(incCssFiles, assetsByFile);
      return { closure, incJsBytes, incCssBytes };
    };

    // 1. Study Home (StudyCanvasView): <= 315 kB incremental JS, <= 45 kB incremental CSS,
    //    6 Study Home/layout components eager, and 0 B of the 19 inactive branches or standalone overlays.
    const studyHome = incrementalMetrics(["src/views/StudyCanvasView.vue"]);
    expect(studyHome.incJsBytes).toBeLessThanOrEqual(315_000);
    expect(studyHome.incCssBytes).toBeLessThanOrEqual(45_000);

    for (const eagerSuffix of STUDY_HOME_EAGER_COMPONENTS) {
      expect(
        studyHome.closure.moduleIds.some((id) =>
          id.split("?", 1)[0].endsWith(eagerSuffix)
        ),
        `StudyCanvasView static closure should retain eager Study Home component ${eagerSuffix}`
      ).toBe(true);
    }
    assertZeroMatchingModules(
      "StudyCanvasView",
      studyHome.closure.moduleIds,
      STUDY_CANVAS_DEFERRED_BRANCHES_AND_OVERLAYS
    );

    // 2. CTMS & Site Startup (CtmsView / CtmsLiveWorkspace):
    //    CtmsView static closure contains 0 B of the 6 lazy tab panels, and landing on /ctms
    //    (SiteActivationPanel) or /ssu (CtmsGreenlightSection) stays <= 195 kB incremental JS.
    const ctmsShell = incrementalMetrics(["src/views/CtmsView.vue"]);
    expect(ctmsShell.incJsBytes).toBeLessThanOrEqual(195_000);
    assertZeroMatchingModules(
      "CtmsView",
      ctmsShell.closure.moduleIds,
      CTMS_TAB_PANEL_MODULES
    );

    const ctmsEntryWithActivation = incrementalMetrics([
      "src/views/CtmsView.vue",
      "src/components/clinical/SiteActivationPanel.vue",
    ]);
    expect(ctmsEntryWithActivation.incJsBytes).toBeLessThanOrEqual(195_000);
    assertZeroMatchingModules(
      "/ctms initial entry",
      ctmsEntryWithActivation.closure.moduleIds,
      CTMS_TAB_PANEL_MODULES.filter(
        (m) => !m.endsWith("SiteActivationPanel.vue")
      )
    );

    const ssuEntryWithGreenlight = incrementalMetrics([
      "src/views/CtmsView.vue",
      "src/components/clinical/CtmsGreenlightSection.vue",
    ]);
    expect(ssuEntryWithGreenlight.incJsBytes).toBeLessThanOrEqual(195_000);
    assertZeroMatchingModules(
      "/ssu initial entry",
      ssuEntryWithGreenlight.closure.moduleIds,
      CTMS_TAB_PANEL_MODULES.filter(
        (m) => !m.endsWith("CtmsGreenlightSection.vue")
      )
    );

    // 3. Embedded eTMF (DocumentManagerView) & RTSM Supply (RtsmSupplySection):
    //    DocumentManagerView stays <= 246 kB incremental JS and excludes closed drawers/modals/QC queue;
    //    RtsmSupplySection excludes closed reconciliation drawer and signature modal.
    const etmfHub = incrementalMetrics(["src/views/DocumentManagerView.vue"]);
    expect(etmfHub.incJsBytes).toBeLessThanOrEqual(246_000);
    assertZeroMatchingModules(
      "DocumentManagerView",
      etmfHub.closure.moduleIds,
      DOCUMENT_MANAGER_DEFERRED_OVERLAYS
    );

    const rtsmSupply = incrementalMetrics([
      "src/components/clinical/RtsmSupplySection.vue",
    ]);
    assertZeroMatchingModules(
      "RtsmSupplySection",
      rtsmSupply.closure.moduleIds,
      [
        "src/components/clinical/BulkIpKitReconciliationDrawer.vue",
        "SignatureCaptureModal.vue",
      ]
    );
  });
});
