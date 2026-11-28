<template>
  <Teleport to="body" :disabled="!props.teleport">
    <div
      id="canvas-palette-backdrop"
      class="palette-backdrop"
      role="presentation"
      @click.self="emit('close')"
    >
      <div
        id="canvas-palette"
        ref="dialogRef"
        class="palette"
        :style="paletteMaxHeightStyle"
        role="dialog"
        aria-modal="true"
        :aria-busy="isSearching ? 'true' : 'false'"
        aria-label="Go to or open anything in this study"
      >
        <div class="palette-header">
          <ClinicalIcon
            name="search"
            :size="16"
            class="palette-search-icon"
            aria-hidden="true"
          />
          <input
            id="canvas-palette-input"
            v-model="query"
            type="text"
            role="combobox"
            aria-autocomplete="list"
            aria-controls="canvas-palette-list"
            :aria-expanded="results.length > 0"
            :aria-busy="isSearching ? 'true' : 'false'"
            :aria-activedescendant="
              results.length ? `canvas-palette-item-${active}` : undefined
            "
            autocomplete="off"
            :placeholder="
              studyCode
                ? 'Search sites, participants, forms, queries, findings, or stages'
                : 'Search studies, sites, participants, forms, queries, or workflows'
            "
            aria-label="Search"
            @keydown="onKeydown"
          />
          <button
            id="canvas-palette-close"
            type="button"
            class="palette-close-btn"
            aria-label="Close command palette"
            @click="emit('close')"
          >
            <ClinicalIcon name="x" :size="18" />
          </button>
        </div>
        <div
          v-if="query.trim().length > 0 && filterChips.length > 1"
          id="canvas-palette-filters"
          class="palette-filters"
          role="toolbar"
          aria-label="Filter results by category"
        >
          <button
            v-for="chip in filterChips"
            :key="chip.id"
            :data-category="chip.id"
            type="button"
            class="palette-filter-chip"
            :class="{ active: selectedCategory === chip.id }"
            :aria-pressed="selectedCategory === chip.id ? 'true' : 'false'"
            @click="selectCategory(chip.id)"
          >
            <span>{{ chip.label }}</span>
            <span class="palette-filter-count">({{ chip.count }})</span>
          </button>
        </div>
        <div class="palette-body">
          <ul
            v-if="results.length || isSearching"
            id="canvas-palette-list"
            class="palette-list"
            role="listbox"
            :aria-busy="isSearching ? 'true' : 'false'"
            aria-label="Results"
          >
            <li
              v-for="(item, index) in results"
              :id="`canvas-palette-item-${index}`"
              :key="item.key"
              class="palette-item"
              :class="{ active: index === active }"
              role="option"
              tabindex="-1"
              :aria-selected="index === active"
              @click="choose(item)"
              @keydown.enter.prevent="choose(item)"
              @keydown.space.prevent="choose(item)"
              @mouseenter="active = index"
              @focus="active = index"
            >
              <span
                class="palette-group"
                :class="`palette-group--${item.category || 'navigation'}`"
                >{{ item.group }}</span
              >
              <div class="palette-item-main">
                <span class="palette-label">
                  <template v-if="item.code && item.titleRest">
                    <span class="palette-code">{{ item.code }}</span>
                    <span> · {{ item.titleRest }}</span>
                  </template>
                  <span
                    v-else-if="item.code === item.label"
                    class="palette-code"
                    >{{ item.label }}</span
                  >
                  <template v-else>{{ item.label }}</template>
                </span>
                <span v-if="item.detail" class="palette-detail">{{
                  item.detail
                }}</span>
              </div>
              <span v-if="item.actionHint" class="palette-action-hint">{{
                item.actionHint
              }}</span>
            </li>
          </ul>
          <p
            v-else
            id="canvas-palette-empty"
            class="palette-empty"
            role="status"
          >
            {{
              query.trim()
                ? "Nothing matches across authorized studies, sites, clinical records, or workflows."
                : "Type a study, site, participant, form, or query code, or choose a workflow destination."
            }}
          </p>
          <p
            v-if="kernelNote"
            id="canvas-palette-kernel-note"
            class="palette-footnote"
            role="status"
          >
            {{ kernelNote }}
          </p>
          <p
            v-if="displaySubjectNote"
            id="canvas-palette-subject-note"
            class="palette-footnote"
            role="status"
          >
            {{ displaySubjectNote }}
          </p>
          <div class="palette-footer palette-footnote">
            <span class="palette-shortcuts">
              <kbd class="palette-kbd">Up/Down</kbd> Navigate ·
              <kbd class="palette-kbd">Enter</kbd> Open scoped view ·
              <kbd class="palette-kbd">Esc</kbd> Close
            </span>
            <span class="palette-scope-caption">
              Authorized across visible studies and sites
            </span>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import { ClinicalIcon, useFocusTrap, useViewportMaxHeight } from "ui";
