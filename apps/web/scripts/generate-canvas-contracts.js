/**
 * Generates TypeScript canvas contract modules in src/canvas/generated/ from the canonical .NET C# sources:
 *   - packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs
 *   - packages/security/src/Rbac/RoleNormalization.cs
 *   - packages/security/src/Rbac/PermissionMatrix.cs + PermissionMatrix.*.cs
 *   - apps/execution/src/Domain/Subjects/SubjectStateCodec.cs
 *   - apps/execution/src/Domain/Subjects/SubjectTransitionGraph.cs
 *   - apps/execution/src/Application/Subjects/SubjectAuthority.cs
 *
 * Invoked automatically by scripts/generate-api-types.js (`pnpm --filter web generate:api`).
 * Supports `--check` (exits 1 when any file in src/canvas/generated/ is missing, extra, or stale).
 */
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import * as prettier from "prettier";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const WEB_DIR = path.resolve(__dirname, "..");
export const REPO_ROOT = path.resolve(WEB_DIR, "../..");
export const CANVAS_GENERATED_DIR = path.resolve(
  WEB_DIR,
  "src/canvas/generated"
);

export const SOURCE_PATHS = Object.freeze({
  nodeKinds: path.join(
    REPO_ROOT,
    "packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs"
  ),
  roleNormalization: path.join(
    REPO_ROOT,
    "packages/security/src/Rbac/RoleNormalization.cs"
  ),
  rbacDir: path.join(REPO_ROOT, "packages/security/src/Rbac"),
  subjectStateCodec: path.join(
    REPO_ROOT,
    "apps/execution/src/Domain/Subjects/SubjectStateCodec.cs"
  ),
  subjectTransitionGraph: path.join(
    REPO_ROOT,
    "apps/execution/src/Domain/Subjects/SubjectTransitionGraph.cs"
  ),
  subjectAuthority: path.join(
    REPO_ROOT,
    "apps/execution/src/Application/Subjects/SubjectAuthority.cs"
  ),
});

function stripCSharpComments(source) {
  let out = "";
  let i = 0;
  let inString = false;
  while (i < source.length) {
    const ch = source[i];
    const next = source[i + 1];
    if (inString) {
      out += ch;
      if (ch === "\\" && i + 1 < source.length) {
        out += next;
        i += 2;
        continue;
      }
      if (ch === '"') {
        inString = false;
      }
      i += 1;
      continue;
    }
    if (ch === '"') {
      inString = true;
      out += ch;
      i += 1;
      continue;
    }
    if (ch === "/" && next === "/") {
      while (i < source.length && source[i] !== "\n") {
        i += 1;
      }
      continue;
    }
    if (ch === "/" && next === "*") {
      i += 2;
      while (
        i < source.length &&
        !(source[i] === "*" && source[i + 1] === "/")
      ) {
        if (source[i] === "\n") out += "\n";
        i += 1;
      }
      i += 2;
      continue;
    }
    out += ch;
    i += 1;
  }
  return out;
}

function extractBalancedBraces(text, openBraceIndex) {
  let depth = 0;
  let inString = false;
  for (let i = openBraceIndex; i < text.length; i += 1) {
    const ch = text[i];
    if (inString) {
      if (ch === "\\" && i + 1 < text.length) {
        i += 1;
        continue;
      }
      if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') {
      inString = true;
      continue;
    }
    if (ch === "{") depth += 1;
    else if (ch === "}") {
      depth -= 1;
      if (depth === 0) {
        return {
          body: text.slice(openBraceIndex + 1, i),
          endIndex: i,
        };
      }
    }
  }
  throw new Error("Unbalanced braces in C# contract source.");
}

