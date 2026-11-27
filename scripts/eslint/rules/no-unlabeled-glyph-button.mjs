import {
  extractStaticExpressionStrings,
  getStaticAttrValue,
  hasStaticOrBoundAttr,
  isAriaHiddenElement,
} from "../shared.mjs";

function hasNonEmptyAriaLabelOrLabelledBy(elementNode) {
  if (!elementNode?.startTag?.attributes) return false;
  for (const attr of elementNode.startTag.attributes) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      (attr.key.name === "aria-label" || attr.key.name === "aria-labelledby")
    ) {
      if ((attr.value?.value ?? "").trim().length > 0) return true;
    }
    if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      (attr.key.argument?.name === "aria-label" ||
        attr.key.argument?.name === "aria-labelledby")
    ) {
      const expr = attr.value?.expression;
      if (!expr) continue;
      if (expr.type === "Literal") {
        if (String(expr.value ?? "").trim().length > 0) return true;
      } else if (
        expr.type === "TemplateLiteral" &&
        expr.expressions.length === 0
      ) {
        const raw = expr.quasis.map((q) => q.value.cooked ?? "").join("");
        if (raw.trim().length > 0) return true;
      } else {
        return true;
      }
    }
  }
  return false;
}

function isCreateButtonCall(node) {
  if (!node || node.type !== "CallExpression") return false;
  const callee = node.callee;
  const isCreateEl =
    callee?.type === "MemberExpression" &&
    !callee.computed &&
    callee.property?.type === "Identifier" &&
    callee.property.name === "createElement";
  if (!isCreateEl || node.arguments.length === 0) return false;
  const firstArg = node.arguments[0];
  return (
    firstArg?.type === "Literal" &&
    typeof firstArg.value === "string" &&
    firstArg.value.toLowerCase() === "button"
  );
}

function getStaticJsString(node) {
  if (!node) return null;
  if (node.type === "Literal" && typeof node.value === "string") {
    return node.value;
  }
  if (node.type === "TemplateLiteral" && node.expressions.length === 0) {
    return node.quasis.map((q) => q.value.cooked ?? "").join("");
  }
  return null;
}

export const noUnlabeledGlyphButtonRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow glyph-only or empty <button> elements without a programmatic accessible name via aria-label or aria-labelledby (WCAG 2.2 SC 4.1.2).",
      category: "Accessibility",
    },
    messages: {
      unlabeledGlyphButton:
        'Glyph or icon-only <button> lacks an accessible name (WCAG 2.2 SC 4.1.2 / Failure F68). Use ClinicalIconButton from "ui" or provide a non-empty aria-label / aria-labelledby.',
    },
  },
  create(context) {
    const buttonDecls = new Map();

    const jsVisitor = {
      VariableDeclarator(node) {
        if (node.id?.type === "Identifier" && isCreateButtonCall(node.init)) {
          buttonDecls.set(node.id.name, {
            node: node.init,
            assignedGlyphOnly: false,
            hasAccessibleName: false,
          });
        }
      },
      AssignmentExpression(node) {
        if (
          node.left?.type !== "MemberExpression" ||
          node.left.computed ||
          node.left.object?.type !== "Identifier" ||
          node.left.property?.type !== "Identifier"
        ) {
          return;
        }
        const entry = buttonDecls.get(node.left.object.name);
        if (!entry) return;
        const prop = node.left.property.name;
        if (prop === "ariaLabel") {
          const val = getStaticJsString(node.right);
          if (val === null || val.trim().length > 0) {
            entry.hasAccessibleName = true;
          }
          return;
        }
        if (
          prop === "innerText" ||
          prop === "textContent" ||
          prop === "innerHTML"
        ) {
          const val = getStaticJsString(node.right);
          if (val !== null) {
            const stripped =
              prop === "innerHTML" ? val.replace(/<[^>]+>/g, " ") : val;
            if (/[\p{L}\p{N}]/u.test(stripped)) {
              entry.hasAccessibleName = true;
            } else {
              entry.assignedGlyphOnly = true;
            }
          } else {
            entry.hasAccessibleName = true;
          }
        }
      },
      CallExpression(node) {
        if (
          node.callee?.type !== "MemberExpression" ||
          node.callee.computed ||
          node.callee.object?.type !== "Identifier" ||
          node.callee.property?.type !== "Identifier"
        ) {
          return;
        }
        const entry = buttonDecls.get(node.callee.object.name);
        if (!entry) return;
        if (
          node.callee.property.name === "setAttribute" &&
          node.arguments.length >= 2
        ) {
          const attrName = getStaticJsString(node.arguments[0]);
          if (attrName === "aria-label" || attrName === "aria-labelledby") {
            const attrVal = getStaticJsString(node.arguments[1]);
            if (attrVal === null || attrVal.trim().length > 0) {
              entry.hasAccessibleName = true;
            }
          }
        }
      },
      "Program:exit"() {
        for (const entry of buttonDecls.values()) {
          if (entry.assignedGlyphOnly && !entry.hasAccessibleName) {
            context.report({
              node: entry.node,
              messageId: "unlabeledGlyphButton",
            });
          }
        }
      },
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return jsVisitor;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          const rawTag = node.rawName || node.name;
          if (rawTag !== "button") return;
          if (hasNonEmptyAriaLabelOrLabelledBy(node)) return;

          let staticText = "";
          const exprStrings = [];
          let hasDynamicText = false;
          let hasSlotOrComponent = false;

          function collectButtonContent(children) {
            for (const child of children || []) {
              if (child.type === "VText") {
                staticText += child.value;
              } else if (child.type === "VExpressionContainer") {
                const info = extractStaticExpressionStrings(child.expression);
                if (info.allLiteral) {
                  exprStrings.push(...info.strings);
                } else {
                  hasDynamicText = true;
                }
              } else if (child.type === "VElement") {
                const childTag = child.rawName || child.name;
                if (childTag === "slot") {
                  if (!child.children || child.children.length === 0) {
                    hasSlotOrComponent = true;
                  } else {
                    collectButtonContent(child.children);
                  }
                } else if (
                  /^[A-Z]/.test(childTag) ||
                  childTag.includes("-") ||
                  childTag === "component"
                ) {
                  hasSlotOrComponent = true;
                } else if (childTag === "img") {
                  const alt = getStaticAttrValue(child, "alt");
                  if (alt && /[\p{L}\p{N}]/u.test(alt)) {
                    staticText += ` ${alt}`;
                  } else if (
                    hasStaticOrBoundAttr(child, "alt") &&
                    alt === null
                  ) {
                    hasDynamicText = true;
                  }
                } else if (!isAriaHiddenElement(child)) {
                  collectButtonContent(child.children);
                }
              }
            }
          }

          collectButtonContent(node.children);
          if (hasDynamicText || hasSlotOrComponent) return;

          const combined = `${staticText} ${exprStrings.join(" ")}`.trim();
          if (!/[\p{L}\p{N}]/u.test(combined)) {
            context.report({
              node: node.startTag,
              messageId: "unlabeledGlyphButton",
            });
          }
        },
      },
      jsVisitor
    );
  },
};