import { kernelApi, type KernelSearchHit } from "../../api/kernelApi";
import { STAGES, participantStateLabel } from "../../canvas/lifecycle";
import { executionApi, type SubjectSummary } from "../../api/executionApi";
import type {
  PaletteTarget,
  WorkspacePaletteOption,
} from "../../canvas/palette";
import { useAiAssistance } from "../../ai/aiAssistance";
import {
  participantNodeRef,
  useStudyCanvasStore,
} from "../../stores/studyCanvas";
import { isDemoBuild } from "../../utils/demoMode";

type PaletteCategory =
  | "all"
  | "studies"
  | "sites"
  | "participants"
  | "forms_queries"
  | "findings"
  | "proposals"
  | "governance"
  | "navigation";

interface PaletteItem {
  key: string;
  group: string;
  label: string;
  code?: string | null;
  titleRest?: string;
  detail?: string;
  actionHint?: string;
  category?: Exclude<PaletteCategory, "all">;
  exactCode?: boolean;
  target: PaletteTarget;
}

const CATEGORY_DEFS: ReadonlyArray<{
  id: Exclude<PaletteCategory, "all">;
  label: string;
}> = [
  { id: "studies", label: "Studies" },
  { id: "sites", label: "Sites" },
  { id: "participants", label: "Participants" },
  { id: "forms_queries", label: "Forms & Queries" },
  { id: "findings", label: "Findings" },
  { id: "proposals", label: "Proposals" },
  { id: "governance", label: "Governance" },
  { id: "navigation", label: "Workspaces" },
];

const props = withDefaults(
  defineProps<{
    studyCode?: string;
    participants?: SubjectSummary[];
    workspaceItems?: WorkspacePaletteOption[];
    teleport?: boolean;
  }>(),
  {
    studyCode: "",
    participants: () => [],
    workspaceItems: () => [],
    teleport: true,
  }
);
const emit = defineEmits<{
  close: [];
  go: [target: PaletteTarget];
}>();

const dialogRef = ref<HTMLElement | null>(null);
const { maxHeightStyle: paletteMaxHeightStyle } = useViewportMaxHeight({
  shortTopOffset: 12,
  bottomMargin: 12,
  minHeight: 160,
});
useFocusTrap(dialogRef, {
  onEscape: () => emit("close"),
  initialFocus: "#canvas-palette-input",
});

const query = ref("");
const active = ref(0);
const selectedCategory = ref<PaletteCategory>("all");

const MAX_STUDY_CODE = 255;
const MAX_RESULTS = 30;
const DEMO_MAX_RESULTS = 80;
const maxResults = computed(() =>
  isDemoBuild() ? DEMO_MAX_RESULTS : MAX_RESULTS
);
const MAX_KERNEL_HITS = 24;
const REMOTE_SUBJECT_LIMIT = 5;
const MIN_SEARCH_LENGTH = 2;
const SEARCH_DELAY_MS = 250;
const MIN_ASK_WORDS = 3;

const { enabled: aiEnabled } = useAiAssistance();