function splitTopLevelArgs(argList) {
  const parts = [];
  let current = "";
  let depthParen = 0;
  let depthBrace = 0;
  let depthBracket = 0;
  let inString = false;
  for (let i = 0; i < argList.length; i += 1) {
    const ch = argList[i];
    if (inString) {
      current += ch;
      if (ch === "\\" && i + 1 < argList.length) {
        current += argList[i + 1];
        i += 1;
        continue;
      }
      if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"') {
      inString = true;
      current += ch;
      continue;
    }
    if (ch === "(") depthParen += 1;
    else if (ch === ")") depthParen -= 1;
    else if (ch === "{") depthBrace += 1;
    else if (ch === "}") depthBrace -= 1;
    else if (ch === "[") depthBracket += 1;
    else if (ch === "]") depthBracket -= 1;
    else if (
      ch === "," &&
      depthParen === 0 &&
      depthBrace === 0 &&
      depthBracket === 0
    ) {
      if (current.trim()) parts.push(current.trim());
      current = "";
      continue;
    }
    current += ch;
  }
  if (current.trim()) parts.push(current.trim());
  return parts;
}

export function parseNodeKinds(
  source = fs.readFileSync(SOURCE_PATHS.nodeKinds, "utf8")
) {
  const clean = stripCSharpComments(source);
  const constants = new Map();
  for (const match of clean.matchAll(
    /public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;/g
  )) {
    constants.set(match[1], match[2]);
  }
  const byIdMatch = clean.match(/ByIdType\s*=\s*new\s+Dictionary<[^>]+>\s*\{/);
  if (!byIdMatch || byIdMatch.index === undefined) {
    throw new Error("Could not find ByIdType in NodeKinds.cs");
  }
  const braceIdx = byIdMatch.index + byIdMatch[0].length - 1;
  const { body } = extractBalancedBraces(clean, braceIdx);
  const ordered = [];
  for (const entry of body.matchAll(/\[typeof\(\w+\)\]\s*=\s*(\w+)/g)) {
    const symbol = entry[1];
    const resolved = constants.get(symbol);
    if (!resolved) {
      throw new Error(`Unresolved NodeKinds constant '${symbol}'.`);
    }
    ordered.push(resolved);
  }
  if (ordered.length === 0) {
    throw new Error("Parsed zero node kinds from NodeKinds.cs");
  }
  return ordered;
}

export function parseRoleNormalization(
  source = fs.readFileSync(SOURCE_PATHS.roleNormalization, "utf8")
) {
  const clean = stripCSharpComments(source);
  const constants = new Map();
  for (const match of clean.matchAll(
    /public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;/g
  )) {
    constants.set(match[1], match[2]);
    constants.set(`RoleNormalization.${match[1]}`, match[2]);
  }

  const resolveExpr = (expr) => {
    const trimmed = expr.trim();
    const strMatch = trimmed.match(/^"([^"]*)"$/);
    if (strMatch) return strMatch[1];
    if (constants.has(trimmed)) return constants.get(trimmed);
    throw new Error(`Cannot resolve RoleNormalization token '${trimmed}'.`);
  };

  const auditorMatch = clean.match(
    /AUDITOR_ROLES[\s\S]*?new\s+HashSet<string>\([^)]*\)\s*\{/
  );
  const auditorRoles = [];
  if (auditorMatch && auditorMatch.index !== undefined) {
    const braceIdx = auditorMatch.index + auditorMatch[0].length - 1;
    const { body } = extractBalancedBraces(clean, braceIdx);
    for (const m of body.matchAll(/"([^"]+)"/g)) {
      auditorRoles.push(m[1]);
    }
  }

  const aliasesMatch = clean.match(
    /ROLE_ALIASES[\s\S]*?new\s+Dictionary<string,\s*string>\([^)]*\)\s*\{/
  );
  if (!aliasesMatch || aliasesMatch.index === undefined) {
    throw new Error("Could not find ROLE_ALIASES in RoleNormalization.cs");
  }
  const aliasBraceIdx = aliasesMatch.index + aliasesMatch[0].length - 1;
  const { body: aliasBody } = extractBalancedBraces(clean, aliasBraceIdx);

  const roleAliases = {};
  for (const m of aliasBody.matchAll(/\["([^"]+)"\]\s*=\s*([^,\r\n}]+)/g)) {
    const rawKey = m[1].trim().toLowerCase();
    roleAliases[rawKey] = resolveExpr(m[2]);
  }

  if (Object.keys(roleAliases).length === 0) {
    throw new Error("Parsed zero role aliases from RoleNormalization.cs");
  }

  return { constants, auditorRoles, roleAliases };
}

