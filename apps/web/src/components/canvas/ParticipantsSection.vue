<template>
  <section
    id="participants-section"
    class="participants"
    aria-labelledby="participants-title"
    :aria-busy="canvas.participantsLoading"
  >
    <header class="participants-header">
      <h2 id="participants-title" tabindex="-1">Participants</h2>
      <ClinicalFieldLayout
        id="participants-search"
        label="Search"
        density="compact"
        :show-query-flag="false"
        extra-class="participants-inline-field"
      >
        <input
          id="participants-search"
          ref="searchInputRef"
          v-model="searchInput"
          type="search"
          class="clinical-input clinical-input--compact"
          placeholder="Subject code"
          aria-label="Search participants by subject code"
          @focus="searchFocused = true"
          @blur="onSearchBlur"
          @input="scheduleSearch"
          @search="flushSearch"
          @keydown.enter.prevent="flushSearch"
        />
      </ClinicalFieldLayout>
      <ClinicalInput
        id="participants-status"
        label="State"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="participants-inline-field"
        :model-value="status"
        @change="onStatusSelect"
      >
        <option value="">Any state</option>
        <option
          v-for="state in PARTICIPANT_STATES"
          :key="state.code"
          :value="state.code"
        >
          {{ state.label }}
        </option>
      </ClinicalInput>
      <ClinicalInput
        id="participants-site"
        label="Site"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="participants-inline-field"
        :model-value="siteId"
        @change="onSiteSelect"
      >
        <option value="">All sites</option>
        <option v-for="site in siteOptions" :key="site.code" :value="site.code">
          {{ site.label }}
        </option>
      </ClinicalInput>
      <span v-if="siteId" id="participants-site-filter" class="filter-chip">
        Site <span class="filter-chip-code">{{ siteId }}</span>
        <button
          id="btn-clear-site-filter"
          type="button"
          :aria-label="`Show participants at every site, not only ${siteId}`"
          @click="clearSiteFilter"
        >
          ×
        </button>
      </span>
      <button
        v-if="canManageParticipants && !adding"
        id="btn-add-participant"
        type="button"
        class="btn btn-secondary"
        @click="openAddParticipant"
      >
        Add participant
      </button>
    </header>

    <div
      v-if="canSwitchToCoordinatorLens"
      id="participants-coordinator-lens-hint"
      class="coordinator-lens-hint"
      role="status"
    >
      <span>
        Sponsor lenses view participant records read-only. Switch to the Site
        Coordinator lens to add or update participants at your assigned site.
      </span>
      <button
        id="btn-switch-coordinator-lens"
        type="button"
        class="btn btn-secondary"
        @click="switchToCoordinatorLens"
      >
        Switch to Site Coordinator lens
      </button>
    </div>

    <AddParticipantForm
      v-if="canManageParticipants && adding"
      :default-site-id="defaultAddSiteId"
      :site-options="authorizedSiteOptions"
      :restrict-to-options="hasExplicitSiteScope"
      @added="onAdded"
      @close="closeAddParticipant"
    />

    <p id="participants-status-line" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <ClinicalDataTable
      v-if="canvas.participants.length"
      title="Participants"
      labelled-by="participants-title"
      hide-header
      plain
      wrapper-class="scrollable-table-wrapper"
      table-class="participants-table"
      :columns="participantColumns"
      :data="canvas.participants"
      row-key="id"
      :row-class="
        (participant) => ({ selected: participant.id === selectedId })
      "
      external-pagination
    >
      <template #cell-subject_id="{ row }">
        <button
          :id="`participant-${row.subject_id}`"
          type="button"
          class="row-select"
          :aria-pressed="row.id === selectedId"
          @click="emit('select', row)"
        >
          {{ row.subject_id }}
        </button>
      </template>
      <template #cell-status="{ row }">
        {{ participantStateLabel(row.status) }}
      </template>
      <template #cell-site_id="{ row }">
        <span :class="{ 'participant-site-code': Boolean(row.site_id) }">
          {{ row.site_id || "Not assigned" }}
        </span>
      </template>
      <template #pagination>
        <span />
      </template>
    </ClinicalDataTable>

    <button
      v-if="canvas.hasMoreParticipants"
      id="btn-more-participants"
      type="button"
      class="btn btn-secondary"
      :disabled="canvas.participantsLoading"
      @click="
        canvas.loadParticipants(canvas.participantsFilter, { append: true })
      "
    >
      Show more
    </button>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from "vue";
