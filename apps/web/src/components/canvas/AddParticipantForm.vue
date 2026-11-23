<template>
  <form
    id="add-participant-form"
    class="add-participant"
    aria-labelledby="add-participant-title"
    @submit.prevent="submit"
  >
    <h3 id="add-participant-title">Add a participant</h3>
    <p class="hint">
      The participant starts in screening. No name, birth date or other identity
      details are recorded here.
    </p>
    <label for="add-participant-code">Participant code</label>
    <input
      id="add-participant-code"
      v-model="subjectCode"
      type="text"
      maxlength="255"
      autocomplete="off"
      required
    />
    <label for="add-participant-site">
      {{
        restrictToOptions && siteOptions.length > 0 ? "Site" : "Site (optional)"
      }}
    </label>
    <select
      v-if="restrictToOptions && siteOptions.length > 0"
      id="add-participant-site"
      v-model="siteId"
      required
    >
      <option
        v-for="option in siteOptions"
        :key="option.code"
        :value="option.code"
      >
        {{ option.label }}
      </option>
    </select>
    <template v-else>
      <input
        id="add-participant-site"
        v-model="siteId"
        type="text"
        maxlength="255"
        autocomplete="off"
        :list="siteOptions.length > 0 ? 'add-participant-site-list' : undefined"
      />
      <datalist v-if="siteOptions.length > 0" id="add-participant-site-list">
        <option
          v-for="option in siteOptions"
          :key="option.code"
          :value="option.code"
        >
          {{ option.label }}
        </option>
      </datalist>
    </template>
    <label for="add-participant-reason">Reason</label>
    <textarea
      id="add-participant-reason"
      v-model="reason"
      rows="2"
      placeholder="Why this participant is being added (recorded in the audit trail)"
      required
    />
    <div class="buttons">
      <button
        id="btn-confirm-add-participant"
        type="submit"
        class="btn btn-primary"
        :disabled="busy || !subjectCode.trim() || !reason.trim()"
      >
        Add participant
      </button>
      <button type="button" class="btn btn-secondary" @click="emit('close')">
        Cancel
      </button>
    </div>
  </form>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import type { StudySiteOption } from "../../canvas/kernelStudy";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import { addParticipantMutation } from "../../stores/studyCanvasParticipantMutations";

/**
 * Adds a participant to the open study through Execution, which starts it in screening and audits the reason.
 * Codes are sent as typed: Execution compares them exactly.
 */
const props = withDefaults(
  defineProps<{
    defaultSiteId: string;
    siteOptions?: ReadonlyArray<StudySiteOption>;
    restrictToOptions?: boolean;
  }>(),
  {
    siteOptions: () => [],
    restrictToOptions: false,
  }
);
const emit = defineEmits<{
  added: [participant: SubjectSummary | null];
  close: [];
}>();

const canvas = useStudyCanvasStore();
const subjectCode = ref("");
const siteId = ref(
  props.defaultSiteId ||
    (props.restrictToOptions && props.siteOptions.length > 0
      ? props.siteOptions[0].code
      : "")
);
const reason = ref("");
const busy = ref(false);

watch(
  () =>
    [props.defaultSiteId, props.restrictToOptions, props.siteOptions] as const,
  ([nextDefault, restricted, options]) => {
    if (!siteId.value && nextDefault) {
      siteId.value = nextDefault;
    } else if (
      restricted &&
      options.length > 0 &&
      !options.some((o) => o.code === siteId.value)
    ) {
      siteId.value = nextDefault || options[0].code;
    }
  }
);

async function submit() {
  const code = subjectCode.value.trim();
  const why = reason.value.trim();
  if (!code || !why || busy.value) return;
  busy.value = true;
  try {
    const result = await addParticipantMutation(
      canvas,
      code,
      siteId.value.trim(),
      why
    );
    // When adding fails nothing was created: keep the form so the user can correct it.
    if (result.added) emit("added", result.participant);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.add-participant {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-width: 420px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.add-participant h3 {
  margin: 0;
  font-size: 1rem;
}

.hint {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.add-participant input,
.add-participant select,
.add-participant textarea {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.buttons {
  display: flex;
  gap: 6px;
}
</style>