export function parsePermissionMatrix(rbacDir = SOURCE_PATHS.rbacDir) {
  const roleNorm = parseRoleNormalization(
    fs.readFileSync(path.join(rbacDir, "RoleNormalization.cs"), "utf8")
  );
  const files = fs
    .readdirSync(rbacDir)
    .filter((f) => /^PermissionMatrix(?:\.\w+)?\.cs$/.test(f))
    .sort((a, b) => {
      if (a === "PermissionMatrix.cs") return -1;
      if (b === "PermissionMatrix.cs") return 1;
      return a.localeCompare(b);
    });

  const cleanByFile = new Map(
    files.map((f) => [
      f,
      stripCSharpComments(fs.readFileSync(path.join(rbacDir, f), "utf8")),
    ])
  );

  const symbols = new Map(roleNorm.constants);
  for (const clean of cleanByFile.values()) {
    for (const m of clean.matchAll(
      /(?:public|private|internal)\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;/g
    )) {
      symbols.set(m[1], m[2]);
      symbols.set(`PermissionMatrix.${m[1]}`, m[2]);
    }
  }

  const resolveStringExpr = (expr, localVars = new Map()) => {
    const trimmed = expr.trim();
    const lit = trimmed.match(/^"([^"]*)"$/);
    if (lit) return lit[1];
    if (localVars.has(trimmed)) return localVars.get(trimmed);
    if (symbols.has(trimmed)) return symbols.get(trimmed);
    throw new Error(`Unable to resolve C# string expression '${trimmed}'.`);
  };

  const mainClean = cleanByFile.get("PermissionMatrix.cs");
  if (!mainClean) {
    throw new Error("PermissionMatrix.cs not found.");
  }

  // Parse static string[] arrays (e.g. OperationalStaffRoles)
  const arrayCollections = new Map();
  for (const m of mainClean.matchAll(
    /static\s+readonly\s+string\[\]\s+(\w+)\s*=\s*\[([\s\S]*?)\]\s*;/g
  )) {
    const arrName = m[1];
    const items = splitTopLevelArgs(m[2]).map((item) =>
      resolveStringExpr(item)
    );
    arrayCollections.set(arrName, items);
  }

  // Model _rolePermissions with reference sharing (admin -> sponsor_dm) matching C# static constructor semantics
  const roleBuckets = new Map();

  const ensureRoleBucket = (role) => {
    const key = role.toLowerCase();
    if (!roleBuckets.has(key)) {
      roleBuckets.set(key, new Map());
    }
    return roleBuckets.get(key);
  };

  const grantPermission = (role, resource, actions) => {
    const bucket = ensureRoleBucket(role);
    const resKey = resource.toLowerCase();
    if (!bucket.has(resKey)) {
      bucket.set(resKey, new Set());
    }
    const set = bucket.get(resKey);
    for (const act of actions) {
      set.add(act.toLowerCase());
    }
  };

  const cloneBucket = (sourceBucket) => {
    const copy = new Map();
    for (const [res, actions] of sourceBucket.entries()) {
      copy.set(res, new Set(actions));
    }
    return copy;
  };

  // 1. Parse _rolePermissions[...] = new(...) { ... } initializers in PermissionMatrix.cs
  const ctorIdx = mainClean.indexOf("static PermissionMatrix()");
  if (ctorIdx < 0) {
    throw new Error("Could not find static PermissionMatrix() constructor.");
  }
  const ctorBraceIdx = mainClean.indexOf("{", ctorIdx);
  const { body: ctorBody } = extractBalancedBraces(mainClean, ctorBraceIdx);

  let cursor = 0;
  while (cursor < ctorBody.length) {
    const assignMatch = ctorBody
      .slice(cursor)
      .match(/_rolePermissions\[([^\]]+)\]\s*=\s*/);
    if (!assignMatch || assignMatch.index === undefined) break;
    const roleExpr = assignMatch[1];
    const rhsStart = cursor + assignMatch.index + assignMatch[0].length;
    const rhsRest = ctorBody.slice(rhsStart);

    const newDictMatch = rhsRest.match(/^new(?:\s*\([^)]*\))?\s*\{/);
    if (newDictMatch) {
      const openIdx = rhsStart + newDictMatch[0].length - 1;
      const { body: dictBody, endIndex } = extractBalancedBraces(
        ctorBody,
        openIdx
      );
      const role = resolveStringExpr(roleExpr);
      ensureRoleBucket(role);
      for (const entryMatch of dictBody.matchAll(
        /\["([^"]+)"\]\s*=\s*\[([^\]]*)\]/g
      )) {
        const resource = entryMatch[1];
        const actions = splitTopLevelArgs(entryMatch[2]).map((a) =>
          resolveStringExpr(a)
        );
        grantPermission(role, resource, actions);
      }
      cursor = endIndex + 1;
      continue;
    }

    const aliasRefMatch = rhsRest.match(/^_rolePermissions\[([^\]]+)\]\s*;/);
    if (aliasRefMatch) {
      const targetRole = resolveStringExpr(roleExpr).toLowerCase();
      const sourceRole = resolveStringExpr(aliasRefMatch[1]).toLowerCase();
      roleBuckets.set(targetRole, ensureRoleBucket(sourceRole));
      cursor = rhsStart + aliasRefMatch[0].length;
      continue;
    }

    const cloneCallMatch = rhsRest.match(/^ClonePermissions\((\w+)\)\s*;/);
    if (cloneCallMatch) {
      cursor = rhsStart + cloneCallMatch[0].length;
      continue;
    }

    cursor = rhsStart + 1;
  }

  // 2. Execute Grant(...) calls across all module files (AddModuleGrants)
  const executeGrantStatements = (blockText, inheritedLocals = new Map()) => {
    const locals = new Map(inheritedLocals);
    for (const localDecl of blockText.matchAll(
      /(?:const\s+)?string\s+(\w+)\s*=\s*([^;]+);/g
    )) {
      const varName = localDecl[1];
      const rhs = localDecl[2].trim();
      try {
        locals.set(varName, resolveStringExpr(rhs, locals));
      } catch {
        // Ignore non-constant expressions (e.g. TrySplit parameters)
      }
    }

    let pos = 0;
    while (pos < blockText.length) {
      const rest = blockText.slice(pos);
      const foreachMatch = rest.match(
        /foreach\s*\(\s*string\s+(\w+)\s+in\s+(?:new\s*\[\s*\]\s*\{([^}]*)\}|(\w+))\s*\)\s*\{/
      );
      const grantMatch = rest.match(/\bGrant\s*\(/);

      const foreachIdx =
        foreachMatch && foreachMatch.index !== undefined
          ? foreachMatch.index
          : Infinity;
      const grantIdx =
        grantMatch && grantMatch.index !== undefined
          ? grantMatch.index
          : Infinity;

      if (foreachIdx === Infinity && grantIdx === Infinity) break;

      if (foreachIdx < grantIdx) {
        const iterVar = foreachMatch[1];
        const inlineList = foreachMatch[2];
        const namedCollection = foreachMatch[3];
        const roles =
          inlineList !== undefined
            ? splitTopLevelArgs(inlineList).map((r) =>
                resolveStringExpr(r, locals)
              )
            : (arrayCollections.get(namedCollection) ?? []);
        if (roles.length === 0) {
          throw new Error(
            `Empty or unresolved foreach collection '${namedCollection ?? inlineList}'.`
          );
        }
        const bodyOpenIdx = pos + foreachIdx + foreachMatch[0].length - 1;
        const { body: loopBody, endIndex } = extractBalancedBraces(
          blockText,
          bodyOpenIdx
        );
        for (const role of roles) {
          const loopLocals = new Map(locals);
          loopLocals.set(iterVar, role);
          executeGrantStatements(loopBody, loopLocals);
        }
        pos = endIndex + 1;
      } else {
        const callStart = pos + grantIdx;
        const openParen = blockText.indexOf("(", callStart);
        let depth = 0;
        let closeParen = -1;
        for (let i = openParen; i < blockText.length; i += 1) {
          if (blockText[i] === "(") depth += 1;
          else if (blockText[i] === ")") {
            depth -= 1;
            if (depth === 0) {
              closeParen = i;
              break;
            }
          }
        }
        const afterCall = blockText.slice(closeParen + 1).trimStart();
        // Skip method declaration `private static void Grant(string role, ...)`
        if (afterCall.startsWith("{")) {
          const bodyBrace = blockText.indexOf("{", closeParen + 1);
          const { endIndex } = extractBalancedBraces(blockText, bodyBrace);
          pos = endIndex + 1;
          continue;
        }
        const args = splitTopLevelArgs(
          blockText.slice(openParen + 1, closeParen)
        );
        const role = resolveStringExpr(args[0], locals);
        const resource = resolveStringExpr(args[1], locals);
        const actions = args.slice(2).map((a) => resolveStringExpr(a, locals));
        grantPermission(role, resource, actions);
        pos = closeParen + 1;
      }
    }
  };

  for (const [fileName, clean] of cleanByFile.entries()) {
    if (fileName === "PermissionMatrix.cs") continue;
    executeGrantStatements(clean);
  }

  // 3. Apply piBase clone + overrides from PermissionMatrix.cs
  const piBaseCloneMatch = ctorBody.match(
    /var\s+piBase\s*=\s*ClonePermissions\(\s*_rolePermissions\[([^\]]+)\]\s*\)\s*;/
  );
  if (piBaseCloneMatch && piBaseCloneMatch.index !== undefined) {
    const baseRole = resolveStringExpr(piBaseCloneMatch[1]).toLowerCase();
    const piBucket = cloneBucket(ensureRoleBucket(baseRole));
    const afterPi = ctorBody.slice(
      piBaseCloneMatch.index + piBaseCloneMatch[0].length
    );
    for (const overMatch of afterPi.matchAll(
      /piBase\["([^"]+)"\]\s*=\s*\[([^\]]*)\]\s*;/g
    )) {
      const resource = overMatch[1].toLowerCase();
      const actions = splitTopLevelArgs(overMatch[2]).map((a) =>
        resolveStringExpr(a).toLowerCase()
      );
      piBucket.set(resource, new Set(actions));
    }
    for (const targetMatch of afterPi.matchAll(
      /_rolePermissions\[([^\]]+)\]\s*=\s*ClonePermissions\(\s*piBase\s*\)\s*;/g
    )) {
      const targetRole = resolveStringExpr(targetMatch[1]).toLowerCase();
      roleBuckets.set(targetRole, cloneBucket(piBucket));
    }
  }

  // Build deterministic plain objects
  const rolePermissions = {};
  const permissionRolesMap = new Map();

  for (const [role, bucket] of roleBuckets.entries()) {
    const resourcesObj = {};
    for (const resource of [...bucket.keys()].sort()) {
      const sortedActions = [...bucket.get(resource)].sort();
      resourcesObj[resource] = sortedActions;
      // Exclude duplicate legacy "admin" key from PERMISSION_ROLES because RoleNormalization maps "admin" -> "sponsor_dm"
      if (role !== "admin") {
        for (const action of sortedActions) {
          const permKey = `${resource}:${action}`;
          if (!permissionRolesMap.has(permKey)) {
            permissionRolesMap.set(permKey, new Set());
          }
          permissionRolesMap.get(permKey).add(role);
        }
      }
    }
    rolePermissions[role] = resourcesObj;
  }

  const permissionRoles = {};
  for (const permKey of [...permissionRolesMap.keys()].sort()) {
    permissionRoles[permKey] = [...permissionRolesMap.get(permKey)].sort();
  }

  return { rolePermissions, permissionRoles };
}

