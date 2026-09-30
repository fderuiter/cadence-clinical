/**
 * Generates TypeScript types for the web API clients from the .NET services' OpenAPI documents in docs/openapi/
 * (the real specs, not the Python-era ones in docs/openapi/legacy/). Output goes to src/api/generated/<service>.ts
 * and is checked in; tests/api/generated_types.test.js fails when it no longer matches the spec.
 *
 * A service is listed here when its first screen moves onto the real API (docs/web/API_MIGRATION.md).
 *
 * Usage: node scripts/generate-api-types.js
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

export const GENERATED_SERVICES = ["execution"];

const HEADER =
  "/**\n" +
  " * Generated from docs/openapi/{service}_openapi.json by scripts/generate-api-types.js.\n" +
  " * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.\n" +
  " */\n\n";

export async function renderServiceTypes(service) {
  const specPath = path.join(SPEC_DIR, `${service}_openapi.json`);
  const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));
  const ast = await openapiTS(spec);
  const source = HEADER.replace("{service}", service) + astToString(ast);
  const config = (await prettier.resolveConfig(WEB_DIR)) ?? {};
  return prettier.format(source, { ...config, parser: "typescript" });
}

export function outputPath(service) {
  return path.join(OUTPUT_DIR, `${service}.ts`);
}

async function main() {
  fs.mkdirSync(OUTPUT_DIR, { recursive: true });
  for (const service of GENERATED_SERVICES) {
    fs.writeFileSync(outputPath(service), await renderServiceTypes(service));
    console.log(`[+] ${path.relative(WEB_DIR, outputPath(service))}`);
  }
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  await main();
}
