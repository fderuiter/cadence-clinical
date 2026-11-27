import path from "node:path";
import { isDisallowedRawDrawerOverlay } from "../../eslint-drawer-overlay-guard.mjs";
import { LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST } from "../allowlists/index.mjs";
import { getStaticAttrValue, hasStaticOrBoundAttr } from "../shared.mjs";

const overlayClassTokenRegex =
  /(?:^|[\s"'`[{])([a-z0-9_-]*(?:modal|drawer|dialog)-(?:overlay|backdrop|mask|bg)|overlay-backdrop)(?:$|[\s"'`\]}:,])/i;

function getElementOverlayClassToken(elementNode, sourceCode) {
  if (!elementNode?.startTag?.attributes) return null;
  for (const attr of elementNode.startTag.attributes) {
    const isStaticClass =
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === "class";
    const isDynamicClass =
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      attr.key.argument?.name === "class";
    if ((isStaticClass || isDynamicClass) && attr.value) {
      const rawVal = sourceCode.getText(attr.value);
      const match = rawVal.match(overlayClassTokenRegex);
      if (match) return match[1];
    }
  }
  return null;
}

function hasClickDirective(elementNode) {
  if (!elementNode?.startTag?.attributes) return false;
  return elementNode.startTag.attributes.some(
    (attr) =>
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "on" &&
      attr.key.argument?.name === "click"
  );
}

function isDialogVElement(node) {
  if (!node || node.type !== "VElement") return false;
  const role = getStaticAttrValue(node, "role");
  if (role !== "dialog" && role !== "alertdialog") return false;
  return hasStaticOrBoundAttr(node, "aria-modal");
}

function hasDialogDescendantOrSelf(node) {
  if (!node || node.type !== "VElement") return false;
  if (isDialogVElement(node)) return true;
  for (const child of node.children || []) {
    if (child.type === "VElement" && hasDialogDescendantOrSelf(child)) {
      return true;
    }
  }
  return false;
}

function hasDialogSibling(node) {
  const siblings = node?.parent?.children || [];
  return siblings.some(
    (sib) => sib !== node && sib.type === "VElement" && isDialogVElement(sib)
  );
}

const rawModalOverlayClassRegex =
  /(?:modal|dialog)-(?:overlay|backdrop|mask|bg)|overlay-backdrop/i;

export const noRawOverlaysRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow raw modal/drawer overlay divs and vuejs-accessibility backdrop suppressions outside packages/ui primitives.",
      category: "Accessibility",
    },
    messages: {
      noRawOverlay:
        'Raw overlay container "{{className}}" is prohibited. Migrate to ClinicalModal or ClinicalDrawer from "ui" (ADR-2545 / ADR-2653 / ADR-2674).',
      noBackdropSuppression:
        'Suppressing vuejs-accessibility rules on overlay/backdrop elements is prohibited. Migrate to ClinicalModal or ClinicalDrawer (or use role="presentation" on focus-trapped backdrops).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    if (
      !filename.endsWith(".vue") ||
      filename.includes("/packages/ui/src/components/")
    ) {
      return {};
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    const webSrcMarker = "/apps/web/src/";
    const webSrcIdx = filename.lastIndexOf(webSrcMarker);
    const relWebSrcPath =
      webSrcIdx >= 0 ? filename.slice(webSrcIdx + webSrcMarker.length) : null;

    const sourceText = context.sourceCode.getText();
    const scriptContent = Array.from(
      sourceText.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi),
      (m) => m[1]
    ).join("\n");
    const importsFocusTrap = /\buseFocusTrap\b/.test(scriptContent);

    const nativeOverlayElements = [];

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          const rawTag = node.rawName || node.name;
          if (!["div", "section", "aside"].includes(rawTag)) return;

          const overlayClass = getElementOverlayClassToken(
            node,
            context.sourceCode
          );
          if (!overlayClass) return;

          nativeOverlayElements.push(node);

          const isDisallowedWebRawModalOverlay =
            relWebSrcPath !== null &&
            rawModalOverlayClassRegex.test(overlayClass) &&
            !LEGACY_WEB_RAW_MODAL_OVERLAY_ALLOWLIST.has(relWebSrcPath);

          if (
            isDisallowedRawDrawerOverlay(filename, overlayClass) ||
            isDisallowedWebRawModalOverlay
          ) {
            context.report({
              node: node.startTag,
              messageId: "noRawOverlay",
              data: { className: overlayClass },
            });
            return;
          }

          const hasGuardedDialog =
            importsFocusTrap &&
            (hasDialogDescendantOrSelf(node) || hasDialogSibling(node));
          const role = getStaticAttrValue(node, "role");
          const validBackdropInteraction =
            isDialogVElement(node) ||
            !hasClickDirective(node) ||
            role === "presentation" ||
            role === "none";

          if (!hasGuardedDialog || !validBackdropInteraction) {
            context.report({
              node: node.startTag,
              messageId: "noRawOverlay",
              data: { className: overlayClass },
            });
          }
        },
      },
      {
        "Program:exit"(programNode) {
          const templateComments = programNode.templateBody?.comments || [];
          for (const comment of templateComments) {
            const text = comment.value || "";
            if (
              !/eslint-disable/.test(text) ||
              !/(?:vuejs-accessibility\/click-events-have-key-events|vuejs-accessibility\/no-static-element-interactions)/.test(
                text
              )
            ) {
              continue;
            }
            const commentEndLine = comment.loc.end.line;
            const targetsOverlay = nativeOverlayElements.some((el) => {
              const startLine = el.startTag.loc.start.line;
              return (
                startLine >= commentEndLine && startLine <= commentEndLine + 3
              );
            });
            if (targetsOverlay) {
              context.report({
                loc: comment.loc,
                messageId: "noBackdropSuppression",
              });
            }
          }
        },
      }
    );
  },
};
