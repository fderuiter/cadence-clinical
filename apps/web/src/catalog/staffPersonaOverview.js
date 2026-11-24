import {
  PERSONA_PRESETS,
  resolvePersonaCatalogKey,
} from "./staffPersonaCore.js";

export const PERSONA_ORIENTATIONS = Object.freeze({
  super_admin: {
    icon: "shield-check",
    title: "Sponsor Administration Workspace",
    roleName: "Sponsor Administrator",
    description:
      "Full oversight across study workspaces, clinical data collection, site readiness, essential documents, and regulatory governance.",
  },
  sponsor_designer: {
    icon: "book-open",
    title: "Sponsor Protocol & Design Workspace",
    roleName: "Sponsor Protocol Designer",
    description:
      "Structure study lifecycle stages, inspect recorded protocol versions, and review clinical schedules.",
  },
  data_manager: {
    icon: "database",
    title: "Clinical Data Management Workspace",
    roleName: "Clinical Data Manager",
    description:
      "Review clinical case report forms, field validation rules, discrepancy query resolution, and database lock readiness.",
  },
  site_crc: {
    icon: "clipboard-list",
    title: "Site Investigator & Coordinator Workspace",
    roleName: "Site Coordinator / CRC",
    description:
      "Manage participant enrollment, record clinical visit observations, and track site operational readiness.",
  },
  site_investigator: {
    icon: "file-signature",
    title: "Principal Investigator Workspace",
    roleName: "Principal Investigator (PI)",
    description:
      "Oversee site clinical conduct, review participant case report forms, and apply 21 CFR Part 11 investigator signatures.",
  },
  cra_monitor: {
    icon: "search",
    title: "Clinical Monitoring & Site Readiness",
    roleName: "CRA / Clinical Monitor",
    description:
      "Perform source data verification, track site activation readiness, and monitor study conduct.",
  },
  safety_officer: {
    icon: "alert-triangle",
    title: "Pharmacovigilance & Safety Review Workspace",
    roleName: "Safety Officer / QPPV",
    description:
      "Monitor serious adverse event narratives, expedited case reporting, and aggregate pharmacovigilance governance.",
  },
  auditor: {
    icon: "lock",
    title: "GxP & Part 11 Regulatory Workspace",
    roleName: "GxP Auditor",
    description:
      "Inspect immutable study activity, protocol version provenance, essential regulatory documents, and 21 CFR Part 11 governance decisions.",
  },
  pharmacist: {
    icon: "check-circle",
    title: "Investigational Pharmacy & Supply Workspace",
    roleName: "Unblinded Site Pharmacist",
    description:
      "Verify investigational product kit inventory, dispensation accountability, temperature excursion handling, and site pharmacy readiness.",
  },
  unblinded_statistician: {
    icon: "trending-up",
    title: "Unblinded Biostatistics & Randomization Workspace",
    roleName: "Unblinded Statistician",
    description:
      "Draft and attest encrypted randomization schemes under two-person control, monitor allocation balance, and oversee analysis readiness.",
  },
});

