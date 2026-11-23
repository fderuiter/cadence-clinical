import {
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  CORE_CLINICAL_ICON_MAP,
  isClinicalIconName,
  normalizeClinicalIconName,
  normalizeClinicalIconSize,
  registerExtendedClinicalIconLoader,
  resolveCoreClinicalIconEntry,
} from "./clinicalCoreIcons.js";
import { renderSvgFromIconEntry } from "./clinicalIconSvg.js";
import { EXTENDED_CLINICAL_ICON_MAP } from "./clinicalExtendedIcons.js";
import { enablePortalClinicalIcons } from "./clinicalPortalIcons.js";

export {
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  enablePortalClinicalIcons,
  isClinicalIconName,
  normalizeClinicalIconName,
  normalizeClinicalIconSize,
};

export const CLINICAL_ICON_MAP = /* @__PURE__ */ Object.freeze(
  /* @__PURE__ */ Object.assign(
    {},
    CORE_CLINICAL_ICON_MAP,
    EXTENDED_CLINICAL_ICON_MAP
  )
);

export const CLINICAL_ICON_NAMES = /* @__PURE__ */ Object.freeze(
  /* @__PURE__ */ Object.keys(CLINICAL_ICON_MAP).sort()
);

export function enableExtendedClinicalIcons() {
  return registerExtendedClinicalIconLoader(() =>
    import("./clinicalExtendedIconsLazy.js").then(
      (mod) => mod.EXTENDED_CLINICAL_ICON_MAP
    )
  );
}

export function resolveClinicalIconEntry(name) {
  return resolveCoreClinicalIconEntry(name);
}

export function renderClinicalIconSvg(name, options = {}) {
  return renderSvgFromIconEntry(resolveCoreClinicalIconEntry(name), options);
}
