<template>
  <form
    id="participant-site-assignment"
    class="site-assign"
    aria-label="Assign clinical site"
    @submit.prevent="submit"
  >
    <div class="field">
      <label for="participant-site-assign-input">Assign site</label>
      <input
        id="participant-site-assign-input"
        v-model="siteId"
        type="text"
        list="participant-site-assign-options"
        placeholder="e.g. SITE-101"
        autocomplete="off"
        required
      />
      <datalist v-if="siteChoices.length" id="participant-site-assign-options">
        <option
          v-for="option in siteChoices"
          :key="option.code"
          :value="option.code"
        >
          {{ option.label }}
        </option>
      </datalist>
    </div>

    <div class="field">
      <label for="participant-site-assign-reason">Reason for assignment</label>
      <textarea
        id="participant-site-assign-reason"
        v-model="reason"
        rows="2"
        placeholder="Why this site is being assigned (recorded in the audit trail)"
        required
      />
    </div>

    <button
      id="btn-assign-participant-site"
      type="submit"
      class="btn btn-primary"
      :disabled="!canSubmit || saving"
    >
      {{ saving ? "Assigning..." : "Assign site" }}
    </button>
  </form>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import { findStudySites, type StudySiteOption } from "../../canvas/kernelStudy";
import { useAuthStore } from "../../stores/auth";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

/**
 * Audited clinical site assignment for a siteless participant. Assigning a site synchronizes the participant's
 * kernel screening enrollment so scheduled eCRF forms can be opened immediately in Clinical Canvas.
 */
const props = defineProps<{
  participant: SubjectSummary;
  studyCode?: string;
}>();

const canvas = useStudyCanvasStore();
const authStore = useAuthStore();

const siteId = ref("");
const reason = ref("");
const saving = ref(false);
const kernelSiteOptions = ref<StudySiteOption[]>([]);

const siteChoices = computed<StudySiteOption[]>(() => {
  const byCode = new Map<string, StudySiteOption>();
  for (const option of kernelSiteOptions.value) {
    byCode.set(option.code, option);
  }
  const assigned = Array.isArray(authStore.assignedSites)
    ? authStore.assignedSites
    : [];
  for (const code of assigned) {
    const trimmed = String(code ?? "").trim();
    if (trimmed && !byCode.has(trimmed)) {
      byCode.set(trimmed, { code: trimmed, label: trimmed });
    }
  }
  return Array.from(byCode.values()).sort((a, b) =>
    a.code.localeCompare(b.code)
  );
});

const canSubmit = computed(
  () => Boolean(siteId.value.trim()) && Boolean(reason.value.trim())
);

watch(
  () => props.studyCode || props.participant.study_id,
  async (study) => {
    kernelSiteOptions.value = [];
    const target = (study ?? "").trim();
    if (!target) return;
    try {
      const options = await findStudySites(target);
      kernelSiteOptions.value = options;
      if (!siteId.value.trim() && options.length === 1) {
        siteId.value = options[0].code;
      }
    } catch {
      kernelSiteOptions.value = [];
    }
  },
  { immediate: true }
);

watch(
  () => props.participant.id,
  () => {
    reason.value = "";
    if (!siteId.value.trim() && siteChoices.value.length === 1) {
      siteId.value = siteChoices.value[0].code;
    }
  },
  { immediate: true }
);

async function submit() {
  if (!canSubmit.value || saving.value) return;
  saving.value = true;
  try {
    const ok = await canvas.assignParticipantSite(
      props.participant,
      siteId.value,
      reason.value
    );
    if (ok) {
      reason.value = "";
    }
  } finally {
    saving.value = false;
  }
}
</script>

<style scoped>
.site-assign {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.field input,
.field textarea {
  min-height: var(--semantic-touch-target-staff);
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font: inherit;
  font-size: 1rem;
  box-sizing: border-box;
}

.site-assign button {
  align-self: flex-start;
}
</style>
