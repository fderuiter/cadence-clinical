<script setup>
import { computed, useAttrs, useSlots } from "vue";
import { isClinicalIconName } from "../../icons/iconNames.js";
import ClinicalIcon from "./ClinicalIcon.vue";

defineOptions({ inheritAttrs: false });

const props = defineProps({
  message: { type: String, default: "" },
  politeMessage: { type: String, default: "" },
  assertiveMessage: { type: String, default: "" },
  politeness: { type: String, default: "polite" },
  variant: { type: String, default: "strip" },
  tone: { type: String, default: "neutral" },
  icon: { type: String, default: "" },
  active: { type: Boolean, default: undefined },
  atomic: { type: Boolean, default: true },
  tabularNums: { type: Boolean, default: true },
  id: { type: String, default: "" },
  testId: { type: String, default: "" },
  persistentId: { type: Boolean, default: false },
  liveClass: { type: [String, Array, Object], default: "" },
  containerClass: { type: [String, Array, Object], default: "" },
});

const attrs = useAttrs();
const slots = useSlots();

const effectivePoliteness = computed(() =>
  props.politeness === "assertive" || props.tone === "error"
    ? "assertive"
    : "polite"
);

const resolvedPoliteText = computed(
  () =>
    props.politeMessage ||
    (effectivePoliteness.value === "polite" ? props.message : "")
);

const resolvedAssertiveText = computed(
  () =>
    props.assertiveMessage ||
    (effectivePoliteness.value === "assertive" ? props.message : "")
);

const isActive = computed(() =>
  props.active !== undefined
    ? Boolean(props.active)
    : Boolean(
        resolvedPoliteText.value || resolvedAssertiveText.value || slots.default
      )
);

const resolvedTone = computed(() =>
  props.tone && props.tone !== "neutral"
    ? props.tone
    : effectivePoliteness.value === "assertive"
      ? "error"
      : "neutral"
);

const TONE_ICONS = {
  error: "alert-circle",
  warning: "alert-triangle",
  success: "check-circle",
  info: "info",
};

const resolvedIcon = computed(() => {
  if (props.icon === "none") return "";
  if (props.icon && isClinicalIconName(props.icon)) return props.icon;
  if (props.variant === "sr-only" || props.variant === "inline") return "";
  return TONE_ICONS[resolvedTone.value] || "";
});

const effectiveTestId = computed(
  () =>
    props.testId ||
    (typeof attrs["data-testid"] === "string" ? attrs["data-testid"] : "")
);

const forwardedWrapperAttrs = computed(() => {
  const next = {};
  for (const [key, value] of Object.entries(attrs)) {
    if (key !== "class" && key !== "style" && key !== "data-testid") {
      next[key] = value;
    }
  }
  return next;
});

const isChannelVisible = (channel) =>
  isActive.value &&
  props.variant !== "sr-only" &&
  effectivePoliteness.value === channel;

const channelAttr = (value, channel) =>
  value &&
  effectivePoliteness.value === channel &&
  (isActive.value || props.persistentId)
    ? value
    : undefined;
</script>

