import path from "node:path";
import { LEGACY_CONDITIONAL_STATUS_MOUNT_ALLOWLIST } from "../allowlists/index.mjs";
import { extractStaticExpressionStrings, repoRoot } from "../shared.mjs";

const ERROR_TOKEN_REGEX =
  /(?:^|[-_\s'"`])(?:[a-z0-9]*[-_])?(?:error|errors|err|invalid|failure)(?:[-_][a-z0-9]+)*(?:$|[-_\s'"`])/i;

function getAttrNode(elementNode, attrName) {
  if (!elementNode?.startTag?.attributes) return null;
  for (const attr of elementNode.startTag.attributes) {
    if (
      !attr.directive &&
      attr.key?.type === "VIdentifier" &&
      attr.key.name === attrName
    ) {
      return { isDynamic: false, attr };
    }
    if (
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      attr.key.name?.name === "bind" &&
      attr.key.argument?.name === attrName
    ) {
      return { isDynamic: true, attr };
    }
  }
  return null;
}

function getAttrLiteralValues(elementNode, attrName) {
  const found = getAttrNode(elementNode, attrName);
  if (!found) return [];
  if (!found.isDynamic) {
    return [found.attr.value?.value ?? ""];
  }
  return extractStaticExpressionStrings(found.attr.value?.expression).strings;
}

function getRawAttrSource(elementNode, attrName, sourceCode) {
  const found = getAttrNode(elementNode, attrName);
  if (!found || !found.attr.value) return "";
  if (!found.isDynamic) return found.attr.value.value ?? "";
  return sourceCode.getText(found.attr.value);
}

function getPairedRoleAndAriaLiveBranches(elementNode, sourceCode) {
  const roleInfo = getAttrNode(elementNode, "role");
  const liveInfo = getAttrNode(elementNode, "aria-live");
  if (!roleInfo || !liveInfo) return [];

  const roleExpr = roleInfo.isDynamic ? roleInfo.attr.value?.expression : null;
  const liveExpr = liveInfo.isDynamic ? liveInfo.attr.value?.expression : null;

  if (
    roleExpr?.type === "ConditionalExpression" &&
    liveExpr?.type === "ConditionalExpression" &&
    sourceCode.getText(roleExpr.test).trim() ===
      sourceCode.getText(liveExpr.test).trim()
  ) {
    const consRoles = extractStaticExpressionStrings(
      roleExpr.consequent
    ).strings;
    const consLives = extractStaticExpressionStrings(
      liveExpr.consequent
    ).strings;
    const altRoles = extractStaticExpressionStrings(roleExpr.alternate).strings;
    const altLives = extractStaticExpressionStrings(liveExpr.alternate).strings;
    const pairs = [];
    for (const r of consRoles) {
      for (const l of consLives) pairs.push([r, l]);
    }
    for (const r of altRoles) {
      for (const l of altLives) pairs.push([r, l]);
    }
    return pairs;
  }

  const roles = getAttrLiteralValues(elementNode, "role");
  const lives = getAttrLiteralValues(elementNode, "aria-live");
  const pairs = [];
  for (const r of roles) {
    for (const l of lives) pairs.push([r, l]);
  }
  return pairs;
}

function hasConditionalDirective(elementNode) {
  if (!elementNode?.startTag?.attributes) return false;
  return elementNode.startTag.attributes.some(
    (attr) =>
      attr.directive &&
      attr.key?.type === "VDirectiveKey" &&
      (attr.key.name?.name === "if" ||
        attr.key.name?.name === "else-if" ||
        attr.key.name?.name === "else")
  );
}

function resolveRepoRelativePath(filename) {
  if (!filename) return "";
  const normalized = filename.split(path.sep).join("/");
  const normalizedRoot = repoRoot.split(path.sep).join("/");
  if (normalized.startsWith(`${normalizedRoot}/`)) {
    return normalized.slice(normalizedRoot.length + 1);
  }
  for (const prefix of ["packages/", "apps/", "docs/"]) {
    const idx = normalized.indexOf(`/${prefix}`);
    if (idx >= 0) return normalized.slice(idx + 1);
    if (normalized.startsWith(prefix)) return normalized;
  }
  return normalized;
}

export const noConflictingLiveRegionRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        'Disallow conflicting ARIA live-region attributes such as role="alert" with aria-live="polite" or role="status" with aria-live="assertive" (WCAG 2.2 SC 4.1.3 / W3C ARIA19).',
      category: "Accessibility",
    },
    messages: {
      alertDowngradedToPolite:
        'Conflicting live-region semantics: role="alert" must not be paired with aria-live="{{ariaLive}}" because it suppresses assertive error announcements (WCAG 2.2 SC 4.1.3 / W3C Technique ARIA19). Use ClinicalLiveRegion from "ui" or omit aria-live="polite" on alert containers.',
      statusUpgradedToAssertive:
        'Conflicting live-region semantics: role="status" must not be paired with aria-live="assertive". Use role="alert" or ClinicalLiveRegion with politeness="assertive" for urgent errors (W3C Technique ARIA19 / ARIA22).',
    },
  },
  create(context) {
    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    return parserServices.defineTemplateBodyVisitor({
      VElement(node) {
        const pairs = getPairedRoleAndAriaLiveBranches(
          node,
          context.sourceCode
        );
        for (const [roleVal, liveVal] of pairs) {
          if (
            roleVal === "alert" &&
            (liveVal === "polite" || liveVal === "off")
          ) {
            context.report({
              node: node.startTag,
              messageId: "alertDowngradedToPolite",
              data: { ariaLive: liveVal },
            });
            return;
          }
          if (roleVal === "status" && liveVal === "assertive") {
            context.report({
              node: node.startTag,
              messageId: "statusUpgradedToAssertive",
            });
            return;
          }
        }
      },
    });
  },
};

export const noStatusRoleOnErrorsRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        'Disallow role="status" on validation or operational error banners; require role="alert" or ClinicalLiveRegion with politeness="assertive" (W3C Technique ARIA19).',
      category: "Accessibility",
    },
    messages: {
      errorUsesStatusRole:
        'Error or validation container "{{hint}}" uses role="status" instead of an assertive alert (W3C Technique ARIA19). Use ClinicalLiveRegion with politeness="assertive" or a persistent role="alert" container.',
    },
  },
  create(context) {
    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    return parserServices.defineTemplateBodyVisitor({
      VElement(node) {
        const roles = getAttrLiteralValues(node, "role");
        if (!roles.includes("status") || roles.includes("alert")) return;

        const idVal = getRawAttrSource(node, "id", context.sourceCode);
        const classVal = getRawAttrSource(node, "class", context.sourceCode);
        const testIdVal = getRawAttrSource(
          node,
          "data-testid",
          context.sourceCode
        );

        for (const candidate of [idVal, testIdVal, classVal]) {
          if (candidate && ERROR_TOKEN_REGEX.test(candidate)) {
            context.report({
              node: node.startTag,
              messageId: "errorUsesStatusRole",
              data: { hint: candidate.trim().slice(0, 60) },
            });
            return;
          }
        }
      },
    });
  },
};

export const noConditionalStatusMountRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        'Disallow mounting role="status" or aria-live="polite" containers conditionally via v-if / v-else-if / v-else outside the shrink-only legacy allowlist (W3C Technique ARIA22).',
      category: "Accessibility",
    },
    messages: {
      conditionalStatusMount:
        'Mounting role="status" or aria-live="polite" conditionally with v-if / v-else-if / v-else prevents screen readers from announcing initial status text (W3C Technique ARIA22). Keep the live container mounted via ClinicalLiveRegion from "ui" (or toggle .sr-only while mounted).',
    },
  },
  create(context) {
    const filename = context.filename || context.getFilename() || "";
    const relPath = resolveRepoRelativePath(filename);
    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    const maxAllowed =
      LEGACY_CONDITIONAL_STATUS_MOUNT_ALLOWLIST.get(relPath) ?? 0;
    const offendingNodes = [];

    return parserServices.defineTemplateBodyVisitor(
      {
        VElement(node) {
          if (!hasConditionalDirective(node)) return;
          const roles = getAttrLiteralValues(node, "role");
          const ariaLives = getAttrLiteralValues(node, "aria-live");
          if (roles.includes("status") || ariaLives.includes("polite")) {
            offendingNodes.push(node);
          }
        },
      },
      {
        "Program:exit"() {
          if (offendingNodes.length <= maxAllowed) return;
          const nodesToReport =
            maxAllowed === 0
              ? offendingNodes
              : offendingNodes.slice(maxAllowed);
          for (const node of nodesToReport) {
            context.report({
              node: node.startTag,
              messageId: "conditionalStatusMount",
            });
          }
        },
      }
    );
  },
};
