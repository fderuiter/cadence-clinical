/**
 * Build-time AST Linter to check for duplicate field identifiers in clinical store configuration.
 */
const parser = require("@babel/parser");
const fs = require("fs");
const path = require("path");

function checkDuplicates(filePath) {
  const code = fs.readFileSync(filePath, "utf8");
  const ast = parser.parse(code, {
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
    console.error("[-] Could not find 'ecrfFields' array in the AST of " + filePath);
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
  const defaultPath = path.resolve(__dirname, "../apps/web/src/stores/clinical.ts");
  const filePath = process.argv[2] ? path.resolve(process.argv[2]) : defaultPath;

  console.log(`[*] Parsing and linting ecrfFields in: ${filePath}`);
  try {
    const { ids, duplicates } = checkDuplicates(filePath);
    if (duplicates.length > 0) {
      console.error(`[!] Build Failure: Duplicate eCRF field identifiers detected: ${JSON.stringify(duplicates)}`);
      process.exit(1);
    }
    console.log(`[+] Success: Checked ${ids.length} eCRF fields. No duplicate identifiers detected.`);
    process.exit(0);
  } catch (err) {
    console.error(`[-] Error running check_duplicate_identifiers:`, err.message);
    process.exit(1);
  }
}

if (require.main === module) {
  main();
}
