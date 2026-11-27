/**
 * Build-time AST Linter to check for duplicate field identifiers in clinical store configuration.
 */
let babelParser = null;
try {
  babelParser = require("@babel/parser");
} catch (err) {
  if (
    !err ||
    (err.code !== "MODULE_NOT_FOUND" && err.code !== "ERR_REQUIRE_ESM")
  ) {
    throw err;
  }
}
const fs = require("fs");
const path = require("path");

function tokenizeJsModule(code) {
  const tokens = [];
  let i = 0;
  const len = code.length;

  while (i < len) {
    const ch = code[i];

    if (/\s/.test(ch)) {
      i += 1;
      continue;
    }

    if (ch === "/" && code[i + 1] === "/") {
      i += 2;
      while (i < len && code[i] !== "\n" && code[i] !== "\r") {
        i += 1;
      }
      continue;
    }

    if (ch === "/" && code[i + 1] === "*") {
      i += 2;
      while (i < len && !(code[i] === "*" && code[i + 1] === "/")) {
        i += 1;
      }
      i += 2;
      continue;
    }

    if (ch === '"' || ch === "'") {
      const quote = ch;
      i += 1;
      let value = "";
      while (i < len && code[i] !== quote) {
        if (code[i] === "\\") {
          i += 1;
          if (i < len) {
            value += code[i];
            i += 1;
          }
        } else {
          value += code[i];
          i += 1;
        }
      }
      i += 1;
      tokens.push({ type: "string", value });
      continue;
    }

    if (ch === "`") {
      i += 1;
      let templateDepth = 0;
      while (i < len) {
        if (code[i] === "\\") {
          i += 2;
          continue;
        }
        if (templateDepth === 0 && code[i] === "`") {
          i += 1;
          break;
        }
        if (code[i] === "$" && code[i + 1] === "{") {
          templateDepth += 1;
          i += 2;
          continue;
        }
        if (templateDepth > 0 && code[i] === "{") {
          templateDepth += 1;
        } else if (templateDepth > 0 && code[i] === "}") {
          templateDepth -= 1;
        }
        i += 1;
      }
      tokens.push({ type: "template" });
      continue;
    }

    if (/[A-Za-z_$]/.test(ch)) {
      let start = i;
      i += 1;
      while (i < len && /[A-Za-z0-9_$]/.test(code[i])) {
        i += 1;
      }
      tokens.push({ type: "ident", value: code.slice(start, i) });
      continue;
    }

    if ("{}[]:,()".includes(ch)) {
      tokens.push({ type: "punct", value: ch });
      i += 1;
      continue;
    }

    tokens.push({ type: "other", value: ch });
    i += 1;
  }

  return tokens;
}

function checkDuplicatesWithFallback(code, filePath) {
  const tokens = tokenizeJsModule(code);
  let arrayStart = -1;

  for (let idx = 0; idx + 2 < tokens.length; idx += 1) {
    const keyTok = tokens[idx];
    const colonTok = tokens[idx + 1];
    const openTok = tokens[idx + 2];
    if (
      (keyTok.type === "ident" || keyTok.type === "string") &&
      keyTok.value === "ecrfFields" &&
      colonTok.type === "punct" &&
      colonTok.value === ":" &&
      openTok.type === "punct" &&
      openTok.value === "["
    ) {
      arrayStart = idx + 2;
      break;
    }
  }

  if (arrayStart === -1) {
    console.error(
      "[-] Could not find 'ecrfFields' array in the AST of " + filePath
    );
    process.exit(1);
  }

  const ids = [];
  const duplicates = [];
  let bracketDepth = 0;
  let braceDepth = 0;
  let currentElementId = null;
  let expectingPropertyKey = false;

  for (let idx = arrayStart; idx < tokens.length; idx += 1) {
    const tok = tokens[idx];
    if (tok.type === "punct") {
      if (tok.value === "[") {
        bracketDepth += 1;
        expectingPropertyKey = false;
        continue;
      }
      if (tok.value === "]") {
        bracketDepth -= 1;
        expectingPropertyKey = false;
        if (bracketDepth === 0) {
          break;
        }
        continue;
      }
      if (tok.value === "{") {
        braceDepth += 1;
        if (bracketDepth === 1 && braceDepth === 1) {
          currentElementId = null;
          expectingPropertyKey = true;
        } else {
          expectingPropertyKey = false;
        }
        continue;
      }
      if (tok.value === "}") {
        if (bracketDepth === 1 && braceDepth === 1) {
          if (currentElementId) {
            if (ids.includes(currentElementId)) {
              duplicates.push(currentElementId);
            } else {
              ids.push(currentElementId);
            }
          }
          currentElementId = null;
        }
        braceDepth -= 1;
        expectingPropertyKey = false;
        continue;
      }
      if (tok.value === "," && bracketDepth === 1 && braceDepth === 1) {
        expectingPropertyKey = true;
        continue;
      }
    }

    if (
      bracketDepth === 1 &&
      braceDepth === 1 &&
      expectingPropertyKey &&
      (tok.type === "ident" || tok.type === "string")
    ) {
      expectingPropertyKey = false;
      const nextColon = tokens[idx + 1];
      const nextValue = tokens[idx + 2];
      if (
        tok.value === "id" &&
        nextColon &&
        nextColon.type === "punct" &&
        nextColon.value === ":" &&
        nextValue &&
        nextValue.type === "string"
      ) {
        currentElementId = nextValue.value;
      }
    } else if (bracketDepth === 1 && braceDepth === 1) {
      expectingPropertyKey = false;
    }
  }

  return { ids, duplicates };
}

