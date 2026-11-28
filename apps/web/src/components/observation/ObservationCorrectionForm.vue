<template>
  <form class="observation-correction" @submit.prevent="emit('submit')">
    <label for="observation-new-value">Corrected value</label>
    <select
      v-if="codelistOptions?.length"
      id="observation-new-value"
      :value="newValue"
      :disabled="saving || Boolean(missingReason)"
      :aria-invalid="parseError !== null"
      :aria-describedby="correctionDescribedBy"
      @change="
        emit('update:newValue', ($event.target as HTMLSelectElement).value)
      "
    >
      <option value="">Select…</option>
      <option
        v-if="
          newValue &&
          !codelistOptions.some((option) => option.code === newValue)
        "
        :value="newValue"
      >
        {{ newValue }}
      </option>
      <option
        v-for="option in codelistOptions"
        :key="option.code"
        :value="option.code"
      >
        {{
          option.display && option.display !== option.code
            ? `${option.display} (${option.code})`
            : option.code
        }}
      </option>
    </select>
    <input
      v-else
      id="observation-new-value"
      :value="newValue"
      type="text"
      autocomplete="off"
      :data-tabular-nums="isTabularObservation ? 'true' : undefined"
      :disabled="saving || Boolean(missingReason)"
      :aria-invalid="parseError !== null"
      :aria-describedby="correctionDescribedBy"
      @input="
        emit('update:newValue', ($event.target as HTMLInputElement).value)
      "
    />
    <span v-if="unit" data-testid="observation-unit">{{ unit }}</span>
    <div class="observation-correction-group">
      <label for="observation-missing-reason">Or missing because</label>
      <select
        id="observation-missing-reason"
        :value="missingReason"
        :disabled="saving"
        @change="
          emit(
            'update:missingReason',
            ($event.target as HTMLSelectElement).value
          )
        "
      >
        <option value="">Not missing</option>
        <option
          v-for="opt in MISSING_REASONS"
          :key="opt.reason"
          :value="opt.reason"
        >
          {{ opt.text }}
        </option>
      </select>
    </div>
    <div
      v-if="Boolean(missingReason)"
      class="observation-correction-group observation-correction-group--detail"
    >
      <label for="observation-missing-detail">
        Missing detail (optional)
      </label>
      <input
        id="observation-missing-detail"
        data-testid="observation-missing-detail"
        type="text"
        autocomplete="off"
        placeholder="Optional clinical detail (--REASND)"
        :value="missingDetail"
        :disabled="saving"
        @input="
          emit(
            'update:missingDetail',
            ($event.target as HTMLInputElement).value
          )
        "
      />
    </div>
    <button
      id="observation-save"
      class="btn btn-primary"
      type="submit"
      :disabled="saving || !canSave"
    >
      {{ saving ? "Saving…" : "Save correction" }}
    </button>
    <p
      id="observation-new-value-hint"
      class="observation-hint"
      :role="parseError ? 'alert' : undefined"
    >
      {{ parseError ?? "" }}
    </p>
  </form>
</template>

<script setup lang="ts">
import {
  MISSING_REASONS,
  type CodelistOption,
} from "../../api/observationValue";

defineProps<{
  codelistOptions?: ReadonlyArray<CodelistOption> | null;
  newValue: string;
  missingReason: string;
  missingDetail: string;
  saving: boolean;
  canSave: boolean;
  parseError: string | null;
  correctionDescribedBy: string;
  isTabularObservation: boolean;
  unit: string | null;
}>();

const emit = defineEmits<{
  (e: "update:newValue", value: string): void;
  (e: "update:missingReason", reason: string): void;
  (e: "update:missingDetail", detail: string): void;
  (e: "submit"): void;
}>();
</script>

<style scoped src="./observationView.css"></style>
<style scoped>
.observation-correction-group {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
}

.observation-correction-group--detail {
  flex: 1 1 320px;
}

#observation-missing-detail {
  flex: 1 1 220px;
  min-width: min(220px, 100%);
}

#observation-new-value,
[data-testid="observation-unit"] {
  font-family: var(--semantic-font-mono);
}

#observation-new-value {
  font-variant-numeric: tabular-nums;
}
</style>
