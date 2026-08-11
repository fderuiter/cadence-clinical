/**
 * AST Parser for front-end JS/TS files.
 * Uses @babel/parser strictly on AST nodes to extract structures and configurations,
 * completely avoiding false positives from comments or UI strings.
 *
 * @req:PRD-SYS-001
 */

const parser = require("../node_modules/.pnpm/@babel+parser@7.29.7/node_modules/@babel/parser");
const fs = require("fs");
const path = require("path");

function parseFile(filePath) {
  const code = fs.readFileSync(filePath, "utf8");
  const ext = path.extname(filePath);

  const plugins = ["typescript"];
  if (ext === ".tsx" || ext === ".jsx") {
    plugins.push("jsx");
  }

  const ast = parser.parse(code, {
    sourceType: "module",
    plugins: plugins,
  });

  const interfaces = {};
  const objects = {};

  // Traversal helper
  function traverse(node, cb) {
    cb(node);
    for (const key in node) {
      if (node[key] && typeof node[key] === "object") {
        if (Array.isArray(node[key])) {
          node[key].forEach((child) => {
            if (child && typeof child.type === "string") {
              traverse(child, cb);
            }
          });
        } else if (typeof node[key].type === "string") {
          traverse(node[key], cb);
        }
      }
    }
  }

  // Extract interfaces
  traverse(ast, (node) => {
    if (node.type === "TSInterfaceDeclaration") {
      const name = node.id.name;
      interfaces[name] = {};
      node.body.body.forEach((prop) => {
        if (prop.type === "TSPropertySignature") {
          const propName = prop.key.name || prop.key.value;
          const typeStr = code
            .slice(prop.typeAnnotation.start, prop.typeAnnotation.end)
            .replace(/^:\s*/, "")
            .trim();
          interfaces[name][propName] = typeStr;
        }
      });
    }
  });

  // Extract object expressions assigned to specific variables like "submission"
  traverse(ast, (node) => {
    if (
      node.type === "VariableDeclarator" &&
      node.id &&
      node.id.type === "Identifier" &&
      node.init &&
      node.init.type === "ObjectExpression"
    ) {
      const varName = node.id.name;
      objects[varName] = {};
      node.init.properties.forEach((prop) => {
        if (prop.type === "ObjectProperty") {
          const propName = prop.key.name || prop.key.value;
          // Determine type from value if possible
          let propType = "any";
          if (prop.value.type === "NumericLiteral") {
            propType = "number";
          } else if (prop.value.type === "StringLiteral") {
            propType = "string";
          } else if (prop.value.type === "BooleanLiteral") {
            propType = "boolean";
          } else if (prop.value.type === "NullLiteral") {
            propType = "null";
          } else if (prop.value.type === "Identifier") {
            // E.g., username, client_id, etc. Map typical names or treat as any/string
            if (
              propName.endsWith("_id") ||
              propName === "username" ||
              propName === "change_reason" ||
              propName === "status" ||
              propName === "device_timestamp"
            ) {
              propType = "string";
            } else if (propName === "sequence_number") {
              propType = "number";
            }
          }
          objects[varName][propName] = propType;
        }
      });
    }
  });

  return { interfaces, objects };
}

function main() {
  const args = process.argv.slice(2);
  if (args.length === 0) {
    console.error("Usage: node parse_frontend_ast.js <file-path>");
    process.exit(1);
  }

  const filePath = path.resolve(args[0]);
  try {
    const result = parseFile(filePath);
    console.log(JSON.stringify(result, null, 2));
  } catch (err) {
    console.error(`Error parsing ${filePath}:`, err.message);
    process.exit(1);
  }
}

if (require.main === module) {
  main();
}
