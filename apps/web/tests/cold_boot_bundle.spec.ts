import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";
import { build, type Rollup } from "vite";
import { describe, expect, it } from "vitest";

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

describe("apps/web cold-boot bundle budget & chunk isolation", () => {
  it("keeps index.html initial JS within budget and excludes authoring, USDM/zod, and route-only API modules", async () => {
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

    const htmlAsset = bundleItems.find(
      (item): item is Rollup.OutputAsset =>
        item.type === "asset" && item.fileName === "index.html"
    );
    expect(htmlAsset).toBeDefined();

    const htmlSource =
      typeof htmlAsset!.source === "string"
        ? htmlAsset!.source
        : Buffer.from(htmlAsset!.source).toString("utf8");

    const chunksByFile = new Map<string, Rollup.OutputChunk>();
    for (const item of bundleItems) {
      if (item.type === "chunk") {
        chunksByFile.set(item.fileName, item);
      }
    }

    const initialJsFiles = extractInitialJsAssetsFromHtml(
      htmlSource,
      new Set(chunksByFile.keys())
    );
    expect(initialJsFiles.length).toBeGreaterThan(0);

    // Collect all initial chunks referenced in index.html plus any transitive static imports.
    const bootChunkFiles = new Set<string>(initialJsFiles);
    const queue = [...initialJsFiles];
    while (queue.length > 0) {
      const current = queue.shift()!;
      const chunk = chunksByFile.get(current);
      expect(chunk, `Expected output chunk for ${current}`).toBeDefined();
      for (const imported of chunk!.imports) {
        if (imported.endsWith(".js") && !bootChunkFiles.has(imported)) {
          bootChunkFiles.add(imported);
          queue.push(imported);
        }
      }
    }

    let rawBytes = 0;
    let gzipBytes = 0;
    const bootModuleIds: string[] = [];

    for (const fileName of bootChunkFiles) {
      const chunk = chunksByFile.get(fileName)!;
      const codeBuffer = Buffer.from(chunk.code, "utf8");
      rawBytes += codeBuffer.byteLength;
      gzipBytes += zlib.gzipSync(codeBuffer).byteLength;
      for (const moduleId of Object.keys(chunk.modules)) {
        bootModuleIds.push(moduleId.replace(/\\/g, "/"));
      }
    }

    // Cold-boot budget after deferring i18n, closed overlays, canvas/onboarding stores, and kernelApi barrel:
    // <= 250 kB raw JS and <= 90 kB gzip JS.
    expect(rawBytes).toBeLessThanOrEqual(Math.floor(250 * 1024));
    expect(gzipBytes).toBeLessThanOrEqual(Math.floor(90 * 1024));

    for (const { label, pattern } of FORBIDDEN_BOOT_PATTERNS) {
      const offending = bootModuleIds.filter((id) => pattern.test(id));
      expect(
        offending,
        `Cold-boot chunks must contain 0 B of ${label}, found in: ${offending.join(", ")}`
      ).toEqual([]);
    }

    // Verify preserved vendor splits, isolated lazy auth chunk, and complete exclusion of demo-only route subgraphs
    // (DocumentsHubView -> MediaPreviewModal -> hls.js and MdrView -> CrfAuthoringCanvas -> vuedraggable/sortablejs).
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

    const allProdModuleIds = Array.from(chunksByFile.values()).flatMap(
      (chunk) => Object.keys(chunk.modules).map((m) => m.replace(/\\/g, "/"))
    );
    expect(
      allProdModuleIds.some((m) =>
        /(?:^|\/)(?:vuedraggable|sortablejs|hls\.js)(?:\/|$)/.test(m)
      )
    ).toBe(false);
    expect(
      allProdModuleIds.filter((m) => /\/src\/views\/demo\//.test(m))
    ).toEqual([]);
    expect(
      allProdModuleIds.filter((m) => /\/src\/stores\/demo\//.test(m))
    ).toEqual([]);
    expect(
      allProdModuleIds.filter((m) =>
        /\/src\/views\/(?:MdrView|ProtocolDigitizationView|ICFBuilderView|ConsentAuthoringView|AmendmentDiffView|ImagingView|DeviceInvestigationView|CecAdjudicationView|AuditView|AuditorView|RulesView|MedicalCodingView|LakehouseView|SensorTelemetryView|LabGovernanceView|TelehealthDtpView|AggregateSafetyView|QualityCockpitView|NotificationsView|DocumentsHubView|ParticipantWalletView|LaySummaryPortal)\.vue/.test(
          m
        )
      )
    ).toEqual([]);

    // Verify live chunks omit demo authoring dialogs, localStorage simulation keys, and mock payloads.
    const allProdCode = Array.from(chunksByFile.values())
      .map((chunk) => chunk.code)
      .join("\n");
    expect(allProdCode).not.toContain("create-art-modal-title");
    expect(allProdCode).not.toContain("DEV-2026-0041");
    expect(allProdCode).not.toContain("demo_documents");
    expect(allProdCode).not.toContain("demo_notifications");
  }, 60_000);

  it("keeps vuedraggable/sortablejs isolated in a single lazy chunk outside cold boot in demo builds and strips portal mock-data.js in live portal builds", async () => {
    const previousNodeEnv = process.env.NODE_ENV;
    process.env.NODE_ENV = "production";
    let demoResult: Awaited<ReturnType<typeof build>>;
    let portalProdResult: Awaited<ReturnType<typeof build>>;
    const portalRoot = path.resolve(WEB_ROOT, "../subject-portal");
    const portalViteConfig = path.resolve(portalRoot, "vite.config.js");

    try {
      [demoResult, portalProdResult] = await Promise.all([
        build({
          root: WEB_ROOT,
          configFile: VITE_CONFIG_PATH,
          mode: "demo",
          logLevel: "silent",
          define: {
            "process.env.NODE_ENV": JSON.stringify("production"),
          },
          build: {
            write: false,
          },
        }),
        build({
          root: portalRoot,
          configFile: portalViteConfig,
          mode: "production",
          logLevel: "silent",
          define: {
            "process.env.NODE_ENV": JSON.stringify("production"),
          },
          build: {
            write: false,
          },
        }),
      ]);
    } finally {
      if (previousNodeEnv === undefined) {
        delete process.env.NODE_ENV;
      } else {
        process.env.NODE_ENV = previousNodeEnv;
      }
    }

    const demoBundleItems = (
      Array.isArray(demoResult) ? demoResult : [demoResult]
    ).flatMap((entry) => ("output" in entry ? entry.output : []));
    const demoHtmlAsset = demoBundleItems.find(
      (item): item is Rollup.OutputAsset =>
        item.type === "asset" && item.fileName === "index.html"
    )!;
    const demoHtmlSource =
      typeof demoHtmlAsset.source === "string"
        ? demoHtmlAsset.source
        : Buffer.from(demoHtmlAsset.source).toString("utf8");

    const demoChunksByFile = new Map<string, Rollup.OutputChunk>();
    for (const item of demoBundleItems) {
      if (item.type === "chunk") {
        demoChunksByFile.set(item.fileName, item);
      }
    }

    const demoBootFiles = new Set<string>(
      extractInitialJsAssetsFromHtml(
        demoHtmlSource,
        new Set(demoChunksByFile.keys())
      )
    );
    const demoQueue = [...demoBootFiles];
    while (demoQueue.length > 0) {
      const current = demoQueue.shift()!;
      const chunk = demoChunksByFile.get(current);
      for (const imported of chunk?.imports || []) {
        if (imported.endsWith(".js") && !demoBootFiles.has(imported)) {
          demoBootFiles.add(imported);
          demoQueue.push(imported);
        }
      }
    }

    const draggableChunks = Array.from(demoChunksByFile.values()).filter(
      (chunk) =>
        Object.keys(chunk.modules).some((m) =>
          /(?:^|\/)(?:vuedraggable|sortablejs)(?:\/|$)/.test(
            m.replace(/\\/g, "/")
          )
        )
    );
    expect(draggableChunks).toHaveLength(1);
    expect(demoBootFiles.has(draggableChunks[0].fileName)).toBe(false);

    const draggableModules = Object.keys(draggableChunks[0].modules).map((m) =>
      m.replace(/\\/g, "/")
    );
    expect(draggableModules.some((m) => /(?:^|\/)vuedraggable\//.test(m))).toBe(
      true
    );
    expect(draggableModules.some((m) => /(?:^|\/)sortablejs\//.test(m))).toBe(
      true
    );

    const portalBundleItems = (
      Array.isArray(portalProdResult) ? portalProdResult : [portalProdResult]
    ).flatMap((entry) => ("output" in entry ? entry.output : []));
    const portalChunks = portalBundleItems.filter(
      (item): item is Rollup.OutputChunk => item.type === "chunk"
    );
    const portalModuleIds = portalChunks.flatMap((chunk) =>
      Object.keys(chunk.modules).map((m) => m.replace(/\\/g, "/"))
    );
    expect(
      portalModuleIds.filter((m) => /\/src\/data\/mock-data\.js$/.test(m))
    ).toEqual([]);
    const portalCode = portalChunks.map((chunk) => chunk.code).join("\n");
    expect(portalCode).not.toContain("inst_daily_diary");
  }, 60_000);
});
