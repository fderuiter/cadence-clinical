<template>
  <ClinicalFieldLayout
    v-bind="layoutProps"
    tag="div"
    extra-class="clinical-lookup-container"
    @create-query="$emit('create-query', $event)"
    @respond-query="$emit('respond-query', $event)"
    @close-query="$emit('close-query')"
    @reopen-query="$emit('reopen-query')"
  >
    <template
      #default="{ id: slotId, describedBy, hasError, required: slotRequired }"
    >
      <input
        v-bind="controlAttributes"
        :id="slotId"
        :type="resolvedType"
        :name="slotId"
        :value="modelValue"
        :disabled="effectiveDisabled || undefined"
        :required="slotRequired || undefined"
        :aria-required="slotRequired ? 'true' : effectiveAriaRequired"
        :min="effectiveMin"
        :max="effectiveMax"
        :step="effectiveStep"
        :data-tabular-nums="effectiveDataTabularNums"
        autocomplete="off"
        :aria-describedby="mergeDescribedBy(describedBy)"
        :aria-invalid="resolveAriaInvalid(hasError)"
        @input="handleInput"
        @change="handleChange"
      />
    </template>

    <template #after-input>
      <!-- Lookup Status Indicator -->
      <div
        v-if="status !== 'none'"
        :id="`lookup-status-${id}`"
        class="lookup-status-indicator"
        :class="stateClass"
        role="status"
        aria-live="polite"
      >
        <span class="lookup-status-icon" aria-hidden="true">{{
          statusIcon
        }}</span>
        <span class="lookup-status-text">{{ ariaLiveMessage }}</span>
      </div>
      <div
        v-else
        :id="`lookup-status-${id}`"
        class="lookup-status-indicator"
        role="status"
        aria-live="polite"
        style="display: none"
      ></div>
    </template>
  </ClinicalFieldLayout>
</template>

<script setup lang="ts">
import { computed, useAttrs } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import {
  buildFieldLayoutProps,
  splitContainerAndControlAttributes,
  firstDefinedAttr,
  resolveBooleanAttr,
  resolveEffectiveRequiredAndAria,
  mergeDescribedByIds,
  resolveAriaInvalidValue,
  resolveTabularNumsAttr,
} from "./sharedProps.js";

defineOptions({ inheritAttrs: false });

const attrs = useAttrs();

const STRIPPED_KEYS = new Set([
  "class",
  "style",
  "type",
  "inputmode",
  "data-tabular-nums",
  "disabled",
  "required",
  "aria-required",
  "min",
  "max",
  "step",
  "aria-describedby",
  "aria-invalid",
]);

interface Props {
  statusMessage?: string;
  status?: "none" | "loading" | "valid" | "invalid" | "degraded";
  id: string;
  label: string;
  modelValue?: string | number;
  type?: string;
  error?: string | null;
  gridSpan?: number | string;
  query?: any;
  attributes?: Record<string, any>;
  canManageQueries?: boolean;
  queryLabel?: string;
  required?: boolean;
  disabled?: boolean;
  min?: number | string | null;
  max?: number | string | null;
  step?: number | string | null;
  hint?: string;
  unit?: string;
  range?: string;
  density?: string;
  hideLabel?: boolean;
  showQueryFlag?: boolean;
}

const props = withDefaults(defineProps<Props>(), {
  statusMessage: "",
  status: "none",
  modelValue: "",
  type: "text",
  error: null,
  gridSpan: 12,
  query: null,
  attributes: () => ({}),
  canManageQueries: false,
  queryLabel: "",
  required: false,
  disabled: false,
  min: null,
  max: null,
  step: null,
  hint: "",
  unit: "",
  range: "",
  density: "comfortable",
  hideLabel: false,
  showQueryFlag: true,
});

const emit = defineEmits<{
  (e: "update:modelValue", value: string | number): void;
  (e: "input", value: string | number): void;
  (e: "change", value: string | number, target: HTMLInputElement | null): void;
  (e: "create-query", event: unknown): void;
  (e: "respond-query", event: unknown): void;
  (e: "close-query"): void;
  (e: "reopen-query"): void;
}>();

