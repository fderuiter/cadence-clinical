<template>
  <form
    id="study-subjects-bar"
    class="study-subjects-bar"
    aria-label="Study"
    @submit.prevent="submit"
  >
    <template v-if="visibleStudies.length > 0">
      <label for="study-subjects-select">Study</label>
      <select
        id="study-subjects-select"
        :value="store.activeStudyId"
        :disabled="store.subjectsLoading"
        @change="onSelectStudy"
      >
        <option value="">Select a study…</option>
        <option
          v-if="
            store.activeStudyId &&
            !visibleStudies.some((s) => s.code === store.activeStudyId)
          "
          :value="store.activeStudyId"
        >
          {{ store.activeStudyId }}
        </option>
        <option
          v-for="study in visibleStudies"
          :key="study.code"
          :value="study.code"
        >
          {{
            study.title && study.title !== study.code
              ? `${study.code} — ${study.title}`
              : study.code
          }}
        </option>
      </select>
      <details class="study-code-disclosure">
        <summary class="study-code-summary">Enter study code directly</summary>
        <div class="study-code-row">
          <label for="study-subjects-code">Study code</label>
          <input
            id="study-subjects-code"
            v-model="studyCode"
            type="text"
            autocomplete="off"
            placeholder="Study code, e.g. CADENCE-101"
          />
          <button
            id="btn-load-study-subjects"
            type="submit"
            class="btn btn-secondary"
            :disabled="!studyCode.trim() || store.subjectsLoading"
          >
            Show subjects
          </button>
        </div>
      </details>
    </template>
    <template v-else>
      <label for="study-subjects-code">Study</label>
      <input
        id="study-subjects-code"
        v-model="studyCode"
        type="text"
        autocomplete="off"
        placeholder="Study code, e.g. CADENCE-101"
      />
      <button
        id="btn-load-study-subjects"
        type="submit"
        class="btn btn-secondary"
        :disabled="!studyCode.trim() || store.subjectsLoading"
      >
        Show subjects
      </button>
    </template>
    <span id="study-subjects-status" class="status" role="status">
      {{ statusText }}
    </span>
  </form>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import * as VueRouter from "vue-router";
import { kernelApi } from "../../api/kernelApi";
import { useClinicalScopeStore } from "../../stores/clinicalScope";
import { syncWorkspaceScope } from "../../stores/workspaceScope";
import { isDemoBuild } from "../../utils/demoMode.js";

/**
 * Chooses the study whose subjects the eCRF lists, and reports how the load went (#4828).
 * Outside demo mode, visible studies are loaded from the Clinical Kernel so users can pick a study
 * directly without typing a code while still allowing direct study code entry for deep links.
 */
const emit = defineEmits(["loaded"]);
const store = useClinicalScopeStore();
const route =
  typeof VueRouter.useRoute === "function" ? VueRouter.useRoute() : null;
const router =
  "useRouter" in VueRouter && typeof VueRouter.useRouter === "function"
    ? VueRouter.useRouter()
    : null;
const studyCode = ref(store.activeStudyId);
const visibleStudies = ref([]);
const studiesLoading = ref(false);

watch(
  () => store.activeStudyId,
  (value) => {
    studyCode.value = value;
  }
);

const statusText = computed(() => {
  if (store.subjectsLoading) return "Loading subjects...";
  if (store.subjectsError) return "Subjects couldn't be loaded.";
  if (!store.activeStudyId) {
    return visibleStudies.value.length > 0
      ? "Select a study or enter a study code to list its subjects."
      : "Enter a study code to list its subjects.";
  }
  const shown = store.subjects.length;
  const total = store.subjectsTotal;
  if (total === 0) return "No subjects in this study yet.";
  const noun = total === 1 ? "subject" : "subjects";
  return shown < total
    ? `Showing the first ${shown} of ${total} ${noun}.`
    : `${total} ${noun}.`;
});

async function loadVisibleStudies() {
  if (isDemoBuild() || typeof kernelApi?.listStudies !== "function") return;
  studiesLoading.value = true;
  try {
    const studies = await kernelApi.listStudies();
    const seen = new Set();
    const next = [];
    for (const item of Array.isArray(studies) ? studies : []) {
      const code = typeof item?.code === "string" ? item.code.trim() : "";
      if (!code || item.mask || seen.has(code)) continue;
      seen.add(code);
      next.push({
        code,
        title: typeof item.title === "string" ? item.title.trim() : "",
      });
    }
    visibleStudies.value = next;
  } catch {
    visibleStudies.value = [];
  } finally {
    studiesLoading.value = false;
  }
}

async function activateStudy(rawCode) {
  const code = String(rawCode ?? "").trim();
  if (!code) return;
  studyCode.value = code;
  syncWorkspaceScope({
    studyId: code,
    router,
    route,
    syncRouteQuery: true,
  });
  if (await store.loadSubjects(code)) emit("loaded");
}

async function onSelectStudy(event) {
  const code = event?.target?.value ?? "";
  if (!code) return;
  await activateStudy(code);
}

async function submit() {
  await activateStudy(studyCode.value);
}

onMounted(() => {
  void loadVisibleStudies();
});
</script>

<style scoped>
.study-subjects-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
}

.study-subjects-bar label {
  font-weight: 600;
}

.study-subjects-bar select,
.study-subjects-bar input {
  flex: 0 1 16rem;
  min-width: 0;
  min-height: var(--semantic-control-target-min, 44px);
  padding: 8px 10px;
  font-size: var(--font-size-md);
  border: 1px solid var(--semantic-color-border-control, var(--border));
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.study-code-disclosure {
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-secondary);
}

.study-code-summary {
  cursor: pointer;
  font-weight: 500;
  padding: 4px 2px;
}

.study-code-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-top: 6px;
}

.study-subjects-bar .status {
  color: var(--semantic-color-text-secondary);
  font-size: var(--font-size-sm);
}
</style>
