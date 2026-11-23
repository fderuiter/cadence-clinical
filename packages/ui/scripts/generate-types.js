import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import ts from "typescript";

const __dirname = fileURLToPath(new URL(".", import.meta.url));
const UI_ROOT = path.resolve(__dirname, "..");

const MODEL_PATH = path.resolve(
  __dirname,
  "../../../docs/CDISC/Library/Data_Collection/CDASH_Model_v1.3.json"
);
const IG_PATH = path.resolve(
  __dirname,
  "../../../docs/CDISC/Library/Data_Collection/CDASHIG_v2.3.json"
);
const OUTPUT_DIR = path.resolve(UI_ROOT, "src/types");
const OUTPUT_FILE = path.resolve(OUTPUT_DIR, "cdash.ts");
const DIST_DIR = path.resolve(UI_ROOT, "dist");
const DIST_DTS_FILE = path.resolve(DIST_DIR, "index.d.ts");
const COMPONENTS_DIR = path.resolve(UI_ROOT, "src/components");
const SHARED_PROPS_FILE = path.resolve(
  COMPONENTS_DIR,
  "clinical/sharedProps.js"
);

function sanitizeJson(filePath) {
  const raw = fs.readFileSync(filePath, "utf8");
  const sanitized = raw.replace(/:\s*NaN\b/g, ": null");
  return JSON.parse(sanitized);
}

function walkVueComponents(dir) {
  const found = [];
  if (!fs.existsSync(dir)) return found;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...walkVueComponents(fullPath));
    } else if (entry.name.endsWith(".vue")) {
      found.push({
        name: path.basename(entry.name, ".vue"),
        fullPath,
      });
    }
  }
  return found.sort((a, b) =>
    a.name === b.name
      ? a.fullPath.localeCompare(b.fullPath)
      : a.name < b.name
        ? -1
        : 1
  );
}

function mapConstructorExprToTs(expr, sourceFile) {
  if (!expr) return "any";
  if (ts.isAsExpression(expr) && expr.type) {
    if (
      ts.isTypeReferenceNode(expr.type) &&
      expr.type.typeArguments &&
      expr.type.typeArguments.length > 0
    ) {
      return expr.type.typeArguments[0].getText(sourceFile);
    }
    return expr.type.getText(sourceFile);
  }
  if (ts.isIdentifier(expr)) {
    switch (expr.text) {
      case "String":
        return "string";
      case "Number":
        return "number";
      case "Boolean":
        return "boolean";
      case "Array":
        return "any[]";
      case "Object":
        return "Record<string, any>";
      case "Function":
        return "((...args: any[]) => any)";
      default:
        return "any";
    }
  }
  if (ts.isArrayLiteralExpression(expr)) {
    const mapped = Array.from(
      new Set(expr.elements.map((el) => mapConstructorExprToTs(el, sourceFile)))
    );
    if (mapped.includes("any") || mapped.length === 0) return "any";
    return mapped.join(" | ");
  }
  return "any";
}

function formatPropertyKey(rawKey) {
  return /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(rawKey)
    ? rawKey
    : JSON.stringify(rawKey);
}

function getRawPropertyKey(nameNode, sourceFile) {
  if (ts.isIdentifier(nameNode) || ts.isStringLiteral(nameNode)) {
    return nameNode.text;
  }
  return nameNode.getText(sourceFile);
}

function getPropertyKeyText(nameNode, sourceFile) {
  if (ts.isIdentifier(nameNode)) return nameNode.text;
  if (ts.isStringLiteral(nameNode)) {
    return formatPropertyKey(nameNode.text);
  }
  return nameNode.getText(sourceFile);
}

function loadSharedPropsMap() {
  const sharedMap = new Map();
  if (!fs.existsSync(SHARED_PROPS_FILE)) return sharedMap;
  const raw = fs.readFileSync(SHARED_PROPS_FILE, "utf8");
  const sourceFile = ts.createSourceFile(
    "sharedProps.js",
    raw,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.JS
  );
  for (const stmt of sourceFile.statements) {
    if (!ts.isVariableStatement(stmt)) continue;
    for (const decl of stmt.declarationList.declarations) {
      if (
        ts.isIdentifier(decl.name) &&
        decl.initializer &&
        ts.isObjectLiteralExpression(decl.initializer)
      ) {
        sharedMap.set(decl.name.text, {
          objNode: decl.initializer,
          sourceFile,
        });
      }
    }
  }
  return sharedMap;
}