const kernelHits = ref<KernelSearchHit[]>([]);
const kernelNote = ref("");
const subjectNote = ref("");
const remoteParticipants = ref<SubjectSummary[]>([]);
const resolvedParticipant = ref<SubjectSummary | null>(null);
const isDebouncePending = ref(false);
const pendingSearchCount = ref(0);
const activeCanvasStore = computed(() => {
  const activePinia = getActivePinia();
  return activePinia ? useStudyCanvasStore(activePinia) : null;
});
const isSearching = computed(
  () =>
    isDebouncePending.value ||
    pendingSearchCount.value > 0 ||
    Boolean(activeCanvasStore.value?.paletteLoading)
);
const displaySubjectNote = computed(() => {
  if (subjectNote.value) return subjectNote.value;
  const preloadError = activeCanvasStore.value?.paletteError;
  return preloadError
    ? `Participants can't be searched right now: ${preloadError}`
    : "";
});

function isNotFoundStatus(err: unknown): boolean {
  return Boolean(
    err &&
    typeof err === "object" &&
    "status" in err &&
    Number((err as { status?: unknown }).status) === 404
  );
}
let searchTimer: ReturnType<typeof setTimeout> | undefined;
let latestSearch = 0;
let latestParticipantLookup = 0;

async function searchKernel(text: string, study: string, request: number) {
  isDebouncePending.value = false;
  const activePinia = getActivePinia();
  const canvasStore = activePinia ? useStudyCanvasStore(activePinia) : null;
  const shouldQueryRemoteParticipants = Boolean(
    study &&
    typeof executionApi.listSubjects === "function" &&
    (!canvasStore ||
      !canvasStore.paletteLoaded ||
      canvasStore.unfilteredLoadedCount < canvasStore.paletteTotal)
  );
  if (shouldQueryRemoteParticipants) {
    pendingSearchCount.value += 1;
    void executionApi
      .listSubjects({
        studyId: study,
        q: text,
        limit: REMOTE_SUBJECT_LIMIT,
      })
      .then((page) => {
        if (request !== latestSearch) return;
        remoteParticipants.value = page?.items ?? [];
        subjectNote.value = "";
      })
      .catch((err) => {
        if (request !== latestSearch) return;
        remoteParticipants.value = [];
        if (!isNotFoundStatus(err)) {
          subjectNote.value = `Participants can't be searched right now: ${err instanceof Error ? err.message : String(err)}`;
        }
      })
      .finally(() => {
        pendingSearchCount.value = Math.max(0, pendingSearchCount.value - 1);
      });
  }

  pendingSearchCount.value += 1;
  try {
    const hits = await kernelApi.search({
      text,
      limit: MAX_KERNEL_HITS,
    });
    if (request !== latestSearch) return;
    kernelHits.value = Array.isArray(hits) ? hits : [];
    kernelNote.value = "";
  } catch (e) {
    if (request !== latestSearch) return;
    kernelHits.value = [];
    kernelNote.value = `Studies can't be searched right now: ${e instanceof Error ? e.message : String(e)}`;
  } finally {
    pendingSearchCount.value = Math.max(0, pendingSearchCount.value - 1);
  }
}

watch([query, () => props.studyCode], ([value, study]) => {
  clearTimeout(searchTimer);
  const request = ++latestSearch;
  const lookupRequest = ++latestParticipantLookup;
  const text = value.trim();
  kernelHits.value = [];
  remoteParticipants.value = [];
  resolvedParticipant.value = null;
  subjectNote.value = "";
  if (text.length < MIN_SEARCH_LENGTH || text.length > MAX_STUDY_CODE) {
    isDebouncePending.value = false;
    kernelNote.value = "";
    return;
  }
  isDebouncePending.value = true;
  searchTimer = setTimeout(
    () => void searchKernel(text, study, request),
    SEARCH_DELAY_MS
  );

  const activePinia = getActivePinia();
  const canvasStore = activePinia ? useStudyCanvasStore(activePinia) : null;
  if (
    canvasStore &&
    study &&
    !/\s/.test(text) &&
    text !== study &&
    !props.participants.some(
      (participant) =>
        participant.subject_id.toLowerCase() === text.toLowerCase()
    )
  ) {
    pendingSearchCount.value += 1;
    void canvasStore
      .resolveParticipantByCode(text, study)
      .then((matched) => {
        if (lookupRequest === latestParticipantLookup && matched) {
          resolvedParticipant.value = matched;
        }
      })
      .catch((err) => {
        if (lookupRequest !== latestParticipantLookup) return;
        if (!isNotFoundStatus(err)) {
          subjectNote.value = `Participants can't be searched right now: ${err instanceof Error ? err.message : String(err)}`;
        }
      })
      .finally(() => {
        pendingSearchCount.value = Math.max(0, pendingSearchCount.value - 1);
      });
  }
});