const splitAttrs = computed(() =>
  splitContainerAndControlAttributes(props.attributes, attrs, STRIPPED_KEYS)
);
const containerAttributes = computed(() => splitAttrs.value.container);
const controlAttributes = computed(() => splitAttrs.value.control);

const resolvedType = computed(() => {
  const raw =
    firstDefinedAttr(props.type, attrs.type, props.attributes?.type) || "text";
  return raw === "concept_code" ? "text" : String(raw);
});

const effectiveDisabled = computed(() =>
  resolveBooleanAttr(
    props.disabled ? true : undefined,
    attrs.disabled,
    props.attributes?.disabled
  )
);

const reqState = computed(() => resolveEffectiveRequiredAndAria(props, attrs));
const effectiveRequired = computed(() => reqState.value.required);
const effectiveAriaRequired = computed(
  () => reqState.value.ariaRequired as "true" | "false" | undefined
);
const layoutProps = computed(() =>
  buildFieldLayoutProps(
    props,
    attrs,
    containerAttributes.value,
    effectiveRequired.value
  )
);

const effectiveMin = computed(() =>
  firstDefinedAttr(props.min, attrs.min, props.attributes?.min)
);
const effectiveMax = computed(() =>
  firstDefinedAttr(props.max, attrs.max, props.attributes?.max)
);
const effectiveStep = computed(() =>
  firstDefinedAttr(props.step, attrs.step, props.attributes?.step)
);
const effectiveDataTabularNums = computed(() => {
  const explicitAttr =
    attrs["data-tabular-nums"] ?? props.attributes?.["data-tabular-nums"];
  const explicit =
    explicitAttr !== undefined && explicitAttr !== null
      ? explicitAttr !== false && explicitAttr !== "false"
      : undefined;
  return resolveTabularNumsAttr(
    explicit,
    resolvedType.value.toLowerCase(),
    String(
      firstDefinedAttr(attrs.inputmode, props.attributes?.inputmode) || ""
    ),
    effectiveMin.value,
    effectiveMax.value,
    effectiveStep.value
  );
});

function resolveAriaInvalid(
  hasError: boolean
): "true" | "false" | "grammar" | "spelling" | undefined {
  return resolveAriaInvalidValue(
    props.status === "invalid" || hasError,
    attrs["aria-invalid"],
    props.attributes?.["aria-invalid"]
  ) as "true" | "false" | "grammar" | "spelling" | undefined;
}

function mergeDescribedBy(layoutDescribedBy?: string): string | undefined {
  return mergeDescribedByIds(
    props.status !== "none" ? `lookup-status-${props.id}` : "",
    props.attributes?.["aria-describedby"],
    attrs["aria-describedby"],
    layoutDescribedBy
  );
}

function handleInput(event: Event) {
  const target = event.target as HTMLInputElement | null;
  const value = target?.value ?? "";
  emit("update:modelValue", value);
  emit("input", value);
}

function handleChange(event: Event) {
  const target = event.target as HTMLInputElement | null;
  emit("change", target?.value ?? "", target);
}

const stateClass = computed(() => {
  if (props.status === "loading") return "lookup-loading";
  if (props.status === "valid") return "lookup-valid";
  if (props.status === "invalid") return "lookup-invalid";
  if (props.status === "degraded") return "lookup-degraded";
  return "";
});

const statusIcon = computed(() => {
  if (props.status === "loading") return "⏳";
  if (props.status === "valid") return "✅";
  if (props.status === "invalid") return "❌";
  if (props.status === "degraded") return "⚠️";
  return "";
});

const ariaLiveMessage = computed(() => {
  if (props.statusMessage) return props.statusMessage;
  if (props.status === "loading") return "Searching terminology database...";
  if (props.status === "valid") return "Code is valid.";
  if (props.status === "invalid")
    return "Invalid code. Please check and try again.";
  if (props.status === "degraded")
    return "Terminology service degraded. Validation offline.";
  return "";
});
</script>
