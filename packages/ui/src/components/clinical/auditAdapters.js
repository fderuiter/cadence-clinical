const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const ENTITY_TYPE_TO_NODE_KIND = {
  tenant: "tenant",
  organization: "organization",
  person: "person",
  user: "person",
  role: "role",
  grant: "grant",
  qualification: "qualification",
  qualification_requirement: "qualification_requirement",
  study: "study",
  site: "site",
  study_site: "study_site",
  studysite: "study_site",
  participant: "participant",
  enrollment: "enrollment",
  subject: "enrollment",
  protocol: "protocol_version",
  protocol_version: "protocol_version",
  protocolversion: "protocol_version",
  protocol_element: "protocol_element",
  effectivity: "effectivity",
  concept: "concept",
  code_system: "code_system",
  code_system_version: "code_system_version",
  value_set: "value_set",
  standard_version: "standard_version",
  library_item: "library_item",
  mapping_spec: "mapping_spec",
  validation_run: "validation_run",
  validation_finding: "validation_finding",
  finding: "validation_finding",
  form_query: "form_query",
  query: "form_query",
  ecrf_query: "form_query",
  attestation: "attestation",
  signature: "attestation",
  audit_entry: "audit_entry",
  auditentry: "audit_entry",
  artifact: "artifact",
  document: "artifact",
  etmf_document: "artifact",
  artifact_version: "artifact_version",
  work_queue_definition: "work_queue_definition",
  proposal: "proposal",
  agent_task_config: "agent_task_config",
  evaluation_run: "evaluation_run",
  observation: "observation",
  vitalsigns: "observation",
  assertion: "assertion",
  collection_instance: "collection_instance",
  form: "collection_instance",
  ecrf_entry: "collection_instance",
  subscription: "subscription",
  product_entitlement: "product_entitlement",
};

export function isNodeRefString(value) {
  if (!value || typeof value !== "string") return false;
  const trimmed = value.trim();
  const base = trimmed.includes("@") ? trimmed.split("@")[0] : trimmed;
  const colonIdx = base.indexOf(":");
  if (colonIdx <= 0 || colonIdx === base.length - 1) return false;
  const kind = base.slice(0, colonIdx);
  const id = base.slice(colonIdx + 1);
  return /^[a-z][a-z0-9_]*$/.test(kind) && UUID_PATTERN.test(id);
}

export function inferPersonNodeRef(actorValue) {
  if (!actorValue || typeof actorValue !== "string") return null;
  const trimmed = actorValue.trim();
  if (trimmed.startsWith("person:") && isNodeRefString(trimmed)) {
    return trimmed.split("@")[0];
  }
  if (UUID_PATTERN.test(trimmed)) {
    return `person:${trimmed.toLowerCase()}`;
  }
  return null;
}

export function inferTargetNodeRef(entityType, entityId) {
  if (isNodeRefString(entityId)) {
    return String(entityId).trim();
  }
  if (!entityType || !entityId) return null;
  const rawId = String(entityId).trim();
  if (!UUID_PATTERN.test(rawId)) return null;
  const normalizedKind =
    ENTITY_TYPE_TO_NODE_KIND[
      String(entityType)
        .trim()
        .toLowerCase()
        .replace(/[\s-]+/g, "_")
    ] || null;
  return normalizedKind ? `${normalizedKind}:${rawId.toLowerCase()}` : null;
}

function normalizeChanges(rawChanges) {
  if (!Array.isArray(rawChanges)) return [];
  return rawChanges
    .filter((item) => item && typeof item === "object")
    .map((item) => ({
      field: String(item.field ?? item.property ?? item.name ?? "value"),
      old_val:
        item.old_val !== undefined
          ? item.old_val
          : item.before !== undefined
            ? item.before
            : item.oldValue !== undefined
              ? item.oldValue
              : null,
      new_val:
        item.new_val !== undefined
          ? item.new_val
          : item.after !== undefined
            ? item.after
            : item.newValue !== undefined
              ? item.newValue
              : null,
    }));
}

