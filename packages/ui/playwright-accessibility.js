import AxeBuilder from "@axe-core/playwright";
import { WCAG_22_AA_TAGS } from "./accessibility-matcher.js";

export { WCAG_22_AA_TAGS };

/**
 * Decorative watermark overlays excluded from contrast/pointer checks per ADR-2388.
 */
export const DEFAULT_WATERMARK_EXCLUSIONS = [".watermark-overlay-container"];

export const DEFAULT_CONTROL_BORDER_SELECTOR = [
  'input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]):not([type="range"]):not([type="file"]):not([type="color"]):not([type="submit"]):not([type="button"]):not([type="reset"]):not(:disabled)',
  "select:not(:disabled)",
  "textarea:not(:disabled)",
  ".clinical-radio-grid",
].join(", ");

function toArray(value) {
  if (!value) return [];
  return (Array.isArray(value) ? value : [value]).filter(Boolean);
}

/**
 * Creates a Playwright AxeBuilder pre-configured with WCAG 2.2 Level A & AA tags
 * and default GxP watermark exclusions.
 */
export function createWcag22AxeBuilder(page, options = {}) {
  const {
    include,
    exclude = DEFAULT_WATERMARK_EXCLUSIONS,
    includeWatermark = false,
    disableRules = [],
    tags = WCAG_22_AA_TAGS,
  } = options;

  let builder = new AxeBuilder({ page }).withTags([...tags]);

  for (const selector of toArray(include)) {
    builder = builder.include(selector);
  }

  if (!includeWatermark) {
    for (const selector of toArray(exclude)) {
      builder = builder.exclude(selector);
    }
  }

  const disabled = toArray(disableRules);
  if (disabled.length > 0) {
    builder = builder.disableRules(disabled);
  }

  return builder;
}

/**
 * Computes WCAG 2.2 SC 1.4.11 Non-text Contrast ratio for a single control's
 * top border against its effective alpha-composited background surface.
 */
export async function getControlBorderContrast(page, selector) {
  return page
    .locator(selector)
    .first()
    .evaluate((el) => {
      function parseRgba(str) {
        if (!str) return [0, 0, 0, 0];
        const m = str.match(
          /rgba?\(\s*([\d.]+)\s*[, ]\s*([\d.]+)\s*[, ]\s*([\d.]+)(?:\s*[,/]\s*([\d.]+))?\s*\)/
        );
        if (!m) return [0, 0, 0, 0];
        return [
          Number(m[1]),
          Number(m[2]),
          Number(m[3]),
          m[4] !== undefined ? Number(m[4]) : 1,
        ];
      }

      function composite(fg, bg) {
        const alpha = fg[3] + bg[3] * (1 - fg[3]);
        if (alpha <= 0) return [255, 255, 255, 0];
        return [
          (fg[0] * fg[3] + bg[0] * bg[3] * (1 - fg[3])) / alpha,
          (fg[1] * fg[3] + bg[1] * bg[3] * (1 - fg[3])) / alpha,
          (fg[2] * fg[3] + bg[2] * bg[3] * (1 - fg[3])) / alpha,
          alpha,
        ];
      }

      function getCanvasFallback() {
        const isDark =
          document.documentElement.getAttribute("data-theme") === "dark" ||
          document.body?.getAttribute("data-theme") === "dark" ||
          document.documentElement.classList.contains("dark");
        return isDark ? [15, 23, 42, 1] : [255, 255, 255, 1];
      }

      function resolveEffectiveBackground(startEl) {
        const layers = [];
        let cur = startEl;
        while (cur && cur.nodeType === 1) {
          const bg = parseRgba(window.getComputedStyle(cur).backgroundColor);
          if (bg[3] > 0) {
            layers.push(bg);
            if (bg[3] >= 0.999) break;
          }
          cur = cur.parentElement;
        }
        let base = getCanvasFallback();
        for (let i = layers.length - 1; i >= 0; i--) {
          base = composite(layers[i], base);
        }
        return base;
      }

      function luminance([r, g, b]) {
        const [rl, gl, bl] = [r, g, b].map((v) => {
          const s = v / 255;
          return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
        });
        return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
      }

      const style = window.getComputedStyle(el);
      const effectiveBg = resolveEffectiveBackground(el);
      const rawBorder = parseRgba(style.borderTopColor);
      const effectiveBorder = composite(rawBorder, effectiveBg);
      const l1 = luminance(effectiveBorder);
      const l2 = luminance(effectiveBg);
      return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    });
}

/**
 * Audits visible interactive form controls within the given scope for
 * WCAG 2.2 SC 1.4.11 Non-text Contrast (>= 3:1 border contrast).
 */