function checkDuplicates(filePath) {
  const code = fs.readFileSync(filePath, "utf8");
  if (!babelParser) {
    return checkDuplicatesWithFallback(code, filePath);
  }

  const ast = babelParser.parse(code, {
    sourceType: "module",
    plugins: ["typescript"],
  });

  let ecrfFieldsArray = null;

  function traverse(node) {
    if (!node) return;

    if (
      (node.type === "ObjectProperty" || node.type === "Property") &&
      node.key &&
      (node.key.name === "ecrfFields" || node.key.value === "ecrfFields") &&
      node.value &&
      node.value.type === "ArrayExpression"
    ) {
      ecrfFieldsArray = node.value;
      return;
    }

    for (const key in node) {
      if (node[key] && typeof node[key] === "object") {
        if (Array.isArray(node[key])) {
          node[key].forEach(traverse);
        } else if (typeof node[key].type === "string") {
          traverse(node[key]);
        }
      }
    }
  }

  traverse(ast);

  if (!ecrfFieldsArray) {
    console.error(
      "[-] Could not find 'ecrfFields' array in the AST of " + filePath
    );
    process.exit(1);
  }

  const ids = [];
  const duplicates = [];

  ecrfFieldsArray.elements.forEach((element) => {
    if (element && element.type === "ObjectExpression") {
      let idValue = null;
      element.properties.forEach((prop) => {
        if (
          (prop.type === "ObjectProperty" || prop.type === "Property") &&
          prop.key &&
          (prop.key.name === "id" || prop.key.value === "id") &&
          prop.value &&
          prop.value.type === "StringLiteral"
        ) {
          idValue = prop.value.value;
        }
      });
      if (idValue) {
        if (ids.includes(idValue)) {
          duplicates.push(idValue);
        } else {
          ids.push(idValue);
        }
      }
    }
  });

  return { ids, duplicates };
}

function main() {
  const defaultPath = path.resolve(
    __dirname,
    "../apps/web/src/stores/demo/clinicalEcrf.ts"
  );
  const filePath = process.argv[2]
    ? path.resolve(process.argv[2])
    : defaultPath;

  console.log(`[*] Parsing and linting ecrfFields in: ${filePath}`);
  try {
    const { ids, duplicates } = checkDuplicates(filePath);
    if (duplicates.length > 0) {
      console.error(
        `[!] Build Failure: Duplicate eCRF field identifiers detected: ${JSON.stringify(duplicates)}`
      );
      process.exit(1);
    }
    console.log(
      `[+] Success: Checked ${ids.length} eCRF fields. No duplicate identifiers detected.`
    );
    process.exit(0);
  } catch (err) {
    console.error(
      `[-] Error running check_duplicate_identifiers:`,
      err.message
    );
    process.exit(1);
  }
}

if (require.main === module) {
  main();
}

module.exports = { checkDuplicates, checkDuplicatesWithFallback };
