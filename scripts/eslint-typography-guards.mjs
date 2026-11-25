// Modular ESLint typography and touch-control guardrails (ADR-2834).
// Extracted from eslint.config.mjs following scripts/eslint-drawer-overlay-guard.mjs
// to isolate typography rule definitions from unrelated lint config additions.
import path from "node:path";

const ALLOWED_FONT_SIZE_TOKEN_RE =
  /^var\(\s*--(?:semantic-)?font-size-(?:2xs|xs|sm|base|md|lg|xl|2xl|3xl|4xl|5xl)\s*\)$/;
const ALLOWED_MAX_1REM_RE =
  /^max\(\s*1rem\s*,\s*var\(\s*--(?:semantic-)?font-size-md\s*\)\s*\)$/i;
const SUB_1REM_FONT_TOKEN_RE =
  /^var\(\s*--(?:semantic-)?font-size-(?:2xs|xs|sm|base)(?:\s*,[^)]+)?\s*\)$/i;

export function stripAllowedTokensFontFaces(cleanCss) {
  return cleanCss.replace(/@font-face\s*\{[\s\S]*?\}/gi, (block) => {
    const famMatch = block.match(/font-family\s*:\s*([^;}]+)/i);
    const fam = famMatch ? famMatch[1].trim().replace(/^['"]|['"]$/g, "") : "";
    if (fam === "Inter" || fam === "JetBrains Mono") {
      return block.replace(/[^\n]/g, " ");
    }
    return block;
  });
}

function isAllowedTokenizedFontSize(rawVal) {
  const val = rawVal.replace(/\s*!important\s*$/i, "").trim();
  if (!val || val === "inherit" || val === "0") return true;
  if (ALLOWED_FONT_SIZE_TOKEN_RE.test(val)) return true;
  if (ALLOWED_MAX_1REM_RE.test(val)) return true;
  return false;
}

function detectSub1RemFontSize(rawVal) {
  const val = rawVal.replace(/\s*!important\s*$/i, "").trim();
  if (!val || val === "inherit" || ALLOWED_MAX_1REM_RE.test(val)) return null;
  if (SUB_1REM_FONT_TOKEN_RE.test(val)) return val;
  for (const m of val.matchAll(/([0-9]*\.?[0-9]+)\s*(px|rem|em|%)/gi)) {
    const num = parseFloat(m[1]);
    const unit = m[2].toLowerCase();
    if (
      (unit === "px" && num > 0 && num < 16) ||
      (unit === "rem" && num > 0 && num < 1) ||
      (unit === "em" && num > 0 && num < 1) ||
      (unit === "%" && num > 0 && num < 100)
    ) {
      return `${m[1]}${m[2]}`;
    }
  }
  return null;
}

function isTouchControlSelectorSubject(
  selectorPart,
  templateControlClasses,
  templateControlIds
) {
  let p = selectorPart.trim();
  if (!p || p.startsWith("@")) return false;
  if (
    /\[type\s*=\s*["']?(?:checkbox|radio|hidden|file)["']?\]/i.test(p) &&
    !/not\(\s*\[type/i.test(p)
  ) {
    return false;
  }
  p = p.replace(/:deep\(([^)]+)\)/g, "$1").trim();
  const segments = p.split(/\s+|(?<!\\)[>+~]\s*/).filter(Boolean);
  const subject = segments[segments.length - 1] || "";
  if (!subject || /::(before|after)/i.test(subject)) return false;
  const cleanSubject = subject.replace(/:+[a-zA-Z0-9_-]+(\([^)]*\))?/g, "");
  if (/^(?:input|select|textarea)(?:[.#[].*)?$/i.test(cleanSubject)) {
    return true;
  }
  for (const cls of cleanSubject.matchAll(/\.([a-zA-Z0-9_-]+)/g)) {
    const name = cls[1];
    if (templateControlClasses?.has(name)) return true;
    if (
      /(?:^|-)(?:input|select|textarea)(?:-(?:sm|md|lg|text|control|field))?$/i.test(
        name
      ) &&
      !/(?:group|wrapper|row|label|hint|error|header|btn|button|icon|container|panel|section|list|bar|badge|pill)$/i.test(
        name
      )
    ) {
      return true;
    }
    if (
      /^(?:form-control|form-select|form-control-sm|form-select-sm|select-control|text-control|textarea-control|input-sm|select-sm|input-text|input-text-gxp)$/i.test(
        name
      )
    ) {
      return true;
    }
  }
  for (const idMatch of cleanSubject.matchAll(/#([a-zA-Z0-9_-]+)/g)) {
    if (templateControlIds?.has(idMatch[1])) return true;
  }
  return false;
}

function splitTopLevelSelectors(selectorText) {
  const parts = [];
  let depth = 0;
  let cur = "";
  for (const ch of selectorText) {
    if (ch === "(") depth++;
    else if (ch === ")" && depth > 0) depth--;
    if (ch === "," && depth === 0) {
      parts.push(cur);
      cur = "";
    } else {
      cur += ch;
    }
  }
  if (cur) parts.push(cur);
  return parts;
}

export function createNoUntokenizedFontSizeRule({
  createTypographyScanVisitors,
  offsetToLoc,
}) {
  return {
    meta: {
      type: "problem",
      docs: {
        description:
          "Disallow untokenized font-size literals across Vue, CSS, and JS/TS files and enforce rem-based --font-size-* scale tokens in packages/ui/tokens.css.",
        category: "Design System",
      },
      messages: {
        untokenizedFontSize:
          'Untokenized font-size "{{size}}" is prohibited. Use shared rem typography tokens (var(--font-size-2xs) .. var(--font-size-5xl)) from packages/ui/tokens.css.',
        nonRemScaleToken:
          'Typography token "{{prop}}: {{size}}" in packages/ui/tokens.css must use rem units (>= 0.75rem), not px.',
      },
    },
    create(context) {
      const cssFontSizeRe = /(?:^|[;{\s])font-size\s*:\s*([^;}"'`\n]+)/gi;
      const inlineFontSizeRe = /(?:^|[;\s])font-size\s*:\s*([^;}"'`\n]+)/gi;
      const jsFontSizeRe = /fontSize\s*:\s*(['"`])([^'"`]+)\1/g;

      return createTypographyScanVisitors(context, {
        onCssBlock(cleanCss, baseOffset, sourceText, filename) {
          if (filename.endsWith("/packages/ui/tokens.css")) {
            for (const tokMatch of cleanCss.matchAll(
              /(--font-size-[a-zA-Z0-9_-]+)\s*:\s*([^;}\n]+)/gi
            )) {
              const prop = tokMatch[1];
              const val = tokMatch[2].trim();
              if (!/^[0-9]*\.?[0-9]+rem$/i.test(val)) {
                context.report({
                  loc: offsetToLoc(sourceText, baseOffset + tokMatch.index),
                  messageId: "nonRemScaleToken",
                  data: { prop, size: val },
                });
              }
            }
          }
          for (const match of cleanCss.matchAll(cssFontSizeRe)) {
            const rawVal = match[1].trim();
            if (!isAllowedTokenizedFontSize(rawVal)) {
              context.report({
                loc: offsetToLoc(sourceText, baseOffset + match.index),
                messageId: "untokenizedFontSize",
                data: { size: rawVal },
              });
            }
          }
        },
        onVueStyleAttr(valueNode, attrText) {
          const inner =
            attrText.length >= 2 &&
            (attrText.startsWith('"') || attrText.startsWith("'"))
              ? attrText.slice(1, -1)
              : attrText;
          for (const [re, idx] of [
            [inlineFontSizeRe, 1],
            [jsFontSizeRe, 2],
          ]) {
            for (const match of inner.matchAll(re)) {
              const rawVal = match[idx].trim();
              if (!isAllowedTokenizedFontSize(rawVal)) {
                context.report({
                  node: valueNode,
                  messageId: "untokenizedFontSize",
                  data: { size: rawVal },
                });
              }
            }
          }
        },
        onJsProgram(cleanJs, sourceText) {
          for (const [re, idx] of [
            [inlineFontSizeRe, 1],
            [jsFontSizeRe, 2],
          ]) {
            for (const match of cleanJs.matchAll(re)) {
              const rawVal = match[idx].trim();
              if (/\$\{/.test(rawVal)) continue;
              if (!isAllowedTokenizedFontSize(rawVal)) {
                context.report({
                  loc: offsetToLoc(sourceText, match.index),
                  messageId: "untokenizedFontSize",
                  data: { size: rawVal },
                });
              }
            }
          }
        },
      });
    },
  };
}

export function createNoSub1RemTouchControlRule({
  stripCssCommentsPreservingNewlines,
  stripUrlFragmentsPreservingLength,
  offsetToLoc,
  isStyleOrColorVAttribute,
}) {
  return {
    meta: {
      type: "problem",
      docs: {
        description:
          "Disallow sub-1rem (< 16px) font-size declarations on input, select, and textarea controls outside @media (pointer: fine).",
        category: "Accessibility",
      },
      messages: {
        sub1RemTouchControl:
          'Touch control font-size "{{size}}" resolves below 1rem (16px) and triggers mobile viewport auto-zoom. Use var(--font-size-md) (1rem) or larger, or scope compact desktop overrides inside @media (pointer: fine).',
      },
    },
    create(context) {
      const filename = (context.filename || context.getFilename() || "")
        .split(path.sep)
        .join("/");
      const sourceText = context.sourceCode.getText();
      const templateControlClasses = new Set();
      const templateControlIds = new Set();

      function scanCssRules(cleanCss, baseOffset) {
        let i = 0;
        const mediaStack = [];
        while (i < cleanCss.length) {
          if (/\s/.test(cleanCss[i])) {
            i++;
            continue;
          }
          if (cleanCss[i] === "}") {
            if (mediaStack.length > 0) mediaStack.pop();
            i++;
            continue;
          }
          let headerStart = i;
          let inQuote = null;
          let parenDepth = 0;
          while (i < cleanCss.length) {
            const ch = cleanCss[i];
            if (inQuote) {
              if (ch === "\\") i += 2;
              else {
                if (ch === inQuote) inQuote = null;
                i++;
              }
              continue;
            }
            if (ch === '"' || ch === "'") {
              inQuote = ch;
              i++;
              continue;
            }
            if (ch === "(") parenDepth++;
            else if (ch === ")" && parenDepth > 0) parenDepth--;
            else if (
              parenDepth === 0 &&
              (ch === "{" || ch === ";" || ch === "}")
            )
              break;
            i++;
          }
          const header = cleanCss.slice(headerStart, i).trim();
          if (i >= cleanCss.length) break;
          if (cleanCss[i] === ";") {
            i++;
            continue;
          }
          if (cleanCss[i] === "}") continue;
          if (/^@(?:media|supports|layer|container)\b/i.test(header)) {
            mediaStack.push(header);
            i++;
            continue;
          }
          let bodyStart = i + 1;
          let braceDepth = 1;
          i++;
          inQuote = null;
          while (i < cleanCss.length && braceDepth > 0) {
            const ch = cleanCss[i];
            if (inQuote) {
              if (ch === "\\") i += 2;
              else {
                if (ch === inQuote) inQuote = null;
                i++;
              }
              continue;
            }
            if (ch === '"' || ch === "'") {
              inQuote = ch;
              i++;
              continue;
            }
            if (ch === "{") braceDepth++;
            else if (ch === "}") braceDepth--;
            i++;
          }
          const body = cleanCss.slice(bodyStart, i - 1);
          const inPointerFine = mediaStack.some((m) =>
            /\(\s*pointer\s*:\s*fine\s*\)/i.test(m)
          );
          if (inPointerFine || header.startsWith("@")) continue;
          const targetsControl = splitTopLevelSelectors(header).some((part) =>
            isTouchControlSelectorSubject(
              part,
              templateControlClasses,
              templateControlIds
            )
          );
          if (!targetsControl) continue;

          for (const fsMatch of body.matchAll(
            /(?:^|[;{\s])font-size\s*:\s*([^;}\n]+)/gi
          )) {
            const badSize = detectSub1RemFontSize(fsMatch[1]);
            if (badSize) {
              context.report({
                loc: offsetToLoc(
                  sourceText,
                  baseOffset + bodyStart + fsMatch.index
                ),
                messageId: "sub1RemTouchControl",
                data: { size: badSize },
              });
            }
          }
        }
      }

      if (filename.endsWith(".css")) {
        return {
          Program() {
            scanCssRules(
              stripUrlFragmentsPreservingLength(
                stripCssCommentsPreservingNewlines(sourceText)
              ),
              0
            );
          },
        };
      }

      if (filename.endsWith(".vue")) {
        const styleExit = {
          "Program:exit"() {
            for (const styleMatch of sourceText.matchAll(
              /(<style\b[^>]*>)([\s\S]*?)<\/style>/gi
            )) {
              const contentStart = styleMatch.index + styleMatch[1].length;
              scanCssRules(
                stripUrlFragmentsPreservingLength(
                  stripCssCommentsPreservingNewlines(styleMatch[2])
                ),
                contentStart
              );
            }
          },
        };
        const parserServices =
          context.sourceCode?.parserServices || context.parserServices;
        if (!parserServices?.defineTemplateBodyVisitor) return styleExit;

        return parserServices.defineTemplateBodyVisitor(
          {
            VElement(node) {
              const tag =
                node.rawName?.toLowerCase() || node.name?.toLowerCase();
              if (tag !== "input" && tag !== "select" && tag !== "textarea") {
                return;
              }
              const attrs = node.startTag?.attributes || [];
              if (tag === "input") {
                const typeAttr = attrs.find(
                  (a) => !a.directive && a.key?.name === "type"
                );
                const t = typeAttr?.value?.value?.toLowerCase();
                if (
                  t === "checkbox" ||
                  t === "radio" ||
                  t === "hidden" ||
                  t === "file"
                ) {
                  return;
                }
              }
              for (const a of attrs) {
                if (!a.directive && a.key?.name === "class" && a.value?.value) {
                  for (const c of a.value.value.split(/\s+/).filter(Boolean)) {
                    templateControlClasses.add(c);
                  }
                }
                if (!a.directive && a.key?.name === "id" && a.value?.value) {
                  templateControlIds.add(a.value.value.trim());
                }
                if (isStyleOrColorVAttribute(a) && a.value) {
                  const rawAttr = context.sourceCode.getText(a.value);
                  const inner =
                    rawAttr.length >= 2 &&
                    (rawAttr.startsWith('"') || rawAttr.startsWith("'"))
                      ? rawAttr.slice(1, -1)
                      : rawAttr;
                  for (const [re, idx] of [
                    [/(?:^|[;\s])font-size\s*:\s*([^;}"'`\n]+)/gi, 1],
                    [/fontSize\s*:\s*(['"`])([^'"`]+)\1/g, 2],
                  ]) {
                    for (const m of inner.matchAll(re)) {
                      const badSize = detectSub1RemFontSize(m[idx]);
                      if (badSize) {
                        context.report({
                          node: a.value,
                          messageId: "sub1RemTouchControl",
                          data: { size: badSize },
                        });
                      }
                    }
                  }
                }
              }
            },
          },
          styleExit
        );
      }

      return {
        Program() {
          const cleanJs = stripCssCommentsPreservingNewlines(
            sourceText
          ).replace(/\/\/.*$/gm, (c) => " ".repeat(c.length));
          for (const tagMatch of cleanJs.matchAll(
            /<(input|select|textarea)\b([^>]*?)>/gi
          )) {
            const tag = tagMatch[1].toLowerCase();
            const attrs = tagMatch[2];
            if (
              tag === "input" &&
              /\btype\s*=\s*(?:"(?:checkbox|radio|hidden|file)"|'(?:checkbox|radio|hidden|file)')/i.test(
                attrs
              )
            ) {
              continue;
            }
            for (const stMatch of attrs.matchAll(
              /\bstyle\s*=\s*(["'])([\s\S]*?)\1/gi
            )) {
              for (const fsMatch of stMatch[2].matchAll(
                /(?:^|[;\s])font-size\s*:\s*([^;]+)/gi
              )) {
                const badSize = detectSub1RemFontSize(fsMatch[1]);
                if (badSize) {
                  context.report({
                    loc: offsetToLoc(sourceText, tagMatch.index),
                    messageId: "sub1RemTouchControl",
                    data: { size: badSize },
                  });
                }
              }
            }
          }
        },
      };
    },
  };
}