onBeforeUnmount(() => {
  clearTimeout(searchTimer);
  isDebouncePending.value = false;
  latestSearch++;
  latestParticipantLookup++;
});

const allParticipants = computed<SubjectSummary[]>(() => {
  const byId = new Map<string, SubjectSummary>();
  for (const participant of props.participants) {
    byId.set(participant.id, participant);
  }
  for (const candidate of [
    ...remoteParticipants.value,
    ...(resolvedParticipant.value ? [resolvedParticipant.value] : []),
  ]) {
    if (candidate.study_id && candidate.study_id !== props.studyCode) continue;
    if (!byId.has(candidate.id)) {
      byId.set(candidate.id, candidate);
    }
  }
  return Array.from(byId.values());
});

function splitCodePrefix(
  code: string | null | undefined,
  title: string
): { code: string | null; titleRest: string; label: string } {
  const cleanCode = code?.trim() || null;
  const cleanTitle = title.trim();
  if (!cleanCode) {
    return { code: null, titleRest: "", label: cleanTitle };
  }
  const prefix = `${cleanCode} · `;
  if (cleanTitle.startsWith(prefix)) {
    const rest = cleanTitle.slice(prefix.length).trim();
    return { code: cleanCode, titleRest: rest, label: cleanTitle };
  }
  if (cleanTitle.toLowerCase() === cleanCode.toLowerCase()) {
    return { code: cleanCode, titleRest: "", label: cleanCode };
  }
  return {
    code: cleanCode,
    titleRest: cleanTitle,
    label: `${cleanCode} · ${cleanTitle}`,
  };
}

function mapKernelHitToPaletteItem(hit: KernelSearchHit): PaletteItem | null {
  const ref = hit.ref;
  const code = hit.code?.trim() || null;
  const title = hit.title?.trim() || ref;
  const detail = hit.detail?.trim() || undefined;
  const exactCode = Boolean(hit.exact_code);

  switch (hit.kind) {
    case "study": {
      if (!code) return null;
      return {
        key: `kernel:${ref}`,
        group: "Study",
        label: code,
        code,
        detail: title,
        actionHint: "Open study",
        category: "studies",
        exactCode,
        target: { studyCode: code },
      };
    }
    case "study_site": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Study Site",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Site activation",
        category: "sites",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "study_site",
          nodeCode: code,
        },
      };
    }
    case "site": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Site",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Open site",
        category: "sites",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "site",
          nodeCode: code,
        },
      };
    }
    case "enrollment": {
      const subjectLabel = code || title;
      return {
        key: `kernel:${ref}`,
        group: "Participant",
        label: subjectLabel,
        code: code || subjectLabel,
        detail: detail || title,
        actionHint: "Open casebook",
        category: "participants",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "enrollment",
          nodeCode: code,
        },
      };
    }
    case "collection_instance":
      return {
        key: `kernel:${ref}`,
        group: "Form",
        label: title,
        detail,
        actionHint: "Open eCRF",
        category: "forms_queries",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "collection_instance",
          nodeCode: code,
        },
      };
    case "observation": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Observation",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Open observation",
        category: "forms_queries",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "observation",
          nodeCode: code,
        },
      };
    }
    case "form_query": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Query",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Open query",
        category: "forms_queries",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "form_query",
          nodeCode: code,
        },
      };
    }
    case "validation_finding": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Finding",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Open finding",
        category: "findings",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "validation_finding",
          nodeCode: code,
        },
      };
    }
    case "proposal":
      return {
        key: `kernel:${ref}`,
        group: "Proposal",
        label: title,
        code,
        detail,
        actionHint: "Review proposal",
        category: "proposals",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "proposal",
          nodeCode: code,
        },
      };
    case "protocol_version": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Protocol",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Protocol versions",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "protocol_version",
          nodeCode: code,
        },
      };
    }
    case "qualification": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Qualification",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Open training",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "qualification",
          nodeCode: code,
        },
      };
    }
    case "artifact":
      return {
        key: `kernel:${ref}`,
        group: "Document",
        label: title,
        code,
        detail,
        actionHint: "Open document",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "artifact",
          nodeCode: code,
        },
      };
    case "organization": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Organization",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Inspect",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "organization",
          nodeCode: code,
        },
      };
    }
    case "concept": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Concept",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Knowledge",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "concept",
          nodeCode: code,
        },
      };
    }
    case "code_system":
    case "code_system_version": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Terminology",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Knowledge",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: hit.kind,
          nodeCode: code,
        },
      };
    }
    case "standard_version":
      return {
        key: `kernel:${ref}`,
        group: "Standard",
        label: title,
        code,
        detail,
        actionHint: "Knowledge",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "standard_version",
          nodeCode: code,
        },
      };
    case "library_item": {
      const formatted = splitCodePrefix(code, title);
      return {
        key: `kernel:${ref}`,
        group: "Library",
        label: formatted.label,
        code: formatted.code,
        titleRest: formatted.titleRest,
        detail,
        actionHint: "Knowledge",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: "library_item",
          nodeCode: code,
        },
      };
    }
    default:
      return {
        key: `kernel:${ref}`,
        group: hit.kind.replace(/_/g, " "),
        label: code ? `${code} · ${title}` : title,
        code,
        detail,
        actionHint: "Inspect",
        category: "governance",
        exactCode,
        target: {
          nodeRef: ref,
          nodeKind: hit.kind,
          nodeCode: code,
        },
      };
  }
}