function normalizeAttestation(rawAttestation, fallbackAttestationRef = null) {
  if (rawAttestation && typeof rawAttestation === "object") {
    const attestationRef =
      rawAttestation.attestation_ref ||
      rawAttestation.attestationRef ||
      fallbackAttestationRef ||
      null;
    const actorId = rawAttestation.actor_id || rawAttestation.actorId || "";
    const actorRef =
      rawAttestation.actor_ref ||
      rawAttestation.actorRef ||
      inferPersonNodeRef(actorId);
    return {
      attestation_ref: attestationRef,
      actor_id: actorId,
      actor_ref: actorRef,
      meaning: rawAttestation.meaning || "Signed",
      method: rawAttestation.method || null,
      authenticated_at:
        rawAttestation.authenticated_at ||
        rawAttestation.authenticatedAt ||
        null,
      recorded_at:
        rawAttestation.recorded_at || rawAttestation.recordedAt || null,
      target_content_hash:
        rawAttestation.target_content_hash ||
        rawAttestation.targetContentHash ||
        null,
      reason: rawAttestation.reason || null,
    };
  }
  if (fallbackAttestationRef && isNodeRefString(fallbackAttestationRef)) {
    return {
      attestation_ref: fallbackAttestationRef.trim(),
      actor_id: "",
      actor_ref: null,
      meaning: "Electronic Signature",
      method: null,
      authenticated_at: null,
      recorded_at: null,
      target_content_hash: null,
      reason: null,
    };
  }
  return null;
}

function classifyActionCategory(action, eventKind, mask) {
  if (mask) return "RESTRICTED";
  const upper = String(action || eventKind || "")
    .trim()
    .toUpperCase();
  if (
    upper === "SIGN" ||
    upper === "SIGNED" ||
    upper.includes("ATTEST") ||
    upper.includes("APPROV")
  ) {
    return "SIGN";
  }
  if (
    upper === "CREATE" ||
    upper === "CREATED" ||
    upper === "RECORD" ||
    upper === "RECORDED" ||
    upper === "REGISTERED" ||
    upper === "DETECTED"
  ) {
    return "CREATE";
  }
  if (
    upper === "DELETE" ||
    upper === "DELETED" ||
    upper === "RETRACT" ||
    upper === "RETRACTED"
  ) {
    return "DELETE";
  }
  if (
    upper === "UPDATE" ||
    upper === "UPDATED" ||
    upper === "CORRECT" ||
    upper === "CORRECTED" ||
    upper === "AMEND" ||
    upper === "SUPERSEDED"
  ) {
    return "UPDATE";
  }
  return upper || "EVENT";
}

function humanizeEventType(typeText) {
  if (!typeText) return "Recorded";
  const tail = String(typeText).split(".").pop() || String(typeText);
  const spaced = tail
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .toLowerCase()
    .trim();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1) : "Event";
}