function hasAccessibleButtonLabelAttr(vElement) {
  for (const attr of vElement.startTag.attributes || []) {
    if (!attr.directive) {
      const name = attr.key?.name;
      if (
        (name === "aria-label" || name === "aria-labelledby") &&
        attr.value?.value &&
        attr.value.value.trim().length > 0
      ) {
        return true;
      }
    } else if (
      attr.key?.name?.name === "bind" &&
      (attr.key?.argument?.name === "aria-label" ||
        attr.key?.argument?.name === "aria-labelledby") &&
      attr.value?.expression
    ) {
      return true;
    }
  }
  return false;
}

function subtreeHasReadableText(node) {
  for (const child of node.children || []) {
    if (child.type === "VText" && /[\p{L}\p{N}]/u.test(child.value || "")) {
      return true;
    }
    if (child.type === "VExpressionContainer" && child.expression) {
      return true;
    }
    if (child.type === "VElement") {
      const raw = child.rawName || child.name || "";
      const isIconComponent =
        raw === "ClinicalIcon" ||
        raw === "clinical-icon" ||
        raw === "svg" ||
        (raw.length > 0 &&
          /^[A-Z]/.test(raw) &&
          (child.children || []).length === 0);
      if (!isIconComponent && subtreeHasReadableText(child)) {
        return true;
      }
    }
  }
  return false;
}

function subtreeHasIconComponent(node) {
  for (const child of node.children || []) {
    if (child.type === "VElement") {
      const raw = child.rawName || child.name || "";
      if (
        raw === "ClinicalIcon" ||
        raw === "clinical-icon" ||
        (/^[A-Z]/.test(raw) &&
          raw !== "RouterLink" &&
          raw !== "Teleport" &&
          raw !== "Transition")
      ) {
        return true;
      }
      if (subtreeHasIconComponent(child)) return true;
    }
  }
  return false;
}

export const enhancedNoUnlabeledGlyphButtonRule = {
  ...noUnlabeledGlyphButtonRule,
  create(context) {
    const reportedStarts = new Set();
    const wrappedContext = Object.create(context, {
      report: {
        value(descriptor) {
          if (descriptor?.node?.range) {
            reportedStarts.add(descriptor.node.range[0]);
          }
          return context.report(descriptor);
        },
      },
    });
    const baseListeners = noUnlabeledGlyphButtonRule.create(wrappedContext);
    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return baseListeners;
    }
    const defaultMsgId =
      Object.keys(noUnlabeledGlyphButtonRule.meta?.messages || {})[0] ||
      "unlabeledGlyphButton";
    const extraVisitor = parserServices.defineTemplateBodyVisitor({
      VElement(node) {
        const rawTag = node.rawName || node.name;
        if (rawTag !== "button") return;
        if (reportedStarts.has(node.startTag.range[0])) return;
        if (hasAccessibleButtonLabelAttr(node)) return;
        if (subtreeHasReadableText(node)) return;
        if (subtreeHasIconComponent(node)) {
          context.report({
            node: node.startTag,
            messageId: defaultMsgId,
            data: { glyph: "<ClinicalIcon>" },
          });
        }
      },
    });
    const merged = { ...baseListeners };
    for (const [k, fn] of Object.entries(extraVisitor)) {
      const prev = merged[k];
      merged[k] = prev
        ? function (...args) {
            prev.apply(this, args);
            fn.apply(this, args);
          }
        : fn;
    }
    return merged;
  },
};
