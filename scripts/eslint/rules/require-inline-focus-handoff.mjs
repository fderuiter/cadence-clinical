import path from "node:path";
import { extractClassNamesFromJsExpr } from "../shared.mjs";

const FOCUS_HANDOFF_CALLEES = new Set([
  "handoffFocus",
  "handoffRetryFocus",
  "useInlineFocusHandoff",
  "useRetryFocusHandoff",
  "retryWithFocus",
]);

function astContainsFocusHandoffCall(rootNode) {
  if (!rootNode || typeof rootNode !== "object") return false;
  const visited = new WeakSet();
  const stack = [rootNode];
  while (stack.length > 0) {
    const node = stack.pop();
    if (!node || typeof node !== "object" || visited.has(node)) continue;
    visited.add(node);
    if (node.type === "CallExpression") {
      const callee = node.callee;
      if (
        callee?.type === "Identifier" &&
        FOCUS_HANDOFF_CALLEES.has(callee.name)
      ) {
        return true;
      }
      if (
        callee?.type === "MemberExpression" &&
        callee.property?.type === "Identifier" &&
        (FOCUS_HANDOFF_CALLEES.has(callee.property.name) ||
          callee.property.name === "handoff" ||
          callee.property.name === "restoreTrigger")
      ) {
        return true;
      }
    }
    for (const [key, value] of Object.entries(node)) {
      if (key === "parent" || !value) continue;
      if (Array.isArray(value)) {
        for (const item of value) {
          if (item && typeof item === "object") stack.push(item);
        }
      } else if (typeof value === "object") {
        stack.push(value);
      }
    }
  }
  return false;
}

function vElementHasClassToken(vElement, targetToken) {
  for (const attr of vElement.startTag?.attributes || []) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === "class" &&
      typeof attr.value?.value === "string"
    ) {
      if (attr.value.value.split(/\s+/).includes(targetToken)) {
        return true;
      }
    } else if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      attr.key.argument?.type === "VIdentifier" &&
      attr.key.argument.name === "class" &&
      attr.value?.expression
    ) {
      const tokens = new Set();
      extractClassNamesFromJsExpr(attr.value.expression, tokens);
      if (tokens.has(targetToken)) {
        return true;
      }
    }
  }
  return false;
}

function vElementHasAttrValue(vElement, attrName, attrVal) {
  for (const attr of vElement.startTag?.attributes || []) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === attrName &&
      attr.value?.value === attrVal
    ) {
      return true;
    }
  }
  return false;
}

function vElementDirectlyEmitsRetryOnClick(vElement) {
  for (const attr of vElement.startTag?.attributes || []) {
    if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "on" &&
      attr.key.argument?.type === "VIdentifier" &&
      attr.key.argument.name === "click" &&
      attr.value?.expression
    ) {
      let expr = attr.value.expression;
      if (
        expr.type === "VOnExpression" &&
        Array.isArray(expr.body) &&
        expr.body.length === 1 &&
        expr.body[0].type === "ExpressionStatement"
      ) {
        expr = expr.body[0].expression;
      }
      if (
        expr?.type === "CallExpression" &&
        expr.callee?.type === "Identifier" &&
        (expr.callee.name === "$emit" || expr.callee.name === "emit") &&
        expr.arguments?.[0]?.type === "Literal" &&
        expr.arguments[0].value === "retry"
      ) {
        return true;
      }
    }
  }
  return false;
}

export const requireInlineFocusHandoffRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Require explicit focus handoff (handoffFocus / handoffRetryFocus) in dropzone remove controls, error alert retry controls, and subject-portal retry* feature handlers",
    },
    schema: [],
    messages: {
      missingDropzoneFocusHandoff:
        "Dropzone remove button (.clinical-dropzone-remove-btn) unmounts on selection removal and must hand focus off via handoffFocus or useInlineFocusHandoff (WCAG 2.2 SC 2.4.3).",
      missingErrorAlertRetryHandoff:
        "Error alert retry button (data-testid=\"canvas-error-retry\") unmounts on retry and must coordinate focus recovery via handoffRetryFocus, useRetryFocusHandoff, or handoffFocus instead of raw emit('retry') (WCAG 2.2 SC 2.4.3).",
      missingPortalRetryFocusHandoff:
        'Subject-portal retry handler "{{fnName}}" hides its failure container on retry and must restore focus via handoffRetryFocus or handoffFocus from "ui" (WCAG 2.2 SC 2.4.3).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");

    if (filename.endsWith(".vue")) {
      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) {
        return {};
      }
      const dropzoneRemoveNodes = [];
      const errorAlertRetryNodes = [];
      let scriptHasFocusHandoff = false;

      return parserServices.defineTemplateBodyVisitor(
        {
          VElement(node) {
            const rawTag = (node.rawName || node.name || "").toLowerCase();
            if (rawTag !== "button" && rawTag !== "clinicaliconbutton") {
              return;
            }
            if (vElementHasClassToken(node, "clinical-dropzone-remove-btn")) {
              dropzoneRemoveNodes.push(node);
            }
            if (
              vElementHasAttrValue(node, "data-testid", "canvas-error-retry")
            ) {
              errorAlertRetryNodes.push(node);
            }
          },
        },
        {
          Program(programNode) {
            scriptHasFocusHandoff = astContainsFocusHandoffCall(programNode);
          },
          "Program:exit"() {
            for (const removeNode of dropzoneRemoveNodes) {
              if (!scriptHasFocusHandoff) {
                context.report({
                  node: removeNode,
                  messageId: "missingDropzoneFocusHandoff",
                });
              }
            }
            for (const retryNode of errorAlertRetryNodes) {
              if (
                !scriptHasFocusHandoff ||
                vElementDirectlyEmitsRetryOnClick(retryNode)
              ) {
                context.report({
                  node: retryNode,
                  messageId: "missingErrorAlertRetryHandoff",
                });
              }
            }
          },
        }
      );
    }

    if (
      filename.includes("apps/subject-portal/src/features/") &&
      filename.endsWith(".js")
    ) {
      return {
        "ExportNamedDeclaration > FunctionDeclaration"(fnNode) {
          const fnName = fnNode.id?.name || "";
          if (!/^retry[A-Z]/.test(fnName)) return;
          if (!astContainsFocusHandoffCall(fnNode.body)) {
            context.report({
              node: fnNode.id || fnNode,
              messageId: "missingPortalRetryFocusHandoff",
              data: { fnName },
            });
          }
        },
      };
    }

    return {};
  },
};
