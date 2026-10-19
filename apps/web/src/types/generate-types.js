import fs from "fs";
import path from "path";
import crypto from "crypto";
import { fileURLToPath } from "url";
import * as prettier from "prettier";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const OPENAPI_DIR = path.resolve(__dirname, "../../../../docs/openapi/legacy");
const OUTPUT_PATH = path.resolve(__dirname, "usdm.ts");
const HASH_PATH = path.resolve(__dirname, ".usdm-hash.json");

function calculateHash() {
  const hash = crypto.createHash("sha256");

  if (fs.existsSync(__filename)) {
    hash.update(fs.readFileSync(__filename));
  }

  if (fs.existsSync(OPENAPI_DIR)) {
    const files = fs
      .readdirSync(OPENAPI_DIR)
      .filter((f) => f.endsWith(".json"))
      .sort();

    for (const file of files) {
      const filePath = path.join(OPENAPI_DIR, file);
      hash.update(file);
      hash.update(fs.readFileSync(filePath));
    }
  }

  return hash.digest("hex");
}

function translateProperty(prop) {
  if (!prop) return "any";
  if (prop.$ref) {
    return prop.$ref
      .split("/")
      .pop()
      .replace(/[^a-zA-Z0-9_]/g, "_");
  }
  if (prop.anyOf) {
    return prop.anyOf.map(translateProperty).join(" | ");
  }
  if (prop.oneOf) {
    return prop.oneOf.map(translateProperty).join(" | ");
  }
  if (prop.allOf) {
    return prop.allOf.map(translateProperty).join(" & ");
  }
  if (Array.isArray(prop.type)) {
    if (prop.type.length === 0) return "any";
    const types = prop.type.map((t) => translateProperty({ ...prop, type: t }));
    const allConstituents = types.flatMap((t) => t.split(" | "));
    return [...new Set(allConstituents)].join(" | ");
  }
  if (prop.type === "array") {
    const itemType = translateProperty(prop.items);
    return `${itemType}[]`;
  }
  if (prop.type === "string") {
    if (prop.enum) {
      return prop.enum.map((v) => JSON.stringify(v)).join(" | ");
    }
    return "string";
  }
  if (prop.type === "integer" || prop.type === "number") {
    return "number";
  }
  if (prop.type === "boolean") {
    return "boolean";
  }
  if (prop.type === "object") {
    if (prop.properties) {
      const subFields = [];
      const required = prop.required || [];
      for (const [subKey, subProp] of Object.entries(prop.properties)) {
        const isReq = required.includes(subKey);
        subFields.push(
          `${subKey}${isReq ? "" : "?"}: ${translateProperty(subProp)}`
        );
      }
      return `{ ${subFields.join("; ")} }`;
    }
    return "Record<string, any>";
  }
  if (prop.type === "null") {
    return "null";
  }
  return "any";
}

async function generate(options = {}) {
  const force = Boolean(options.force || process.argv.includes("--force"));
  const currentHash = calculateHash();

  if (!force && fs.existsSync(OUTPUT_PATH) && fs.existsSync(HASH_PATH)) {
    try {
      const manifest = JSON.parse(fs.readFileSync(HASH_PATH, "utf8"));
      if (manifest && manifest.hash === currentHash) {
        console.log(
          `Type definitions are up-to-date (hash: ${currentHash.slice(0, 8)}). Skipping generation.`
        );
        return { generated: false, hash: currentHash };
      }
    } catch {
      // Unparseable or corrupted manifest -> invalidate and regenerate
    }
  }

  console.log(`Generating types from ${OPENAPI_DIR} to ${OUTPUT_PATH}...`);
  if (!fs.existsSync(OPENAPI_DIR)) {
    console.error(`Schema directory not found at ${OPENAPI_DIR}`);
    process.exit(1);
  }

  const files = fs
    .readdirSync(OPENAPI_DIR)
    .filter((f) => f.endsWith(".json"))
    .sort();

  if (files.length === 0) {
    console.error(`No OpenAPI JSON schema files found in ${OPENAPI_DIR}`);
    process.exit(1);
  }

  const allSchemas = {};
  for (const file of files) {
    const filePath = path.join(OPENAPI_DIR, file);
    try {
      const schemaRaw = fs.readFileSync(filePath, "utf8");
      const api = JSON.parse(schemaRaw);
      const schemas = api?.components?.schemas || {};
      for (const [schemaName, schema] of Object.entries(schemas)) {
        if (!allSchemas[schemaName]) {
          allSchemas[schemaName] = schema;
        }
      }
    } catch (err) {
      console.warn(`Failed to parse ${file}: ${err.message}`);
    }
  }

  // No timestamp or absolute path, so regenerating unchanged specs leaves the file unchanged.
  let output = `// Auto-generated from the OpenAPI schema definitions in docs/openapi/legacy by src/types/generate-types.js. Do not edit.\n\n`;

  for (const [schemaName, schema] of Object.entries(allSchemas)) {
    const sanitizedName = schemaName.replace(/[^a-zA-Z0-9_]/g, "_");

    if (schema.enum) {
      output += `export type ${sanitizedName} = ${schema.enum.map((v) => JSON.stringify(v)).join(" | ")};\n\n`;
    } else if (schema.properties || schema.type === "object") {
      output += `export interface ${sanitizedName} {\n`;
      const required = schema.required || [];
      for (const [propName, prop] of Object.entries(schema.properties || {})) {
        const isReq = required.includes(propName);
        output += `  ${propName}${isReq ? "" : "?"}: ${translateProperty(prop)};\n`;
      }
      output += `}\n\n`;
    } else if (schema.type) {
      output += `export type ${sanitizedName} = ${translateProperty(schema)};\n\n`;
    } else {
      output += `export type ${sanitizedName} = any;\n\n`;
    }
  }

  const prettierConfig = (await prettier.resolveConfig(OUTPUT_PATH)) ?? {};
  const formatted = await prettier.format(output, {
    ...prettierConfig,
    filepath: OUTPUT_PATH,
  });
  fs.writeFileSync(OUTPUT_PATH, formatted, "utf8");
  fs.writeFileSync(
    HASH_PATH,
    JSON.stringify(
      { hash: currentHash, generatedAt: new Date().toISOString() },
      null,
      2
    ),
    "utf8"
  );
  console.log(
    `Successfully generated ${Object.keys(allSchemas).length} schemas from ${files.length} OpenAPI definitions into ${OUTPUT_PATH}`
  );
  return { generated: true, hash: currentHash };
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))
) {
  await generate();
}

export { translateProperty, generate, calculateHash };
