import { shallowRef } from "vue";
import {
  AlertTriangle,
  Check,
  CheckCircle,
  Loader,
  MessageSquare,
  Plus,
  ShieldCheck,
  Siren,
  X,
  XCircle,
  createLucideIcon,
} from "lucide-vue-next";

export const CLINICAL_ICON_STROKE_WIDTH = 1.75;
export const CLINICAL_ICON_SIZES = /* @__PURE__ */ Object.freeze([16, 20]);

const FallbackCircleOutline = /* @__PURE__ */ createLucideIcon("circle", [
  ["path", { d: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20", key: "fb" }],
]);

export const CORE_CLINICAL_ICON_MAP = /* @__PURE__ */ Object.freeze({
  "alert-triangle": AlertTriangle,
  check: Check,
  "check-circle": CheckCircle,
  loader: Loader,
  "message-square": MessageSquare,
  plus: Plus,
  "shield-check": ShieldCheck,
  siren: Siren,
  warning: AlertTriangle,
  x: X,
  "x-circle": XCircle,
});

export const registeredExtendedIconsRef = /* @__PURE__ */ shallowRef(null);
let extendedLoaderFn = null;
let extendedLoadPromise = null;

export function registerClinicalIcons(iconMap) {
  if (!iconMap || typeof iconMap !== "object") {
    return;
  }
  registeredExtendedIconsRef.value = Object.freeze(
    Object.assign({}, registeredExtendedIconsRef.value || {}, iconMap)
  );
}

export function registerExtendedClinicalIconLoader(loader) {
  if (typeof loader === "function") {
    extendedLoaderFn = loader;
  }
}

export function ensureExtendedClinicalIconsLoaded() {
  if (extendedLoadPromise) {
    return extendedLoadPromise;
  }
  if (!extendedLoaderFn) {
    return registeredExtendedIconsRef.value
      ? Promise.resolve(registeredExtendedIconsRef.value)
      : null;
  }
  extendedLoadPromise = Promise.resolve(extendedLoaderFn()).then((map) => {
    if (map) {
      registerClinicalIcons(map);
    }
    return registeredExtendedIconsRef.value;
  });
  return extendedLoadPromise;
}

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

export function resolveCoreClinicalIconEntry(
  name,
  extendedMap = registeredExtendedIconsRef.value
) {
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