const entityHits = computed<PaletteItem[]>(() => {
  const inStudyEnrollmentRefs = new Set<string>();
  const inStudySubjectCodes = new Set<string>();
  for (const p of allParticipants.value) {
    const pRef = participantNodeRef(p);
    if (pRef) inStudyEnrollmentRefs.add(pRef.split("@")[0].toLowerCase());
    if (p.subject_id) inStudySubjectCodes.add(p.subject_id.toLowerCase());
  }

  const mapped: PaletteItem[] = [];
  for (const hit of kernelHits.value) {
    if (hit.kind === "enrollment") {
      const unversioned = hit.ref.split("@")[0].toLowerCase();
      if (inStudyEnrollmentRefs.has(unversioned)) continue;
      if (
        props.studyCode &&
        hit.code &&
        inStudySubjectCodes.has(hit.code.toLowerCase()) &&
        (!hit.detail ||
          hit.detail.toUpperCase().includes(props.studyCode.toUpperCase()))
      ) {
        continue;
      }
    }
    const item = mapKernelHitToPaletteItem(hit);
    if (item) mapped.push(item);
  }
  return mapped;
});

const items = computed<PaletteItem[]>(() => {
  const list: PaletteItem[] = [];
  const study = props.studyCode;
  if (study) {
    list.push({
      key: "home",
      group: "Study",
      label: `${study} home`,
      category: "studies",
      target: { studyCode: study },
    });
    for (const stage of STAGES) {
      list.push({
        key: `stage:${stage.id}`,
        group: "Stage",
        label: stage.label,
        detail: stage.scope,
        category: "navigation",
        target: { studyCode: study, stage: stage.id },
      });
      for (const section of stage.sections) {
        list.push({
          key: `section:${section.id}`,
          group: stage.label,
          label: section.label,
          detail: section.description,
          category: "navigation",
          target: { studyCode: study, stage: stage.id, section: section.id },
        });
      }
    }
  }
  for (const wsItem of props.workspaceItems) {
    list.push({
      ...wsItem,
      category: "navigation",
    });
  }
  if (study) {
    for (const participant of allParticipants.value) {
      list.push({
        key: participantNodeRef(participant) || `participant:${participant.id}`,
        group: "Participant",
        label: participant.subject_id,
        code: participant.subject_id,
        detail: [
          participantStateLabel(participant.status),
          study,
          participant.site_id ? `Site ${participant.site_id}` : "",
        ]
          .filter(Boolean)
          .join(" · "),
        actionHint: "Open casebook",
        category: "participants",
        target: {
          studyCode: study,
          stage: "conduct",
          ...(participant.site_id ? { site: participant.site_id } : {}),
          participant,
        },
      });
    }
  }
  return list;
});

