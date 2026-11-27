import fs from "node:fs";
import path from "node:path";
import {
  isStyleOrColorVAttribute,
  offsetToLoc,
  portalStyleCssPath,
  stripCssCommentsPreservingNewlines,
  tokensCssPath,
  webStyleCssPath,
} from "../shared.mjs";

const TABULAR_NATIVE_INPUT_TYPES = new Set([
  "number",
  "date",
  "datetime-local",
  "time",
  "month",
  "week",
  "tel",
  "range",
  "numeric",
]);

const NON_TEXT_INPUT_TYPES = new Set([
  "checkbox",
  "radio",
  "hidden",
  "file",
  "submit",
  "button",
  "reset",
  "image",
]);

const TABULAR_INPUT_MODES = new Set(["numeric", "decimal"]);

const COUNTDOWN_CLASS_SET = new Set([
  "countdown-badge",
  "overview-countdown-text",
  "sla-mini-badge",
  "retry-countdown-banner",
  "sla-timer",
  "countdown-timer",
]);

const COUNT_BADGE_CLASS_SET = new Set([
  "badge-count",
  "count-badge",
  "column-count",
  "bulk-count-badge",
  "progress-badge",
  "sync-badge",
  "badge-unread",
  "vas-value-badge",
  "grade-badge",
]);

const KPI_VALUE_CLASS_SET = new Set([
  "kpi-value",
  "metric-value",
  "stat-value",
  "score-value",
  "kpi-number",
  "metric-number",
]);

const COUNTDOWN_ID_REGEX =
  /(?:^|[-_])(?:[a-z0-9]+-)*(?:countdown|sla-timer|retry-timer|time-remaining)(?:[-_]|$)/i;
const COUNTDOWN_EXPR_REGEX =
  /\b(?:remainingSeconds|days_remaining|trialDaysRemaining|formatSlaRemaining|retryCountdown|countdownSeconds|slaRemaining|timeRemaining)\b/;
const BADGE_CLASS_TOKEN_REGEX = /(?:^|-)(?:badge|pill)(?:-|$)/i;
const BADGE_COUNT_EXPR_REGEX =
  /\.(?:length|size)\b|\b[a-zA-Z0-9_]*(?:Count|Pct|Percent|Total|Unread|Queued)\b/;
const KPI_ID_REGEX =
  /^(?:observation-value|observation-version|compliance-rate-pct|compliance-(?:completed|pending|overdue)-count|wallet-balance-amount|wallet-lifetime-[a-z-]+|wallet-taxable-ytd)$/;
const KPI_EXPR_REGEX =
  /\b(?:formatCurrency|formatSignedCurrency|formatObservationValue)\s*\(/;

function extractTabularCssRulesFromText(rawCss, alignedClasses, scopedPairs) {
  if (!rawCss) return;
  const clean = stripCssCommentsPreservingNewlines(rawCss);
  const blockRegex = /([^{};@]+)\{([^{}]*)\}/g;
  for (const match of clean.matchAll(blockRegex)) {
    const selectorGroup = match[1].trim();
    const body = match[2];
    const decls = [
      ...body.matchAll(
        /font-variant-numeric\s*:\s*([^;!]+?)(?:\s*!important)?\s*(?:;|$)/gi
      ),
    ];
    if (decls.length === 0) continue;
    const lastVal = decls[decls.length - 1][1].trim();
    const isTabular =
      /\btabular-nums\b/.test(lastVal) ||
      /--(?:semantic-)?font-variant-numeric-tabular\b/.test(lastVal);
    if (!isTabular || /\bnormal\b/.test(lastVal)) continue;

    for (const rawSel of selectorGroup.split(",")) {
      const sel = rawSel
        .replace(/:not\([^)]*\)/g, "")
        .replace(/::?[a-zA-Z-]+(?:\([^)]*\))?/g, "")
        .trim();
      if (!sel || /\[/.test(sel)) continue;
      const parts = sel.split(/[\s>+~]+/).filter(Boolean);
      if (parts.length === 1 && parts[0].startsWith(".")) {
        for (const cm of parts[0].matchAll(/\.([a-zA-Z0-9_-]+)/g)) {
          alignedClasses.add(cm[1]);
        }
      } else if (
        parts.length === 2 &&
        parts[0].startsWith(".") &&
        parts[1].startsWith(".")
      ) {
        const anc = parts[0].slice(1).split(".")[0];
        const child = parts[1].slice(1).split(".")[0];
        if (anc && child) {
          scopedPairs.push({ ancestorClass: anc, className: child });
        }
      }
    }
  }
}

