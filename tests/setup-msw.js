import { setupServer } from 'msw/node';
import { http, HttpResponse } from 'msw';
import fs from 'fs';
import path from 'path';
import yaml from 'js-yaml';
import { beforeAll, afterEach, afterAll, beforeEach } from 'vitest';

let spec = null;
let activeTestContractError = null;

function loadOpenApiSpec() {
  const cachedPath = path.resolve(__dirname, 'cached-openapi.json');
  
  // Try loading cached-openapi.json
  if (fs.existsSync(cachedPath)) {
    try {
      return JSON.parse(fs.readFileSync(cachedPath, 'utf8'));
    } catch (err) {}
  }

  // Fallback to docs/SDLC/03_API_Integration_Specification.md
  try {
    const mdPath = path.resolve(__dirname, '../docs/SDLC/03_API_Integration_Specification.md');
    if (fs.existsSync(mdPath)) {
      const content = fs.readFileSync(mdPath, 'utf8');
      const secTitle = "## 7. Complete OpenAPI 3.0 Contract Specification";
      const idx = content.indexOf(secTitle);
      if (idx !== -1) {
        const secContent = content.substring(idx + secTitle.length);
        const startFence = "```yaml";
        const startIdx = secContent.indexOf(startFence);
        if (startIdx !== -1) {
          const startPos = startIdx + startFence.length;
          const endPos = secContent.indexOf("```", startPos);
          if (endPos !== -1) {
            const yamlContent = secContent.substring(startPos, endPos).trim();
            const loaded = yaml.load(yamlContent);
            fs.writeFileSync(cachedPath, JSON.stringify(loaded, null, 2), 'utf8');
            return loaded;
          }
        }
      }
    }
  } catch (err) {
    console.error("Error reading SDLC OpenAPI markdown fallback:", err);
  }

  return null;
}

function matchPath(requestPath, openApiPaths) {
  if (!openApiPaths) return null;
  const url = new URL(requestPath, 'http://localhost:8000');
  let cleanPath = url.pathname;
  
  // Try exact match first
  if (openApiPaths[cleanPath]) {
    return { pathPattern: cleanPath, pathParams: {} };
  }
  
  // Try pattern matching for routes with placeholders like {id}
  for (const pattern of Object.keys(openApiPaths)) {
    const regexPattern = pattern
      .replace(/[-\/\\^$*+?.()|[\]]/g, '\\$&') // escape regex chars
      .replace(/\\\{([^}]+)\\\}/g, '([^/]+)'); // replace {id} with capturing group
    
    const regex = new RegExp(`^${regexPattern}$`);
    const match = cleanPath.match(regex);
    if (match) {
      const paramNames = [];
      const paramRegex = /\{([^}]+)\}/g;
      let paramMatch;
      while ((paramMatch = paramRegex.exec(pattern)) !== null) {
        paramNames.push(paramMatch[1]);
      }
      const pathParams = {};
      paramNames.forEach((name, idx) => {
        pathParams[name] = match[idx + 1];
      });
      return { pathPattern: pattern, pathParams };
    }
  }
  return null;
}

function resolveSchema(schema, spec) {
  if (!schema) return schema;
  if (typeof schema === 'object' && schema !== null) {
    if ('$ref' in schema) {
      const refPath = schema['$ref'].split('/');
      let resolved = spec;
      for (const part of refPath.slice(1)) {
        resolved = resolved ? resolved[part] : undefined;
      }
      return resolveSchema(resolved, spec);
    }
    const resolved = {};
    for (const [k, v] of Object.entries(schema)) {
      resolved[k] = resolveSchema(v, spec);
    }
    return resolved;
  }
  return schema;
}

