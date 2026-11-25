// Dedicated AST helper for ADR-2674 bounded ClinicalDrawer overlay enforcement.
// Extracted from eslint.config.mjs so drawer-overlay patterns evolve independently
// of the monolithic root ESLint rule table.

const RAW_DRAWER_OVERLAY_CLASS_REGEX = /drawer-(?:overlay|backdrop|mask|bg)/i;

const EXEMPT_SUBJECT_PORTAL_DRAWER_SUFFIX =
  "/apps/subject-portal/src/components/SyncDrawer.vue";

export function isDisallowedRawDrawerOverlay(filename, overlayClass) {
  if (!overlayClass || !RAW_DRAWER_OVERLAY_CLASS_REGEX.test(overlayClass)) {
    return false;
  }
  const normalized = String(filename || "").replace(/\\/g, "/");
  return !normalized.endsWith(EXEMPT_SUBJECT_PORTAL_DRAWER_SUFFIX);
}