export function normalizeAuditEntry(entry, index = 0) {
  if (!entry || typeof entry !== "object") {
    return {
      id: `audit-${index}`,
      timestamp: "",
      user_id: "",
      action: "EVENT",
      action_category: "EVENT",
      entity_type: "Record",
      entity_id: "",
      changes: [],
      mask: null,
    };
  }

  const mask = entry.mask ?? entry.masked ?? null;
  const auditEntryRef =
    entry.audit_entry_ref ??
    entry.auditEntryRef ??
    (typeof entry.id === "string" && entry.id.startsWith("audit_entry:")
      ? entry.id
      : null);

  const timestamp =
    entry.timestamp ??
    entry.recorded_at ??
    entry.recordedAt ??
    entry.recorded ??
    entry.created_at ??
    entry.createdAt ??
    "";

  const validAt =
    entry.valid_at ??
    entry.validAt ??
    entry.occurred_valid ??
    entry.occurredValid ??
    null;

  const userId =
    entry.user_id ??
    entry.actor_id ??
    entry.actorId ??
    entry.actor ??
    entry.performed_by ??
    entry.performedBy ??
    "";

  const actorRef =
    entry.actor_ref ?? entry.actorRef ?? inferPersonNodeRef(userId);
  const explicitActorLabel = entry.actor_label ?? entry.actor_name ?? null;
  const fallbackActorLabel =
    explicitActorLabel ||
    (!userId
      ? actorRef
        ? null
        : "System"
      : UUID_PATTERN.test(String(userId).trim()) || isNodeRefString(userId)
        ? null
        : String(userId));

  const entityType =
    entry.entity_type ??
    entry.target_kind ??
    entry.targetKind ??
    entry.subject_kind ??
    entry.subjectKind ??
    "Record";

  const rawTargetRef =
    entry.target_ref ??
    entry.targetRef ??
    entry.subject ??
    inferTargetNodeRef(entityType, entry.entity_id ?? entry.entityId);

  const entityId = entry.entity_id ?? entry.entityId ?? rawTargetRef ?? "";

  const action = mask
    ? "RESTRICTED"
    : entry.action ||
      entry.event_kind ||
      entry.eventKind ||
      (entry.type ? humanizeEventType(entry.type) : "EVENT");

  const changes = normalizeChanges(
    entry.changes ?? entry.field_changes ?? entry.fieldChanges
  );

  const attestation = mask
    ? null
    : normalizeAttestation(
        entry.attestation,
        entry.attestation_ref ?? entry.attestationRef ?? null
      );

  const reasonForChange = mask
    ? null
    : (entry.reason_for_change ??
      entry.reasonForChange ??
      entry.reason ??
      attestation?.reason ??
      null);

  return {
    ...entry,
    id:
      entry.id ??
      entry.entry_id ??
      entry.entryId ??
      auditEntryRef ??
      entry.event_id ??
      entry.eventId ??
      `audit-${index}`,
    audit_entry_ref: mask ? null : auditEntryRef,
    timestamp,
    valid_at: mask ? null : validAt,
    user_id: mask ? "" : userId,
    actor_label: mask ? null : fallbackActorLabel,
    actor_ref: mask ? null : actorRef,
    actor_kind: mask ? null : (entry.actor_kind ?? entry.actorKind ?? null),
    action,
    action_category: classifyActionCategory(
      action,
      entry.event_kind ?? entry.eventKind,
      mask
    ),
    event_kind: entry.event_kind ?? entry.eventKind ?? null,
    command_type: mask
      ? null
      : (entry.command_type ?? entry.commandType ?? null),
    outcome: mask ? null : (entry.outcome ?? null),
    entity_type: entityType,
    entity_id: mask ? "" : entityId,
    target_ref: mask ? null : rawTargetRef,
    target_label: mask ? null : (entry.target_label ?? null),
    version: mask ? null : (entry.version ?? null),
    reason_for_change: reasonForChange,
    summary: mask
      ? null
      : (entry.summary ?? entry.details ?? entry.description ?? null),
    changes: mask ? [] : changes,
    attestation,
    sequence: mask ? null : (entry.sequence ?? null),
    hash: mask ? null : (entry.hash ?? null),
    predecessor_hash: mask
      ? null
      : (entry.predecessor_hash ?? entry.predecessorHash ?? null),
    activity_id: mask ? null : (entry.activity_id ?? entry.activityId ?? null),
    correlation_id: mask
      ? null
      : (entry.correlation_id ?? entry.correlationId ?? null),
    what_label: entry.what_label ?? null,
    masked_subject_label: entry.masked_subject_label ?? null,
    raw_actor_id: entry.raw_actor_id ?? null,
    raw_actor_ref: entry.raw_actor_ref ?? null,
    target_href: mask ? null : (entry.target_href ?? null),
    mask,
  };
}

export function adaptKernelHistoryEntries(entries = []) {
  if (!Array.isArray(entries)) return [];
  return entries.map((entry, idx) =>
    normalizeAuditEntry(
      {
        id: entry.entry_id ?? entry.audit_entry_ref ?? `history-${idx}`,
        audit_entry_ref: entry.audit_entry_ref ?? null,
        timestamp: entry.recorded_at,
        valid_at: entry.valid_at ?? null,
        user_id: entry.actor_id ?? "",
        actor_ref: entry.actor_ref ?? null,
        actor_kind: entry.actor_kind ?? null,
        action: entry.action || entry.event_kind || "Recorded",
        event_kind: entry.event_kind ?? null,
        command_type: entry.command_type ?? null,
        outcome: entry.outcome ?? null,
        entity_type: entry.target_kind ?? "Record",
        entity_id: entry.target_ref ?? "",
        target_ref: entry.target_ref ?? null,
        version: entry.version ?? null,
        reason_for_change: entry.reason_for_change ?? null,
        summary: entry.summary ?? null,
        changes: Array.isArray(entry.field_changes)
          ? entry.field_changes.map((fc) => ({
              field: fc.field,
              old_val: fc.before ?? null,
              new_val: fc.after ?? null,
            }))
          : [],
        attestation: entry.attestation ?? null,
        sequence: entry.sequence ?? null,
        hash: entry.hash ?? null,
        predecessor_hash: entry.predecessor_hash ?? null,
        mask: entry.mask ?? null,
      },
      idx
    )
  );
}

