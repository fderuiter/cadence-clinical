<template>
  <ParticipantRecord
    v-if="participant"
    :participant="participant"
    :can-manage="canManage"
    :form-location="formLocation"
    :new-form-location="newFormLocation!"
    :new-form-cell-location="newFormCellLocation"
    :handoff-location="handoffLocation"
    :observation-location="observationLocation"
    @retry="emit('retry')"
  />
  <p
    v-else-if="loading"
    id="record-loading"
    class="stage-empty"
    role="status"
    tabindex="-1"
  >
    Loading participant…
  </p>
  <p v-else id="record-not-loaded" class="stage-empty" tabindex="-1">
    This participant isn't among those loaded for {{ studyCode }}.
    <router-link :to="participantsLocation">
      Find it in Participants </router-link
    >.
  </p>
</template>

<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";
import type { SubjectSummary } from "../../api/executionApi";
import ParticipantRecord from "./ParticipantRecord.vue";

defineProps<{
  studyCode: string;
  participant: SubjectSummary | null;
  loading: boolean;
  canManage: boolean;
  participantsLocation: RouteLocationRaw;
  formLocation: (formId: string, itemId?: string | null) => RouteLocationRaw;
  newFormLocation?: RouteLocationRaw;
  newFormCellLocation?: (
    collectionSpecId: string,
    plannedActivityId?: string | null
  ) => RouteLocationRaw;
  handoffLocation?: RouteLocationRaw | null;
  observationLocation: (
    observationId: string,
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw;
}>();

const emit = defineEmits<{
  retry: [];
}>();
</script>

<style scoped>
.stage-empty {
  color: var(--neutral-dark);
}
</style>
