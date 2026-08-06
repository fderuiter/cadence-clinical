#!/usr/bin/env node
/**
 * JS/TS AST block analyzer.
 * Parses a JavaScript or TypeScript file and outputs the line ranges of top-level nodes as JSON.
 */

const fs = require("fs");
const path = require("path");

function getBabelParser() {
  const pnpmDir = path.join(__dirname, "../node_modules/.pnpm");
  if (fs.existsSync(pnpmDir)) {
    const files = fs.readdirSync(pnpmDir);
    const babelParserDir = files.find((f) => f.startsWith("@babel+parser@"));
    if (babelParserDir) {
      const parserPath = path.join(
        pnpmDir,
        babelParserDir,
        "node_modules/@babel/parser/lib/index.js"
      );
      if (fs.existsSync(parserPath)) {
        return require(parserPath);
      }
    }
  }
  try {
    return require("@babel/parser");
  } catch (e) {
    const repoRoot = path.dirname(__dirname);
    const webNodeModules = path.join(
      repoRoot,
      "apps/web/node_modules/@babel/parser/lib/index.js"
    );
    if (fs.existsSync(webNodeModules)) {
      return require(webNodeModules);
    }
    throw new Error("Could not find @babel/parser");
  }
}

function getNodeKey(node) {
  if (node.type === "ImportDeclaration") {
    return "imports";
  }
  if (node.type === "FunctionDeclaration" && node.id) {
    return `func:${node.id.name}`;
  }
  if (node.type === "ClassDeclaration" && node.id) {
    return `class:${node.id.name}`;
  }
  if (node.type === "ExportNamedDeclaration" && node.declaration) {
    const decl = node.declaration;
    if (decl.type === "FunctionDeclaration" && decl.id) {
      return `func:${decl.id.name}`;
    }
    if (decl.type === "ClassDeclaration" && decl.id) {
      return `class:${decl.id.name}`;
    }
    if (
      decl.type === "VariableDeclaration" &&
      decl.declarations &&
      decl.declarations[0] &&
      decl.declarations[0].id
    ) {
      return `var:${decl.declarations[0].id.name}`;
    }
  }
  if (
    node.type === "VariableDeclaration" &&
    node.declarations &&
    node.declarations[0] &&
    node.declarations[0].id
  ) {
    return `var:${node.declarations[0].id.name}`;
  }
  if (node.type === "ExportDefaultDeclaration") {
    return "export-default";
  }
  return null;
}

function main() {
  const args = process.argv.slice(2);
  if (args.length === 0) {
    console.error("Usage: node parse_js_blocks.js <file_path>");
    process.exit(1);
  }

  const filePath = args[0];
  if (!fs.existsSync(filePath)) {
    console.error(`File not found: ${filePath}`);
    process.exit(1);
  }

  const source = fs.readFileSync(filePath, "utf8");
  let babel;
  try {
    babel = getBabelParser();
  } catch (e) {
    console.error(e.message);
    process.exit(1);
  }

  try {
    const ast = babel.parse(source, {
      sourceType: "module",
      plugins: ["typescript", "jsx", "decorators-legacy", "classProperties"],
    });

    const blocks = [];
    for (const item of ast.program.body) {
      const key = getNodeKey(item);
      if (key && item.loc) {
        blocks.push({
          key,
          start: item.loc.start.line,
          end: item.loc.end.line,
        });
      }
    }

    console.log(JSON.stringify(blocks, null, 2));
  } catch (err) {
    console.error(`Parsing error in ${filePath}: ${err.message}`);
    process.exit(1);
  }
}

main();