function collectRuntimeObjectProps(
  objNode,
  sourceFile,
  sharedPropsMap,
  propMap
) {
  for (const prop of objNode.properties) {
    if (ts.isSpreadAssignment(prop) && ts.isIdentifier(prop.expression)) {
      const shared = sharedPropsMap.get(prop.expression.text);
      if (shared) {
        collectRuntimeObjectProps(
          shared.objNode,
          shared.sourceFile,
          sharedPropsMap,
          propMap
        );
      }
      continue;
    }
    if (!ts.isPropertyAssignment(prop)) continue;
    const rawKey = getRawPropertyKey(prop.name, sourceFile);
    const key = getPropertyKeyText(prop.name, sourceFile);
    const init = prop.initializer;
    let propType = "any";
    let isRequired = false;

    if (ts.isObjectLiteralExpression(init)) {
      let hasDefault = false;
      let hasNullDefault = false;
      for (const sub of init.properties) {
        if (ts.isPropertyAssignment(sub)) {
          const subKey = getPropertyKeyText(sub.name, sourceFile);
          if (subKey === "type") {
            propType = mapConstructorExprToTs(sub.initializer, sourceFile);
          } else if (subKey === "required") {
            isRequired = sub.initializer.kind === ts.SyntaxKind.TrueKeyword;
          } else if (subKey === "default") {
            hasDefault = true;
            if (sub.initializer.kind === ts.SyntaxKind.NullKeyword) {
              hasNullDefault = true;
            }
          }
        } else if (ts.isMethodDeclaration(sub)) {
          const subKey = getPropertyKeyText(sub.name, sourceFile);
          if (subKey === "default") {
            hasDefault = true;
          }
        }
      }
      if (hasDefault) {
        isRequired = false;
      }
      if (hasNullDefault && propType !== "any" && !propType.includes("null")) {
        propType = `${propType} | null`;
      }
    } else {
      propType = mapConstructorExprToTs(init, sourceFile);
    }

    propMap.set(rawKey, `    ${key}${isRequired ? "" : "?"}: ${propType};`);
  }
}

function collectPropsFromTypeMembers(
  members,
  defaultKeys,
  sourceFile,
  propMap
) {
  for (const member of members) {
    if (!ts.isPropertySignature(member) || !member.name) continue;
    const rawKey = getRawPropertyKey(member.name, sourceFile);
    const formattedKey = getPropertyKeyText(member.name, sourceFile);
    const optional = Boolean(member.questionToken) || defaultKeys.has(rawKey);
    const typeText = member.type ? member.type.getText(sourceFile) : "any";
    propMap.set(
      rawKey,
      `    ${formattedKey}${optional ? "?" : ""}: ${typeText};`
    );
  }
}

function collectEmitsFromNode(defineEmitsCall, sourceFile, emitNames) {
  if (
    defineEmitsCall.typeArguments &&
    defineEmitsCall.typeArguments.length > 0
  ) {
    const typeArg = defineEmitsCall.typeArguments[0];
    if (ts.isTypeLiteralNode(typeArg)) {
      for (const member of typeArg.members) {
        if (ts.isPropertySignature(member) && member.name) {
          emitNames.add(getRawPropertyKey(member.name, sourceFile));
        } else if (
          ts.isCallSignatureDeclaration(member) &&
          member.parameters.length > 0
        ) {
          const firstParamType = member.parameters[0].type;
          if (
            firstParamType &&
            ts.isLiteralTypeNode(firstParamType) &&
            ts.isStringLiteral(firstParamType.literal)
          ) {
            emitNames.add(firstParamType.literal.text);
          } else if (firstParamType && ts.isUnionTypeNode(firstParamType)) {
            for (const u of firstParamType.types) {
              if (ts.isLiteralTypeNode(u) && ts.isStringLiteral(u.literal)) {
                emitNames.add(u.literal.text);
              }
            }
          }
        }
      }
    }
  }
  if (defineEmitsCall.arguments.length > 0) {
    const arg = defineEmitsCall.arguments[0];
    if (ts.isArrayLiteralExpression(arg)) {
      for (const el of arg.elements) {
        if (ts.isStringLiteral(el)) {
          emitNames.add(el.text);
        }
      }
    } else if (ts.isObjectLiteralExpression(arg)) {
      for (const prop of arg.properties) {
        if (
          (ts.isPropertyAssignment(prop) || ts.isMethodDeclaration(prop)) &&
          prop.name
        ) {
          emitNames.add(getRawPropertyKey(prop.name, sourceFile));
        }
      }
    }
  }
}

