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
  { label: "services/api.js", pattern: /src\/services\/api\.js$/ },
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
    const result = await build({
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

    // Target success metric: <= 512.18 kB raw (<= 165.49 kB gzip); acceptance ceiling: <= 530 kB raw (<= 172 kB gzip).
    expect(rawBytes).toBeLessThanOrEqual(Math.floor(512.18 * 1024));
    expect(gzipBytes).toBeLessThanOrEqual(Math.floor(165.49 * 1024));

    for (const { label, pattern } of FORBIDDEN_BOOT_PATTERNS) {
      const offending = bootModuleIds.filter((id) => pattern.test(id));
      expect(
        offending,
        `Cold-boot chunks must contain 0 B of ${label}, found in: ${offending.join(", ")}`
      ).toEqual([]);
    }

    // Verify preserved vendor splits and isolated lazy draggable chunk.
    const allChunkNames = Array.from(chunksByFile.keys());
    expect(allChunkNames.some((f) => f.includes("vendor-vue"))).toBe(true);
    expect(allChunkNames.some((f) => f.includes("vendor-auth"))).toBe(true);
    expect(allChunkNames.some((f) => f.includes("vendor-media"))).toBe(true);

    const draggableChunks = Array.from(chunksByFile.values()).filter((chunk) =>
      Object.keys(chunk.modules).some((m) =>
        /(?:^|\/)(?:vuedraggable|sortablejs)(?:\/|$)/.test(
          m.replace(/\\/g, "/")
        )
      )
    );
    expect(draggableChunks).toHaveLength(1);
    expect(bootChunkFiles.has(draggableChunks[0].fileName)).toBe(false);

    const draggableModules = Object.keys(draggableChunks[0].modules).map((m) =>
      m.replace(/\\/g, "/")
    );
    expect(draggableModules.some((m) => /(?:^|\/)vuedraggable\//.test(m))).toBe(
      true
    );
    expect(draggableModules.some((m) => /(?:^|\/)sortablejs\//.test(m))).toBe(
      true
    );
  }, 60_000);
});
