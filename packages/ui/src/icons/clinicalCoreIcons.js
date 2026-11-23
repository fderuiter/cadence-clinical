import {
  AlertTriangle,
  Check,
  CheckCircle,
  ClipboardList,
  CreditCard,
  FileSignature,
  Info,
  Loader,
  Mail,
  MessageSquare,
  PenTool,
  Plus,
  RefreshCw,
  ScrollText,
  Search,
  ShieldCheck,
  Siren,
  Sparkles,
  TrendingUp,
  Wallet,
  X,
  XCircle,
  createLucideIcon,
} from "lucide-vue-next";

export const CLINICAL_ICON_STROKE_WIDTH = 1.75;
export const CLINICAL_ICON_SIZES = Object.freeze([16, 20]);

const FallbackCircleOutline = createLucideIcon("circle", [
  ["path", { d: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20", key: "fb" }],
]);

export const CORE_CLINICAL_ICON_MAP = Object.freeze({
  "alert-triangle": AlertTriangle,
  check: Check,
  "check-circle": CheckCircle,
  "clipboard-list": ClipboardList,
  "credit-card": CreditCard,
  "file-signature": FileSignature,
  info: Info,
  loader: Loader,
  mail: Mail,
  "message-square": MessageSquare,
  "pen-tool": PenTool,
  plus: Plus,
  "refresh-cw": RefreshCw,
  "scroll-text": ScrollText,
  search: Search,
  "shield-check": ShieldCheck,
  siren: Siren,
  sparkles: Sparkles,
  "trending-up": TrendingUp,
  wallet: Wallet,
  warning: AlertTriangle,
  x: X,
  "x-circle": XCircle,
});

const SEMANTIC_ICON_NAME_RE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;

export function normalizeClinicalIconName(name) {
  return String(name ?? "")
    .trim()
    .toLowerCase();
}

export function isClinicalIconName(name) {
  const normalized = normalizeClinicalIconName(name);
  return Boolean(normalized && SEMANTIC_ICON_NAME_RE.test(normalized));
}

export function normalizeClinicalIconSize(size = 16) {
  if (
    size === 20 ||
    size === "20" ||
    size === "20px" ||
    size === "md" ||
    size === "lg" ||
    (typeof size === "number" && Number.isFinite(size) && size >= 20)
  ) {
    return 20;
  }
  return 16;
}

export function resolveCoreClinicalIconEntry(name, extendedMap = null) {
  const normalized = normalizeClinicalIconName(name);
  if (normalized) {
    if (
      Object.prototype.hasOwnProperty.call(CORE_CLINICAL_ICON_MAP, normalized)
    ) {
      return {
        name: normalized,
        component: CORE_CLINICAL_ICON_MAP[normalized],
      };
    }
    if (
      extendedMap &&
      Object.prototype.hasOwnProperty.call(extendedMap, normalized)
    ) {
      return {
        name: normalized,
        component: extendedMap[normalized],
      };
    }
  }
  return {
    name: "circle",
    component: FallbackCircleOutline,
  };
}

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