import {
  ClinicalDataTable,
  ClinicalFieldLayout,
  ClinicalInput,
  handoffFocus,
  useInlineFocusHandoff,
} from "ui";
import type { SubjectSummary } from "../../api/executionApi";
import {
  authorizedParticipantSiteCodes,
  holdsParticipantRole,
} from "../../canvas/authority";
import { findStudySites, type StudySiteOption } from "../../canvas/kernelStudy";
import {
  PARTICIPANT_STATES,
  participantStateLabel,
} from "../../canvas/lifecycle";
import { useAuthStore } from "../../stores/auth";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import AddParticipantForm from "./AddParticipantForm.vue";

/**
 * The study's participants in enrollment order, from Execution, filtered by search text, state and site.
 * The filter lives in the canvas URL, so the parent owns it; selecting a participant fills the inspector.
 */
const props = withDefaults(
  defineProps<{
    status: string;
    siteId: string;
    query?: string;
    selectedId: string;
    /** Whether to offer adding participants (Execution enforces who may, #4943). */
    canManageParticipants?: boolean;
  }>(),
  {
    query: "",
    canManageParticipants: false,
  }
);
const emit = defineEmits<{
  select: [participant: SubjectSummary];
  filter: [filter: { status: string; siteId: string; q: string }];
}>();

const SEARCH_DEBOUNCE_MS = 180;

const participantColumns = [
  { key: "subject_id", label: "Participant", rowHeader: true },
  { key: "status", label: "State" },
  { key: "site_id", label: "Site" },
  { key: "enrollment_index", label: "Enrollment", align: "right" },
];

const canvas = useStudyCanvasStore();
const authStore = useAuthStore();
const adding = ref(false);
const addParticipantHandoff = useInlineFocusHandoff({
  openTarget: "#add-participant-code",
  closeFallback: ["#btn-add-participant", "#participants-search"],
});

function clearSiteFilter() {
  emitFilter({ siteId: "" });
  void handoffFocus("#participants-site");
}

function openAddParticipant() {
  addParticipantHandoff.rememberTrigger("#btn-add-participant");
  adding.value = true;
  void addParticipantHandoff.handoff("#add-participant-code", {
    scrollIfOffscreen: true,
  });
}

function closeAddParticipant() {
  adding.value = false;
  void addParticipantHandoff.restoreTrigger("#btn-add-participant");
}
const searchInputRef = ref<HTMLInputElement | null>(null);
const searchInput = ref(props.query);
const searchFocused = ref(false);
const kernelSites = ref<StudySiteOption[]>([]);
let searchTimer: ReturnType<typeof setTimeout> | undefined;
let sitesRequest = 0;

watch(
  () => props.query,
  (nextQuery) => {
    const typingInSearch =
      searchFocused.value ||
      (typeof document !== "undefined" &&
        searchInputRef.value !== null &&
        document.activeElement === searchInputRef.value);
    if (!typingInSearch) {
      clearTimeout(searchTimer);
      searchInput.value = nextQuery;
    }
  }
);

watch(
  () => canvas.studyCode,
  async (code) => {
    const req = ++sitesRequest;
    kernelSites.value = [];
    if (!code) return;
    try {
      const loaded = await findStudySites(code);
      if (req === sitesRequest) {
        kernelSites.value = loaded;
      }
    } catch {
      if (req === sitesRequest) {
        kernelSites.value = [];
      }
    }
  },
  { immediate: true }
);

onBeforeUnmount(() => {
  clearTimeout(searchTimer);
  sitesRequest++;
});

const siteOptions = computed<StudySiteOption[]>(() => {
  const byCode = new Map<string, StudySiteOption>();
  for (const site of kernelSites.value) {
    byCode.set(site.code, site);
  }
  for (const participant of Object.values(canvas.knownParticipants)) {
    const code = participant.site_id?.trim();
    if (code && !byCode.has(code)) {
      byCode.set(code, { code, label: code });
    }
  }
  const activeSite = props.siteId.trim();
  if (activeSite && !byCode.has(activeSite)) {
    byCode.set(activeSite, { code: activeSite, label: activeSite });
  }
  return Array.from(byCode.values()).sort((a, b) =>
    a.code.localeCompare(b.code)
  );
});

const hasExplicitSiteScope = computed(
  () => (authStore.assignedSiteIds?.length ?? 0) > 0
);

const authorizedSiteOptions = computed<StudySiteOption[]>(() => {
  const allowedCodes = authorizedParticipantSiteCodes(
    authStore,
    kernelSites.value.map((s) => s.code)
  );
  const byKnown = new Map(siteOptions.value.map((s) => [s.code, s]));
  return allowedCodes.map((code) => byKnown.get(code) ?? { code, label: code });
});

