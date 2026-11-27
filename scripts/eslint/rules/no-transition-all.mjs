import {
  createTypographyScanVisitors,
  maskCssStringsPreservingLength,
  offsetToLoc,
  stripCssCommentsPreservingNewlines,
} from "../shared.mjs";

const transitionAllDeclRegex =
  /(?:^|[;{\s])(transition(?:-property)?\s*:\s*[^;{}]*?\ball\b[^;{}]*)/gi;

const dynamicTransitionAllRegex =
  /\btransition(?:Property|-property)?\s*:\s*(['"`])[^'"`]*\ball\b[^'"`]*\1/gi;

function findTransitionAllInCss(cssText) {
  const noComments = stripCssCommentsPreservingNewlines(cssText);
  const clean = maskCssStringsPreservingLength(noComments);
  const violations = [];
  for (const match of clean.matchAll(transitionAllDeclRegex)) {
    const fullMatch = match[0];
    const decl = match[1];
    const leadLen = fullMatch.length - decl.length;
    const offset = match.index + leadLen;
    violations.push({
      offset,
      declaration: noComments.slice(offset, offset + decl.length).trim(),
    });
  }
  return violations;
}

export const noTransitionAllRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow transition: all and transition-property: all across CSS and Vue styles/templates so :focus-visible outlines and box-shadow halos render with >= 3:1 contrast at t=0ms (WCAG 2.2 SC 2.4.7 / SC 1.4.11).",
      category: "Accessibility",
    },
    messages: {
      noTransitionAll:
        '"{{declaration}}" is prohibited because transitioning all properties animates focus outlines and halos from 0 contrast at t=0ms. Transition explicit non-focus properties instead (e.g., background-color, border-color, color, opacity, transform).',
    },
  },
  create(context) {
    return createTypographyScanVisitors(context, {
      onCssBlock(cleanCss, baseOffset, sourceText) {
        const rawBlock = sourceText.slice(
          baseOffset,
          baseOffset + cleanCss.length
        );
        for (const v of findTransitionAllInCss(rawBlock)) {
          context.report({
            loc: offsetToLoc(sourceText, baseOffset + v.offset),
            messageId: "noTransitionAll",
            data: { declaration: v.declaration },
          });
        }
      },
      onVueStyleAttr(valueNode, rawAttr) {
        const attrNode = valueNode.parent;
        const isStaticStyle =
          !attrNode?.directive && attrNode?.key?.name === "style";
        const isDynamicStyle =
          Boolean(attrNode?.directive) &&
          attrNode?.key?.argument?.name === "style";
        if (isStaticStyle) {
          for (const v of findTransitionAllInCss(valueNode.value ?? "")) {
            context.report({
              node: valueNode,
              messageId: "noTransitionAll",
              data: { declaration: v.declaration },
            });
          }
          return;
        }
        if (!isDynamicStyle) return;
        const reported = new Set();
        for (const match of rawAttr.matchAll(dynamicTransitionAllRegex)) {
          const decl = match[0].trim();
          reported.add(decl);
          context.report({
            node: valueNode,
            messageId: "noTransitionAll",
            data: { declaration: decl },
          });
        }
        for (const v of findTransitionAllInCss(rawAttr)) {
          if (!reported.has(v.declaration)) {
            context.report({
              node: valueNode,
              messageId: "noTransitionAll",
              data: { declaration: v.declaration },
            });
          }
        }
      },
      onJsProgram() {},
    });
  },
};
