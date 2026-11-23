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
    <label for="add-participant-site">Site</label>
    <template v-if="hasInactiveStudySites && siteOptions.length === 0">
      <select id="add-participant-site" disabled required>
        <option value="">No active study sites available</option>
      </select>
      <p id="add-participant-no-active-sites" class="hint" role="status">
        Activate a linked study site in Start before adding participants in
        screening.
      </p>
    </template>
    <select
      v-else-if="siteOptions.length > 0"
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
    <input
      v-else
      id="add-participant-site"
      v-model="siteId"
      type="text"
      maxlength="255"
      autocomplete="off"
      placeholder="e.g. SITE-101"
      required
    />
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
        :disabled="
          busy ||
          (hasInactiveStudySites && siteOptions.length === 0) ||
          !subjectCode.trim() ||
          !siteId.trim() ||
          !reason.trim()
        "
      >
        Add participant
      </button>
      <button
        id="btn-cancel-add-participant"
        type="button"
        class="btn btn-secondary"
        @click="emit('close')"
      >
        Cancel
      </button>
    </div>
  </form>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from "vue";
import { handoffFocus } from "ui";
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
    hasInactiveStudySites?: boolean;
  }>(),
  {
    siteOptions: () => [],
    restrictToOptions: false,
    hasInactiveStudySites: false,
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
    (props.siteOptions.length > 0 ? props.siteOptions[0].code : "")
);
const reason = ref("");
const busy = ref(false);

onMounted(() => {
  void handoffFocus("#add-participant-code", { scrollIfOffscreen: true });
});

watch(
  () =>
    [props.defaultSiteId, props.restrictToOptions, props.siteOptions] as const,
  ([nextDefault, restricted, options]) => {
    if (options.length > 0) {
      if (!siteId.value) {
        siteId.value = nextDefault || options[0].code;
      } else if (
        restricted &&
        !options.some(
          (o) => o.code.toLowerCase() === siteId.value.toLowerCase()
        )
      ) {
        const matchedDefault = options.find(
          (o) => o.code.toLowerCase() === nextDefault.toLowerCase()
        );
        siteId.value = matchedDefault?.code ?? options[0].code;
      }
    } else if (!siteId.value && nextDefault) {
      siteId.value = nextDefault;
    }
  }
);

async function submit() {
  const code = subjectCode.value.trim();
  const site = siteId.value.trim();
  const why = reason.value.trim();
  if (
    !code ||
    !site ||
    !why ||
    busy.value ||
    (props.hasInactiveStudySites && props.siteOptions.length === 0)
  ) {
    return;
  }
  busy.value = true;
  try {
    const result = await addParticipantMutation(canvas, code, site, why);
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
  font-size: 1rem;
}

.buttons {
  display: flex;
  gap: 6px;
}
</style>