const defaultAddSiteId = computed(() => {
  const active = props.siteId.trim();
  if (active) {
    if (
      !hasExplicitSiteScope.value ||
      authorizedSiteOptions.value.some(
        (s) => s.code.toLowerCase() === active.toLowerCase()
      )
    ) {
      return active;
    }
  }
  if (authorizedSiteOptions.value.length === 1) {
    return authorizedSiteOptions.value[0].code;
  }
  return "";
});

const canSwitchToCoordinatorLens = computed(
  () =>
    !props.canManageParticipants &&
    holdsParticipantRole(authStore) &&
    Boolean(
      authStore.currentPersona &&
      authStore.currentPersona !== "site_crc" &&
      authStore.currentPersona !== "site_investigator"
    )
);

function switchToCoordinatorLens() {
  authStore.switchPersona("site_crc");
}

/** Closes the form and selects the new participant when the list could place it. */
function onAdded(participant: SubjectSummary | null) {
  adding.value = false;
  if (participant) {
    emit("select", participant);
    void handoffFocus([
      `#participant-${participant.subject_id}`,
      "#btn-add-participant",
      "#participants-search",
    ]);
  } else {
    void addParticipantHandoff.restoreTrigger("#btn-add-participant");
  }
}

const statusLine = computed(() => {
  if (canvas.participantsLoading && !canvas.participants.length) {
    return "Loading participants...";
  }
  if (canvas.participantsError) return "Participants couldn't be loaded.";
  const total = canvas.participantsTotal;
  if (total === 0) {
    return props.status || props.siteId || props.query
      ? "No participants match these filters."
      : `No participants in ${canvas.studyCode} yet.`;
  }
  const shown = canvas.participants.length;
  const noun = total === 1 ? "participant" : "participants";
  return shown < total
    ? `Showing ${shown} of ${total} ${noun}.`
    : `${total} ${noun}.`;
});

function emitFilter(
  overrides: Partial<{ status: string; siteId: string; q: string }>
) {
  emit("filter", {
    status: overrides.status ?? props.status,
    siteId: overrides.siteId ?? props.siteId,
    q: overrides.q ?? searchInput.value.trim(),
  });
}

function flushSearch() {
  clearTimeout(searchTimer);
  const trimmed = searchInput.value.trim();
  if (trimmed !== props.query) {
    emitFilter({ q: trimmed });
  }
}

function scheduleSearch() {
  clearTimeout(searchTimer);
  if (!searchInput.value.trim()) {
    flushSearch();
    return;
  }
  searchTimer = setTimeout(flushSearch, SEARCH_DEBOUNCE_MS);
}

function onSearchBlur() {
  searchFocused.value = false;
  flushSearch();
}

function onStatusSelect(value: string | number) {
  clearTimeout(searchTimer);
  emitFilter({ status: String(value ?? "") });
}

function onSiteSelect(value: string | number) {
  clearTimeout(searchTimer);
  emitFilter({ siteId: String(value ?? "") });
}
</script>

<style scoped>
.participants {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  max-width: 100%;
}

.participants-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.participants-header h2 {
  margin: 0 auto 0 0;
  font-size: var(--font-size-md);
}

.participants-header :deep(.participants-inline-field) {
  display: inline-flex;
  flex-direction: row;
  align-items: center;
  gap: 6px;
  margin: 0;
}

.participants-header :deep(.participants-inline-field .clinical-field-header) {
  margin: 0;
}

.participants-header :deep(.participants-inline-field .clinical-input) {
  width: auto;
  max-width: 100%;
}

#participants-search,
#participants-site,
.filter-chip-code,
.participant-site-code {
  font-family: var(--semantic-font-mono);
}

.filter-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 4px 2px 8px;
  border-radius: 999px;
  background: var(--accent-bg);
  font-size: var(--font-size-sm);
}

.filter-chip button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 0 4px;
  border: 0;
  border-radius: 999px;
  background: transparent;
  color: inherit;
  cursor: pointer;
  box-sizing: border-box;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
  font-size: var(--font-size-sm);
  font-variant-numeric: tabular-nums;
}

.coordinator-lens-hint {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
  font-size: var(--font-size-sm);
}

.participants-table :deep(tr.selected) {
  background: var(--accent-bg);
}

.row-select {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 4px;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: var(--accent);
  font-family: var(--semantic-font-mono);
  cursor: pointer;
  box-sizing: border-box;
}
</style>
