<script setup>
import { computed, inject, ref } from "vue";
import ClinicalDrawer from "./ClinicalDrawer.vue";
import EntityLink, { ENTITY_LINK_RESOLVER_KEY } from "./EntityLink.vue";
import SupportDetails from "./SupportDetails.vue";
import {
  adaptKernelAuditEntryDetail,
  normalizeAuditEntry,
} from "./auditAdapters.js";

const DEFAULT_FILTER_OPTIONS = ["CREATE", "UPDATE", "DELETE", "SIGN"];

const props = defineProps({
  logs: {
    type: Array,
    default: () => [],
  },
  entries: {
    type: Array,
    default: null,
  },
  title: {
    type: String,
    default: "21 CFR Part 11 Audit Trail",
  },
  headingTag: {
    type: String,
    default: "h3",
  },
  variant: {
    type: String,
    default: "timeline",
  },
  listId: {
    type: String,
    default: null,
  },
  hideHeader: {
    type: Boolean,
    default: false,
  },
  compact: {
    type: Boolean,
    default: false,
  },
  loading: {
    type: Boolean,
    default: false,
  },
  emptyMessage: {
    type: String,
    default: "No audit trail events match the filter criteria.",
  },
  enablePeekDrawer: {
    type: Boolean,
    default: true,
  },
  fetchAuditEntry: {
    type: Function,
    default: null,
  },
  fullViewLabel: {
    type: String,
    default: "Open full view",
  },
  teleportDrawer: {
    type: [Boolean, String],
    default: true,
  },
});

const emit = defineEmits(["select-entry", "open-full-view", "navigate-entity"]);

const entityResolver = inject(ENTITY_LINK_RESOLVER_KEY, null);

const searchQuery = ref("");
const selectedFilter = ref("ALL");
const peekOpen = ref(false);
const selectedRawEntry = ref(null);
const fetchedDetailEntry = ref(null);
const peekLoading = ref(false);
const peekError = ref(null);

const normalizedLogs = computed(() => {
  const source = Array.isArray(props.entries) ? props.entries : props.logs;
  if (!Array.isArray(source)) return [];
  return source.map((item, index) => normalizeAuditEntry(item, index));
});

const actionFilterOptions = computed(() => {
  const seen = new Set(DEFAULT_FILTER_OPTIONS);
  for (const item of normalizedLogs.value) {
    if (item.action && typeof item.action === "string") {
      seen.add(item.action);
    }
  }
  return Array.from(seen);
});

const filteredLogs = computed(() => {
  return normalizedLogs.value.filter((log) => {
    const matchesFilter =
      selectedFilter.value === "ALL" ||
      log.action === selectedFilter.value ||
      log.action_category === selectedFilter.value;
    if (!matchesFilter) return false;
    if (!searchQuery.value) return true;
    const q = searchQuery.value.toLowerCase();
    return Boolean(
      (log.user_id && String(log.user_id).toLowerCase().includes(q)) ||
      (log.actor_label && String(log.actor_label).toLowerCase().includes(q)) ||
      (log.action && String(log.action).toLowerCase().includes(q)) ||
      (log.entity_type && String(log.entity_type).toLowerCase().includes(q)) ||
      (log.entity_id && String(log.entity_id).toLowerCase().includes(q)) ||
      (log.summary && String(log.summary).toLowerCase().includes(q)) ||
      (log.command_type &&
        String(log.command_type).toLowerCase().includes(q)) ||
      (log.reason_for_change &&
        String(log.reason_for_change).toLowerCase().includes(q)) ||
      (log.attestation?.meaning &&
        String(log.attestation.meaning).toLowerCase().includes(q))
    );
  });
});

const activePeekEntry = computed(
  () => fetchedDetailEntry.value || selectedRawEntry.value
);

const peekSubtitle = computed(() => {
  const entry = activePeekEntry.value;
  if (!entry) return "";
  if (entry.mask) {
    return entry.mask === "Blinded"
      ? "Masked under K-AUTH-4 blinding protection"
      : "Masked outside caller scope";
  }
  const parts = [];
  if (entry.action) parts.push(entry.action);
  if (entry.timestamp) parts.push(formatTimestamp(entry.timestamp));
  return parts.join(" · ");
});

