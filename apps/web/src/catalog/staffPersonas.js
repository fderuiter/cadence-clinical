import {
  ADMIN_ROLE_ALIASES,
  PERSONA_LENS_ROLES,
  PERSONA_PRESETS,
  ROLE_ALIASES,
  expandGrantedRoles,
  holdsAnyRole,
  normalizeRole,
  personasForRoles,
  resolveDefaultPersonaForRoles,
  resolveLensEffectiveRoles,
  resolvePersonaCatalogKey,
} from "./staffPersonaCore.js";
import {
  DEFAULT_SETUP_TASKS,
  PERSONA_CHECKLISTS,
  PERSONA_TOUR_SEQUENCES,
} from "./staffPersonaOnboarding.js";
import {
  PERSONA_LAUNCHERS,
  PERSONA_ORIENTATIONS,
} from "./staffPersonaOverview.js";

export {
  ADMIN_ROLE_ALIASES,
  DEFAULT_SETUP_TASKS,
  PERSONA_CHECKLISTS,
  PERSONA_LAUNCHERS,
  PERSONA_LENS_ROLES,
  PERSONA_ORIENTATIONS,
  PERSONA_PRESETS,
  PERSONA_TOUR_SEQUENCES,
  ROLE_ALIASES,
  expandGrantedRoles,
  holdsAnyRole,
  normalizeRole,
  personasForRoles,
  resolveDefaultPersonaForRoles,
  resolveLensEffectiveRoles,
  resolvePersonaCatalogKey,
};

export const STAFF_PERSONA_CATALOG = Object.freeze(
  Object.fromEntries(
    PERSONA_PRESETS.map((preset) => [
      preset.key,
      Object.freeze({
        ...preset,
        lensRoles: PERSONA_LENS_ROLES[preset.key],
        orientation: PERSONA_ORIENTATIONS[preset.key],
        checklist: PERSONA_CHECKLISTS[preset.key],
        launchers: PERSONA_LAUNCHERS[preset.key],
        tourSequence: PERSONA_TOUR_SEQUENCES[preset.key],
      }),
    ])
  )
);

export function getPersonaCatalogEntry(personaKey, rawRoles = []) {
  const resolvedKey = resolvePersonaCatalogKey(personaKey, rawRoles);
  return (
    STAFF_PERSONA_CATALOG[resolvedKey] || STAFF_PERSONA_CATALOG.super_admin
  );
}
