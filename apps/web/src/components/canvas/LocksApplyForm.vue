<template>
  <form
    id="study-locks-apply-form"
    class="apply-card"
    data-testid="study-locks-apply-form"
    @submit.prevent="emit('submit')"
  >
    <header class="apply-card-header">
      <div>
        <h3>Milestone lock &amp; readiness</h3>
        <p class="apply-card-subtitle">
          Enforce protocol gates before asserting immutable study closeout
          status
        </p>
      </div>
      <span class="milestone-pill">Study Milestone</span>
    </header>

    <div class="milestone-switch-group">
      <span id="lock-milestone-label" class="picker-label">
        Milestone Target
      </span>
      <div
        class="milestone-segmented"
        role="group"
        aria-labelledby="lock-milestone-label"
      >
        <button
          type="button"
          data-testid="lock-milestone-interim"
          :aria-pressed="selectedMilestone === 'InterimCut'"
          :class="[
            'milestone-segment-btn',
            selectedMilestone === 'InterimCut'
              ? 'milestone-segment-btn--active'
              : '',
          ]"
          @click="emit('update:selectedMilestone', 'InterimCut')"
        >
          Interim Cut
        </button>
        <button
          type="button"
          data-testid="lock-milestone-final"
          :aria-pressed="selectedMilestone === 'FinalDatabaseLock'"
          :class="[
            'milestone-segment-btn',
            selectedMilestone === 'FinalDatabaseLock'
              ? 'milestone-segment-btn--active'
              : '',
          ]"
          @click="emit('update:selectedMilestone', 'FinalDatabaseLock')"
        >
          Final Database Lock
        </button>
      </div>
    </div>

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

    <LocksReadinessChecklist
      :readiness="readiness"
      :readiness-loading="readinessLoading"
      :readiness-gates="readinessGates"
    />

    <div class="apply-actions">
      <div class="apply-actions-buttons">
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
      <span class="oidc-role-badge" data-testid="locks-verified-oidc-role">
        Verified OIDC Role: {{ verifiedSignerRole }}
      </span>
    </div>
  </form>
</template>

<script setup lang="ts">
import { ClinicalInput } from "ui";
import type {
  KernelLockKind,
  KernelLockMilestone,
  KernelLockReadiness,
} from "../../api/kernelApi";
import type { useSignedKernelLock } from "../../composables/useSignedKernelLock";
import LocksReadinessChecklist from "./LocksReadinessChecklist.vue";
import NodeRefChip from "./NodeRefChip.vue";
import type { PickerOption } from "./useLockHierarchyPickers";

defineProps<{
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
  selectedMilestone: KernelLockMilestone;
  verifiedSignerRole: string;
  applyReason: string;
  applyScopeRef: string;
  readiness: KernelLockReadiness | null;
  readinessLoading: boolean;
  readinessGates: ReturnType<
    typeof useSignedKernelLock
  >["readinessGates"]["value"];
  isApplyDisabled: boolean;
  busy: boolean;
  canFreezeByReadiness: boolean;
  isLockBlockedByReadiness: boolean;
}>();

const emit = defineEmits<{
  (e: "update:applyScopeKind", value: string): void;
  (e: "update:selectedSiteRef", value: string): void;
  (e: "update:selectedEnrollmentRef", value: string): void;
  (e: "update:selectedFormRef", value: string): void;
  (e: "update:selectedObservationRef", value: string): void;
  (e: "update:applyKind", value: KernelLockKind): void;
  (e: "update:selectedMilestone", value: KernelLockMilestone): void;
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