export function parseParticipantContracts(
  codecSource = fs.readFileSync(SOURCE_PATHS.subjectStateCodec, "utf8"),
  graphSource = fs.readFileSync(SOURCE_PATHS.subjectTransitionGraph, "utf8"),
  authoritySource = fs.readFileSync(SOURCE_PATHS.subjectAuthority, "utf8")
) {
  const cleanCodec = stripCSharpComments(codecSource);
  const codeByEnum = new Map();
  const stateCodes = [];
  for (const m of cleanCodec.matchAll(
    /\["([A-Z_]+)"\]\s*=\s*SubjectState\.(\w+)/g
  )) {
    stateCodes.push(m[1]);
    codeByEnum.set(m[2], m[1]);
  }
  if (stateCodes.length === 0) {
    throw new Error("Parsed zero states from SubjectStateCodec.cs");
  }

  const cleanGraph = stripCSharpComments(graphSource);
  const transitions = {};
  for (const m of cleanGraph.matchAll(
    /\[SubjectState\.(\w+)\]\s*=\s*new\s+HashSet<SubjectState>\s*(?:\{([^}]*)\}|\(\))/g
  )) {
    const fromCode = codeByEnum.get(m[1]);
    if (!fromCode) {
      throw new Error(
        `Unknown SubjectState '${m[1]}' in SubjectTransitionGraph.cs`
      );
    }
    const toCodes = [...(m[2] ?? "").matchAll(/SubjectState\.(\w+)/g)].map(
      ([, member]) => {
        const resolved = codeByEnum.get(member);
        if (!resolved) {
          throw new Error(`Unknown target SubjectState '${member}'`);
        }
        return resolved;
      }
    );
    transitions[fromCode] = toCodes;
  }

  const cleanAuth = stripCSharpComments(authoritySource);
  const extractIsRoles = (methodName) => {
    const idx = cleanAuth.indexOf(`bool ${methodName}`);
    if (idx < 0) {
      throw new Error(`Could not find ${methodName} in SubjectAuthority.cs`);
    }
    const slice = cleanAuth.slice(idx);
    const retIdx = slice.indexOf("return normalized is");
    const semiIdx = slice.indexOf(";", retIdx);
    const expr = slice.slice(retIdx, semiIdx);
    return [...expr.matchAll(/"([^"]+)"/g)].map(([, r]) => r);
  };

  const participantAuthorityRoles = extractIsRoles("CanManageParticipants");
  const unblindAuthorityRoles = extractIsRoles("CanUnblind");

  return {
    stateCodes,
    transitions,
    participantAuthorityRoles,
    unblindAuthorityRoles,
  };
}