function extractTemplateSlots(rawVueContent) {
  const templateRegion = rawVueContent
    .replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, "")
    .replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, "");
  const slotNames = new Set();
  let hasDynamicSlotName = false;
  for (const match of templateRegion.matchAll(/<slot\b([^>]*)>/gi)) {
    const attrs = match[1] || "";
    const nameMatch = attrs.match(/\bname\s*=\s*["']([^"']+)["']/i);
    if (nameMatch) {
      slotNames.add(nameMatch[1]);
    } else if (/:name\b|v-bind:name\b/i.test(attrs)) {
      hasDynamicSlotName = true;
      slotNames.add("default");
    } else {
      slotNames.add("default");
    }
  }
  return { slotNames: Array.from(slotNames), hasDynamicSlotName };
}

function extractVueComponentSignature(vueFilePath, sharedPropsMap) {
  const raw = fs.readFileSync(vueFilePath, "utf8");
  const { slotNames, hasDynamicSlotName } = extractTemplateSlots(raw);
  const scriptBlocks = Array.from(
    raw.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi),
    (m) => m[1]
  );

  const propMap = new Map();
  const emitNames = new Set();

  if (scriptBlocks.length > 0) {
    const scriptContent = scriptBlocks.join("\n");
    const sourceFile = ts.createSourceFile(
      path.basename(vueFilePath) + ".ts",
      scriptContent,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TS
    );

    const localTypeDecls = new Map();
    for (const stmt of sourceFile.statements) {
      if (ts.isInterfaceDeclaration(stmt)) {
        localTypeDecls.set(stmt.name.text, stmt.members);
      } else if (
        ts.isTypeAliasDeclaration(stmt) &&
        ts.isTypeLiteralNode(stmt.type)
      ) {
        localTypeDecls.set(stmt.name.text, stmt.type.members);
      }
    }

    let propsResolved = false;

    function visit(node) {
      if (ts.isCallExpression(node)) {
        if (
          !propsResolved &&
          ts.isIdentifier(node.expression) &&
          node.expression.text === "withDefaults" &&
          node.arguments.length >= 1 &&
          ts.isCallExpression(node.arguments[0]) &&
          ts.isIdentifier(node.arguments[0].expression) &&
          node.arguments[0].expression.text === "defineProps"
        ) {
          const defineCall = node.arguments[0];
          const defaultKeys = new Set();
          if (
            node.arguments.length >= 2 &&
            ts.isObjectLiteralExpression(node.arguments[1])
          ) {
            for (const p of node.arguments[1].properties) {
              if (
                (ts.isPropertyAssignment(p) ||
                  ts.isShorthandPropertyAssignment(p) ||
                  ts.isMethodDeclaration(p)) &&
                p.name
              ) {
                defaultKeys.add(getRawPropertyKey(p.name, sourceFile));
              }
            }
          }
          resolveDefinePropsCall(
            defineCall,
            defaultKeys,
            localTypeDecls,
            sourceFile,
            sharedPropsMap,
            propMap
          );
          propsResolved = true;
          return;
        }

        if (
          !propsResolved &&
          ts.isIdentifier(node.expression) &&
          node.expression.text === "defineProps"
        ) {
          resolveDefinePropsCall(
            node,
            new Set(),
            localTypeDecls,
            sourceFile,
            sharedPropsMap,
            propMap
          );
          propsResolved = true;
          return;
        }

        if (
          ts.isIdentifier(node.expression) &&
          node.expression.text === "defineEmits"
        ) {
          collectEmitsFromNode(node, sourceFile, emitNames);
          return;
        }
      }
      ts.forEachChild(node, visit);
    }

    visit(sourceFile);
  }

  for (const ev of emitNames) {
    if (!ev) continue;
    const listenerKey = `on${ev.charAt(0).toUpperCase()}${ev.slice(1)}`;
    if (!propMap.has(listenerKey)) {
      propMap.set(
        listenerKey,
        `    ${formatPropertyKey(listenerKey)}?: (...args: any[]) => any;`
      );
    }
  }

  const fields = Array.from(propMap.values());
  const propsType =
    fields.length > 0
      ? `{\n${fields.join("\n")}\n    [key: string]: any;\n  }`
      : "Record<string, any>";

  if (slotNames.length > 0) {
    const slotFields = slotNames.map(
      (slotName) => `    ${formatPropertyKey(slotName)}?: (props: any) => any;`
    );
    if (hasDynamicSlotName) {
      slotFields.push("    [name: string]: ((props: any) => any) | undefined;");
    }
    const slotsType = `{\n${slotFields.join("\n")}\n  }`;
    return { propsType, slotsType };
  }

  return { propsType, slotsType: null };
}