function validateSchema(data, schema, spec, pathContext = 'data') {
  schema = resolveSchema(schema, spec);
  if (!schema) return;

  if (schema.anyOf && Array.isArray(schema.anyOf)) {
    const nonNullSchemas = schema.anyOf.filter(s => resolveSchema(s, spec)?.type !== 'null');
    if (nonNullSchemas.length === 1) {
      schema = nonNullSchemas[0];
    }
  }

  const expectedType = schema.type;
  if (data === null || data === undefined) {
    if (schema.nullable || expectedType === 'null' || (Array.isArray(expectedType) && expectedType.includes('null'))) {
      return;
    }
    throw new Error(`Value at ${pathContext} is required but is null or undefined`);
  }

  const actualType = typeof data;
  if (expectedType) {
    const normalizedExpected = Array.isArray(expectedType) ? expectedType : [expectedType];
    const isTypeValid = normalizedExpected.some(type => {
      if (type === 'string') return actualType === 'string';
      if (type === 'integer') return Number.isInteger(data);
      if (type === 'number') return actualType === 'number';
      if (type === 'boolean') return actualType === 'boolean';
      if (type === 'array') return Array.isArray(data);
      if (type === 'object') return actualType === 'object' && !Array.isArray(data);
      return true;
    });

    if (!isTypeValid) {
      throw new Error(`Type mismatch at ${pathContext}: expected ${expectedType}, got ${actualType} (value: ${JSON.stringify(data)})`);
    }
  }

  if (schema.enum && Array.isArray(schema.enum)) {
    if (!schema.enum.includes(data)) {
      throw new Error(`Value mismatch at ${pathContext}: expected one of [${schema.enum.join(', ')}], got ${JSON.stringify(data)}`);
    }
  }

  if (expectedType === 'object' || schema.properties) {
    if (typeof data !== 'object' || data === null || Array.isArray(data)) {
      throw new Error(`Expected object at ${pathContext}, got ${actualType}`);
    }

    const properties = schema.properties || {};
    const requiredFields = Array.isArray(schema.required) ? schema.required : [];

    for (const reqField of requiredFields) {
      if (!(reqField in data) || data[reqField] === undefined) {
        throw new Error(`Required property '${reqField}' is missing at ${pathContext}`);
      }
    }

    for (const [propName, propValue] of Object.entries(data)) {
      if (properties[propName]) {
        validateSchema(propValue, properties[propName], spec, `${pathContext}.${propName}`);
      }
    }
  }

  if (expectedType === 'array' || schema.items) {
    if (!Array.isArray(data)) {
      throw new Error(`Expected array at ${pathContext}, got ${actualType}`);
    }
    if (schema.items) {
      data.forEach((item, index) => {
        validateSchema(item, schema.items, spec, `${pathContext}[${index}]`);
      });
    }
  }
}

async function handleInterceptedRequest(req, spec) {
  const url = new URL(req.url);
  const method = req.method.toLowerCase();
  
  const match = matchPath(req.url, spec.paths);
  if (!match) return; // Not documented or whitelisted, skip
  
  const { pathPattern, pathParams } = match;
  const pathItem = spec.paths[pathPattern];
  const op = pathItem[method];
  if (!op) {
    throw new Error(`HTTP Method '${method.toUpperCase()}' on path '${pathPattern}' is missing in contract specification`);
  }
  
  const specParams = op.parameters || [];
  const queryParams = Object.fromEntries(url.searchParams.entries());
  
  for (const param of specParams) {
    const name = param.name;
    const place = param.in;
    const required = param.required;
    const schema = param.schema;
    
    let value;
    if (place === 'path') {
      value = pathParams[name];
    } else if (place === 'query') {
      value = queryParams[name];
    }
    
    if (required && value === undefined) {
      throw new Error(`Required parameter '${name}' in ${place} is missing on '${method.toUpperCase()} ${pathPattern}'`);
    }
    
    if (value !== undefined && schema) {
      let typedValue = value;
      const resolvedSchema = resolveSchema(schema, spec);
      if (resolvedSchema) {
        if (resolvedSchema.type === 'integer') {
          typedValue = parseInt(value, 10);
        } else if (resolvedSchema.type === 'number') {
          typedValue = parseFloat(value);
        } else if (resolvedSchema.type === 'boolean') {
          typedValue = value === 'true' || value === '1';
        }
      }
      validateSchema(typedValue, schema, spec, `parameter:${name}`);
    }
  }
  
  if (op.requestBody) {
    const required = op.requestBody.required;
    let bodyText;
    try {
      bodyText = await req.clone().text();
    } catch (e) {}
    
    if (required && (!bodyText || bodyText.trim() === '')) {
      throw new Error(`RequestBody is required on '${method.toUpperCase()} ${pathPattern}' but missing`);
    }
    
    if (bodyText && bodyText.trim() !== '') {
      let bodyData;
      try {
        bodyData = JSON.parse(bodyText);
      } catch (e) {
        throw new Error(`Failed to parse requestBody as JSON: ${e.message}`);
      }
      
      const content = op.requestBody.content || {};
      const mediaTypes = Object.keys(content);
      const jsonMediaType = mediaTypes.find(t => t.includes('json'));
      
      if (jsonMediaType && content[jsonMediaType].schema) {
        validateSchema(bodyData, content[jsonMediaType].schema, spec, `requestBody:${method.toUpperCase()} ${pathPattern}`);
      }
    }
  }
}

