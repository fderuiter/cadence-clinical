import path from "node:path";
import {
  colorFunctionRegex,
  hexColorRegex,
  isStyleOrColorVAttribute,
  namedColorDeclRegex,
  offsetToLoc,
  stripCssCommentsPreservingNewlines,
  stripUrlFragmentsPreservingLength,
} from "../shared.mjs";

function findColorViolationsInCssText(text) {
  const violations = [];
  for (const match of text.matchAll(hexColorRegex)) {
    violations.push({ index: match.index, literal: match[0] });
  }
  for (const match of text.matchAll(colorFunctionRegex)) {
    violations.push({ index: match.index, literal: match[0] });
  }
  for (const match of text.matchAll(namedColorDeclRegex)) {
    const colorWord = match[1];
    const wordOffset = match[0]
      .toLowerCase()
      .lastIndexOf(colorWord.toLowerCase());
    violations.push({
      index: match.index + Math.max(0, wordOffset),
      literal: colorWord,
    });
  }
  return violations;
}

function collectColorLiteralsFromScriptString(rawText) {
  const cleaned = stripUrlFragmentsPreservingLength(rawText);
  const found = new Set();
  for (const m of cleaned.matchAll(hexColorRegex)) found.add(m[0]);
  for (const m of cleaned.matchAll(colorFunctionRegex)) found.add(m[0]);
  for (const m of cleaned.matchAll(namedColorDeclRegex)) found.add(m[1]);
  return [...found];
}

function isModuleSpecifierNode(node) {
  const parent = node.parent;
  if (!parent) return false;
  return (
    (parent.type === "ImportDeclaration" ||
      parent.type === "ExportNamedDeclaration" ||
      parent.type === "ExportAllDeclaration" ||
      parent.type === "ImportExpression") &&
    parent.source === node
  );
}

export const noInlineHexRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow hardcoded color literals (#hex, rgb/rgba/hsl/hsla, named white/black declarations) in Vue templates, <script> blocks, <style> blocks, JS/TS modules, and CSS stylesheets outside packages/ui/tokens.css theme blocks.",
      category: "Best Practices",
    },
    messages: {
      noHexInStyle:
        'Hardcoded color literal "{{hex}}" is prohibited. Use design system tokens defined in packages/ui/tokens.css (e.g. var(--semantic-color-...)).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();

    // 1. Plain .css files
    if (filename.endsWith(".css")) {
      return {
        Program() {
          let masked = stripUrlFragmentsPreservingLength(
            stripCssCommentsPreservingNewlines(sourceText)
          );
          if (filename.endsWith("/packages/ui/tokens.css")) {
            masked = masked.replace(
              /(?::root(?:\[[^\]]+\])?|\[data-theme="[^"]+"\]|\.dark)[^{]*\{[^}]*\}/g,
              (rootBlock) => rootBlock.replace(/[^\r\n]/g, " ")
            );
          }
          for (const v of findColorViolationsInCssText(masked)) {
            const loc = offsetToLoc(sourceText, v.index);
            context.report({
              loc,
              messageId: "noHexInStyle",
              data: { hex: v.literal },
            });
          }
        },
      };
    }

    const scriptColorVisitors = {
      Literal(node) {
        if (typeof node.value !== "string" || isModuleSpecifierNode(node)) {
          return;
        }
        const trimmed = node.value.trim().toLowerCase();
        const isCanvasNamedColor =
          (trimmed === "white" || trimmed === "black") &&
          node.parent?.type === "AssignmentExpression" &&
          node.parent.right === node &&
          node.parent.left?.type === "MemberExpression" &&
          (node.parent.left.property?.name === "fillStyle" ||
            node.parent.left.property?.name === "strokeStyle");
        const literals = collectColorLiteralsFromScriptString(node.value);
        if (isCanvasNamedColor) literals.push(node.value.trim());
        for (const hex of new Set(literals)) {
          context.report({
            node,
            messageId: "noHexInStyle",
            data: { hex },
          });
        }
      },
      TemplateElement(node) {
        const raw = node.value?.raw || "";
        if (!raw) return;
        for (const hex of collectColorLiteralsFromScriptString(raw)) {
          context.report({
            node,
            messageId: "noHexInStyle",
            data: { hex },
          });
        }
      },
    };

    // 2. Vue SFCs & JS/TS modules: check <template> style/presentation attributes, <script> AST literals, and <style> blocks
    const scriptAndStyleCheck = {
      ...scriptColorVisitors,
      ...(filename.endsWith(".vue")
        ? {
            "Program:exit"() {
              for (const styleMatch of sourceText.matchAll(
                /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
              )) {
                const contentStart = styleMatch.index + styleMatch[1].length;
                const masked = stripUrlFragmentsPreservingLength(
                  stripCssCommentsPreservingNewlines(styleMatch[2])
                );
                for (const v of findColorViolationsInCssText(masked)) {
                  const loc = offsetToLoc(sourceText, contentStart + v.index);
                  context.report({
                    loc,
                    messageId: "noHexInStyle",
                    data: { hex: v.literal },
                  });
                }
              }
            },
          }
        : {}),
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return scriptAndStyleCheck;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VAttribute(node) {
          if (isStyleOrColorVAttribute(node) && node.value) {
            const rawAttr = context.sourceCode.getText(node.value);
            const literals = collectColorLiteralsFromScriptString(rawAttr);
            const attrName = !node.directive
              ? node.key?.name
              : node.key?.argument?.name;
            if (
              (attrName === "fill" ||
                attrName === "stroke" ||
                attrName === "color") &&
              /^['"]?(?:white|black)['"]?$/i.test(rawAttr.trim())
            ) {
              literals.push(rawAttr.replace(/['"]/g, "").trim());
            }
            for (const hex of new Set(literals)) {
              context.report({
                node: node.value,
                messageId: "noHexInStyle",
                data: { hex },
              });
            }
          }
        },
      },
      scriptAndStyleCheck
    );
  },
};
