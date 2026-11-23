<template>
  <p
    v-if="
      isNewForm &&
      hasRecordRef &&
      !recordParticipant &&
      isRecordParticipantLoading
    "
    id="record-loading"
    class="stage-empty"
    role="status"
    tabindex="-1"
  >
    Loading participant…
  </p>
  <p
    v-else-if="isNewForm && hasRecordRef && !recordParticipant"
    id="record-not-loaded"
    class="stage-empty"
    tabindex="-1"
  >
    This participant isn't among those loaded for {{ studyCode }}.
    <router-link :to="participantsLocation">
      Find it in Participants </router-link
    >.
  </p>
  <CanvasFormWorkspace
    v-else
    :study-code="studyCode"
    :participant="participant"
    :enrollment-id="enrollmentId"
    :record-loading="recordLoading"
    :form-id="formId"
    :collection-spec-id="collectionSpecId"
    :planned-activity-id="plannedActivityId"
    :form-label="formLabel"
    :can-manage-queries="canManageQueries"
    :can-answer-queries="canAnswerQueries"
    :can-verify-source="canVerifySource"
    :highlight-item-id="highlightItemId"
    :back-location="backLocation"
    :back-label="backLabel"
    :handoff-location="handoffLocation"
    :form-location="formLocation"
    :new-form-cell-location="newFormCellLocation"
    :observation-location="observationLocation"
    @dirty-change="emit('dirty-change', $event)"
    @form-loaded="emit('form-loaded', $event)"
    @closed="emit('closed')"
  />
</template>

<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";
import type { EcrfForm } from "../../api/ecrfFormsApi";
import type { SubjectSummary } from "../../api/executionApi";
import CanvasFormWorkspace, {
  type CanvasFormWorkspaceProps,
} from "./CanvasFormWorkspace.vue";

defineProps<
  CanvasFormWorkspaceProps & {
    isNewForm: boolean;
    hasRecordRef: boolean;
    recordParticipant: SubjectSummary | null;
    isRecordParticipantLoading: boolean;
    participantsLocation: RouteLocationRaw;
  }
>();

const emit = defineEmits<{
  (e: "dirty-change", dirty: boolean): void;
  (e: "form-loaded", loaded: EcrfForm): void;
  (e: "closed"): void;
}>();
</script>

<style scoped>
.stage-empty {
  color: var(--neutral-dark);
}
</style>
