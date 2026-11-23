import {
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  CORE_CLINICAL_ICON_MAP,
  isClinicalIconName,
  normalizeClinicalIconName,
  normalizeClinicalIconSize,
  renderSvgFromIconEntry,
  resolveCoreClinicalIconEntry,
} from "./clinicalCoreIcons.js";
import { EXTENDED_CLINICAL_ICON_MAP } from "./clinicalExtendedIcons.js";

export {
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  isClinicalIconName,
  normalizeClinicalIconName,
  normalizeClinicalIconSize,
};

export const CLINICAL_ICON_MAP = Object.freeze({
  ...CORE_CLINICAL_ICON_MAP,
  ...EXTENDED_CLINICAL_ICON_MAP,
});

export const CLINICAL_ICON_NAMES = Object.freeze(
  Object.keys(CLINICAL_ICON_MAP).sort()
);

export function resolveClinicalIconEntry(name) {
  return resolveCoreClinicalIconEntry(name, EXTENDED_CLINICAL_ICON_MAP);
}

export function renderClinicalIconSvg(name, options = {}) {
  return renderSvgFromIconEntry(resolveClinicalIconEntry(name), options);
}
