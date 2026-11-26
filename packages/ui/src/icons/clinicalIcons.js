import {
  CLINICAL_ICON_NAMES,
  CLINICAL_ICON_STROKE_WIDTH,
  CLINICAL_ROUTE_ICON_NAMES,
  CLINICAL_SHELL_ICON_NAMES,
  isClinicalIconName,
  isClinicalRouteIconName,
  isClinicalShellIconName,
  normalizeClinicalIconSize,
  shouldThrowOnUnknownIcon,
} from "./iconNames.js";
import { CLINICAL_SHELL_ICONS } from "./shellIcons.js";
import { CLINICAL_ROUTE_ICONS } from "./routeIcons.js";

export {
  CLINICAL_SHELL_ICONS,
  CLINICAL_SHELL_ICON_NAMES,
  CLINICAL_ROUTE_ICONS,
  CLINICAL_ROUTE_ICON_NAMES,
  CLINICAL_ICON_NAMES,
  CLINICAL_ICON_STROKE_WIDTH,
  isClinicalShellIconName,
  isClinicalRouteIconName,
  isClinicalIconName,
  normalizeClinicalIconSize,
};

export const CLINICAL_ICONS = /* @__PURE__ */ Object.assign(
  {},
  CLINICAL_SHELL_ICONS,
  CLINICAL_ROUTE_ICONS
);

/**
 * Resolves a registered lucide-vue-next icon component by ClinicalIconName.
 * Throws in development and test environments when an unknown icon name is supplied.
 *
 * @param {string} name
 * @returns {import("vue").Component}
 */
export function resolveClinicalIcon(name = "info") {
  if (isClinicalShellIconName(name)) {
    return CLINICAL_SHELL_ICONS[name];
  }
  if (isClinicalRouteIconName(name)) {
    return CLINICAL_ROUTE_ICONS[name];
  }
  if (shouldThrowOnUnknownIcon()) {
    throw new Error(
      `Unknown ClinicalIcon name "${String(name)}". Register a curated lucide-vue-next icon in packages/ui/src/icons/ or pass a valid ClinicalIconName.`
    );
  }
  return CLINICAL_SHELL_ICONS.info;
}