export async function auditInteractiveControlBorders(page, options = {}) {
  const {
    include,
    exclude = DEFAULT_WATERMARK_EXCLUSIONS,
    controlSelector = DEFAULT_CONTROL_BORDER_SELECTOR,
    minContrastRatio = 3,
  } = options;

  const includeSelectors = toArray(include);
  const excludeSelectors = toArray(exclude);

  return page.evaluate(
    ({ roots, excluded, selector, minRatio }) => {
      function parseRgba(str) {
        if (!str) return [0, 0, 0, 0];
        const m = str.match(
          /rgba?\(\s*([\d.]+)\s*[, ]\s*([\d.]+)\s*[, ]\s*([\d.]+)(?:\s*[,/]\s*([\d.]+))?\s*\)/
        );
        if (!m) return [0, 0, 0, 0];
        return [
          Number(m[1]),
          Number(m[2]),
          Number(m[3]),
          m[4] !== undefined ? Number(m[4]) : 1,
        ];
      }

      function composite(fg, bg) {
        const alpha = fg[3] + bg[3] * (1 - fg[3]);
        if (alpha <= 0) return [255, 255, 255, 0];
        return [
          (fg[0] * fg[3] + bg[0] * bg[3] * (1 - fg[3])) / alpha,
          (fg[1] * fg[3] + bg[1] * bg[3] * (1 - fg[3])) / alpha,
          (fg[2] * fg[3] + bg[2] * bg[3] * (1 - fg[3])) / alpha,
          alpha,
        ];
      }

      function getCanvasFallback() {
        const isDark =
          document.documentElement.getAttribute("data-theme") === "dark" ||
          document.body?.getAttribute("data-theme") === "dark" ||
          document.documentElement.classList.contains("dark");
        return isDark ? [15, 23, 42, 1] : [255, 255, 255, 1];
      }

      function resolveEffectiveBackground(startEl) {
        const layers = [];
        let cur = startEl;
        while (cur && cur.nodeType === 1) {
          const bg = parseRgba(window.getComputedStyle(cur).backgroundColor);
          if (bg[3] > 0) {
            layers.push(bg);
            if (bg[3] >= 0.999) break;
          }
          cur = cur.parentElement;
        }
        let base = getCanvasFallback();
        for (let i = layers.length - 1; i >= 0; i--) {
          base = composite(layers[i], base);
        }
        return base;
      }

      function luminance([r, g, b]) {
        const [rl, gl, bl] = [r, g, b].map((v) => {
          const s = v / 255;
          return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
        });
        return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
      }

      function contrastRatio(c1, c2) {
        const l1 = luminance(c1);
        const l2 = luminance(c2);
        return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
      }

      function isElementVisible(el) {
        if (!el || !el.isConnected) return false;
        let cur = el;
        while (cur && cur.nodeType === 1) {
          if (
            cur.hasAttribute("hidden") ||
            cur.hasAttribute("inert") ||
            cur.getAttribute("aria-hidden") === "true"
          ) {
            return false;
          }
          const st = window.getComputedStyle(cur);
          if (
            st.display === "none" ||
            st.visibility === "hidden" ||
            parseFloat(st.opacity) === 0
          ) {
            return false;
          }
          cur = cur.parentElement;
        }
        const rect = el.getBoundingClientRect();
        return rect.width > 2 && rect.height > 2;
      }

      function describeElement(el) {
        if (el.id) return `#${el.id}`;
        const tag = el.tagName.toLowerCase();
        const cls =
          typeof el.className === "string" && el.className.trim()
            ? "." + el.className.trim().split(/\s+/).slice(0, 2).join(".")
            : "";
        return `${tag}${cls}`;
      }

      const scopeElements =
        roots.length > 0
          ? roots.flatMap((r) => Array.from(document.querySelectorAll(r)))
          : [document.body];

      const candidates = new Set();
      for (const scope of scopeElements) {
        if (!scope) continue;
        if (scope.matches && scope.matches(selector)) {
          candidates.add(scope);
        }
        for (const el of scope.querySelectorAll(selector)) {
          candidates.add(el);
        }
      }

      const failures = [];
      for (const el of candidates) {
        if (excluded.some((ex) => el.closest(ex))) continue;
        if (el.classList.contains("sr-only")) continue;
        if (!isElementVisible(el)) continue;

        const style = window.getComputedStyle(el);
        const borderWidth = parseFloat(style.borderTopWidth) || 0;
        if (borderWidth <= 0 || style.borderTopStyle === "none") continue;

        const rawBorder = parseRgba(style.borderTopColor);
        if (rawBorder[3] <= 0) continue;

        const innerBg = resolveEffectiveBackground(el);
        const outerBg = resolveEffectiveBackground(
          el.parentElement || document.body
        );
        const effectiveBorder = composite(rawBorder, outerBg);
        const innerRatio = contrastRatio(effectiveBorder, innerBg);
        const outerRatio = contrastRatio(effectiveBorder, outerBg);
        const bestRatio = Math.max(innerRatio, outerRatio);

        if (bestRatio + 1e-6 < minRatio) {
          failures.push({
            id: "non-text-contrast-control-border",
            impact: "serious",
            target: [describeElement(el)],
            ratio: Number(bestRatio.toFixed(2)),
            borderColor: style.borderTopColor,
            html: el.outerHTML.slice(0, 200),
          });
        }
      }

      return failures;
    },
    {
      roots: includeSelectors,
      excluded: excludeSelectors,
      selector: controlSelector,
      minRatio: minContrastRatio,
    }
  );
}

/**
 * Formats axe-core violations and SC 1.4.11 control border failures into a concise diagnostic string.
 */
export function formatAccessibilityViolations(
  violations = [],
  borderViolations = []
) {
  const parts = [];
  for (const v of violations) {
    const targets = (v.nodes || [])
      .map((n) => (n.target || []).join(" "))
      .join(", ");
    parts.push(`[${v.id}] (${v.impact || "unknown"}): ${v.help} -> ${targets}`);
  }
  for (const b of borderViolations) {
    parts.push(
      `[${b.id}] (${b.impact}): SC 1.4.11 control border contrast ${b.ratio}:1 < 3:1 (${b.borderColor}) -> ${b.target.join(" ")}`
    );
  }
  return parts.join("\n");
}

/**
 * Runs a WCAG 2.2 Level A & AA axe-core audit and optional SC 1.4.11 interactive
 * control border contrast check on a Playwright page.
 */
export async function runWcag22Audit(page, options = {}) {
  const { checkControlBorders = true, ...axeOptions } = options;
  const results = await createWcag22AxeBuilder(page, axeOptions).analyze();
  const borderViolations = checkControlBorders
    ? await auditInteractiveControlBorders(page, axeOptions)
    : [];

  return {
    ...results,
    borderViolations,
  };
}
