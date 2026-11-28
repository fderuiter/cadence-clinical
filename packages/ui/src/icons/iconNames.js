export const CLINICAL_ICON_STROKE_WIDTH = 1.75;

/** @type {readonly string[]} */
export const CLINICAL_SHELL_ICON_NAMES = [
  "bell",
  "globe",
  "help-circle",
  "info",
  "menu",
  "search",
  "x",
];

/** @type {readonly string[]} */
export const CLINICAL_ROUTE_ICON_NAMES = [
  "alert-circle",
  "alert-triangle",
  "arrow-down",
  "arrow-up",
  "arrow-up-down",
  "book-open",
  "calendar",
  "camera",
  "check",
  "check-circle",
  "chevron-down",
  "chevron-left",
  "chevron-right",
  "chevron-up",
  "clipboard-list",
  "clock",
  "copy",
  "download",
  "edit",
  "external-link",
  "eye",
  "file-signature",
  "file-text",
  "flask-conical",
  "folder",
  "lock",
  "mail",
  "message-square",
  "mic",
  "pause",
  "phone",
  "play",
  "plus",
  "refresh",
  "rotate-ccw",
  "shield-check",
  "sparkles",
  "stethoscope",
  "ticket",
  "trash",
  "trending-up",
  "unlock",
  "upload",
  "users",
  "video",
  "wallet",
  "x-circle",
];

/** @type {readonly string[]} */
export const CLINICAL_ICON_NAMES = [
  ...CLINICAL_SHELL_ICON_NAMES,
  ...CLINICAL_ROUTE_ICON_NAMES,
];

let shellNameSet = null;
let routeNameSet = null;

function getShellNameSet() {
  if (!shellNameSet) {
    shellNameSet = new Set(CLINICAL_SHELL_ICON_NAMES);
  }
  return shellNameSet;
}

function getRouteNameSet() {
  if (!routeNameSet) {
    routeNameSet = new Set(CLINICAL_ROUTE_ICON_NAMES);
  }
  return routeNameSet;
}

/**
 * Checks whether a string is a registered shell icon name.
 *
 * @param {unknown} name
 * @returns {boolean}
 */
export function isClinicalShellIconName(name) {
  return typeof name === "string" && getShellNameSet().has(name);
}

/**
 * Checks whether a string is a registered route icon name.
 *
 * @param {unknown} name
 * @returns {boolean}
 */
export function isClinicalRouteIconName(name) {
  return typeof name === "string" && getRouteNameSet().has(name);
}

/**
 * Checks whether a string is a registered ClinicalIconName.
 *
 * @param {unknown} name
 * @returns {boolean}
 */
export function isClinicalIconName(name) {
  return isClinicalShellIconName(name) || isClinicalRouteIconName(name);
}

export function shouldThrowOnUnknownIcon() {
  const nodeEnv = globalThis.process?.env?.NODE_ENV;
  if (typeof nodeEnv === "string" && nodeEnv.length > 0) {
    return nodeEnv !== "production";
  }
  return true;
}

/**
 * Normalizes a requested icon size onto the clinical 16px / 20px vector scale.
 *
 * @param {number | string | undefined} size
 * @returns {16 | 20}
 */
export function normalizeClinicalIconSize(size = 16) {
  const numeric = Number(size);
  if (!Number.isFinite(numeric)) {
    return 16;
  }
  return numeric >= 20 ? 20 : 16;
}
