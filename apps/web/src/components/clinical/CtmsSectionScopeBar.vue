<template>
  <form
    :id="formId"
    class="scope-bar"
    :aria-label="ariaLabel || undefined"
    @submit.prevent="$emit('submit')"
  >
    <template v-if="showStudyInput">
      <ClinicalInput
        :id="`${prefix}-study`"
        :model-value="study"
        label="Study"
        type="text"
        autocomplete="off"
        placeholder="Study code"
        :list="assignedStudies.length ? `${prefix}-studies-list` : ''"
        density="compact"
        :show-query-flag="false"
        @update:model-value="$emit('update:study', String($event))"
      >
        <template v-if="assignedStudies.length" #after-input>
          <datalist :id="`${prefix}-studies-list`">
            <option v-for="s in assignedStudies" :key="s" :value="s" />
          </datalist>
        </template>
      </ClinicalInput>
    </template>

    <slot name="before-site" />

    <ClinicalInput
      :id="`${prefix}-site`"
      :model-value="site"
      :label="siteLabel"
      type="text"
      autocomplete="off"
      :placeholder="sitePlaceholder"
      :list="assignedSites.length ? `${prefix}-sites-list` : ''"
      density="compact"
      :show-query-flag="false"
      @update:model-value="$emit('update:site', String($event))"
    >
      <template v-if="assignedSites.length" #after-input>
        <datalist :id="`${prefix}-sites-list`">
          <option v-for="item in assignedSites" :key="item" :value="item" />
        </datalist>
      </template>
    </ClinicalInput>

    <slot name="after-site" />

    <button
      :id="submitId"
      type="submit"
      class="btn btn-secondary"
      :disabled="submitDisabled"
    >
      {{ submitLabel }}
    </button>
  </form>

  <div
    v-if="scopeGuidance"
    :id="`${prefix}-scope-guidance`"
    class="scope-guidance"
    role="status"
  >
    {{ scopeGuidance }}
  </div>
  <p v-else-if="loadError" class="error-banner" role="status">
    {{ loadError }}
  </p>
  <p v-else-if="idleMessage" class="muted-status" role="status">
    {{ idleMessage }}
  </p>
</template>

<script setup lang="ts">
import { ClinicalInput } from "ui";
withDefaults(
  defineProps<{
    prefix: string;
    formId: string;
    ariaLabel?: string;
    showStudyInput?: boolean;
    study?: string;
    site?: string;
    siteLabel?: string;
    sitePlaceholder?: string;
    assignedStudies?: string[];
    assignedSites?: string[];
    submitId: string;
    submitLabel: string;
    submitDisabled?: boolean;
    scopeGuidance?: string;
    loadError?: string;
    idleMessage?: string;
  }>(),
  {
    ariaLabel: "",
    showStudyInput: true,
    study: "",
    site: "",
    siteLabel: "Site",
    sitePlaceholder: "Site code",
    assignedStudies: () => [],
    assignedSites: () => [],
    submitDisabled: false,
    scopeGuidance: "",
    loadError: "",
    idleMessage: "",
  }
);

defineEmits<{
  (e: "update:study", value: string): void;
  (e: "update:site", value: string): void;
  (e: "submit"): void;
}>();
</script>