const unfilteredResults = computed<PaletteItem[]>(() => {
  const text = query.value.trim();
  const needle = text.toLowerCase();
  const matches = needle
    ? items.value.filter((item) =>
        `${item.group} ${item.label} ${item.detail ?? ""}`
          .toLowerCase()
          .includes(needle)
      )
    : items.value;

  const exactEntities = entityHits.value.filter((item) => item.exactCode);
  const nonExactEntities = entityHits.value.filter((item) => !item.exactCode);
  const shown = [
    ...exactEntities,
    ...matches.slice(0, maxResults.value),
    ...nonExactEntities,
  ];

  // A sentence is a question for Assist, which answers by reading or proposing, never by writing (ADR-2635).
  if (
    aiEnabled.value &&
    props.studyCode &&
    text.split(/\s+/).length >= MIN_ASK_WORDS
  ) {
    shown.push({
      key: `ask:${text}`,
      group: "Ask AI",
      label: text,
      detail: "Opens Assist. Nothing changes until a person decides.",
      category: "navigation",
      target: { studyCode: props.studyCode, ask: text },
    });
  }
  return shown;
});

const filterChips = computed(() => {
  const list = unfilteredResults.value;
  if (!list.length) return [];
  const counts = new Map<Exclude<PaletteCategory, "all">, number>();
  for (const item of list) {
    const cat = item.category || "navigation";
    counts.set(cat, (counts.get(cat) || 0) + 1);
  }
  const populated = CATEGORY_DEFS.filter(
    (def) => (counts.get(def.id) || 0) > 0
  ).map((def) => ({
    id: def.id as PaletteCategory,
    label: def.label,
    count: counts.get(def.id) || 0,
  }));
  if (populated.length === 0) return [];
  return [
    { id: "all" as PaletteCategory, label: "All", count: list.length },
    ...populated,
  ];
});

watch(unfilteredResults, (nextList) => {
  if (selectedCategory.value === "all") return;
  const stillHasMatches = nextList.some(
    (item) => (item.category || "navigation") === selectedCategory.value
  );
  if (!stillHasMatches) {
    selectedCategory.value = "all";
  }
});

const results = computed<PaletteItem[]>(() => {
  if (selectedCategory.value === "all") {
    return unfilteredResults.value;
  }
  return unfilteredResults.value.filter(
    (item) => (item.category || "navigation") === selectedCategory.value
  );
});

function selectCategory(category: PaletteCategory) {
  selectedCategory.value = category;
  active.value = 0;
}

let lastEvaluatedQuery = "";

watch(results, (nextResults, prevResults) => {
  const normalizedQuery = query.value.trim();
  const queryChanged = normalizedQuery !== lastEvaluatedQuery;
  lastEvaluatedQuery = normalizedQuery;

  if (!queryChanged && prevResults && prevResults.length > 0) {
    const previousKey = prevResults[active.value]?.key;
    if (previousKey) {
      const retainedIndex = nextResults.findIndex(
        (item) => item.key === previousKey
      );
      if (retainedIndex >= 0) {
        active.value = retainedIndex;
        return;
      }
    }
  }
  active.value = 0;
});

function choose(item: PaletteItem) {
  emit("go", item.target);
}

function scrollActiveIntoView() {
  nextTick(() =>
    dialogRef.value
      ?.querySelector<HTMLElement>(`#canvas-palette-item-${active.value}`)
      ?.scrollIntoView?.({ block: "nearest" })
  );
}

function onKeydown(event: KeyboardEvent) {
  const count = results.value.length;
  if (event.key === "Escape") {
    event.preventDefault();
    emit("close");
  } else if (event.key === "ArrowDown" && count) {
    event.preventDefault();
    active.value = (active.value + 1) % count;
    scrollActiveIntoView();
  } else if (event.key === "ArrowUp" && count) {
    event.preventDefault();
    active.value = (active.value - 1 + count) % count;
    scrollActiveIntoView();
  } else if (event.key === "Home" && count) {
    event.preventDefault();
    active.value = 0;
    scrollActiveIntoView();
  } else if (event.key === "End" && count) {
    event.preventDefault();
    active.value = count - 1;
    scrollActiveIntoView();
  } else if (event.key === "Enter" && count) {
    event.preventDefault();
    choose(results.value[active.value]);
  }
}
</script>

