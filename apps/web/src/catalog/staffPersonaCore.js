/**
 * Lightweight cold-boot role normalization, persona preset metadata, and UX-6
 * lens role intersection shared across auth, router, shell, and onboarding.
 */

const SITE_ROLES = ["site_investigator", "crc"];
const PI_ROLES = ["principal_investigator", ...SITE_ROLES];
const MONITOR_ROLES = ["cra", "monitor"];
const SAFETY_CORE_ROLES = ["safety_reviewer", "safety_officer"];
const AUDITOR_ROLES = ["auditor", "tmf_auditor"];

export const ADMIN_ROLE_ALIASES = [
  "sponsor_admin",
  "sponsor_designer",
  "data_manager",
  ...PI_ROLES,
  ...MONITOR_ROLES,
  ...SAFETY_CORE_ROLES,
  ...AUDITOR_ROLES,
  "pharmacist",
  "unblinded_statistician",
];

export const ROLE_ALIASES = {
  crc: SITE_ROLES,
  trial_coordinator: [...SITE_ROLES, "trial_coordinator"],
  site_investigator: SITE_ROLES,
  principal_investigator: PI_ROLES,
  cra: MONITOR_ROLES,
  monitor: MONITOR_ROLES,
  safety_reviewer: SAFETY_CORE_ROLES,
  safety_officer: SAFETY_CORE_ROLES,
  safety_physician: ["safety_physician", ...SAFETY_CORE_ROLES],
  qppv: ["qppv", ...SAFETY_CORE_ROLES],
  auditor: AUDITOR_ROLES,
  tmf_auditor: AUDITOR_ROLES,
  pharmacist: ["pharmacist"],
  unblinded_statistician: ["unblinded_statistician"],
  designer: ["sponsor_designer"],
  sponsor_designer: ["sponsor_designer"],
  study_designer: ["sponsor_designer"],
  admin: ADMIN_ROLE_ALIASES,
  sponsor_admin: ADMIN_ROLE_ALIASES,
  super_admin: ADMIN_ROLE_ALIASES,
};

export const PERSONA_PRESETS = [
  {
    key: "super_admin",
    label: "Sponsor Administrator",
    badge: "Admin",
    icon: "shield-check",
    requires: ["sponsor_admin"],
  },
  {
    key: "sponsor_designer",
    label: "Sponsor Protocol Designer",
    badge: "Designer",
    icon: "book-open",
    requires: ["sponsor_designer"],
  },
  {
    key: "data_manager",
    label: "Clinical Data Manager",
    badge: "DM",
    icon: "database",
    requires: ["data_manager"],
  },
  {
    key: "site_crc",
    label: "Site Coordinator / CRC",
    badge: "Site",
    icon: "clipboard-list",
    requires: ["site_investigator", "crc", "trial_coordinator"],
  },
  {
    key: "site_investigator",
    label: "Principal Investigator",
    badge: "PI",
    icon: "file-signature",
    requires: ["principal_investigator"],
  },
  {
    key: "cra_monitor",
    label: "CRA Clinical Monitor",
    badge: "Monitor",
    icon: "search",
    requires: ["cra", "monitor"],
  },
  {
    key: "safety_officer",
    label: "Pharmacovigilance & Safety",
    badge: "Safety",
    icon: "alert-triangle",
    requires: ["safety_reviewer", "safety_officer", "safety_physician", "qppv"],
  },
  {
    key: "auditor",
    label: "GxP & Part 11 Auditor",
    badge: "Auditor",
    icon: "lock",
    requires: ["auditor", "tmf_auditor"],
  },
  {
    key: "pharmacist",
    label: "Site Pharmacist",
    badge: "Pharmacy",
    icon: "check-circle",
    requires: ["pharmacist"],
  },
  {
    key: "unblinded_statistician",
    label: "Unblinded Statistician",
    badge: "Biostats",
    icon: "trending-up",
    requires: ["unblinded_statistician"],
  },
];