async function handleInterceptedResponse(req, res, spec) {
  const method = req.method.toLowerCase();
  const match = matchPath(req.url, spec.paths);
  if (!match) return;
  
  const { pathPattern } = match;
  const pathItem = spec.paths[pathPattern];
  const op = pathItem[method];
  if (!op) return;
  
  const status_code = res.status.toString();
  if (['401', '403', '404', '429', '500'].includes(status_code)) {
    return;
  }
  
  const specResponses = op.responses || {};
  if (!specResponses[status_code]) {
    if (status_code.startsWith('2') || status_code === '400') {
      throw new Error(`Expected response status code '${status_code}' on '${req.method.toUpperCase()} ${pathPattern}' is missing in specification`);
    }
    return;
  }
  
  const s_resp = specResponses[status_code];
  const s_content = s_resp.content || {};
  
  let resBodyText;
  try {
    resBodyText = await res.clone().text();
  } catch (e) {}
  
  for (const [media_type, s_media] of Object.entries(s_content)) {
    if (s_media.schema) {
      if (!resBodyText || resBodyText.trim() === '') {
        throw new Error(`Response schema expected but response body is empty on '${req.method.toUpperCase()} ${pathPattern}' (${status_code})`);
      }
      let resData;
      try {
        resData = JSON.parse(resBodyText);
      } catch (e) {
        throw new Error(`Failed to parse responseBody as JSON: ${e.message}`);
      }
      validateSchema(resData, s_media.schema, spec, `response:${req.method.toUpperCase()} ${pathPattern}:${status_code}:${media_type}`);
    }
  }
}

const server = setupServer(
  http.all('*', async ({ request }) => {
    if (!spec) return;
    try {
      await handleInterceptedRequest(request, spec);
    } catch (err) {
      activeTestContractError = err;
      throw err;
    }
  })
);

beforeAll(async () => {
  // Try on-the-fly fetch
  try {
    const cachedPath = path.resolve(__dirname, 'cached-openapi.json');
    const controller = new AbortController();
    const id = setTimeout(() => controller.abort(), 800);
    const res = await fetch('http://localhost:8000/openapi.json', { signal: controller.signal });
    clearTimeout(id);
    if (res.ok) {
      const data = await res.json();
      fs.writeFileSync(cachedPath, JSON.stringify(data, null, 2), 'utf8');
    }
  } catch (err) {}

  spec = loadOpenApiSpec();
  server.listen({ onUnhandledRequest: 'bypass' });
});

beforeEach(() => {
  activeTestContractError = null;
});

afterEach(() => {
  server.resetHandlers();
  if (activeTestContractError) {
    const err = activeTestContractError;
    activeTestContractError = null;
    throw err;
  }
});

afterAll(() => {
  server.close();
});

server.events.on('response:mocked', async ({ request, response }) => {
  if (!spec) return;
  try {
    await handleInterceptedResponse(request, response, spec);
  } catch (err) {
    activeTestContractError = err;
  }
});

// Expose globally to eliminate boilerplate
globalThis.mswServer = server;
globalThis.http = http;
globalThis.HttpResponse = HttpResponse;

export { server, http, HttpResponse };