<style scoped>
.palette-backdrop {
  position: fixed;
  inset: 0;
  z-index: 9999;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 10vh 16px 16px;
  box-sizing: border-box;
  background: var(--semantic-color-backdrop);
  backdrop-filter: blur(1.5px);
}

.palette {
  display: flex;
  flex-direction: column;
  width: min(680px, 100%);
  max-height: calc(100dvh - 24px);
  box-sizing: border-box;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow-xl);
  overflow: hidden;
}

.palette-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 8px 0 16px;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.palette-search-icon {
  flex-shrink: 0;
  color: var(--neutral-dark);
}

.palette-header input {
  flex: 1 1 auto;
  min-width: 0;
  width: 100%;
  box-sizing: border-box;
  padding: 14px 4px;
  border: 0;
  font-size: max(1rem, 16px);
  background: transparent;
  color: inherit;
}

.palette-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  box-sizing: border-box;
  min-width: 44px;
  min-height: 44px;
  width: 44px;
  height: 44px;
  padding: 0;
  border: 1px solid transparent;
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.palette-close-btn:not(:disabled):hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.palette-filters {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 16px;
  background: var(--semantic-color-surface-header);
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.palette-filter-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 10px;
  border-radius: 999px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 0.75rem;
  font-weight: 500;
  line-height: 1.4;
  cursor: pointer;
  font-variant-numeric: tabular-nums;
}

.palette-filter-chip:not(:disabled):not(.active):hover {
  background: var(--accent-bg);
}

.palette-filter-chip.active,
.palette-filter-chip.active:not(:disabled):hover {
  background: var(--semantic-color-interactive-primary);
  border-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-text-on-primary);
  font-weight: 600;
}

.palette-filter-count {
  font-variant-numeric: tabular-nums;
}

.palette-body {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.palette-list {
  list-style: none;
  margin: 0;
  padding: 0;
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}

.palette-item {
  display: grid;
  grid-template-columns: 6.75rem minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px 12px;
  padding: 10px 16px;
  border-left: 3px solid transparent;
  border-bottom: 1px solid var(--border);
  cursor: pointer;
}

.palette-item:last-child {
  border-bottom: 0;
}

.palette-item.active {
  background: var(--accent-bg);
  border-left-color: var(--semantic-color-interactive-primary);
}

.palette-group {
  color: var(--neutral-dark);
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.palette-item.active .palette-group {
  color: var(--semantic-color-interactive-primary);
}

.palette-item-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.palette-label {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--semantic-color-text);
  overflow-wrap: anywhere;
}

.palette-code {
  font-family: var(--font-mono);
  font-weight: 600;
}

.palette-detail {
  color: var(--neutral-dark);
  font-size: 0.78rem;
  overflow-wrap: anywhere;
}

.palette-action-hint {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--neutral-dark);
  white-space: nowrap;
}

.palette-item.active .palette-action-hint {
  color: var(--semantic-color-interactive-primary);
  font-weight: 600;
}

.palette-empty,
.palette-footnote {
  margin: 0;
  padding: 12px 16px;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.palette-footnote {
  border-top: 1px solid var(--border);
}

.palette-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px;
  padding: 10px 16px;
  border-top: 1px solid var(--border);
  background: var(--semantic-color-surface-header);
  color: var(--neutral-dark);
  font-size: 0.75rem;
  flex-shrink: 0;
}

.palette-kbd {
  display: inline-block;
  padding: 1px 5px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  font-family: var(--font-mono);
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text);
}

@media (max-width: 639px) {
  .palette-backdrop {
    padding: 12px;
  }

  .palette-item {
    grid-template-columns: minmax(0, 1fr);
    gap: 2px;
  }

  .palette-action-hint {
    justify-self: start;
  }
}

@media (max-height: 520px) {
  .palette-backdrop {
    padding: 12px;
  }
}
</style>
