<template>
  <form
    id="study-locks-apply-form"
    class="apply-card"
    data-testid="study-locks-apply-form"
    @submit.prevent="canApply ? emit('submit') : undefined"
  >
    <h3>
      {{ canApply ? "Apply signed freeze or lock" : "Inspect closeout scope" }}
    </h3>
    <div class="apply-grid">
      <ClinicalInput
        id="lock-apply-scope-kind"
        :model-value="applyScopeKind"
        label="Scope level"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        @update:model-value="emit('update:applyScopeKind', String($event))"
        @change="emit('scopeKindChange')"
      >
        <option value="study">Study</option>
        <option value="study_site">Study site</option>
        <option value="enrollment">Participant enrollment</option>
        <option value="collection_instance">Form instance</option>
        <option value="observation">Observation</option>
      </ClinicalInput>

      <div
        v-if="applyScopeKind === 'study'"
        class="field-group"
        data-testid="lock-picker-study-target"
      >
        <span class="picker-label">Study target</span>
        <div class="selected-scope-chip">
          <NodeRefChip
            :node-ref="studyRef"
            :fallback-title="studyCode"
            include-kind
          />
        </div>
      </div>

      <ClinicalInput
        v-if="needsSitePicker"
        id="lock-picker-site"
        :model-value="selectedSiteRef"
        :label="
          applyScopeKind === 'study_site'
            ? 'Study site'
            : 'Filter by study site'
        "
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        :attributes="{ 'data-testid': 'lock-picker-site' }"
        @update:model-value="emit('update:selectedSiteRef', String($event))"
        @change="emit('sitePickerChange')"
      >
        <option value="">
          {{
            applyScopeKind === "study_site"
              ? "Select a study site…"
              : "All visible sites"
          }}
        </option>
        <option
          v-for="opt in sitePickerOptions"
          :key="opt.key"
          :value="opt.ref"
          :disabled="opt.masked"
        >
          {{ opt.label }}
        </option>
      </ClinicalInput>

      <ClinicalInput
        v-if="needsEnrollmentPicker"
        id="lock-picker-enrollment"
        :model-value="selectedEnrollmentRef"
        label="Participant enrollment"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        :disabled="enrollmentsLoading"
        :attributes="{ 'data-testid': 'lock-picker-enrollment' }"
        @update:model-value="
          emit('update:selectedEnrollmentRef', String($event))
        "
        @change="emit('enrollmentPickerChange')"
      >
        <option value="">
          {{
            enrollmentsLoading
              ? "Loading enrollments…"
              : "Select a participant enrollment…"
          }}
        </option>
        <option
          v-for="opt in enrollmentPickerOptions"
          :key="opt.key"
          :value="opt.ref"
          :disabled="opt.masked"
        >
          {{ opt.label }}
        </option>
      </ClinicalInput>

      <ClinicalInput
        v-if="needsFormPicker"
        id="lock-picker-form"
        :model-value="selectedFormRef"
        label="Form instance"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        :disabled="!selectedEnrollmentRef || formsLoading"
        :attributes="{ 'data-testid': 'lock-picker-form' }"
        @update:model-value="emit('update:selectedFormRef', String($event))"
        @change="emit('formPickerChange')"
      >
        <option value="">
          {{
            !selectedEnrollmentRef
              ? "Choose an enrollment first…"
              : formsLoading
                ? "Loading form instances…"
                : "Select a form instance…"
          }}
        </option>
        <option
          v-for="opt in formPickerOptions"
          :key="opt.key"
          :value="opt.ref"
          :disabled="opt.masked"
        >
          {{ opt.label }}
        </option>
      </ClinicalInput>

      <ClinicalInput
        v-if="needsObservationPicker"
        id="lock-picker-observation"
        :model-value="selectedObservationRef"
        label="Observation"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        :disabled="!selectedFormRef || observationsLoading"
        :attributes="{ 'data-testid': 'lock-picker-observation' }"
        @update:model-value="
          emit('update:selectedObservationRef', String($event))
        "
      >
        <option value="">
          {{
            !selectedFormRef
              ? "Choose a form instance first…"
              : observationsLoading
                ? "Loading observations…"
                : "Select an observation…"
          }}
        </option>
        <option
          v-for="opt in observationPickerOptions"
          :key="opt.key"
          :value="opt.ref"
          :disabled="opt.masked"
        >
          {{ opt.label }}
        </option>
      </ClinicalInput>

      <ClinicalInput
        v-if="canApply"
        id="lock-apply-kind"
        :model-value="applyKind"
        label="Assertion"
        type="select"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group"
        @update:model-value="emit('update:applyKind', $event as KernelLockKind)"
      >
        <option value="Frozen">
          Frozen (blocks entry &amp; corrections; query closure stays open)
        </option>
        <option value="Locked">Locked (blocks all changes)</option>
      </ClinicalInput>

      <ClinicalInput
        v-if="canApply"
        id="lock-apply-reason"
        :model-value="applyReason"
        label="Reason for change"
        type="text"
        density="compact"
        :show-query-flag="false"
        extra-class="field-group field-group--wide"
        autocomplete="off"
        placeholder="Interim analysis cut / database lock after query closure"
        @update:model-value="emit('update:applyReason', String($event))"
      />
    </div>

    <div v-if="canApply" class="apply-actions">
      <button
        id="btn-apply-study-lock"
        type="submit"
        class="btn btn-primary"
        :disabled="isApplyDisabled"
      >
        Sign &amp; apply {{ applyKind }}
      </button>
      <button
        type="button"
        class="btn btn-secondary btn-mini-freeze"
        :disabled="busy || !applyScopeRef.trim() || !canFreezeByReadiness"
        @click="emit('quickApply', 'Frozen')"
      >
        Quick Freeze…
      </button>
      <button
        type="button"
        class="btn btn-secondary btn-mini-lock"
        :disabled="busy || !applyScopeRef.trim() || isLockBlockedByReadiness"
        @click="emit('quickApply', 'Locked')"
      >
        Quick Lock…
      </button>
    </div>
  </form>
