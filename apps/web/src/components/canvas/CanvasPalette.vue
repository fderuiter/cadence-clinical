<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div class="palette-backdrop" @click.self="emit('close')">
    <div
      id="canvas-palette"
      ref="dialogRef"
      class="palette"
      role="dialog"
      aria-modal="true"
      aria-label="Go to or open anything in this study"
    >
      <input
        id="canvas-palette-input"
        v-model="query"
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-controls="canvas-palette-list"
        :aria-expanded="results.length > 0"
        :aria-activedescendant="
          results.length ? `canvas-palette-item-${active}` : undefined
        "
        autocomplete="off"
        :placeholder="
          studyCode
            ? 'Stage, section, participant or study code'
            : 'Study code, e.g. CAD-101'
        "
        aria-label="Search"
        @keydown="onKeydown"
      />
      <ul
        v-if="results.length"
        id="canvas-palette-list"
        class="palette-list"
        role="listbox"
        aria-label="Results"
      >
        <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/mouse-events-have-key-events -->
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
          @mouseenter="active = index"
        >
          <span class="palette-group">{{ item.group }}</span>
          <span class="palette-label">{{ item.label }}</span>
          <span v-if="item.detail" class="palette-detail">{{
            item.detail
          }}</span>
        </li>
      </ul>
      <p v-else id="canvas-palette-empty" class="palette-empty" role="status">
        {{
          query.trim()
            ? "Nothing matches. Participants are found among those loaded in this study."
            : "Type a study code to open a study."
        }}
      </p>
      <p class="palette-footnote">
        Actions and search across every study object arrive with the kernel's
        command catalogue (#4847) and search (#4848).
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap";
import { STAGES, participantStateLabel } from "../../canvas/lifecycle";
import type { SubjectSummary } from "../../api/executionApi";
import type { PaletteTarget } from "../../canvas/palette";

/**
 * The Clinical Canvas ⌘K palette (docs/ux/CLINICAL_CANVAS.md §2.5): navigation to stages, sections and the
 * participants the canvas has loaded, and opening a study by code. It offers no action a service cannot run,
 * so it lists no commands until the kernel serves its command catalogue.
 */
interface PaletteItem {
  key: string;
  group: string;
  label: string;
  detail?: string;
  target: PaletteTarget;
}

const props = defineProps<{
  studyCode: string;
  participants: SubjectSummary[];
}>();
const emit = defineEmits<{
  close: [];
  go: [target: PaletteTarget];
}>();

const dialogRef = ref<HTMLElement | null>(null);
useFocusTrap(dialogRef);

const query = ref("");
const active = ref(0);

// Execution accepts any non-blank study code up to 255 characters, compared exactly.
const MAX_STUDY_CODE = 255;
const MAX_RESULTS = 12;

const items = computed<PaletteItem[]>(() => {
  const study = props.studyCode;
  if (!study) return [];
  const list: PaletteItem[] = [
    {
      key: "home",
      group: "Study",
      label: `${study} home`,
      target: { studyCode: study },
    },
  ];
  for (const stage of STAGES) {
    list.push({
      key: `stage:${stage.id}`,
      group: "Stage",
      label: stage.label,
      detail: stage.scope,
      target: { studyCode: study, stage: stage.id },
    });
    for (const section of stage.sections) {
      list.push({
        key: `section:${section.id}`,
        group: stage.label,
        label: section.label,
        detail: section.description,
        target: { studyCode: study, stage: stage.id, section: section.id },
      });
    }
  }
  for (const participant of props.participants) {
    list.push({
      key: `participant:${participant.id}`,
      group: "Participant",
      label: participant.subject_id,
      detail: [
        participantStateLabel(participant.status),
        participant.site_id ? `Site ${participant.site_id}` : "",
      ]
        .filter(Boolean)
        .join(" · "),
      target: {
        studyCode: study,
        stage: "conduct",
        section: "participants",
        participant,
      },
    });
  }
  return list;
});

const results = computed<PaletteItem[]>(() => {
  const text = query.value.trim();
  const needle = text.toLowerCase();
  const matches = needle
    ? items.value.filter((item) =>
        `${item.group} ${item.label} ${item.detail ?? ""}`
          .toLowerCase()
          .includes(needle)
      )
    : items.value;
  const shown = matches.slice(0, MAX_RESULTS);
  if (text && text.length <= MAX_STUDY_CODE && text !== props.studyCode) {
    shown.push({
      key: `open:${text}`,
      group: "Open study",
      label: text,
      target: { studyCode: text },
    });
  }
  return shown;
});

watch(results, () => {
  active.value = 0;
});

function choose(item: PaletteItem) {
  emit("go", item.target);
}

function onKeydown(event: KeyboardEvent) {
  const count = results.value.length;
  if (event.key === "Escape") {
    event.preventDefault();
    emit("close");
  } else if (event.key === "ArrowDown" && count) {
    event.preventDefault();
    active.value = (active.value + 1) % count;
  } else if (event.key === "ArrowUp" && count) {
    event.preventDefault();
    active.value = (active.value - 1 + count) % count;
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
  z-index: 50;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 12vh 16px 16px;
  background: rgb(15 23 42 / 40%);
}

.palette {
  width: min(640px, 100%);
  background: var(--semantic-color-surface, #fff);
  border: 1px solid var(--border);
  border-radius: 10px;
  box-shadow: 0 20px 50px rgb(15 23 42 / 25%);
  overflow: hidden;
}

.palette input {
  width: 100%;
  box-sizing: border-box;
  padding: 14px 16px;
  border: 0;
  border-bottom: 1px solid var(--border);
  font-size: 1rem;
  background: transparent;
  color: inherit;
}

.palette-list {
  list-style: none;
  margin: 0;
  padding: 6px;
  max-height: 50vh;
  overflow-y: auto;
}

.palette-item {
  display: grid;
  grid-template-columns: 7rem 1fr;
  gap: 2px 12px;
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
}

.palette-item.active {
  background: var(--accent-bg);
}

.palette-group {
  grid-row: span 2;
  color: var(--neutral-dark);
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.palette-label {
  font-weight: 600;
}

.palette-detail {
  color: var(--neutral-dark);
  font-size: 0.85rem;
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
</style>