const GENERATED_HEADER = (sources) =>
  `/**\n` +
  ` * Generated from ${sources} by scripts/generate-canvas-contracts.js.\n` +
  ` * Do not edit manually; run \`pnpm --filter web generate:api\` to regenerate.\n` +
  ` */\n\n`;

export async function renderCanvasContractFiles() {
  const prettierConfig = (await prettier.resolveConfig(WEB_DIR)) ?? {};
  const formatTs = (filepath, code) =>
    prettier.format(code, {
      ...prettierConfig,
      filepath,
      parser: "typescript",
    });

  const nodeKinds = parseNodeKinds();
  const { auditorRoles, roleAliases } = parseRoleNormalization();
  const { rolePermissions, permissionRoles } = parsePermissionMatrix();
  const {
    stateCodes,
    transitions,
    participantAuthorityRoles,
    unblindAuthorityRoles,
  } = parseParticipantContracts();

  const files = new Map();

  files.set(
    "nodeKinds.ts",
    await formatTs(
      path.join(CANVAS_GENERATED_DIR, "nodeKinds.ts"),
      GENERATED_HEADER(
        "packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs"
      ) +
        `/** Registered kernel node kinds, in the exact order \`NodeKinds.cs\` declares them. */\n` +
        `export const NODE_KINDS = ${JSON.stringify(nodeKinds, null, 2)} as const;\n\n` +
        `export type NodeKind = (typeof NODE_KINDS)[number];\n`
    )
  );

  files.set(
    "roleAliases.ts",
    await formatTs(
      path.join(CANVAS_GENERATED_DIR, "roleAliases.ts"),
      GENERATED_HEADER("packages/security/src/Rbac/RoleNormalization.cs") +
        `export const AUDITOR_ROLES = ${JSON.stringify(auditorRoles, null, 2)} as const;\n\n` +
        `/** Exact mirror of \`RoleNormalization.ROLE_ALIASES\` (lower-cased input -> canonical backend role). */\n` +
        `export const BACKEND_ROLE_ALIASES: Readonly<Record<string, string>> = Object.freeze(${JSON.stringify(
          roleAliases,
          null,
          2
        )});\n`
    )
  );

  files.set(
    "permissions.ts",
    await formatTs(
      path.join(CANVAS_GENERATED_DIR, "permissions.ts"),
      GENERATED_HEADER("packages/security/src/Rbac/PermissionMatrix*.cs") +
        `/** Canonical role -> resource -> allowed actions from \`Cadence.Security.Rbac.PermissionMatrix\`. */\n` +
        `export const ROLE_PERMISSIONS: Readonly<Record<string, Readonly<Record<string, readonly string[]>>>> = Object.freeze(${JSON.stringify(
          rolePermissions,
          null,
          2
        )});\n\n` +
        `/** \`resource:action\` -> canonical roles holding that permission in \`PermissionMatrix\`. */\n` +
        `export const PERMISSION_ROLES: Readonly<Record<string, readonly string[]>> = Object.freeze(${JSON.stringify(
          permissionRoles,
          null,
          2
        )});\n`
    )
  );

  files.set(
    "participantContracts.ts",
    await formatTs(
      path.join(CANVAS_GENERATED_DIR, "participantContracts.ts"),
      GENERATED_HEADER(
        "apps/execution/src/Domain/Subjects/{SubjectStateCodec,SubjectTransitionGraph}.cs and Application/Subjects/SubjectAuthority.cs"
      ) +
        `export const PARTICIPANT_STATE_CODES = ${JSON.stringify(stateCodes, null, 2)} as const;\n\n` +
        `export type ParticipantStateCode = (typeof PARTICIPANT_STATE_CODES)[number];\n\n` +
        `export const PARTICIPANT_TRANSITIONS: Readonly<Record<string, readonly string[]>> = Object.freeze(${JSON.stringify(
          transitions,
          null,
          2
        )});\n\n` +
        `export const PARTICIPANT_AUTHORITY_ROLES = ${JSON.stringify(
          participantAuthorityRoles,
          null,
          2
        )} as const;\n\n` +
        `export const UNBLIND_AUTHORITY_ROLES = ${JSON.stringify(
          unblindAuthorityRoles,
          null,
          2
        )} as const;\n`
    )
  );

  files.set(
    "index.ts",
    await formatTs(
      path.join(CANVAS_GENERATED_DIR, "index.ts"),
      GENERATED_HEADER(
        "canonical .NET kernel, security, and execution contracts"
      ) +
        `export * from "./nodeKinds";\n` +
        `export * from "./roleAliases";\n` +
        `export * from "./permissions";\n` +
        `export * from "./participantContracts";\n`
    )
  );

  return files;
}