export function adaptKernelAuditEntryDetail(detail) {
  if (!detail || typeof detail !== "object") return null;
  return normalizeAuditEntry({
    id: detail.audit_entry_ref || "audit-detail",
    audit_entry_ref: detail.audit_entry_ref ?? null,
    timestamp: detail.recorded_at,
    valid_at: detail.valid_at ?? null,
    user_id: detail.actor_id ?? "",
    actor_ref: detail.actor_ref ?? null,
    actor_kind: detail.actor_kind ?? null,
    action: detail.action || "Recorded",
    command_type: detail.command_type ?? null,
    outcome: detail.outcome ?? null,
    entity_type: detail.target_kind ?? "Record",
    entity_id: detail.target_ref ?? "",
    target_ref: detail.target_ref ?? null,
    version: detail.version ?? null,
    reason_for_change: detail.reason_for_change ?? null,
    summary: detail.summary ?? null,
    changes: Array.isArray(detail.field_changes)
      ? detail.field_changes.map((fc) => ({
          field: fc.field,
          old_val: fc.before ?? null,
          new_val: fc.after ?? null,
        }))
      : [],
    attestation: detail.attestation ?? null,
    activity_id: detail.activity_id ?? null,
    correlation_id: detail.correlation_id ?? null,
    sequence: detail.sequence ?? null,
    hash: detail.hash ?? null,
    predecessor_hash: detail.predecessor_hash ?? null,
    mask: detail.mask ?? null,
  });
}

export function adaptKernelActivityEntries(entries = [], options = {}) {
  if (!Array.isArray(entries)) return [];
  const resolveHref =
    typeof options.resolveSubjectHref === "function"
      ? options.resolveSubjectHref
      : null;
  const formatWhat =
    typeof options.formatWhatLabel === "function"
      ? options.formatWhatLabel
      : null;
  const formatMaskedSubject =
    typeof options.formatMaskedSubjectLabel === "function"
      ? options.formatMaskedSubjectLabel
      : null;

  return entries.map((entry, idx) => {
    const whatLabel = formatWhat
      ? formatWhat(entry.type)
      : humanizeEventType(entry.type);
    const maskedSubjectLabel = entry.mask
      ? entry.masked_subject_label ||
        (formatMaskedSubject
          ? formatMaskedSubject(entry)
          : `Hidden ${String(entry.subject_kind || "record").replace(/_/g, " ")}`)
      : null;
    const normalized = normalizeAuditEntry(
      {
        id: entry.event_id || entry.audit_entry_ref || `activity-${idx}`,
        audit_entry_ref: entry.audit_entry_ref ?? null,
        attestation_ref: entry.attestation_ref ?? null,
        timestamp: entry.recorded,
        valid_at: entry.occurred_valid ?? null,
        user_id: entry.actor_id ?? "",
        action: entry.mask ? "RESTRICTED" : whatLabel,
        event_kind: entry.family ?? null,
        command_type: entry.type ?? null,
        entity_type: entry.subject_kind ?? entry.family ?? "Activity",
        entity_id: entry.subject ?? "",
        target_ref: entry.subject ?? null,
        reason_for_change: entry.reason_for_change ?? null,
        correlation_id: entry.correlation_id ?? null,
        mask: entry.mask ?? null,
      },
      idx
    );
    return {
      ...normalized,
      what_label: whatLabel,
      masked_subject_label: maskedSubjectLabel,
      raw_actor_id: entry.actor_id ?? "",
      raw_actor_ref: inferPersonNodeRef(entry.actor_id ?? ""),
      target_href:
        !entry.mask && entry.subject && resolveHref ? resolveHref(entry) : null,
    };
  });
}

export function adaptObservationAuditEntries(entries = [], context = {}) {
  if (!Array.isArray(entries)) return [];
  return entries.map((entry, idx) => {
    const explicitChanges = Array.isArray(entry.changes)
      ? entry.changes
      : entry.old_value !== undefined || entry.new_value !== undefined
        ? [
            {
              field: entry.field || entry.parameter || context.field || "value",
              old_val: entry.old_value ?? null,
              new_val: entry.new_value ?? null,
            },
          ]
        : [];
    const targetRef =
      entry.target_ref ||
      entry.observation_ref ||
      context.observationRef ||
      inferTargetNodeRef(
        "observation",
        entry.observation_id || context.observationId
      );
    return normalizeAuditEntry(
      {
        ...entry,
        id:
          entry.id ||
          entry.audit_entry_ref ||
          `obs-audit-${entry.sequence ?? idx}`,
        timestamp:
          entry.timestamp || entry.recorded_at || entry.changed_at || "",
        user_id:
          entry.user_id ||
          entry.actor_id ||
          entry.actor ||
          entry.changed_by ||
          entry.recorded_by ||
          "",
        action: entry.action || "UPDATE",
        entity_type: entry.entity_type || "observation",
        entity_id:
          entry.entity_id ||
          targetRef ||
          entry.observation_id ||
          context.observationId ||
          "",
        target_ref: targetRef,
        target_label:
          entry.target_label ||
          context.targetLabel ||
          (entry.subject && !isNodeRefString(entry.subject)
            ? String(entry.subject)
            : null),
        version: entry.version ?? null,
        sequence: entry.sequence ?? null,
        reason_for_change:
          entry.reason_for_change || entry.reason || entry.comment || null,
        changes: explicitChanges,
      },
      idx
    );
  });
}