function buildSupportEntries(entry) {
  if (!entry || entry.mask) return [];
  const items = [];
  if (entry.sequence !== null && entry.sequence !== undefined) {
    items.push({
      key: "sequence",
      label: "Chain sequence",
      value: `#${entry.sequence}`,
    });
  }
  if (entry.hash) {
    items.push({
      key: "hash",
      label: "Entry hash",
      value: entry.hash,
    });
  }
  if (entry.predecessor_hash) {
    items.push({
      key: "predecessor_hash",
      label: "Predecessor hash",
      value: entry.predecessor_hash,
    });
  }
  if (entry.audit_entry_ref) {
    items.push({
      key: "audit_entry_ref",
      label: "Audit reference",
      value: entry.audit_entry_ref,
    });
  }
  if (entry.activity_id) {
    items.push({
      key: "activity_id",
      label: "Provenance activity",
      value: entry.activity_id,
    });
  }
  if (entry.correlation_id) {
    items.push({
      key: "correlation_id",
      label: "Correlation ID",
      value: entry.correlation_id,
    });
  }
  if (entry.attestation?.target_content_hash) {
    items.push({
      key: "target_content_hash",
      label: "Signed content hash",
      value: entry.attestation.target_content_hash,
    });
  }
  return items;
}

const activePeekSupportEntries = computed(() =>
  buildSupportEntries(activePeekEntry.value)
);

const canOpenFullView = computed(() => {
  const entry = activePeekEntry.value;
  if (!entry || entry.mask) return false;
  return Boolean(
    entry.target_ref ||
    entry.audit_entry_ref ||
    (entry.entity_type && entry.entity_id)
  );
});

function formatTimestamp(value) {
  if (!value) return "";
  const str = String(value);
  const parsed = Date.parse(str);
  if (Number.isNaN(parsed)) return str;
  try {
    return new Intl.DateTimeFormat("en-US", {
      year: "numeric",
      month: "short",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
      timeZone: "UTC",
      timeZoneName: "short",
    }).format(new Date(parsed));
  } catch {
    return str;
  }
}

function describeMaskMessage(maskReason) {
  if (maskReason === "Blinded") {
    return "Restricted record — treatment-arm or unblinded clinical details are hidden until authorized under K-AUTH-4 (enrollment.unblind).";
  }
  return "Restricted record — actor, target, and change details are outside your current study or site grant scope.";
}

const RAW_UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function formatActorFallbackLabel(entry) {
  if (!entry) return null;
  if (entry.actor_label) return entry.actor_label;
  if (!entry.user_id) return entry.actor_ref ? null : "System";
  const raw = String(entry.user_id).trim();
  if (!raw || RAW_UUID_REGEX.test(raw) || raw.includes(":")) {
    return entry.actor_ref ? null : "Staff member";
  }
  return raw;
}

function formatSignerFallbackLabel(attestation) {
  if (!attestation || !attestation.actor_id) return undefined;
  const raw = String(attestation.actor_id).trim();
  if (!raw || RAW_UUID_REGEX.test(raw) || raw.includes(":")) {
    return undefined;
  }
  return raw;
}