export async function generateCanvasContracts({
  check = false,
  outputDir = CANVAS_GENERATED_DIR,
  baseDir = WEB_DIR,
} = {}) {
  const renderedFiles = await renderCanvasContractFiles();
  const stale = [];

  if (!check) {
    fs.mkdirSync(outputDir, { recursive: true });
  }

  for (const [fileName, expectedContent] of renderedFiles.entries()) {
    const filePath = path.join(outputDir, fileName);
    if (check) {
      const current = fs.existsSync(filePath)
        ? fs.readFileSync(filePath, "utf8")
        : "";
      if (current !== expectedContent) {
        stale.push(path.relative(baseDir, filePath));
      }
      continue;
    }
    fs.writeFileSync(filePath, expectedContent);
    console.log(`[+] ${path.relative(baseDir, filePath)}`);
  }

  if (fs.existsSync(outputDir)) {
    for (const existing of fs.readdirSync(outputDir)) {
      if (!renderedFiles.has(existing)) {
        const extraPath = path.join(outputDir, existing);
        if (check) {
          stale.push(path.relative(baseDir, extraPath));
        } else {
          fs.rmSync(extraPath, { recursive: true, force: true });
        }
      }
    }
  }

  return stale;
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const check = process.argv.includes("--check");
  const stale = await generateCanvasContracts({ check });
  if (stale.length > 0) {
    console.error(
      `[ERROR] Out of date canvas contracts: ${stale.join(", ")}. Run 'pnpm --filter web generate:api' and commit the result.`
    );
    process.exitCode = 1;
  }
}
