/**
 * Generates TypeScript types for the web API clients from the .NET services' and modules' OpenAPI documents in
 * docs/openapi/ (the real specs, not the Python-era ones in docs/openapi/legacy/). Output goes to
 * src/api/generated/<contract>.ts and is checked in; tests/api/generated_types.test.js fails when it no longer matches
 * the spec.
 *
 * Every `docs/openapi/*_openapi.json` contract except the aggregated `cdisc_openapi.json` is discovered automatically
 * (docs/web/API_MIGRATION.md).
 *
 * Usage: node scripts/generate-api-types.js [--check]
 *   --check  write nothing; exit 1 and name the stale files when any no longer matches its spec
 *            (the api-clients quality gate runs this, so a spec change without regenerated types fails pre-commit).
 */
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import openapiTS, { astToString } from "openapi-typescript";
import * as prettier from "prettier";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const WEB_DIR = path.resolve(__dirname, "..");
export const SPEC_DIR = path.resolve(WEB_DIR, "../../docs/openapi");
export const OUTPUT_DIR = path.resolve(WEB_DIR, "src/api/generated");

const SPEC_SUFFIX = "_openapi.json";
const AGGREGATE_SPEC = "cdisc_openapi.json";

export function discoverOpenApiServices(specDir = SPEC_DIR) {
  if (!fs.existsSync(specDir)) return [];
  return fs
    .readdirSync(specDir, { withFileTypes: true })
    .filter(
      (entry) =>
        entry.isFile() &&
        entry.name.endsWith(SPEC_SUFFIX) &&
        entry.name !== AGGREGATE_SPEC
    )
    .map((entry) => entry.name.slice(0, -SPEC_SUFFIX.length))
    .sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

const HEADER =
  "/**\n" +
  " * Generated from docs/openapi/{service}_openapi.json by scripts/generate-api-types.js.\n" +
  " * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.\n" +
  " */\n\n";

export async function renderServiceTypes(service, specDir = SPEC_DIR) {
  const specPath = path.join(specDir, `${service}${SPEC_SUFFIX}`);
  const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));
  const ast = await openapiTS(spec);
  const source = HEADER.replace("{service}", service) + astToString(ast);
  const config = (await prettier.resolveConfig(WEB_DIR)) ?? {};
  return prettier.format(source, { ...config, parser: "typescript" });
}

export function outputPath(service, outputDir = OUTPUT_DIR) {
  return path.join(outputDir, `${service}.ts`);
}

async function main(check) {
  fs.mkdirSync(OUTPUT_DIR, { recursive: true });
  const services = discoverOpenApiServices();
  const expectedNames = new Set(services.map((service) => `${service}.ts`));
  const stale = [];

  for (const service of services) {
    const file = outputPath(service);
    const rendered = await renderServiceTypes(service);
    if (check) {
      const current = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
      if (current !== rendered) stale.push(path.relative(WEB_DIR, file));
      continue;
    }
    fs.writeFileSync(file, rendered);
    console.log(`[+] ${path.relative(WEB_DIR, file)}`);
  }

  for (const entry of fs.readdirSync(OUTPUT_DIR, { withFileTypes: true })) {
    if (!entry.isFile() || !entry.name.endsWith(".ts")) continue;
    if (expectedNames.has(entry.name)) continue;
    const orphanPath = path.join(OUTPUT_DIR, entry.name);
    if (check) {
      stale.push(path.relative(WEB_DIR, orphanPath));
    } else {
      fs.unlinkSync(orphanPath);
      console.log(`[-] ${path.relative(WEB_DIR, orphanPath)}`);
    }
  }

  if (stale.length > 0) {
    console.error(
      `[ERROR] Out of date with docs/openapi: ${stale.join(", ")}. Run 'pnpm --filter web generate:api' and commit the result.`
    );
    process.exitCode = 1;
  }
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  await main(process.argv.includes("--check"));
}