function resolveDefinePropsCall(
  defineCall,
  defaultKeys,
  localTypeDecls,
  sourceFile,
  sharedPropsMap,
  propMap
) {
  if (defineCall.typeArguments && defineCall.typeArguments.length > 0) {
    const typeArg = defineCall.typeArguments[0];
    if (ts.isTypeLiteralNode(typeArg)) {
      collectPropsFromTypeMembers(
        typeArg.members,
        defaultKeys,
        sourceFile,
        propMap
      );
      return;
    }
    if (ts.isTypeReferenceNode(typeArg) && ts.isIdentifier(typeArg.typeName)) {
      const members = localTypeDecls.get(typeArg.typeName.text);
      if (members) {
        collectPropsFromTypeMembers(members, defaultKeys, sourceFile, propMap);
      }
    }
    return;
  }

  if (defineCall.arguments.length > 0) {
    const arg = defineCall.arguments[0];
    if (ts.isObjectLiteralExpression(arg)) {
      collectRuntimeObjectProps(arg, sourceFile, sharedPropsMap, propMap);
      return;
    }
    if (ts.isArrayLiteralExpression(arg)) {
      for (const el of arg.elements) {
        if (ts.isStringLiteral(el)) {
          propMap.set(
            el.text,
            `    ${getPropertyKeyText(el, sourceFile)}?: any;`
          );
        }
      }
    }
  }
}

function generateCdashTypes() {
  console.log("Generating CDASH TypeScript definitions...");

  if (!fs.existsSync(OUTPUT_DIR)) {
    fs.mkdirSync(OUTPUT_DIR, { recursive: true });
  }

  const modelData = sanitizeJson(MODEL_PATH);
  const igData = sanitizeJson(IG_PATH);

  const modelVars = modelData.Variables || [];
  const igVars = igData.Variables || [];

  const variableCodesSet = new Set();
  modelVars.forEach((v) => {
    if (v["CDASH Variable"]) {
      variableCodesSet.add(v["CDASH Variable"]);
    }
  });
  igVars.forEach((v) => {
    if (v["CDASHIG Variable"]) {
      variableCodesSet.add(v["CDASHIG Variable"]);
    }
  });

  const variableCodes = Array.from(variableCodesSet).sort();

  const tsCode = `// Auto-generated CDASH and CDASHIG TypeScript interfaces
// Generated directly from CDISC CDASH v1.3 and CDASHIG v2.3 schemas.
// DO NOT EDIT THIS FILE MANUALLY.

/**
 * Represents a variable definition in the CDASH Model v1.3
 */
export interface CDASHModelVariable {
  "Version": string;
  "Class": string;
  "Domain": string | null;
  "Variable Order": number | null;
  "CDASH Variable": string;
  "CDASH Variable Label": string;
  "DRAFT CDASH Definition"?: string | null;
  "Domain Specific"?: boolean | null;
  "Question Text"?: string | null;
  "Prompt"?: string | null;
  "Type": string;
  "SDTM Target"?: string | null;
  "Mapping Instructions"?: string | null;
  "Controlled Terminology Codelist Code"?: string | null;
  "Implementation Notes"?: string | null;
}

/**
 * Represents a variable definition in the CDASHIG v2.3
 */
export interface CDASHIGVariable {
  "Version": string;
  "Class": string;
  "Domain": string;
  "Data Collection Scenario/Implementation Option"?: string | null;
  "Variable Order": number | null;
  "CDASHIG Variable": string;
  "CDASHIG Variable Label": string;
  "DRAFT CDASHIG Definition"?: string | null;
  "Question Text"?: string | null;
  "Prompt"?: string | null;
  "Type": string;
  "CDASHIG Core": "HR" | "R" | "O" | string;
  "Case Report Form Completion Instructions"?: string | null;
  "SDTMIG Target"?: string | null;
  "Mapping Instructions"?: string | null;
  "CDISC CT Codelist Code(s), Subset Codes(s)"?: string | null;
  "CDISC CT Codelist Submission Values(s), Subset Submission Value(s)"?: string | null;
  "Implementation Notes"?: string | null;
}

/**
 * A union type of all valid variable names from both CDASH Model v1.3 and CDASHIG v2.3 schemas
 */
export type CdashVariableCode =
${variableCodes.map((code) => `  | ${JSON.stringify(code)}`).join("\n")};

/**
 * Strict interface definition for clinical fields used by form components.
 * Restricts input to valid CDASH field properties.
 */
export interface CdashField {
  id: string;
  label: string;
  type: "radio" | "choice_single" | "concept_code" | "text" | string;
  cdash?: CdashVariableCode | null;
  gridSpan?: number | string;
  options?: any[] | null;
  required?: boolean;
  [key: string]: any;
}
`;

  fs.writeFileSync(OUTPUT_FILE, tsCode, "utf8");
  console.log("Successfully generated " + OUTPUT_FILE);
}