function formatCommandDisplay(commandType) {
  if (!commandType) return "";
  const tail = String(commandType).split(".").pop() || String(commandType);
  return tail
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function formatTargetFallbackLabel(entry) {
  if (!entry) return null;
  if (entry.target_label) return entry.target_label;
  if (entry.target_ref) return null;
  if (entry.entity_type && entry.entity_id) {
    const rawId = String(entry.entity_id).trim();
    if (
      rawId.startsWith(`${entry.entity_type}:`) ||
      RAW_UUID_REGEX.test(rawId) ||
      rawId.includes(":")
    ) {
      return null;
    }
    return `${entry.entity_type} (${rawId})`;
  }
  return entry.entity_type || "Record";
}

async function openEventPeek(entry) {
  if (!entry) return;
  emit("select-entry", entry);
  if (!props.enablePeekDrawer) return;

  selectedRawEntry.value = entry;
  fetchedDetailEntry.value = null;
  peekError.value = null;
  peekOpen.value = true;

  const loader =
    props.fetchAuditEntry ||
    (typeof entityResolver?.fetchAuditEntry === "function"
      ? entityResolver.fetchAuditEntry
      : null);

  if (loader && entry.audit_entry_ref && !entry.mask) {
    peekLoading.value = true;
    try {
      const detail = await loader(entry.audit_entry_ref);
      if (detail && selectedRawEntry.value?.id === entry.id) {
        const adapted = adaptKernelAuditEntryDetail(detail);
        if (adapted) {
          fetchedDetailEntry.value = {
            ...entry,
            ...adapted,
            changes:
              adapted.changes && adapted.changes.length > 0
                ? adapted.changes
                : entry.changes,
          };
        }
      }
    } catch (err) {
      peekError.value =
        err?.message ||
        "Live audit detail could not be loaded; showing cached event fields.";
    } finally {
      peekLoading.value = false;
    }
  }
}

function closeEventPeek() {
  peekOpen.value = false;
}

function handleEntityNavigate(payload) {
  emit("navigate-entity", payload);
}

async function handleOpenFullView() {
  const entry = activePeekEntry.value;
  if (!entry || entry.mask) return;
  const payload = {
    entry,
    nodeRef: entry.target_ref || entry.audit_entry_ref || null,
    auditEntryRef: entry.audit_entry_ref || null,
    entityType: entry.entity_type || null,
    entityId: entry.entity_id || null,
  };
  emit("open-full-view", payload);
  if (entityResolver && typeof entityResolver.openFullView === "function") {
    await entityResolver.openFullView(payload);
  } else if (
    entityResolver &&
    typeof entityResolver.navigate === "function" &&
    payload.nodeRef
  ) {
    await entityResolver.navigate({
      nodeRef: payload.nodeRef,
      entityType: payload.entityType,
      entityId: payload.entityId,
    });
  }
  closeEventPeek();
}

defineExpose({
  searchQuery,
  selectedFilter,
  filteredLogs,
  peekOpen,
  activePeekEntry,
  openEventPeek,
  closeEventPeek,
});
</script>

<template>
  <div
    :class="[
      'audit-log-viewer',
      {
        'audit-log-viewer--compact': compact,
        'audit-log-viewer--activity': variant === 'activity',
      },
    ]"
  >
    <div v-if="!hideHeader" class="audit-header">
      <div class="header-titles">
        <component :is="headingTag" class="audit-title">
          {{ title }}
        </component>
        <span class="part11-badge">21 CFR Part 11 GxP Immutable</span>
      </div>
      <div class="audit-controls">
        <slot name="controls" />
        <input
          v-model="searchQuery"
          type="search"
          class="audit-search"
          placeholder="Filter by actor, action, reason..."
          aria-label="Filter audit entries"
        />
        <select
          v-model="selectedFilter"
          class="audit-select"
          aria-label="Filter by action"
        >
          <option value="ALL">All Actions</option>
          <option v-for="opt in actionFilterOptions" :key="opt" :value="opt">
            {{ opt }}
          </option>
        </select>
      </div>
    </div>

    <div
      v-if="loading"
      class="audit-loading-state"
      role="status"
      aria-live="polite"
    >
      Loading audit history...
    </div>

    <div v-else-if="filteredLogs.length === 0" class="empty-state">
      {{ emptyMessage }}
    </div>

    <ol
      v-else-if="variant === 'activity'"
      :id="listId || undefined"
      class="audit-activity-list"
    >
      <li
        v-for="entry in filteredLogs"
        :key="entry.id"
        class="activity-entry"
        :data-family="entry.event_kind || undefined"
      >
        <time class="activity-time" :datetime="entry.timestamp">{{
          formatTimestamp(entry.timestamp) || entry.timestamp
        }}</time>
        <span class="activity-what">{{
          entry.what_label || entry.action
        }}</span>
        <span v-if="entry.mask" class="activity-subject masked">{{
          entry.masked_subject_label || describeMaskMessage(entry.mask)
        }}</span>
        <span v-else-if="entry.target_ref" class="activity-subject">
          <EntityLink
            :node-ref="entry.target_ref"
            :entity-type="entry.entity_type"
            :entity-id="entry.entity_id"
            :label="entry.target_label || undefined"
            :href="entry.target_href || undefined"
            kind-label=""
            @navigate="handleEntityNavigate"
          />
        </span>
        <span
          v-if="entry.raw_actor_id || entry.user_id || entry.actor_ref"
          class="activity-actor"
          ><span>by </span
          ><EntityLink
            :node-ref="entry.actor_ref || entry.raw_actor_ref || null"
            entity-type="person"
            :entity-id="entry.raw_actor_id || entry.user_id || 'system'"
            :label="
              entry.actor_label ||
              entry.raw_actor_id ||
              entry.user_id ||
              undefined
            "
            kind-label=""
            @navigate="handleEntityNavigate"
        /></span>
        <EntityLink
          v-if="!entry.mask && entry.attestation"
          :node-ref="entry.attestation.attestation_ref"
          entity-type="attestation"
          :entity-id="entry.attestation.attestation_ref"
          :label="entry.attestation.meaning || 'Signed'"
          kind-label="Signature"
          @navigate="handleEntityNavigate"
        />
        <span
          v-if="!entry.mask && entry.reason_for_change"
          class="activity-reason"
        >
          Reason: {{ entry.reason_for_change }}
        </span>
        <button
          v-if="enablePeekDrawer"
          type="button"
          class="audit-peek-trigger"
          @click="openEventPeek(entry)"
        >
          Inspect event<span class="sr-only">
            {{
              entry.mask
                ? "(restricted audit event)"
                : `(${entry.what_label || entry.action})`
            }}</span
          >
        </button>
      </li>
    </ol>

    <div v-else class="audit-timeline" role="feed" aria-label="Audit Timeline">
      <article
        v-for="(entry, idx) in filteredLogs"
        :key="entry.id"
        :class="[
          'timeline-item',
          { 'timeline-item--masked': Boolean(entry.mask) },
        ]"
        :aria-posinset="idx + 1"
        :aria-setsize="filteredLogs.length"
        tabindex="0"
      >
        <div
          :class="[
            'timeline-marker',
            { 'timeline-marker--masked': Boolean(entry.mask) },
          ]"
        ></div>
        <div class="timeline-content">
          <div class="content-header">
            <span
              class="action-tag"
              :data-action="entry.action_category || entry.action"
            >
              {{ entry.mask ? "RESTRICTED" : entry.action }}
            </span>

            <span class="timestamp" :title="entry.timestamp">
              {{ formatTimestamp(entry.timestamp) || entry.timestamp }}
            </span>

            <template v-if="!entry.mask">
              <span class="actor-chip-wrap">
                <span class="meta-prefix">by</span>
                <EntityLink
                  :node-ref="entry.actor_ref"
                  entity-type="person"
                  :entity-id="entry.user_id || 'system'"
                  :label="formatActorFallbackLabel(entry)"
                  kind-label="Actor"
                  @navigate="handleEntityNavigate"
                />
              </span>
            </template>

            <button
              v-if="enablePeekDrawer"
              type="button"
              class="audit-peek-trigger"
              @click="openEventPeek(entry)"
            >
              Inspect event<span class="sr-only">
                {{
                  entry.mask ? "(restricted audit event)" : `(${entry.action})`
                }}</span
              >
            </button>
          </div>

          <p v-if="entry.mask" class="masked-placeholder">
            {{ describeMaskMessage(entry.mask) }}
          </p>

          <template v-else>
            <p v-if="entry.summary" class="audit-summary">
              {{ entry.summary }}
            </p>

            <div class="target-info">
              <span class="meta-prefix">Target:</span>
              <EntityLink
                :node-ref="entry.target_ref"
                :entity-type="entry.entity_type"
                :entity-id="entry.entity_id"
                :label="formatTargetFallbackLabel(entry)"
                @navigate="handleEntityNavigate"
              />
              <span v-if="entry.version" class="version-pill">
                v{{ entry.version }}
              </span>
            </div>

            <div v-if="entry.attestation" class="attestation-row">
              <span class="meta-prefix">Attestation:</span>
              <EntityLink
                :node-ref="entry.attestation.attestation_ref"
                entity-type="attestation"
                :entity-id="entry.attestation.attestation_ref"
                :label="entry.attestation.meaning || 'Electronic Signature'"
                kind-label="Signature"
                @navigate="handleEntityNavigate"
              />
              <EntityLink
                v-if="entry.attestation.actor_ref || entry.attestation.actor_id"
                :node-ref="entry.attestation.actor_ref"
                entity-type="person"
                :entity-id="entry.attestation.actor_id"
                :label="formatSignerFallbackLabel(entry.attestation)"
                kind-label="Signer"
                @navigate="handleEntityNavigate"
              />
            </div>

            <div v-if="entry.reason_for_change" class="change-reason">
              <span class="reason-label">Reason for Change:</span>
              {{ entry.reason_for_change }}
            </div>

            <div
              v-if="entry.changes && entry.changes.length > 0"
              class="field-diffs"
            >
              <table class="diff-table">
                <thead>
                  <tr>
                    <th scope="col">Field</th>
                    <th scope="col">Previous Value</th>
                    <th scope="col">New Value</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(change, cIdx) in entry.changes" :key="cIdx">
                    <td>
                      <code>{{ change.field }}</code>
                    </td>
                    <td class="diff-old">
                      {{ change.old_val ?? "null" }}
                    </td>
                    <td class="diff-new">
                      {{ change.new_val ?? "null" }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <SupportDetails
              v-if="!compact && buildSupportEntries(entry).length > 0"
              :entries="buildSupportEntries(entry)"
              summary-label="Support details"
            />
          </template>
        </div>
      </article>
    </div>

    <ClinicalDrawer
      v-if="enablePeekDrawer"
      v-model="peekOpen"
      title="Audit Event"
      :subtitle="peekSubtitle"
      size="md"
      :teleport="teleportDrawer"
      close-label="Close audit event drawer"
    >
      <div v-if="activePeekEntry" class="audit-peek-body">
        <div
          v-if="peekLoading"
          class="audit-peek-loading"
          role="status"
          aria-live="polite"
        >
          Refreshing tamper-evident audit record...
        </div>

        <p v-if="peekError" class="audit-peek-notice" role="status">
          {{ peekError }}
        </p>

        <div v-if="activePeekEntry.mask" class="audit-peek-masked-card">
          <p class="audit-peek-masked-title">
            Restricted audit record ({{ activePeekEntry.mask }})
          </p>
          <p class="audit-peek-masked-copy">
            {{ describeMaskMessage(activePeekEntry.mask) }}
          </p>
          <dl class="audit-peek-grid">
            <div class="audit-peek-field">
              <dt>Recorded at</dt>
              <dd>
                {{
                  formatTimestamp(activePeekEntry.timestamp) ||
                  activePeekEntry.timestamp ||
                  "Recorded"
                }}
              </dd>
            </div>
            <div class="audit-peek-field">
              <dt>Target kind</dt>
              <dd>{{ activePeekEntry.entity_type || "Record" }}</dd>
            </div>
          </dl>
        </div>

        <template v-else>
          <section class="audit-peek-section" aria-label="Event overview">
            <div class="audit-peek-badges">
              <span
                class="action-tag"
                :data-action="
                  activePeekEntry.action_category || activePeekEntry.action
                "
              >
                {{ activePeekEntry.action }}
              </span>
              <span v-if="activePeekEntry.outcome" class="outcome-pill">
                {{ activePeekEntry.outcome }}
              </span>
              <span v-if="activePeekEntry.version" class="version-pill">
                v{{ activePeekEntry.version }}
              </span>
            </div>

            <p v-if="activePeekEntry.summary" class="audit-peek-summary">
              {{ activePeekEntry.summary }}
            </p>

            <dl class="audit-peek-grid">
              <div class="audit-peek-field">
                <dt>Actor</dt>
                <dd>
                  <EntityLink
                    :node-ref="activePeekEntry.actor_ref"
                    entity-type="person"
                    :entity-id="activePeekEntry.user_id || 'system'"
                    :label="formatActorFallbackLabel(activePeekEntry)"
                    kind-label="Actor"
                    size="md"
                    @navigate="handleEntityNavigate"
                  />
                </dd>
              </div>

              <div class="audit-peek-field">
                <dt>Target</dt>
                <dd>
                  <EntityLink
                    :node-ref="activePeekEntry.target_ref"
                    :entity-type="activePeekEntry.entity_type"
                    :entity-id="activePeekEntry.entity_id"
                    :label="formatTargetFallbackLabel(activePeekEntry)"
                    size="md"
                    @navigate="handleEntityNavigate"
                  />
                </dd>
              </div>

              <div class="audit-peek-field">
                <dt>Recorded at (system)</dt>
                <dd>
                  {{
                    formatTimestamp(activePeekEntry.timestamp) ||
                    activePeekEntry.timestamp
                  }}
                </dd>
              </div>

              <div v-if="activePeekEntry.valid_at" class="audit-peek-field">
                <dt>Valid at (clinical)</dt>
                <dd>
                  {{
                    formatTimestamp(activePeekEntry.valid_at) ||
                    activePeekEntry.valid_at
                  }}
                </dd>
              </div>

              <div v-if="activePeekEntry.command_type" class="audit-peek-field">
                <dt>Operation</dt>
                <dd>
                  {{ formatCommandDisplay(activePeekEntry.command_type) }}
                </dd>
              </div>
            </dl>
          </section>

          <section
            v-if="activePeekEntry.reason_for_change"
            class="audit-peek-section"
            aria-label="Reason for change"
          >
            <h4 class="audit-peek-heading">Reason for change</h4>
            <p class="change-reason">
              {{ activePeekEntry.reason_for_change }}
            </p>
          </section>

          <section
            v-if="activePeekEntry.attestation"
            class="audit-peek-section"
            aria-label="21 CFR Part 11 electronic signature"
          >
            <h4 class="audit-peek-heading">Electronic signature</h4>
            <div class="audit-peek-attestation-chips">
              <EntityLink
                :node-ref="activePeekEntry.attestation.attestation_ref"
                entity-type="attestation"
                :entity-id="activePeekEntry.attestation.attestation_ref"
                :label="
                  activePeekEntry.attestation.meaning || 'Electronic Signature'
                "
                kind-label="Signature"
                size="md"
                @navigate="handleEntityNavigate"
              />
              <EntityLink
                v-if="
                  activePeekEntry.attestation.actor_ref ||
                  activePeekEntry.attestation.actor_id
                "
                :node-ref="activePeekEntry.attestation.actor_ref"
                entity-type="person"
                :entity-id="activePeekEntry.attestation.actor_id"
                :label="formatSignerFallbackLabel(activePeekEntry.attestation)"
                kind-label="Signer"
                size="md"
                @navigate="handleEntityNavigate"
              />
            </div>
            <dl class="audit-peek-grid">
              <div
                v-if="activePeekEntry.attestation.method"
                class="audit-peek-field"
              >
                <dt>Authentication method</dt>
                <dd>{{ activePeekEntry.attestation.method }}</dd>
              </div>
              <div
                v-if="activePeekEntry.attestation.authenticated_at"
                class="audit-peek-field"
              >
                <dt>Authenticated at</dt>
                <dd>
                  {{
                    formatTimestamp(
                      activePeekEntry.attestation.authenticated_at
                    )
                  }}
                </dd>
              </div>
            </dl>
          </section>

          <section
            v-if="activePeekEntry.changes && activePeekEntry.changes.length > 0"
            class="audit-peek-section"
            aria-label="Field changes"
          >
            <h4 class="audit-peek-heading">Field changes</h4>
            <div class="field-diffs">
              <table class="diff-table">
                <thead>
                  <tr>
                    <th scope="col">Field</th>
                    <th scope="col">Previous Value</th>
                    <th scope="col">New Value</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(change, cIdx) in activePeekEntry.changes"
                    :key="cIdx"
                  >
                    <td>
                      <code>{{ change.field }}</code>
                    </td>
                    <td class="diff-old">
                      {{ change.old_val ?? "null" }}
                    </td>
                    <td class="diff-new">
                      {{ change.new_val ?? "null" }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

          <section
            v-if="activePeekSupportEntries.length > 0"
            class="audit-peek-section"
            aria-label="Cryptographic chain support details"
          >
            <SupportDetails
              :entries="activePeekSupportEntries"
              summary-label="Support details (sequence & cryptographic chain)"
            />
          </section>
        </template>
      </div>

      <template #footer="{ close }">
        <div class="audit-peek-footer-actions">
          <button type="button" class="audit-peek-secondary-btn" @click="close">
            Close
          </button>
          <button
            v-if="canOpenFullView"
            type="button"
            class="audit-peek-primary-btn"
            data-testid="audit-open-full-view"
            @click="handleOpenFullView"
          >
            {{ fullViewLabel }}
          </button>
        </div>
      </template>
    </ClinicalDrawer>
  </div>
</template>

<style scoped>
.audit-log-viewer {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  overflow: hidden;
}

.audit-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 12px 16px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.audit-log-viewer--compact .audit-header {
  padding: 10px 12px;
  gap: 8px;
}

.header-titles {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.audit-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.audit-log-viewer--compact .audit-title {
  font-size: 13px;
}

.part11-badge {
  font-size: 11px;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.audit-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.audit-search,
.audit-select {
  padding: 6px 10px;
  font-size: 13px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.audit-loading-state {
  padding: 24px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--semantic-color-text-muted);
}

.audit-timeline {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  max-height: 600px;
  overflow-y: auto;
}

.audit-log-viewer--compact .audit-timeline {
  padding: 10px;
  gap: 10px;
  max-height: none;
}

.timeline-item {
  position: relative;
  display: flex;
  gap: 12px;
  padding-left: 4px;
}

.timeline-item:focus-visible {
  outline: 2px solid var(--semantic-color-accent, var(--semantic-color-primary));
  outline-offset: 2px;
  border-radius: 6px;
}

.timeline-marker {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--semantic-color-primary);
  margin-top: 8px;
  flex-shrink: 0;
}

.timeline-marker--masked {
  background: var(--semantic-color-text-muted);
}

.timeline-content {
  flex: 1;
  min-width: 0;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 12px;
}

.timeline-item--masked .timeline-content {
  border-style: dashed;
}

.content-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 13px;
  margin-bottom: 6px;
}

.action-tag {
  font-size: 11px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.action-tag[data-action="CREATE"] {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.action-tag[data-action="UPDATE"] {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.action-tag[data-action="DELETE"] {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.action-tag[data-action="SIGN"] {
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning-text);
}

.action-tag[data-action="RESTRICTED"] {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-muted);
}

.timestamp {
  color: var(--semantic-color-text-muted);
  font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 12px;
}

.actor-chip-wrap,
.target-info,
.attestation-row {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.target-info,
.attestation-row {
  display: flex;
  font-size: 12px;
  color: var(--semantic-color-text-body);
  margin-bottom: 6px;
}

.meta-prefix {
  color: var(--semantic-color-text-muted);
  font-size: 12px;
}

.version-pill,
.outcome-pill {
  font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-muted);
}

.audit-peek-trigger {
  margin-left: auto;
  padding: 3px 8px;
  font-size: 12px;
  font-weight: 500;
  color: var(--semantic-color-primary);
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  cursor: pointer;
}

.audit-peek-trigger:hover {
  background: var(--semantic-color-primary-light);
  border-color: var(--semantic-color-primary);
}

.audit-peek-trigger:focus-visible {
  outline: 2px solid var(--semantic-color-accent, var(--semantic-color-primary));
  outline-offset: 2px;
}

.audit-summary {
  margin: 0 0 6px;
  font-size: 13px;
  color: var(--semantic-color-text);
}

.masked-placeholder {
  margin: 4px 0 0;
  font-size: 12.5px;
  color: var(--semantic-color-text-muted);
}

.change-reason {
  font-size: 13px;
  padding: 6px 10px;
  background: var(--semantic-color-surface);
  border-left: 3px solid var(--semantic-color-primary);
  border-radius: 0 4px 4px 0;
  margin: 6px 0;
  color: var(--semantic-color-text);
}

.reason-label {
  font-weight: 600;
  margin-right: 4px;
}

.field-diffs {
  margin-top: 8px;
  overflow-x: auto;
}

.diff-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
}

.diff-table th,
.diff-table td {
  padding: 6px 10px;
  text-align: left;
  border-bottom: 1px solid var(--semantic-color-border);
}

.diff-table th {
  background: var(--semantic-color-surface-header);
  font-weight: 600;
  color: var(--semantic-color-text-muted);
}

.diff-old {
  color: var(--semantic-color-error);
  font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
}

.diff-new {
  color: var(--semantic-color-success);
  font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-weight: 600;
}

.empty-state {
  text-align: center;
  padding: 28px 16px;
  color: var(--semantic-color-text-muted);
  font-size: 14px;
}

.audit-peek-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.audit-peek-loading,
.audit-peek-notice {
  margin: 0;
  padding: 8px 10px;
  border-radius: 6px;
  font-size: 12.5px;
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
}

.audit-peek-masked-card {
  padding: 14px;
  border-radius: 8px;
  border: 1px dashed var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
}

.audit-peek-masked-title {
  margin: 0 0 6px;
  font-size: 14px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.audit-peek-masked-copy {
  margin: 0 0 12px;
  font-size: 13px;
  color: var(--semantic-color-text-muted);
}

.audit-peek-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.audit-peek-badges,
.audit-peek-attestation-chips {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.audit-peek-summary {
  margin: 0;
  font-size: 14px;
  font-weight: 500;
  color: var(--semantic-color-text);
}

.audit-peek-heading {
  margin: 0;
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--semantic-color-text-muted);
}

.audit-peek-grid {
  margin: 0;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px 14px;
}

.audit-peek-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.audit-peek-field dt {
  font-size: 11.5px;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
}

.audit-peek-field dd {
  margin: 0;
  font-size: 13px;
  color: var(--semantic-color-text);
}

.audit-peek-footer-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  width: 100%;
}

.audit-peek-secondary-btn,
.audit-peek-primary-btn {
  padding: 7px 14px;
  border-radius: 6px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}

.audit-peek-secondary-btn {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.audit-log-viewer--activity {
  border: 0;
  border-radius: 0;
  background: transparent;
}

.audit-activity-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.activity-entry {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 10px;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  font-size: 13.5px;
}

.activity-time {
  color: var(--semantic-color-text-muted);
  font-size: 12.5px;
  font-variant-numeric: tabular-nums;
}

.activity-what {
  font-weight: 500;
  color: var(--semantic-color-text);
}

.activity-subject.masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}

.activity-actor,
.activity-reason {
  color: var(--semantic-color-text-muted);
  font-size: 12.5px;
}

.audit-peek-primary-btn {
  background: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-primary);
  color: var(--semantic-color-text-inverse);
}

.audit-peek-secondary-btn:focus-visible,
.audit-peek-primary-btn:focus-visible {
  outline: 2px solid var(--semantic-color-accent, var(--semantic-color-primary));
  outline-offset: 2px;
}
</style>