export function adaptKnowledgeAuditLogs(entries = [], context = {}) {
  if (!Array.isArray(entries)) return [];
  return entries.map((entry, idx) => {
    const changes = Array.isArray(entry.changes) ? [...entry.changes] : [];
    if (
      changes.length === 0 &&
      (entry.code ||
        entry.previous_code ||
        entry.term ||
        entry.dictionary_term ||
        entry.target_status)
    ) {
      if (entry.code || entry.previous_code) {
        changes.push({
          field: "code",
          old_val: entry.previous_code ?? null,
          new_val: entry.code ?? null,
        });
      }
      if (entry.dictionary_term || entry.term) {
        changes.push({
          field: "term",
          old_val: entry.previous_term ?? null,
          new_val: entry.dictionary_term ?? entry.term ?? null,
        });
      }
      if (entry.target_status) {
        changes.push({
          field: "status",
          old_val: entry.previous_status ?? "—",
          new_val: entry.target_status,
        });
      }
    }
    const rawDigest = entry.sha256_hash || entry.hash || null;
    const verifiedHash =
      rawDigest &&
      !String(rawDigest).startsWith("sha256:e3b0c442") &&
      !String(rawDigest).startsWith("e3b0c44298fc1c149afbf4c8996fb924")
        ? rawDigest
        : null;
    return normalizeAuditEntry(
      {
        ...entry,
        id: entry.id || `knowledge-audit-${idx}`,
        timestamp: entry.timestamp || entry.created_at || entry.coded_at || "",
        user_id:
          entry.user_id ||
          entry.actor_user_id ||
          entry.performed_by ||
          entry.coded_by ||
          entry.actor_id ||
          "system",
        action: entry.action || entry.event_type || entry.status || "RECORDED",
        entity_type: entry.entity_type || entry.dictionary || "artifact",
        entity_id:
          entry.entity_id ||
          entry.article_id ||
          context.articleId ||
          entry.item_id ||
          entry.code ||
          `sop-${idx + 1}`,
        target_label: context.articleTitle || entry.article_title || null,
        version: entry.version ?? entry.version_label ?? null,
        reason_for_change:
          entry.reason_for_change || entry.reason || entry.notes || null,
        summary: entry.summary || entry.details || null,
        changes,
        hash: verifiedHash,
      },
      idx
    );
  });
}

export function adaptTicketAuditLogs(entries = [], ticket = null) {
  if (!Array.isArray(entries)) return [];
  return entries.map((entry, idx) => {
    const relatedType =
      entry.entity_type ||
      entry.related_entity_type ||
      ticket?.related_entity_type ||
      ticket?.relatedEntityType ||
      "ticket";
    const relatedId =
      entry.entity_id ||
      entry.related_entity_id ||
      ticket?.related_entity_id ||
      ticket?.relatedEntityId ||
      ticket?.id ||
      "";
    const targetRef =
      entry.target_ref || inferTargetNodeRef(relatedType, relatedId);
    const rawHash = entry.hash || entry.block_hash || null;
    return normalizeAuditEntry(
      {
        ...entry,
        id: entry.id || `ticket-audit-${idx}`,
        timestamp: entry.timestamp || entry.created_at || entry.createdAt || "",
        user_id:
          entry.user_id ||
          entry.actor_id ||
          entry.actorId ||
          entry.author_id ||
          entry.performed_by ||
          "system",
        action: entry.action || entry.event_type || "UPDATE",
        entity_type: relatedType,
        entity_id: relatedId,
        target_ref: targetRef,
        reason_for_change:
          entry.reason_for_change ||
          entry.change_reason ||
          entry.reason ||
          null,
        summary: entry.summary || entry.details || entry.message || null,
        changes: entry.changes || [],
        hash: rawHash,
      },
      idx
    );
  });
}
