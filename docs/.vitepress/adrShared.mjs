import { HISTORICAL_ADR_DOMAINS } from "./adrHistoricalDomains.mjs";

export const ADR_DOMAINS = Object.freeze([
  {
    index: 0,
    slug: "clinical-kernel",
    shortLabel: "Clinical Kernel",
    label: "0. Clinical Kernel (K001–K037)",
    aliases: [
      "0",
      "kernel",
      "clinical-kernel",
      "0. clinical kernel",
      "0. clinical kernel (k001-k037)",
      "0. clinical kernel (k001–k037)",
    ],
  },
  {
    index: 1,
    slug: "core-platform",
    shortLabel: "Core Platform",
    label: "1. Core Platform & Execution Engine",
    aliases: [
      "1",
      "platform",
      "core",
      "core-platform",
      "execution",
      "1. core platform & execution engine",
    ],
  },
  {
    index: 2,
    slug: "security-gateway",
    shortLabel: "Security & Gateway",
    label: "2. API Gateway, Security & Identity",
    aliases: [
      "2",
      "security",
      "gateway",
      "identity",
      "security-gateway",
      "2. api gateway, security & identity",
    ],
  },
  {
    index: 3,
    slug: "interoperability-standards",
    shortLabel: "Standards & Interop",
    label: "3. Clinical Data Interoperability & Standards",
    aliases: [
      "3",
      "standards",
      "interoperability",
      "interoperability-standards",
      "3. clinical data interoperability & standards",
    ],
  },
  {
    index: 4,
    slug: "clinical-operations",
    shortLabel: "Clinical Operations",
    label: "4. Clinical Operations & Business Modules",
    aliases: [
      "4",
      "operations",
      "clinical-operations",
      "modules",
      "4. clinical operations & business modules",
    ],
  },
  {
    index: 5,
    slug: "compliance-governance",
    shortLabel: "Compliance & Audit",
    label: "5. Compliance, Audit & Governance",
    aliases: [
      "5",
      "compliance",
      "audit",
      "governance",
      "compliance-governance",
      "5. compliance, audit & governance",
    ],
  },
  {
    index: 6,
    slug: "frontend-design-system",
    shortLabel: "Frontend & UI",
    label: "6. Frontend & Design System",
    aliases: [
      "6",
      "frontend",
      "ui",
      "design-system",
      "frontend-design-system",
      "6. frontend & design system",
    ],
  },
  {
    index: 7,
    slug: "devops-tooling",
    shortLabel: "DevOps & Tooling",
    label: "7. DevOps, Tooling & CI/CD",
    aliases: [
      "7",
      "tooling",
      "devops",
      "ci",
      "cicd",
      "devops-tooling",
      "7. devops, tooling & ci/cd",
    ],
  },
]);

function inferBlockDomainIndex(number) {
  if (!Number.isInteger(number)) return 1;
  if (
    (number >= 2446 && number <= 2459) ||
    (number >= 2480 && number <= 2489) ||
    (number >= 2600 && number <= 2609) ||
    (number >= 2620 && number <= 2639)
  ) {
    return 0;
  }
  if (number >= 2460 && number <= 2479) return 3;
  if (number >= 2510 && number <= 2519) return 2;
  if (number >= 2530 && number <= 2539) return 5;
  if (number >= 2550 && number <= 2559) return 7;
  if (
    (number >= 2540 && number <= 2549) ||
    (number >= 2590 && number <= 2599) ||
    (number >= 2650 && number <= 2659)
  ) {
    return 6;
  }
  if (number >= 2640 && number <= 2649) return 4;
  return 1;
}

/**
 * Resolves an ADR's canonical domain definition from inline metadata, historical lookup, or number block.
 *
 * @param {{ rawDomain?: string | null, slug?: string, number?: number | null }} options
 */
export function resolveAdrDomain({ rawDomain, slug, number } = {}) {
  if (typeof rawDomain === "string" && rawDomain.trim().length > 0) {
    const cleaned = rawDomain
      .trim()
      .replace(/^["']|["']$/g, "")
      .trim()
      .toLowerCase();
    const matched = ADR_DOMAINS.find(
      (domain) =>
        domain.label.toLowerCase() === cleaned ||
        domain.slug === cleaned ||
        domain.aliases.includes(cleaned)
    );
    if (matched) {
      return matched;
    }
  }

  if (slug && Object.hasOwn(HISTORICAL_ADR_DOMAINS, slug)) {
    return ADR_DOMAINS[HISTORICAL_ADR_DOMAINS[slug]] ?? ADR_DOMAINS[1];
  }

  return ADR_DOMAINS[inferBlockDomainIndex(number)] ?? ADR_DOMAINS[1];
}

/**
 * Filters ADR records by domain slug (`"all"` for every domain) and search query.
 *
 * @param {Array<any>} adrs
 * @param {{ domainSlug?: string, query?: string }} filters
 */
export function filterAdrEntries(
  adrs,
  { domainSlug = "all", query = "" } = {}
) {
  const normalizedDomain = String(domainSlug || "all")
    .trim()
    .toLowerCase();
  const normalizedQuery = String(query || "")
    .trim()
    .toLowerCase();

  return (adrs ?? []).filter((adr) => {
    if (
      normalizedDomain &&
      normalizedDomain !== "all" &&
      adr.domainSlug !== normalizedDomain
    ) {
      return false;
    }

    if (!normalizedQuery) {
      return true;
    }

    const haystack = [
      adr.code,
      adr.title,
      adr.date,
      adr.status,
      adr.alsoKnownAs,
      adr.domainLabel,
      adr.slug,
    ]
      .filter(Boolean)
      .join(" ")
      .toLowerCase();

    return haystack.includes(normalizedQuery);
  });
}

/**
 * Groups filtered ADR records into ordered canonical domain buckets (omitting empty domains).
 *
 * @param {Array<any>} adrs
 */
export function groupAdrEntriesByDomain(adrs) {
  const byDomain = new Map();
  for (const domain of ADR_DOMAINS) {
    byDomain.set(domain.index, {
      ...domain,
      items: [],
    });
  }

  for (const adr of adrs ?? []) {
    const bucket = byDomain.get(adr.domainIndex) ?? byDomain.get(1);
    bucket.items.push(adr);
  }

  return Array.from(byDomain.values()).filter(
    (group) => group.items.length > 0
  );
}
