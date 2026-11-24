import {
  DEMO_SITES,
  DEMO_TENANT_UUID as MANIFEST_DEMO_TENANT_UUID,
} from "demo-fixtures";

export const DEMO_TENANT_UUID =
  MANIFEST_DEMO_TENANT_UUID || "00000000-0000-0000-0000-000000000010"; // deid-ignore
export const DEMO_SUBSCRIPTION_UUID = "00000000-0000-0000-0000-000000000011"; // deid-ignore

export const STUDY_UUID = "00000000-0000-0000-0000-000000000001"; // deid-ignore
export const STUDY_REF = `study:${STUDY_UUID}`;
export const VERSION_V1_UUID = "00000000-0000-0000-0000-000000000101"; // deid-ignore
export const VERSION_V2_UUID = "00000000-0000-0000-0000-000000000102"; // deid-ignore
export const VERSION_V1_REF = `protocol_version:${VERSION_V1_UUID}`;
export const VERSION_V2_REF = `protocol_version:${VERSION_V2_UUID}`;
export const SITE_101_UUID = "00000000-0000-0000-0000-000000000201"; // deid-ignore
export const SITE_102_UUID = "00000000-0000-0000-0000-000000000202"; // deid-ignore
export const ENROLLMENT_1001_UUID = "00000000-0000-0000-0000-000000001001"; // deid-ignore
export const ENROLLMENT_1002_UUID = "00000000-0000-0000-0000-000000001002"; // deid-ignore
export const OBS_1_UUID = "00000000-0000-0000-0000-000000000001"; // deid-ignore
export const OBS_2_UUID = "00000000-0000-0000-0000-000000000002"; // deid-ignore
export const OBS_3_UUID = "00000000-0000-0000-0000-000000000003"; // deid-ignore
export const OBS_4_UUID = "00000000-0000-0000-0000-000000000004"; // deid-ignore
export const DEFAULT_FORM_UUID = "00000000-0000-0000-0000-000000002001"; // deid-ignore
export const DEFAULT_QUERY_UUID = "00000000-0000-0000-0000-000000003001"; // deid-ignore
export const PROPOSAL_1_REF = "proposal:00000000-0000-0000-0000-000000004001"; // deid-ignore

export function cloneJson(value) {
  return JSON.parse(JSON.stringify(value));
}

export function makeUuidFromIndex(prefixHex, index) {
  const suffix = String(index).padStart(12, "0").slice(-12);
  return `${prefixHex}-0000-4000-8000-${suffix}`; // deid-ignore
}

export function computeContentHash(items) {
  const raw = JSON.stringify(
    items.map((item) => ({
      id: item.item_id,
      rev: item.revision,
      val: item.value,
    }))
  );
  let hash = 0x811c9dc5;
  for (let i = 0; i < raw.length; i += 1) {
    hash ^= raw.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return `sha256-demo-${(hash >>> 0).toString(16).padStart(8, "0")}`;
}

export function buildDecision(
  capability,
  resource,
  permitted = true,
  reason = null
) {
  return {
    capability,
    verb_class: capability.includes("read") ? "read" : "write",
    resource,
    permitted,
    reason,
    explanation: {
      subject: "usr-demo-operator",
      claim: permitted
        ? `Capability ${capability} permitted in isolated demo session.`
        : `Capability ${capability} restricted: ${reason || "policy"}.`,
      producer: "demo-kernel-authorizer",
      steps: [
        {
          claim: "Evaluated active demo session role bindings",
          evidence: [`resource=${resource}`, `capability=${capability}`],
        },
      ],
      conditions: [
        {
          code: "DEMO_SESSION_ACTIVE",
          description: "Synthetic demo workspace authorization active",
          met: permitted,
          evidence: ["mode=demo"],
        },
      ],
      next_actions: [],
    },
  };
}

export function resolveCollectionTitle(collectionSpecId) {
  const normalized = String(collectionSpecId || "")
    .trim()
    .toLowerCase();
  if (normalized === "form-vital-signs" || normalized === "form-vitals") {
    return "Vital Signs (CDASH VS)";
  }
  if (normalized === "form-chem-panel") {
    return "Clinical Chemistry Panel (CDASH LB)";
  }
  if (normalized === "form-adverse-event") {
    return "Adverse Event (CDASH AE)";
  }
  return null;
}

export function resolveSiteUuid(siteCode) {
  const normalized = String(siteCode || "SITE-101")
    .trim()
    .toUpperCase();
  const manifestSite = DEMO_SITES.find((s) => s.code === normalized);
  if (manifestSite && normalized !== "SITE-101" && normalized !== "SITE-102") {
    return manifestSite.studySiteUuid;
  }
  if (normalized === "SITE-101") return SITE_101_UUID;
  if (normalized === "SITE-102") return SITE_102_UUID;
  const digits = normalized.replace(/\D/g, "") || "299";
  return makeUuidFromIndex("00000200", Number(digits));
}

export function resolveSiteCodeFromRef(
  nodeRef,
  subjects = [],
  studySites = null
) {
  const unpinned = String(nodeRef || "").split("@")[0];
  const raw = unpinned.replace(/^study_site:/, "");
  if (studySites) {
    for (const links of studySites.values()) {
      const found = links.find(
        (item) =>
          item.study_site_ref === unpinned ||
          item.study_site_id === raw ||
          String(item.ref || "").split("@")[0] === unpinned
      );
      if (found?.site_code) return found.site_code;
    }
  }
  const manifestSite = DEMO_SITES.find(
    (s) =>
      s.studySiteUuid === raw ||
      String(s.studySiteRef || "").split("@")[0] === unpinned
  );
  if (manifestSite) return manifestSite.code;
  if (raw === SITE_101_UUID) return "SITE-101";
  if (raw === SITE_102_UUID) return "SITE-102";
  for (const subj of subjects) {
    if (subj.site_id && resolveSiteUuid(subj.site_id) === raw) {
      return subj.site_id;
    }
  }
  return raw.toUpperCase().startsWith("SITE-") ? raw.toUpperCase() : "SITE-101";
}