</template>

<script setup lang="ts">
import { ClinicalInput } from "ui";
import type { KernelLockKind } from "../../api/kernelApi";
import NodeRefChip from "./NodeRefChip.vue";
import type { PickerOption } from "./useLockHierarchyPickers";

withDefaults(
  defineProps<{
    canApply?: boolean;
    studyCode: string;
    studyRef: string;
    applyScopeKind: string;
    selectedSiteRef: string;
    selectedEnrollmentRef: string;
    selectedFormRef: string;
    selectedObservationRef: string;
    needsSitePicker: boolean;
    needsEnrollmentPicker: boolean;
    needsFormPicker: boolean;
    needsObservationPicker: boolean;
    sitePickerOptions: PickerOption[];
    enrollmentPickerOptions: PickerOption[];
    formPickerOptions: PickerOption[];
    observationPickerOptions: PickerOption[];
    enrollmentsLoading: boolean;
    formsLoading: boolean;
    observationsLoading: boolean;
    applyKind: KernelLockKind;
    applyReason: string;
    applyScopeRef: string;
    isApplyDisabled: boolean;
    busy: boolean;
    canFreezeByReadiness: boolean;
    isLockBlockedByReadiness: boolean;
  }>(),
  { canApply: true }
);

const emit = defineEmits<{
  (e: "update:applyScopeKind", value: string): void;
  (e: "update:selectedSiteRef", value: string): void;
  (e: "update:selectedEnrollmentRef", value: string): void;
  (e: "update:selectedFormRef", value: string): void;
  (e: "update:selectedObservationRef", value: string): void;
  (e: "update:applyKind", value: KernelLockKind): void;
  (e: "update:applyReason", value: string): void;
  (e: "scopeKindChange"): void;
  (e: "sitePickerChange"): void;
  (e: "enrollmentPickerChange"): void;
  (e: "formPickerChange"): void;
  (e: "submit"): void;
  (e: "quickApply", kind: KernelLockKind): void;
}>();
</script>

<style scoped src="./locksSection.css"></style>
<style scoped src="./canvasBanners.css"></style>
