import {
  CLINICAL_ICON_STROKE_WIDTH,
  normalizeClinicalIconSize,
  resolveCoreClinicalIconEntry,
} from "./clinicalCoreIcons.js";

function escapeXmlAttr(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

export function renderSvgFromIconEntry(entry, options = {}) {
  const { name: resolvedName, component } = entry;
  const resolvedSize = normalizeClinicalIconSize(options?.size);
  const vnode = component(
    { size: resolvedSize, strokeWidth: CLINICAL_ICON_STROKE_WIDTH },
    { slots: {}, attrs: {} }
  );
  const iconNode = Array.isArray(vnode?.props?.iconNode)
    ? vnode.props.iconNode
    : [];

  const childrenHtml = iconNode
    .map(([childTag, childAttrs = {}]) => {
      const safeTag = String(childTag || "path").replace(/[^a-zA-Z0-9-]/g, "");
      const attrsStr = Object.entries(childAttrs)
        .filter(
          ([attrKey, attrVal]) =>
            attrKey !== "key" && attrVal !== undefined && attrVal !== null
        )
        .map(([attrKey, attrVal]) => `${attrKey}="${escapeXmlAttr(attrVal)}"`)
        .join(" ");
      return `<${safeTag}${attrsStr ? ` ${attrsStr}` : ""} />`;
    })
    .join("");

  const extraClass = String(options?.className ?? options?.class ?? "").trim();
  const classes = [
    "clinical-icon",
    "lucide",
    `lucide-${resolvedName}`,
    options?.spin ? "clinical-icon--spin" : "",
    extraClass,
  ]
    .filter(Boolean)
    .join(" ");

  const accessibleLabel = String(
    options?.label ?? options?.ariaLabel ?? ""
  ).trim();
  const a11yAttrs = accessibleLabel
    ? `role="img" aria-label="${escapeXmlAttr(accessibleLabel)}"`
    : `aria-hidden="true"`;

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${resolvedSize}" height="${resolvedSize}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${CLINICAL_ICON_STROKE_WIDTH}" stroke-linecap="round" stroke-linejoin="round" class="${escapeXmlAttr(classes)}" data-clinical-icon="${escapeXmlAttr(resolvedName)}" focusable="false" ${a11yAttrs}>${childrenHtml}</svg>`;
}

export function renderCoreClinicalIconSvg(name, options = {}) {
  return renderSvgFromIconEntry(resolveCoreClinicalIconEntry(name), options);
}
