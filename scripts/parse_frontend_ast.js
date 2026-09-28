const fs = require("fs");
const path = require("path");

let parser = null;
try {
  parser = require("@babel/parser");
} catch (e) {
  try {
    const webNodeModules = path.resolve(
      __dirname,
      "../apps/web/node_modules/@babel/parser"
    );
    parser = require(webNodeModules);
  } catch (e2) {
    parser = null;
  }
}

function parseFileFallback(code) {
  const interfaces = {};
  const objects = {};

  // Extract interfaces via regex
  const interfaceRegex = /export\s+interface\s+(\w+)\s*\{([^}]+)\}/g;
  let match;
  while ((match = interfaceRegex.exec(code)) !== null) {
    const name = match[1];
    const body = match[2];
    interfaces[name] = {};
    const propRegex = /(\w+)\s*:\s*([^;]+);/g;
    let propMatch;
    while ((propMatch = propRegex.exec(body)) !== null) {
      interfaces[name][propMatch[1]] = propMatch[2].trim();
    }
  }

  // Extract object expressions assigned to variables
  const objectRegex = /(?:const|let|var)\s+(\w+)\s*=\s*\{([^}]+)\}/g;
  while ((match = objectRegex.exec(code)) !== null) {
    const varName = match[1];
    const body = match[2];
    objects[varName] = {};
    const propRegex = /(\w+)\s*:\s*([^,\n}]+)/g;
    let propMatch;
    while ((propMatch = propRegex.exec(body)) !== null) {
      const propName = propMatch[1];
      const val = propMatch[2].trim();
      let propType = "any";
      if (/^\d+(\.\d+)?$/.test(val)) {
        propType = "number";
      } else if (/^["'`]/.test(val)) {
        propType = "string";
      } else if (val === "true" || val === "false") {
        propType = "boolean";
      } else if (val === "null") {
        propType = "null";
      } else if (
        propName.endsWith("_id") ||
        propName === "username" ||
        propName === "change_reason" ||
        propName === "status" ||
        propName === "device_timestamp"
      ) {
        propType = "string";
      } else if (
        propName === "sequence_number" ||
        propName === "version_index"
      ) {
        propType = "number";
      }
      objects[varName][propName] = propType;
    }
  }

  return { interfaces, objects };
}

function parseFile(filePath) {
  const code = fs.readFileSync(filePath, "utf8");
  if (!parser) {
    return parseFileFallback(code);
  }
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
            } else if (propName === "sequence_number" || propName === "version_index") {
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