export const PERSONA_LAUNCHERS = Object.freeze({
  super_admin: [
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Open a study workspace to navigate lifecycle stages, participant status, protocol versions, and activity.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "data-collection",
      title: "Data collection",
      icon: "clipboard-list",
      description:
        "Capture clinical visit observations, validate case report forms, and review discrepancy resolution.",
      routeSpec: "ecrf",
      badgeText: "Capture · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "site-readiness",
      title: "Site readiness",
      icon: "trending-up",
      description:
        "Track site activation requirements, startup milestones, and operational site readiness.",
      routeSpec: "site-readiness",
      badgeText: "Orbit · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "essential-documents",
      title: "Essential documents",
      icon: "folder",
      description:
        "Inspect trial master file artifacts, completeness milestones, and regulatory filing status.",
      routeSpec: "etmf",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  sponsor_designer: [
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Open a study workspace to inspect recorded protocol versions, schedules, and amendment governance.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "knowledge",
      title: "Knowledge & SOPs",
      icon: "file-text",
      description:
        "Browse published protocol design standard operating procedures and governance references.",
      routeSpec: "knowledge",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "training",
      title: "Study training",
      icon: "check-circle",
      description:
        "Complete role-assigned protocol design training and qualification attestations.",
      routeSpec: "training",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  data_manager: [
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Inspect study-wide participant progress, data lock milestones, and submission exports.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "data-collection",
      title: "Data collection",
      icon: "clipboard-list",
      description:
        "Review case report forms, automated edit checks, and discrepancy query workflows.",
      routeSpec: "ecrf",
      badgeText: "Capture · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "site-readiness",
      title: "Site readiness",
      icon: "trending-up",
      description:
        "Monitor site activation state and data capture readiness across clinical sites.",
      routeSpec: "site-readiness",
      badgeText: "Orbit · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "essential-documents",
      title: "Essential documents",
      icon: "folder",
      description:
        "Verify data management plans and essential regulatory documentation.",
      routeSpec: "etmf",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  site_crc: [
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Open your assigned study workspace to review participant schedules and site conduct status.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "data-collection",
      title: "Data collection",
      icon: "clipboard-list",
      description:
        "Screen participants, enter scheduled visit forms, and respond to open data queries.",
      routeSpec: "ecrf",
      badgeText: "Capture · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "site-readiness",
      title: "Site readiness",
      icon: "trending-up",
      description:
        "Complete site startup tasks and maintain operational activation readiness.",
      routeSpec: "site-readiness",
      badgeText: "Orbit · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "site-documents",
      title: "Site regulatory files",
      icon: "folder",
      description:
        "Upload and maintain essential investigator site documents and regulatory binders.",
      routeSpec: "etmf",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  site_investigator: [
    {
      id: "data-collection",
      title: "Casebook review & sign-off",
      icon: "file-signature",
      description:
        "Inspect participant case report forms and apply 21 CFR Part 11 electronic signatures.",
      routeSpec: "ecrf",
      badgeText: "Capture · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Review site participant safety, delegation records, and protocol governance.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "site-readiness",
      title: "Site readiness",
      icon: "trending-up",
      description:
        "Confirm site startup obligations and investigator oversight milestones.",
      routeSpec: "site-readiness",
      badgeText: "Orbit · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "training",
      title: "Investigator training",
      icon: "check-circle",
      description:
        "Attest to protocol amendments and GCP qualification requirements.",
      routeSpec: "training",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  cra_monitor: [
    {
      id: "site-readiness",
      title: "Site readiness & monitoring",
      icon: "trending-up",
      description:
        "Track site startup greenlight gates, delegation logs, and monitoring visit follow-up.",
      routeSpec: "site-readiness",
      badgeText: "Orbit · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "data-collection",
      title: "Data collection & SDV",
      icon: "clipboard-list",
      description:
        "Perform source data verification and raise discrepancy queries on clinical forms.",
      routeSpec: "ecrf",
      badgeText: "Capture · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Inspect study conduct metrics, site enrollment, and risk indicators.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "essential-documents",
      title: "Essential documents",
      icon: "folder",
      description:
        "Verify essential regulatory documents and site filing completeness.",
      routeSpec: "etmf",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  safety_officer: [
    {
      id: "safety",
      title: "Safety & pharmacovigilance",
      icon: "alert-triangle",
      description:
        "Assess serious adverse events, author clinical narratives, and compile expedited safety submissions.",
      routeSpec: "safety",
      badgeText: "Vigil · Demo",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "study",
      title: "Studies",
      icon: "book-open",
      description:
        "Inspect study conduct status, safety indicators, and protocol governance.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "tickets",
      title: "Escalation tickets",
      icon: "message-square",
      description:
        "Coordinate medical review follow-ups and audited safety support escalations.",
      routeSpec: "tickets",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
    {
      id: "knowledge",
      title: "Safety SOPs",
      icon: "file-text",
      description:
        "Access pharmacovigilance case processing and aggregate reporting guidance.",
      routeSpec: "knowledge",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  auditor: [
    {
      id: "study",
      title: "Studies & audit provenance",
      icon: "book-open",
      description:
        "Inspect immutable study activity streams, protocol hashes, and 21 CFR Part 11 electronic attestations.",
      routeSpec: "study",
      badgeText: "Blueprint · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "essential-documents",
      title: "Essential documents",
      icon: "folder",
      description:
        "Perform read-only regulatory inspection across filed trial master file records.",
      routeSpec: "etmf",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "knowledge",
      title: "Controlled SOP library",
      icon: "file-text",
      description:
        "Inspect effective standard operating procedures and quality governance guidance.",
      routeSpec: "knowledge",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  pharmacist: [
    {
      id: "study",
      title: "Studies & investigational supply",
      icon: "book-open",
      description:
        "Open your site-scoped study workspace to verify investigational product kits and accountability.",
      routeSpec: "study",
      badgeText: "Allocate · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "training",
      title: "Pharmacy qualification",
      icon: "check-circle",
      description:
        "Maintain unblinded pharmacy handling and temperature excursion qualification.",
      routeSpec: "training",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "tickets",
      title: "Supply & excursion support",
      icon: "message-square",
      description:
        "Coordinate depot shipments, quarantine reviews, and supply chain support.",
      routeSpec: "tickets",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
    {
      id: "knowledge",
      title: "Pharmacy SOPs",
      icon: "file-text",
      description:
        "Consult investigational product accountability and blinding-protection procedures.",
      routeSpec: "knowledge",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
  unblinded_statistician: [
    {
      id: "study",
      title: "Studies & randomization schemes",
      icon: "trending-up",
      description:
        "Draft or attest encrypted randomization schemes and inspect Analyze stage readiness in the study workspace.",
      routeSpec: "study",
      badgeText: "Lens · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "knowledge",
      title: "Biostatistics & blinding SOPs",
      icon: "file-text",
      description:
        "Consult statistical analysis, randomization sealing, and unblinded access SOPs.",
      routeSpec: "knowledge",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: true,
    },
    {
      id: "training",
      title: "Unblinded qualification",
      icon: "check-circle",
      description:
        "Attest to unblinded data handling and separation-of-duties requirements.",
      routeSpec: "training",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
    {
      id: "tickets",
      title: "Statistical support queue",
      icon: "message-square",
      description:
        "Coordinate audited biostatistics and randomization governance requests.",
      routeSpec: "tickets",
      badgeText: "Assure · Live",
      badgeClass: "badge-active",
      featured: false,
    },
  ],
});

export function getPersonaOverviewEntry(personaKey, rawRoles = []) {
  const resolvedKey = resolvePersonaCatalogKey(personaKey, rawRoles);
  const preset =
    PERSONA_PRESETS.find((p) => p.key === resolvedKey) || PERSONA_PRESETS[0];
  return {
    ...preset,
    orientation:
      PERSONA_ORIENTATIONS[resolvedKey] || PERSONA_ORIENTATIONS.super_admin,
    launchers: PERSONA_LAUNCHERS[resolvedKey] || PERSONA_LAUNCHERS.super_admin,
  };
}