export function normalizeRole(role) {
  const normalized = (role || "")
    .trim()
    .toLowerCase()
    .replace(/[\s-_]+/g, "_");
  if (
    normalized === "study_designer" ||
    normalized === "designer" ||
    normalized === "sponsor_designer"
  ) {
    return "sponsor_designer";
  }
  if (normalized === "admin" || normalized === "super_admin") {
    return "sponsor_admin";
  }
  if (
    normalized === "site_crc" ||
    normalized === "clinical_research_coordinator"
  ) {
    return "crc";
  }
  if (normalized === "pi") {
    return "principal_investigator";
  }
  if (normalized === "cra_monitor" || normalized === "clinical_monitor") {
    return "cra";
  }
  if (normalized === "safety_officer") {
    return "safety_reviewer";
  }
  return normalized;
}

export function expandGrantedRoles(rawRoles) {
  const expanded = new Set();
  for (const raw of rawRoles || []) {
    const normalized = normalizeRole(raw);
    if (!normalized) continue;
    expanded.add(normalized);
    for (const implied of ROLE_ALIASES[normalized] || []) {
      expanded.add(implied);
    }
  }
  return Array.from(expanded);
}

export function holdsAnyRole(rawRoles, targets) {
  const normalizedTargets = (targets || []).map(normalizeRole).filter(Boolean);
  if (normalizedTargets.length === 0) return true;
  const expanded = expandGrantedRoles(rawRoles);
  return normalizedTargets.some((target) => expanded.includes(target));
}

export function personasForRoles(rawRoles) {
  return PERSONA_PRESETS.filter((p) => holdsAnyRole(rawRoles, p.requires));
}

export function resolveDefaultPersonaForRoles(rawRoles) {
  const entitled = personasForRoles(rawRoles);
  if (entitled.length === 0) return null;

  const normalized = (rawRoles || []).map(normalizeRole).filter(Boolean);
  const primaryRole = normalized[0] || "";

  if (
    normalized.includes("principal_investigator") &&
    (primaryRole === "site_investigator" ||
      primaryRole === "principal_investigator")
  ) {
    const piPreset = entitled.find((p) => p.key === "site_investigator");
    if (piPreset) return piPreset.key;
  }

  if (primaryRole) {
    const directPrimary = entitled.find((p) =>
      p.requires.includes(primaryRole)
    );
    if (directPrimary) return directPrimary.key;
  }

  return entitled[0]?.key || null;
}

export function resolvePersonaCatalogKey(personaKey, rawRoles = []) {
  if (personaKey && PERSONA_LENS_ROLES[personaKey]) {
    return personaKey;
  }
  if (personaKey === "designer" || personaKey === "study_designer")
    return "sponsor_designer";
  if (personaKey === "crc") return "site_crc";
  if (personaKey === "principal_investigator") return "site_investigator";
  if (personaKey === "cra" || personaKey === "monitor") return "cra_monitor";
  if (personaKey === "safety_reviewer" || personaKey === "safety_physician")
    return "safety_officer";
  if (personaKey === "tmf_auditor") return "auditor";
  if (personaKey === "sponsor_admin" || personaKey === "admin")
    return "super_admin";
  return resolveDefaultPersonaForRoles(rawRoles) || "super_admin";
}

export const PERSONA_LENS_ROLES = {
  super_admin: ADMIN_ROLE_ALIASES,
  sponsor_designer: ["sponsor_designer"],
  data_manager: ["data_manager"],
  site_crc: SITE_ROLES,
  site_investigator: PI_ROLES,
  cra_monitor: MONITOR_ROLES,
  safety_officer: SAFETY_CORE_ROLES,
  auditor: AUDITOR_ROLES,
  pharmacist: ["pharmacist"],
  unblinded_statistician: ["unblinded_statistician"],
};

export function resolveLensEffectiveRoles(personaKey, rawRoles = []) {
  const resolvedKey = resolvePersonaCatalogKey(personaKey, rawRoles);
  const lensRoles = PERSONA_LENS_ROLES[resolvedKey];

  const hasExplicitRoles = Array.isArray(rawRoles) && rawRoles.length > 0;
  if (!hasExplicitRoles) {
    return lensRoles ? [...lensRoles] : ["sponsor_admin"];
  }

  const expandedGranted = expandGrantedRoles(rawRoles);
  if (!lensRoles || resolvedKey === "super_admin") {
    return expandedGranted;
  }

  const lensSet = expandGrantedRoles(lensRoles);
  const intersection = lensSet.filter((role) => expandedGranted.includes(role));
  if (intersection.length > 0) {
    return intersection;
  }
  return expandedGranted;
}
