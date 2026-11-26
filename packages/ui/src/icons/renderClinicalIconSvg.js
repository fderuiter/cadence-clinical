import {
  CLINICAL_ICON_STROKE_WIDTH,
  normalizeClinicalIconSize,
} from "./iconNames.js";
import { resolveClinicalIcon } from "./clinicalIcons.js";

const SVG_PROP_ATTR_MAP = {
  strokeWidth: "stroke-width",
  strokeLinecap: "stroke-linecap",
  strokeLinejoin: "stroke-linejoin",
  fillRule: "fill-rule",
  clipRule: "clip-rule",
};

function escapeAttrValue(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function serializeChildVnode(node) {
  if (!node || typeof node !== "object" || typeof node.type !== "string") {
    return "";
  }
  const tag = node.type;
  const rawProps = node.props || {};
  const attrs = [];
  for (const [key, val] of Object.entries(rawProps)) {
    if (key === "key" || val === undefined || val === null || val === false) {
      continue;
    }
    const attrName = SVG_PROP_ATTR_MAP[key] || key;
    attrs.push(`${attrName}="${escapeAttrValue(val)}"`);
  }
  const attrSuffix = attrs.length > 0 ? ` ${attrs.join(" ")}` : "";
  const childrenMarkup = Array.isArray(node.children)
    ? node.children.map(serializeChildVnode).join("")
    : "";
  return childrenMarkup
    ? `<${tag}${attrSuffix}>${childrenMarkup}</${tag}>`
    : `<${tag}${attrSuffix} />`;
}

/**
 * Renders a curated lucide-vue-next icon into an accessible decorative SVG string
 * (16px or 20px, stroke-width="1.75", aria-hidden="true", focusable="false")
 * for vanilla DOM builders and imperatively constructed widgets.
 *
 * @param {string} name - Registered ClinicalIconName.
 * @param {{ size?: number | string, className?: string }} [options={}]
 * @returns {string}
 */
export function renderClinicalIconSvg(name, options = {}) {
  const iconComp = resolveClinicalIcon(name);
  const resolvedSize = normalizeClinicalIconSize(options.size ?? 16);
  const extraClass =
    typeof options.className === "string" ? options.className.trim() : "";
  const mergedClass = extraClass
    ? `clinical-icon ${extraClass}`
    : "clinical-icon";

  const outerVnode = iconComp(
    {
      size: resolvedSize,
      strokeWidth: CLINICAL_ICON_STROKE_WIDTH,
      class: mergedClass,
    },
    { slots: {}, attrs: {} }
  );
  const svgVnode =
    outerVnode && typeof outerVnode.type === "function"
      ? outerVnode.type(outerVnode.props || {}, { slots: {}, attrs: {} })
      : outerVnode;

  const childrenMarkup = Array.isArray(svgVnode?.children)
    ? svgVnode.children.map(serializeChildVnode).join("")
    : "";

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${resolvedSize}" height="${resolvedSize}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${CLINICAL_ICON_STROKE_WIDTH}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false" class="${escapeAttrValue(mergedClass)}">${childrenMarkup}</svg>`;
}
