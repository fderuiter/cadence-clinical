/**
 * Shared NodeRef parser and peek-drawer handler registry for <EntityLink>,
 * <ClinicalExplanation>, and <ClinicalGatedAction> (ADR-2482, K-EXP-2).
 */
let activePeekHandler = null;

const KIND_LABELS = {
  tenant: "Workspace",
  sponsor: "Sponsor",
  person: "Person",
  site: "Clinical Site",
  study: "Study",
  study_site: "Study Site",
  protocol_version: "Protocol Version",
  protocol_element: "Protocol Element",
  participant: "Participant",
  enrollment: "Enrollment",
  observation: "Observation",
  observation_collection: "eCRF Form",
  assertion: "Lock / Assertion",
  attestation: "Signature / Attestation",
  validation_rule_pack: "Validation Rule Pack",
  validation_rule_pack_version: "Rule Pack Version",
  validation_run: "Validation Run",
  validation_finding: "Validation Finding",
  grant: "Authority Grant",
  role: "Role",
  qualification: "Training Record",
  qualification_requirement: "Training Requirement",
  subscription: "Workspace Subscription",
  entitlement: "Product Entitlement",
  proposal: "Agent Proposal",
};

const MASK_REASON_LABELS = {
  NotPermitted: "Restricted record",
  Blinded: "Blinded treatment record",
  IdentityVault: "Protected identity record",
};

export function parseNodeRef(raw) {
  const value = typeof raw === "string" ? raw.trim() : "";
  if (!value) {
    return {
      raw: "",
      kind: "entity",
      kindLabel: "Record",
      id: null,
      version: null,
      masked: false,
      maskReason: null,
      displayLabel: "Record",
      shortRef: "",
    };
  }

  const maskedMatch = /^masked:([^:]+):(.+)$/.exec(value);
  if (maskedMatch) {
    const kind = maskedMatch[1];
    const maskReason = maskedMatch[2];
    const kindLabel = KIND_LABELS[kind] || kind.replace(/_/g, " ");
    const reasonLabel = MASK_REASON_LABELS[maskReason] || "Restricted record";
    return {
      raw: value,
      kind,
      kindLabel,
      id: null,
      version: null,
      masked: true,
      maskReason,
      displayLabel: `${reasonLabel} (${kindLabel})`,
      shortRef: `masked:${kind}`,
    };
  }

  const refMatch = /^([a-z0-9_]+):([^@]+)(?:@(\d+))?$/i.exec(value);
  if (refMatch) {
    const kind = refMatch[1];
    const id = refMatch[2];
    const version = refMatch[3] ? Number(refMatch[3]) : null;
    const kindLabel = KIND_LABELS[kind] || kind.replace(/_/g, " ");
    const compactId = /^[0-9a-f-]{32,36}$/i.test(id) ? id.slice(0, 8) : id;
    const versionSuffix = version !== null ? ` v${version}` : "";
    return {
      raw: value,
      kind,
      kindLabel,
      id,
      version,
      masked: false,
      maskReason: null,
      displayLabel: `${kindLabel}${versionSuffix}`,
      shortRef: `${kind}:${compactId}${version !== null ? `@${version}` : ""}`,
    };
  }

  return {
    raw: value,
    kind: "entity",
    kindLabel: "Record",
    id: value,
    version: null,
    masked: false,
    maskReason: null,
    displayLabel: value,
    shortRef: value,
  };
}

export function stripRawPermissionTokens(text) {
  if (typeof text !== "string" || !text) {
    return "";
  }
  return text
    .replace(/\(\s*[a-z0-9_]+[:.][a-z0-9_*]+\s*\)/gi, "")
    .replace(/\s{2,}/g, " ")
    .trim();
}

export function registerEntityPeekHandler(handler) {
  activePeekHandler = typeof handler === "function" ? handler : null;
  return () => {
    if (activePeekHandler === handler) {
      activePeekHandler = null;
    }
  };
}

export function openEntityPeek(nodeRef, context = {}) {
  const parsed = parseNodeRef(nodeRef);
  if (parsed.masked || !parsed.raw) {
    return false;
  }
  if (typeof activePeekHandler === "function") {
    activePeekHandler({ nodeRef: parsed.raw, parsed, ...context });
    return true;
  }
  return false;
}
