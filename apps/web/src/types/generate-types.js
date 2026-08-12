import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const OPENAPI_PATH = path.resolve(__dirname, '../../../../docs/openapi/designer_openapi.json');
const OUTPUT_PATH = path.resolve(__dirname, 'usdm.ts');

function translateProperty(prop) {
  if (!prop) return 'any';
  if (prop.$ref) {
    return prop.$ref.split('/').pop().replace(/[^a-zA-Z0-9_]/g, '_');
  }
  if (prop.anyOf) {
    return prop.anyOf.map(translateProperty).join(' | ');
  }
  if (prop.oneOf) {
    return prop.oneOf.map(translateProperty).join(' | ');
  }
  if (prop.allOf) {
    return prop.allOf.map(translateProperty).join(' & ');
  }
  if (prop.type === 'array') {
    const itemType = translateProperty(prop.items);
    return `${itemType}[]`;
  }
  if (prop.type === 'string') {
    if (prop.enum) {
      return prop.enum.map(v => JSON.stringify(v)).join(' | ');
    }
    return 'string';
  }
  if (prop.type === 'integer' || prop.type === 'number') {
    return 'number';
  }
  if (prop.type === 'boolean') {
    return 'boolean';
  }
  if (prop.type === 'object') {
    if (prop.properties) {
      const subFields = [];
      const required = prop.required || [];
      for (const [subKey, subProp] of Object.entries(prop.properties)) {
        const isReq = required.includes(subKey);
        subFields.push(`${subKey}${isReq ? '' : '?'}: ${translateProperty(subProp)}`);
      }
      return `{ ${subFields.join('; ')} }`;
    }
    return 'Record<string, any>';
  }
  if (prop.type === 'null') {
    return 'null';
  }
  return 'any';
}

function generate() {
  console.log(`Generating types from ${OPENAPI_PATH} to ${OUTPUT_PATH}...`);
  if (!fs.existsSync(OPENAPI_PATH)) {
    console.error(`Schema file not found at ${OPENAPI_PATH}`);
    process.exit(1);
  }

  const schemaRaw = fs.readFileSync(OPENAPI_PATH, 'utf8');
  const api = JSON.parse(schemaRaw);

  const schemas = api?.components?.schemas || {};
  let output = `// Auto-generated from OpenAPI schema definition\n// Generated on: ${new Date().toISOString()}\n\n`;

  for (const [schemaName, schema] of Object.entries(schemas)) {
    // Sanitize schema name to be a valid TS identifier
    const sanitizedName = schemaName.replace(/[^a-zA-Z0-9_]/g, '_');

    if (schema.enum) {
      output += `export type ${sanitizedName} = ${schema.enum.map(v => JSON.stringify(v)).join(' | ')};\n\n`;
    } else if (schema.properties || schema.type === 'object') {
      output += `export interface ${sanitizedName} {\n`;
      const required = schema.required || [];
      for (const [propName, prop] of Object.entries(schema.properties || {})) {
        const isReq = required.includes(propName);
        output += `  ${propName}${isReq ? '' : '?'}: ${translateProperty(prop)};\n`;
      }
      output += `}\n\n`;
    } else if (schema.type) {
      output += `export type ${sanitizedName} = ${translateProperty(schema)};\n\n`;
    } else {
      output += `export type ${sanitizedName} = any;\n\n`;
    }
  }

  fs.writeFileSync(OUTPUT_PATH, output, 'utf8');
  console.log(`Successfully generated ${Object.keys(schemas).length} schemas into ${OUTPUT_PATH}`);
}

generate();