function generateIndexDts() {
  console.log("Generating packages/ui/dist/index.d.ts declarations...");

  if (!fs.existsSync(DIST_DIR)) {
    fs.mkdirSync(DIST_DIR, { recursive: true });
  }

  const sharedPropsMap = loadSharedPropsMap();
  const components = walkVueComponents(COMPONENTS_DIR);
  const componentDeclarations = components
    .map(({ name, fullPath }) => {
      const { propsType, slotsType } = extractVueComponentSignature(
        fullPath,
        sharedPropsMap
      );
      if (slotsType) {
        return `export const ${name}: DefineSlotsComponent<\n  ${propsType},\n  ${slotsType}\n>;`;
      }
      return `export const ${name}: DefineComponent<\n  ${propsType},\n  {},\n  any\n>;`;
    })
    .join("\n\n");
  const augmentedSrcBarrelExports = components
    .map(({ name, fullPath }) => {
      const { propsType, slotsType } = extractVueComponentSignature(
        fullPath,
        sharedPropsMap
      );
      if (slotsType) {
        return `  export const ${name}: DefineSlotsComponent<\n  ${propsType},\n  ${slotsType}\n>;`;
      }
      return `  export const ${name}: DefineComponent<\n  ${propsType},\n  {},\n  any\n>;`;
    })
    .join("\n\n");

  const dtsCode = `// Auto-generated TypeScript declarations for packages/ui
// Generated by packages/ui/scripts/generate-types.js during build and test.
// DO NOT EDIT THIS FILE MANUALLY.

import type {
  DefineComponent,
  DefineSetupFnComponent,
  SlotsType,
} from "vue";
import type { CdashField } from "../src/types/cdash.js";

type DefineSlotsComponent<
  Props extends Record<string, any>,
  Slots extends Record<string, any>,
> = DefineSetupFnComponent<Props, Record<string, any>, SlotsType<Slots>>;

export * from "../src/types/cdash.js";

export interface ClinicalTabItem {
  id: string | number;
  label: string;
  badge?: string | number | null;
  disabled?: boolean;
  buttonId?: string;
  panelId?: string;
  buttonClass?: string | any[] | Record<string, boolean>;
  title?: string;
  dataTestid?: string;
}

export interface ClinicalDataTableColumn {
  key: string;
  label: string;
  sortable?: boolean;
}

export interface ClinicalToastItem {
  id: string | number;
  title?: string;
  message?: string;
  tone?: "info" | "success" | "warning" | "error" | "danger" | string;
  variant?: "info" | "success" | "warning" | "error" | "danger" | string;
  timeoutMs?: number;
  duration?: number;
  action?: {
    label: string;
    to: string | Record<string, any>;
  } | null;
}

export interface ClinicalBreadcrumbItem {
  id?: string;
  label: string;
  href?: string;
  current?: boolean;
  onSelect?: (item: ClinicalBreadcrumbItem, event: MouseEvent) => void;
}

export interface EntityLinkNodeRef {
  type: string;
  id: string;
  label?: string;
  studyId?: string | null;
  siteId?: string | null;
}

export const UI_COMPONENTS: Readonly<
  Record<string, DefineComponent<Record<string, any>, {}, any>>
>;

// Auto-discovered shared Vue components
${componentDeclarations}

// Shared focus, retry, and draft-guard composables
export interface FocusTrapOptions {
  isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  modal?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  onEscape?: (event: KeyboardEvent) => void;
  escapeDeactivates?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  initialFocus?:
    | import("vue").Ref<HTMLElement | null>
    | (() => HTMLElement | null)
    | string;
  restoreFocus?:
    | boolean
    | import("vue").Ref<HTMLElement | null>
    | (() => HTMLElement | null)
    | string;
  fallbackFocus?:
    | import("vue").Ref<HTMLElement | null>
    | (() => HTMLElement | null)
    | string
    | null;
}

export function useFocusTrap(
  targetRef: import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null),
  optionsOrIsOpen?:
    | FocusTrapOptions
    | import("vue").Ref<boolean>
    | (() => boolean)
    | boolean,
  options?: Omit<FocusTrapOptions, "isOpen">
): {
  activate: () => void;
  deactivate: () => void;
};

export function getTopActiveModalContainer(): HTMLElement | null;

export function hasActiveModalFocusTrap(options?: {
  excludeId?: string | null;
  excludeElement?: HTMLElement | null;
}): boolean;

export function getFocusableElements(
  container: HTMLElement | null | undefined
): HTMLElement[];

export function trapTabFocus(
  event: KeyboardEvent,
  container: HTMLElement | null | undefined
): boolean;

export type InlineFocusCandidate =
  | HTMLElement
  | string
  | import("vue").Ref<HTMLElement | null | undefined>
  | (() =>
      | HTMLElement
      | string
      | Array<HTMLElement | string | null | undefined>
      | null
      | undefined)
  | null
  | undefined;

export interface HandoffFocusOptions {
  container?:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null;
  fallback?: InlineFocusCandidate | InlineFocusCandidate[];
  scrollIfOffscreen?: boolean;
  preventScroll?: boolean;
  awaitNextTick?: boolean;
  includeDefaultFallbacks?: boolean;
  keepTrigger?: boolean;
}

export function handoffFocus(
  targets: InlineFocusCandidate | InlineFocusCandidate[],
  options?: HandoffFocusOptions
): Promise<HTMLElement | null>;

export interface InlineFocusHandoffOptions {
  isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  openTarget?: InlineFocusCandidate | InlineFocusCandidate[];
  closeFallback?: InlineFocusCandidate | InlineFocusCandidate[];
  fallback?: InlineFocusCandidate | InlineFocusCandidate[];
  container?:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null;
  scrollIfOffscreen?: boolean;
}

export interface InlineFocusHandoffControls {
  rememberTrigger: (
    explicitTrigger?: InlineFocusCandidate
  ) => HTMLElement | null;
  restoreTrigger: (
    fallbackTargets?: InlineFocusCandidate | InlineFocusCandidate[],
    options?: HandoffFocusOptions
  ) => Promise<HTMLElement | null>;
  clearTrigger: () => void;
  handoff: (
    targets: InlineFocusCandidate | InlineFocusCandidate[],
    options?: HandoffFocusOptions
  ) => Promise<HTMLElement | null>;
}

export function useInlineFocusHandoff(
  options?: InlineFocusHandoffOptions
): InlineFocusHandoffControls;

export function useEscapeClose(
  onCloseCallback: (event: KeyboardEvent) => void,
  options?: {
    isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
    target?: EventTarget | null;
  }
): {
  stop: () => void;
};

export function useAsyncRetry<T extends (...args: any[]) => any>(
  asyncFn?: T
): {
  isLoading: import("vue").Ref<boolean>;
  error: import("vue").Ref<any>;
  execute: (...args: any[]) => Promise<any>;
  retry: (...args: any[]) => Promise<any>;
  clearError: () => void;
};

export function registerDraftSource(source: {
  flush?: () => void | Promise<void>;
  hasDrafts?: () => boolean;
}): () => void;
export function flushRegisteredDraftSources(): Promise<void>;
export function flushRegisteredDraftSourcesSync(): void;
export function hasRegisteredUnsavedDrafts(): boolean;
export function resetDraftSourcesForTests(): void;
export function useSignOutGuard(options?: {
  onConfirmSignOut?: () => void | Promise<void>;
  flushExternalDrafts?: () => void | Promise<void>;
  hasExternalDrafts?: () => boolean;
}): {
  isConfirmOpen: import("vue").Ref<boolean>;
  isFlushing: import("vue").Ref<boolean>;
  requestSignOut: () => Promise<{ prompted: boolean; signedOut: boolean }>;
  confirmSignOut: () => Promise<void>;
  cancelSignOut: () => void;
};

// Legacy HTML builders and utility functions
export type DebouncedFunction<T extends (...args: any[]) => any> = ((
  ...args: Parameters<T>
) => void) & {
  flush: () => ReturnType<T> | undefined;
  cancel: () => void;
  pending: () => boolean;
};

export function debounce<T extends (...args: any[]) => any>(
  func: T,
  wait: number
): DebouncedFunction<T>;

export function createSrOnlyActionButton(options?: {
  label: string;
  srContext?: string;
  className?: string;
  dataId?: string | number;
  disabled?: boolean;
  ariaBusy?: boolean;
  type?: string;
  id?: string;
}): string;

export function createClinicalLookupInput(
  id: string,
  label: string,
  value?: string,
  status?: "none" | "loading" | "valid" | "invalid" | "degraded",
  statusMessage?: string
): string;
export function createConditionRowHTML(
  index: number,
  forms?: any[],
  fields?: any[],
  initialData?: any
): string;
export function createRuleEditorHTML(
  forms?: any[],
  fields?: any[],
  options?: any
): string;
export function serializeConditionsTree(
  conditions?: any[],
  matchOperator?: string
): any;
export function deserializeConditionsTree(tree: any): {
  conditions: any[];
  matchOperator: string;
};
export function createSoaBuilderMatrix(soaData: any): string;
export function createClinicalVisitMatrix(
  visitsOrSoa: any,
  forms?: any[]
): string;
export function initHoverDetection(): void;

// eConsent, signing, and OIDC session helpers
export function normalizeApprovedConsent(approved: any): any;
export function shapeComprehensionAnswers(answers: any): any;
export function interpretComprehensionResult(result: any): any;

export function canonicalSerialize(obj: any): string;
export function generateCanonicalSignature(
  obj: any,
  key: string | Uint8Array
): Promise<string>;
export function verifyCanonicalSignature(
  obj: any,
  sig: string,
  key: string | Uint8Array
): Promise<boolean>;
export function generateGatewaySignature(
  userId: string,
  roles: string,
  timestamp: string,
  version: string | undefined,
  changeReason: string | null | undefined,
  secret: string | Uint8Array
): Promise<string>;
export function verifyGatewaySignature(
  signature: string,
  userId: string,
  roles: string,
  timestamp: string,
  version: string | undefined,
  changeReason: string | null | undefined,
  secret: string | Uint8Array
): Promise<boolean>;
export function generateJwtHS256(
  payload: any,
  secret: string | Uint8Array
): Promise<string>;
export function sha256(message: string): Promise<string>;
export function validateField(value: any, rules: any): any;
export function buildLedgerBlock(
  index: number,
  timestamp: string,
  action: string,
  details: any,
  reason: string,
  prevHash: string
): Promise<any>;
export function encryptAESGCM(
  plaintext: string,
  key: Uint8Array
): Promise<{ ciphertext: string; iv: string; tag: string }>;
export function decryptAESGCM(
  ciphertext: string,
  iv: string,
  tag: string,
  key: Uint8Array
): Promise<string>;
export function deriveSessionKey(
  pin: string,
  salt: string
): Promise<Uint8Array>;
export function deriveKeyFromPIN(
  pin: string,
  salt: string | Uint8Array,
  iterations?: number
): Promise<Uint8Array>;

export const SEMANTIC_ACTION_PATTERN: RegExp;
export const STEP_UP_ACTION_MAX_LENGTH: number;
export const SEMANTIC_ACTIONS: Readonly<{
  CTMS_DOA_SIGN_OFF: "ctms.doa.sign_off";
  ECONSENT_CAPTURE: "econsent.consent.capture";
  ECONSENT_RECONSENT_SIGN: "econsent.reconsent.sign";
  CEC_CHAIR_ADJUDICATE: "execution.cec.chair_adjudicate";
  ETMF_DOCUMENT_SIGN_OFF: "etmf.document.sign_off";
  COLLECTION_SIGN: "collection.sign";
  SOA_APPROVE: "designer.soa.approve";
}>;
export function isValidStepUpAction(action: string | null | undefined): boolean;
export function isValidSemanticAction(
  semanticAction: string | null | undefined
): boolean;
export interface StepUpVerificationPayloadInput {
  username?: string | null;
  password: string;
  totp?: string | null;
  action: string;
  semanticAction?: string;
  semantic_action?: string;
  batchId?: string | null;
  batch_id?: string | null;
}
export interface StepUpVerificationPayload {
  username?: string;
  password: string;
  totp?: string;
  action: string;
  semantic_action: string;
  batch_id?: string;
}
export function buildStepUpVerificationPayload(
  input: StepUpVerificationPayloadInput
): StepUpVerificationPayload;

export const SIGNATURE_OTHER_REASON: "Other";
export const SIGNATURE_PASSWORD_INPUT_ATTRS: Readonly<{
  type: "password";
  inputmode: "text";
  autocomplete: "current-password";
  autocapitalize: "off";
  autocorrect: "off";
  spellcheck: "false";
}>;
export function requiresCustomSignatureReason(
  selectedReason: string | null | undefined
): boolean;
export function resolveSignatureReason(
  selectedReason: string | null | undefined,
  customReason?: string | null
): { valid: boolean; reason: string; error: string | null };
export function deriveSignerRoleFromClaims(
  claimsOrRoles: string[] | string | Record<string, any> | null | undefined,
  fallback?: string
): string;

export const DEFAULT_AUTH_RETURN_URL_KEY: string;
export function sanitizeReturnUrl(
  rawUrl: string | null | undefined,
  options?: { baseOrigin?: string }
): string | null;
export function saveAuthReturnUrl(
  url: string | null | undefined,
  options?: { storage?: Storage | null; key?: string; baseOrigin?: string }
): string | null;
export function peekAuthReturnUrl(options?: {
  storage?: Storage | null;
  key?: string;
  baseOrigin?: string;
}): string | null;
export function consumeAuthReturnUrl(options?: {
  storage?: Storage | null;
  key?: string;
  baseOrigin?: string;
}): string | null;
export function clearAuthReturnUrl(options?: {
  storage?: Storage | null;
  key?: string;
}): void;
export function resolveOidcRedirectUri(
  targetPathOrUrl?: string | null,
  options?: { origin?: string; basePath?: string }
): string;
export function isStepUpAuthFailure(
  url: string | URL | null | undefined,
  status: number,
  errorBody?: any
): boolean;

export interface OidcTokenRefreshResult {
  ok: boolean;
  refreshed: boolean;
  token: string | null;
  error?: any;
}

export interface OidcSessionCoordinator {
  attachKeycloak: (keycloakInstance: any) => any;
  resolveKeycloak: () => any;
  ensureFreshToken: (minValidity?: number) => Promise<OidcTokenRefreshResult>;
  refreshOnUnauthorized: (options?: {
    returnUrl?: string | null;
  }) => Promise<OidcTokenRefreshResult>;
  markSessionExpired: (options?: {
    reason?: string;
    error?: any;
    returnUrl?: string | null;
  }) => string | null;
  markAuthenticated: (token?: string | null) => void;
  beginVoluntaryLogout: () => void;
  reset: () => void;
  isSessionExpired: () => boolean;
  isRefreshing: () => boolean;
  saveReturnUrl: (url: string | null | undefined) => string | null;
  peekReturnUrl: () => string | null;
  consumeReturnUrl: () => string | null;
}

export function createOidcSessionCoordinator(options?: {
  getKeycloak?: () => any;
  minValiditySeconds?: number;
  forceRefreshMinValiditySeconds?: number;
  onTokenRefreshed?: (token: string | null, keycloak: any) => void;
  onSessionExpired?: (context: {
    reason: string;
    error?: any;
    returnUrl: string | null;
  }) => void;
  onAuthLogout?: () => void;
  getCurrentReturnUrl?: () => string | null;
  storage?: Storage | null;
  returnUrlKey?: string;
}): OidcSessionCoordinator;

// Caller scope claim helpers
export interface ScopeAssignmentClaims {
  assigned_studies?: unknown;
  assignedStudies?: unknown;
  study_ids?: unknown;
  studies?: unknown;
  study_id?: unknown;
  studyId?: unknown;
  assigned_sites?: unknown;
  assignedSites?: unknown;
  site_ids?: unknown;
  sites?: unknown;
  site_id?: unknown;
  siteId?: unknown;
  [key: string]: unknown;
}

export interface CallerScopeGuidanceContext {
  studyId?: string;
  siteId?: string;
  assignedStudies?: readonly string[];
  assignedSites?: readonly string[];
  hasAllStudiesScope?: boolean;
}

export function parseClaimStringList(raw: unknown): string[];
export function decodeJwtPayloadClaims(
  token: string | null | undefined
): Record<string, unknown> | null;
export function extractAssignedStudies(
  claimsOrToken?: Record<string, unknown> | string | null
): {
  assignedStudies: string[];
  hasAllStudiesScope: boolean;
};
export function extractAssignedSites(
  claimsOrToken?: Record<string, unknown> | string | null
): string[];
export function extractPrimaryStudyId(
  claimsOrToken?: Record<string, unknown> | string | null
): string | null;
export function extractPrimarySiteId(
  claimsOrToken?: Record<string, unknown> | string | null
): string | null;
export function isForbiddenScopeError(err: unknown): boolean;
export function formatScopeForbiddenGuidance(
  err: unknown,
  context?: CallerScopeGuidanceContext
): string;

// Module augmentation for internal relative imports before Vite barrel injection
declare module "../src/index.js" {
${augmentedSrcBarrelExports}
}
`;

  fs.writeFileSync(DIST_DTS_FILE, dtsCode, "utf8");
  console.log("Successfully generated " + DIST_DTS_FILE);
}

function generate() {
  generateCdashTypes();
  generateIndexDts();
}

generate();