function getBaseTabularCssConfig(filename) {
  const normalized = filename.split(path.sep).join("/");
  const alignedClasses = new Set(["tabular-nums", "numeric", "clinical-badge"]);
  const scopedPairs = [];
  if (fs.existsSync(tokensCssPath)) {
    extractTabularCssRulesFromText(
      fs.readFileSync(tokensCssPath, "utf8"),
      alignedClasses,
      scopedPairs
    );
  }
  if (normalized.includes("/apps/web/") && fs.existsSync(webStyleCssPath)) {
    extractTabularCssRulesFromText(
      fs.readFileSync(webStyleCssPath, "utf8"),
      alignedClasses,
      scopedPairs
    );
  } else if (
    normalized.includes("/apps/subject-portal/") &&
    fs.existsSync(portalStyleCssPath)
  ) {
    extractTabularCssRulesFromText(
      fs.readFileSync(portalStyleCssPath, "utf8"),
      alignedClasses,
      scopedPairs
    );
  }
  return { alignedClasses, scopedPairs };
}

export const requireTabularNumsRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Require tabular numerals (tabular-nums, data-tabular-nums, <time>, or --font-variant-numeric-tabular) on numeric/date form controls, countdown timers, count badges, and KPI readouts.",
      category: "Design System",
    },
    messages: {
      unalignedNumericControl:
        'Numeric or date control <{{name}}> must align digits using tabular numerals (e.g., data-tabular-nums, class="tabular-nums", inputmode="numeric", or native numeric/date input type).',
      unalignedCountdown:
        'Live countdown or timer element <{{name}}> must apply tabular numerals (e.g., class="tabular-nums", <time>, or font-variant-numeric: var(--font-variant-numeric-tabular)).',
      unalignedBadge:
        'Numeric count badge <{{name}}> must apply tabular numerals (e.g., class="clinical-badge", class="tabular-nums", or font-variant-numeric: var(--font-variant-numeric-tabular)).',
      unalignedKpi:
        'Clinical value, KPI, or currency readout <{{name}}> must apply tabular numerals (e.g., class="tabular-nums" or font-variant-numeric: var(--font-variant-numeric-tabular)).',
      tabularOverrideForbidden:
        "Overriding tabular numerals with 'font-variant-numeric: normal' is prohibited; rely on shared tokens in packages/ui/tokens.css.",
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const sourceText = context.sourceCode.getText();
    const normalOverrideRegex =
      /font-variant-numeric\s*:\s*normal\b|fontVariantNumeric\s*:\s*['"`]normal['"`]/gi;

    if (filename.endsWith(".css")) {
      return {
        Program() {
          const clean = stripCssCommentsPreservingNewlines(sourceText);
          for (const m of clean.matchAll(normalOverrideRegex)) {
            context.report({
              loc: offsetToLoc(sourceText, m.index),
              messageId: "tabularOverrideForbidden",
            });
          }
        },
      };
    }

    if (filename.endsWith(".vue")) {
      const { alignedClasses, scopedPairs } = getBaseTabularCssConfig(filename);
      for (const sm of sourceText.matchAll(
        /<style\b[^>]*>([\s\S]*?)<\/style>/gi
      )) {
        extractTabularCssRulesFromText(sm[1], alignedClasses, scopedPairs);
      }

      const styleBlockCheck = {
        "Program:exit"() {
          for (const sm of sourceText.matchAll(
            /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
          )) {
            const contentStart = sm.index + sm[1].length;
            const clean = stripCssCommentsPreservingNewlines(sm[2]);
            for (const m of clean.matchAll(normalOverrideRegex)) {
              context.report({
                loc: offsetToLoc(sourceText, contentStart + m.index),
                messageId: "tabularOverrideForbidden",
              });
            }
          }
        },
      };

      const parserServices =
        context.sourceCode?.parserServices || context.parserServices;
      if (!parserServices?.defineTemplateBodyVisitor) {
        return styleBlockCheck;
      }

      function getStaticAttrValue(el, attrName) {
        const attr = el.startTag.attributes.find(
          (a) =>
            !a.directive &&
            a.key.type === "VIdentifier" &&
            a.key.name.toLowerCase() === attrName.toLowerCase()
        );
        if (!attr) return null;
        return attr.value ? attr.value.value : "";
      }

      function getBoundAttr(el, argName) {
        return (
          el.startTag.attributes.find(
            (a) =>
              a.directive &&
              a.key.type === "VDirectiveKey" &&
              a.key.name?.name === "bind" &&
              a.key.argument?.type === "VIdentifier" &&
              a.key.argument.name.toLowerCase() === argName.toLowerCase()
          ) || null
        );
      }

      function collectClassesFromExpr(expr, out) {
        if (!expr) return;
        if (expr.type === "Literal" && typeof expr.value === "string") {
          for (const tok of expr.value.split(/\s+/).filter(Boolean)) {
            out.add(tok);
          }
        } else if (expr.type === "ArrayExpression") {
          for (const elem of expr.elements) {
            if (
              elem &&
              elem.type === "Literal" &&
              typeof elem.value === "string"
            ) {
              for (const tok of elem.value.split(/\s+/).filter(Boolean)) {
                out.add(tok);
              }
            } else if (elem && elem.type === "ObjectExpression") {
              collectClassesFromExpr(elem, out);
            }
          }
        } else if (expr.type === "ObjectExpression") {
          for (const prop of expr.properties) {
            if (
              prop.type === "Property" &&
              prop.value?.type === "Literal" &&
              prop.value.value === true
            ) {
              const keyName =
                prop.key.type === "Identifier"
                  ? prop.key.name
                  : prop.key.type === "Literal"
                    ? String(prop.key.value)
                    : "";
              for (const tok of keyName.split(/\s+/).filter(Boolean)) {
                out.add(tok);
              }
            }
          }
        }
      }

      function getElementClasses(el) {
        const out = new Set();
        const staticClass = getStaticAttrValue(el, "class");
        if (staticClass) {
          for (const tok of staticClass.split(/\s+/).filter(Boolean)) {
            out.add(tok);
          }
        }
        const boundClass = getBoundAttr(el, "class");
        if (boundClass?.value?.expression) {
          collectClassesFromExpr(boundClass.value.expression, out);
        }
        return out;
      }

      function getStyleAttrText(el) {
        let txt = "";
        const staticStyle = getStaticAttrValue(el, "style");
        if (staticStyle) txt += staticStyle + ";";
        const boundStyle = getBoundAttr(el, "style");
        if (boundStyle?.value) {
          txt += context.sourceCode.getText(boundStyle.value);
        }
        return txt;
      }

      function isExplicitlyDisabledTabular(el) {
        const staticData = getStaticAttrValue(el, "data-tabular-nums");
        if (staticData === "false") return true;
        const boundData = getBoundAttr(el, "data-tabular-nums");
        if (boundData?.value) {
          const raw = context.sourceCode.getText(boundData.value).trim();
          if (
            raw === '"false"' ||
            raw === "'false'" ||
            raw === `"'false'"` ||
            raw === `'"false"'` ||
            raw === '"false"'
          ) {
            return true;
          }
          if (
            boundData.value.expression?.type === "Literal" &&
            (boundData.value.expression.value === false ||
              boundData.value.expression.value === "false")
          ) {
            return true;
          }
        }
        const boundProp =
          getBoundAttr(el, "tabular-nums") || getBoundAttr(el, "tabularnums");
        if (
          boundProp?.value?.expression?.type === "Literal" &&
          boundProp.value.expression.value === false
        ) {
          return true;
        }
        const styleTxt = getStyleAttrText(el);
        if (/font-variant-numeric\s*:\s*normal\b/i.test(styleTxt)) return true;
        return false;
      }

      function hasDataTabularNums(el, isClinicalInput) {
        const staticData = getStaticAttrValue(el, "data-tabular-nums");
        if (staticData !== null && staticData !== "false") return true;
        const boundData = getBoundAttr(el, "data-tabular-nums");
        if (boundData && !isExplicitlyDisabledTabular(el)) return true;
        if (isClinicalInput) {
          const staticProp =
            getStaticAttrValue(el, "tabular-nums") ??
            getStaticAttrValue(el, "tabularnums");
          if (staticProp !== null && staticProp !== "false") return true;
          const boundProp =
            getBoundAttr(el, "tabular-nums") || getBoundAttr(el, "tabularnums");
          if (boundProp && !isExplicitlyDisabledTabular(el)) return true;
          const boundAttrs = getBoundAttr(el, "attributes");
          if (boundAttrs?.value) {
            const raw = context.sourceCode.getText(boundAttrs.value);
            if (
              /data-tabular-nums/.test(raw) &&
              !/data-tabular-nums['"]?\s*:\s*(?:false|['"]false['"])/.test(raw)
            ) {
              return true;
            }
          }
        }
        return false;
      }

      function hasInlineTabularStyle(el) {
        const txt = getStyleAttrText(el);
        return (
          /\btabular-nums\b/.test(txt) ||
          /--(?:semantic-)?font-variant-numeric-tabular\b/.test(txt)
        );
      }

      function hasAlignedCssClass(el, classes) {
        for (const c of classes) {
          if (alignedClasses.has(c)) return true;
        }
        for (const pair of scopedPairs) {
          if (classes.has(pair.className)) {
            let anc = el.parent;
            while (anc && anc.type === "VElement") {
              if (getElementClasses(anc).has(pair.ancestorClass)) return true;
              anc = anc.parent;
            }
          }
        }
        return false;
      }

      function isControlTabularAligned(el, tag, isClinicalInput) {
        if (isExplicitlyDisabledTabular(el)) return false;
        if (hasDataTabularNums(el, isClinicalInput)) return true;
        if (hasInlineTabularStyle(el)) return true;
        const classes = getElementClasses(el);
        if (hasAlignedCssClass(el, classes)) return true;

        if (tag === "input" || isClinicalInput) {
          const staticType = (
            getStaticAttrValue(el, "type") || ""
          ).toLowerCase();
          if (TABULAR_NATIVE_INPUT_TYPES.has(staticType)) return true;
          const boundType = getBoundAttr(el, "type");
          if (
            boundType?.value?.expression?.type === "Literal" &&
            TABULAR_NATIVE_INPUT_TYPES.has(
              String(boundType.value.expression.value || "").toLowerCase()
            )
          ) {
            return true;
          }
          const staticMode = (
            getStaticAttrValue(el, "inputmode") || ""
          ).toLowerCase();
          if (TABULAR_INPUT_MODES.has(staticMode)) return true;
        }

        let anc = el.parent;
        while (anc && anc.type === "VElement") {
          if (isExplicitlyDisabledTabular(anc)) return false;
          const ancTag = (anc.rawName || anc.name).toLowerCase();
          if (ancTag === "td" || ancTag === "th") return true;
          const ancClasses = getElementClasses(anc);
          if (
            ancClasses.has("tabular-nums") ||
            ancClasses.has("numeric") ||
            hasDataTabularNums(anc, false)
          ) {
            return true;
          }
          anc = anc.parent;
        }
        return false;
      }

      function isReadoutTabularAligned(el) {
        let cur = el;
        while (cur && cur.type === "VElement") {
          if (isExplicitlyDisabledTabular(cur)) return false;
          const curTag = (cur.rawName || cur.name).toLowerCase();
          if (
            curTag === "time" ||
            curTag === "table" ||
            curTag === "td" ||
            curTag === "th"
          ) {
            return true;
          }
          if (hasDataTabularNums(cur, false) || hasInlineTabularStyle(cur)) {
            return true;
          }
          const curClasses = getElementClasses(cur);
          if (hasAlignedCssClass(cur, curClasses)) return true;
          cur = cur.parent;
        }
        return false;
      }

      function getDirectExpressionText(el) {
        let out = "";
        for (const child of el.children || []) {
          if (child.type === "VExpressionContainer" && child.expression) {
            out += " " + context.sourceCode.getText(child.expression);
          }
        }
        return out;
      }

      function getDirectStaticProseLength(el) {
        let text = "";
        for (const child of el.children || []) {
          if (child.type === "VText") {
            text += child.value;
          }
        }
        return text.replace(/\s+/g, " ").trim().length;
      }

      return parserServices.defineTemplateBodyVisitor(
        {
          VAttribute(node) {
            if (isStyleOrColorVAttribute(node) && node.value) {
              const text = context.sourceCode.getText(node.value);
              if (
                /font-variant-numeric\s*:\s*normal\b|fontVariantNumeric\s*:\s*['"`]normal['"`]/i.test(
                  text
                )
              ) {
                context.report({
                  node: node.value,
                  messageId: "tabularOverrideForbidden",
                });
              }
            }
          },
          VElement(node) {
            const rawName = node.rawName || node.name;
            const tag = rawName.toLowerCase();
            if (
              tag === "textarea" ||
              tag === "svg" ||
              tag === "path" ||
              tag === "style" ||
              tag === "script"
            ) {
              return;
            }

            const isClinicalInput =
              tag === "clinicalinput" || tag === "clinical-input";
            if (tag === "input" || tag === "select" || isClinicalInput) {
              const staticType = (
                getStaticAttrValue(node, "type") || ""
              ).toLowerCase();
              if (NON_TEXT_INPUT_TYPES.has(staticType)) return;

              const hasVModelNumber = node.startTag.attributes.some(
                (a) =>
                  a.directive &&
                  a.key.type === "VDirectiveKey" &&
                  a.key.name?.name === "model" &&
                  a.key.modifiers?.some((m) => m.name === "number")
              );
              const hasNumericRangeConstraint = node.startTag.attributes.some(
                (a) => {
                  if (!a.directive && a.key.type === "VIdentifier") {
                    const n = a.key.name.toLowerCase();
                    return n === "step" || n === "min" || n === "max";
                  }
                  if (
                    a.directive &&
                    a.key.type === "VDirectiveKey" &&
                    a.key.name?.name === "bind" &&
                    a.key.argument?.type === "VIdentifier"
                  ) {
                    const n = a.key.argument.name.toLowerCase();
                    return n === "step" || n === "min" || n === "max";
                  }
                  return false;
                }
              );
              const staticPattern = getStaticAttrValue(node, "pattern") || "";
              const hasDigitPattern = /^\[0-9|\\d/.test(staticPattern);
              const isNativeTabularControl =
                TABULAR_NATIVE_INPUT_TYPES.has(staticType) ||
                TABULAR_INPUT_MODES.has(
                  (getStaticAttrValue(node, "inputmode") || "").toLowerCase()
                );

              if (
                (hasVModelNumber ||
                  hasNumericRangeConstraint ||
                  hasDigitPattern ||
                  isNativeTabularControl) &&
                !isControlTabularAligned(node, tag, isClinicalInput)
              ) {
                context.report({
                  node: node.startTag,
                  messageId: "unalignedNumericControl",
                  data: { name: rawName },
                });
              }
              return;
            }

            const classes = getElementClasses(node);
            const idVal = getStaticAttrValue(node, "id") || "";
            const testIdVal = getStaticAttrValue(node, "data-testid") || "";
            const exprText = getDirectExpressionText(node);

            const isCountdown =
              [...classes].some((c) => COUNTDOWN_CLASS_SET.has(c)) ||
              COUNTDOWN_ID_REGEX.test(idVal) ||
              COUNTDOWN_ID_REGEX.test(testIdVal) ||
              COUNTDOWN_EXPR_REGEX.test(exprText);

            if (isCountdown && !isReadoutTabularAligned(node)) {
              context.report({
                node: node.startTag,
                messageId: "unalignedCountdown",
                data: { name: rawName },
              });
              return;
            }

            const isCountBadge =
              [...classes].some((c) => COUNT_BADGE_CLASS_SET.has(c)) ||
              ([...classes].some((c) => BADGE_CLASS_TOKEN_REGEX.test(c)) &&
                BADGE_COUNT_EXPR_REGEX.test(exprText));

            if (isCountBadge && !isReadoutTabularAligned(node)) {
              context.report({
                node: node.startTag,
                messageId: "unalignedBadge",
                data: { name: rawName },
              });
              return;
            }

            const isKpi =
              [...classes].some((c) => KPI_VALUE_CLASS_SET.has(c)) ||
              KPI_ID_REGEX.test(idVal) ||
              KPI_ID_REGEX.test(testIdVal) ||
              (KPI_EXPR_REGEX.test(exprText) &&
                getDirectStaticProseLength(node) <= 45);

            if (isKpi && !isReadoutTabularAligned(node)) {
              context.report({
                node: node.startTag,
                messageId: "unalignedKpi",
                data: { name: rawName },
              });
            }
          },
        },
        styleBlockCheck
      );
    }

    // JS / TS UI helper & widget files
    const { alignedClasses } = getBaseTabularCssConfig(filename);
    const fileHasTabularConst =
      /--(?:semantic-)?font-variant-numeric-tabular\b/.test(sourceText) ||
      /font-variant-numeric\s*:\s*tabular-nums\b/.test(sourceText);

    function checkHtmlFragment(node, htmlText) {
      if (/font-variant-numeric\s*:\s*normal\b/i.test(htmlText)) {
        context.report({
          node,
          messageId: "tabularOverrideForbidden",
        });
      }
      const openTagRegex = /<([a-zA-Z][a-zA-Z0-9-]*)\b([^>]*)>/g;
      for (const m of htmlText.matchAll(openTagRegex)) {
        const tagName = m[1];
        const lowerTag = tagName.toLowerCase();
        const attrs = m[2];
        const classMatch = attrs.match(/\bclass\s*=\s*["']([^"']*)["']/i);
        const classTokens = classMatch
          ? classMatch[1].split(/\s+/).filter(Boolean)
          : [];
        const hasAlignedClass = classTokens.some((c) => alignedClasses.has(c));
        const hasDataAttr =
          /\bdata-tabular-nums\b/.test(attrs) &&
          !/\bdata-tabular-nums\s*=\s*["']false["']/i.test(attrs);
        const hasTabularInterpolation = /\$\{[^}]*tabular[^}]*\}/i.test(attrs);
        const hasInlineStyleTabular =
          /\btabular-nums\b|--(?:semantic-)?font-variant-numeric-tabular\b/.test(
            attrs
          ) ||
          (/\bstyle\s*=\s*["'][^"']*\$\{[^}]*BadgeStyle[^}]*\}/i.test(attrs) &&
            fileHasTabularConst);

        if (lowerTag === "input" || lowerTag === "select") {
          const explicitFalse = /\bdata-tabular-nums\s*=\s*["']false["']/i.test(
            attrs
          );
          const staticTypeMatch = attrs.match(
            /\btype\s*=\s*["']([a-zA-Z-]+)["']/i
          );
          const staticType = staticTypeMatch
            ? staticTypeMatch[1].toLowerCase()
            : "";
          if (NON_TEXT_INPUT_TYPES.has(staticType)) continue;
          const isDynamicInputType = /\btype\s*=\s*["']\$\{/.test(attrs);
          const hasRangeAttrs = /\b(?:step|min|max)\s*=/.test(attrs);
          const isNativeTabular =
            TABULAR_NATIVE_INPUT_TYPES.has(staticType) ||
            /\binputmode\s*=\s*["'](?:numeric|decimal)["']/i.test(attrs);

          const controlAligned =
            !explicitFalse &&
            (hasAlignedClass ||
              hasDataAttr ||
              hasTabularInterpolation ||
              hasInlineStyleTabular ||
              (isNativeTabular && !isDynamicInputType));

          if (
            (explicitFalse ||
              isDynamicInputType ||
              hasRangeAttrs ||
              (isNativeTabular && explicitFalse)) &&
            !controlAligned
          ) {
            context.report({
              node,
              messageId: "unalignedNumericControl",
              data: { name: tagName },
            });
          }
          continue;
        }

        const tagAligned =
          lowerTag === "time" ||
          hasAlignedClass ||
          hasDataAttr ||
          hasTabularInterpolation ||
          hasInlineStyleTabular;

        if (
          classTokens.some((c) => COUNTDOWN_CLASS_SET.has(c)) &&
          !tagAligned
        ) {
          context.report({
            node,
            messageId: "unalignedCountdown",
            data: { name: tagName },
          });
        } else if (
          classTokens.some((c) => COUNT_BADGE_CLASS_SET.has(c)) &&
          !tagAligned
        ) {
          context.report({
            node,
            messageId: "unalignedBadge",
            data: { name: tagName },
          });
        } else if (
          classTokens.some((c) => KPI_VALUE_CLASS_SET.has(c)) &&
          !tagAligned
        ) {
          context.report({
            node,
            messageId: "unalignedKpi",
            data: { name: tagName },
          });
        }
      }
    }

    return {
      TemplateLiteral(node) {
        const raw = context.sourceCode.getText(node);
        if (raw.includes("<")) {
          checkHtmlFragment(node, raw.slice(1, -1));
        }
      },
      Literal(node) {
        if (typeof node.value === "string" && node.value.includes("<")) {
          checkHtmlFragment(node, node.value);
        } else if (
          typeof node.value === "string" &&
          /font-variant-numeric\s*:\s*normal\b/i.test(node.value)
        ) {
          context.report({
            node,
            messageId: "tabularOverrideForbidden",
          });
        }
      },
      Property(node) {
        const keyName =
          node.key?.type === "Identifier"
            ? node.key.name
            : node.key?.type === "Literal"
              ? String(node.key.value)
              : "";
        if (
          (keyName === "fontVariantNumeric" ||
            keyName === "font-variant-numeric") &&
          node.value?.type === "Literal" &&
          typeof node.value.value === "string" &&
          /\bnormal\b/i.test(node.value.value)
        ) {
          context.report({
            node: node.value,
            messageId: "tabularOverrideForbidden",
          });
        }
      },
    };
  },
};