<template>
  <div
    v-bind="forwardedWrapperAttrs"
    class="clinical-live-region"
    :class="[
      `clinical-live-region--${variant}`,
      `clinical-live-region--${resolvedTone}`,
      {
        'clinical-live-region--inactive': !isActive || variant === 'sr-only',
        'tabular-nums': tabularNums,
      },
      containerClass,
      isActive ? attrs.class : null,
    ]"
    :style="isActive ? attrs.style : undefined"
  >
    <div
      :id="channelAttr(id, 'polite')"
      :data-testid="channelAttr(effectiveTestId, 'polite')"
      class="clinical-live-region__body clinical-live-region__polite"
      :class="[
        { 'sr-only': !isChannelVisible('polite') },
        isActive && effectivePoliteness === 'polite' ? liveClass : null,
      ]"
      role="status"
      aria-live="polite"
      :aria-atomic="atomic ? 'true' : undefined"
    >
      <ClinicalIcon
        v-if="isChannelVisible('polite') && resolvedIcon"
        :name="resolvedIcon"
        :size="16"
        :class="[
          'clinical-live-region__icon',
          {
            'clinical-live-region__icon--check':
              resolvedIcon === 'check-circle',
          },
        ]"
      />
      <span class="clinical-live-region__text">
        <slot v-if="isActive && effectivePoliteness === 'polite'">{{
          resolvedPoliteText
        }}</slot>
        <template v-else>{{ resolvedPoliteText }}</template>
      </span>
    </div>

    <div
      :id="channelAttr(id, 'assertive')"
      :data-testid="channelAttr(effectiveTestId, 'assertive')"
      class="clinical-live-region__body clinical-live-region__assertive"
      :class="[
        { 'sr-only': !isChannelVisible('assertive') },
        isActive && effectivePoliteness === 'assertive' ? liveClass : null,
      ]"
      role="alert"
      :aria-atomic="atomic ? 'true' : undefined"
    >
      <ClinicalIcon
        v-if="isChannelVisible('assertive') && resolvedIcon"
        :name="resolvedIcon"
        :size="16"
        class="clinical-live-region__icon"
      />
      <span class="clinical-live-region__text">
        <slot v-if="isActive && effectivePoliteness === 'assertive'">{{
          resolvedAssertiveText
        }}</slot>
        <template v-else>{{ resolvedAssertiveText }}</template>
      </span>
    </div>

    <div
      v-if="isActive && variant !== 'sr-only' && $slots.pills"
      class="clinical-live-region__pills"
    >
      <slot name="pills" />
    </div>

    <div
      v-if="isActive && variant !== 'sr-only' && $slots.actions"
      class="clinical-live-region__actions"
    >
      <slot name="actions" />
    </div>
  </div>
</template>

<style scoped>
.clinical-live-region {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  box-sizing: border-box;
  font-size: var(--font-size-sm);
  line-height: 1.45;
  color: var(--semantic-color-text);
}

.clinical-live-region.tabular-nums {
  font-variant-numeric: tabular-nums;
}

.clinical-live-region--strip {
  padding: var(--spacing-xs) var(--spacing-md);
  border-radius: var(--radius-md);
  border: 1px solid var(--semantic-color-border);
  background-color: var(--semantic-color-surface-muted);
}

.clinical-live-region--banner {
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-md);
  border: 1px solid var(--semantic-color-border);
  background-color: var(--semantic-color-surface-muted);
}

.clinical-live-region--inline {
  padding: 0;
  border: 0;
  background: transparent;
}

.clinical-live-region--strip.clinical-live-region--info,
.clinical-live-region--banner.clinical-live-region--info {
  background-color: var(--semantic-color-info-bg-soft);
  border-color: var(--semantic-color-primary);
}

.clinical-live-region--strip.clinical-live-region--success,
.clinical-live-region--banner.clinical-live-region--success {
  background-color: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success);
}

.clinical-live-region--strip.clinical-live-region--warning,
.clinical-live-region--banner.clinical-live-region--warning {
  background-color: var(--semantic-color-warning-bg);
  border-color: var(--semantic-color-warning);
}

.clinical-live-region--strip.clinical-live-region--error,
.clinical-live-region--banner.clinical-live-region--error {
  background-color: var(--semantic-color-error-bg-soft);
  border-color: var(--semantic-color-error);
  color: var(--semantic-color-error);
}

.clinical-live-region--inline.clinical-live-region--error {
  color: var(--semantic-color-error);
}

.clinical-live-region--inactive,
.clinical-live-region--sr-only {
  padding: 0;
  margin: 0;
  border: 0;
  min-height: 0;
  background: transparent;
  box-shadow: none;
  gap: 0;
}

.clinical-live-region__body {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  min-width: 0;
  flex: 0 1 auto;
}

.clinical-live-region__icon {
  flex-shrink: 0;
}

.clinical-live-region__icon--check,
.clinical-live-region--success .clinical-live-region__icon {
  color: var(--semantic-color-success);
}

.clinical-live-region--info .clinical-live-region__icon {
  color: var(--semantic-color-primary);
}

.clinical-live-region--warning .clinical-live-region__icon {
  color: var(--semantic-color-warning);
}

.clinical-live-region--error .clinical-live-region__icon {
  color: var(--semantic-color-error);
}

.clinical-live-region__text {
  min-width: 0;
}

.clinical-live-region__pills {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-2xs);
}

.clinical-live-region__actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-xs);
  margin-left: auto;
}
</style>
