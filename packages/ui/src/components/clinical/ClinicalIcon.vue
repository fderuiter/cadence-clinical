<script setup>
import { computed, watchEffect } from "vue";
import {
  CLINICAL_ICON_STROKE_WIDTH,
  CORE_CLINICAL_ICON_MAP,
  ensureExtendedClinicalIconsLoaded,
  normalizeClinicalIconName,
  normalizeClinicalIconSize,
  registeredExtendedIconsRef,
  resolveCoreClinicalIconEntry,
} from "../../icons/clinicalCoreIcons.js";

const props = defineProps({
  name: {
    type: String,
    required: true,
  },
  size: {
    type: [Number, String],
    default: 16,
  },
  label: {
    type: String,
    default: "",
  },
  ariaLabel: {
    type: String,
    default: "",
  },
  spin: {
    type: Boolean,
    default: false,
  },
});

watchEffect(() => {
  const normalized = normalizeClinicalIconName(props.name);
  if (
    normalized &&
    !Object.prototype.hasOwnProperty.call(CORE_CLINICAL_ICON_MAP, normalized) &&
    !registeredExtendedIconsRef.value
  ) {
    ensureExtendedClinicalIconsLoaded();
  }
});

const resolvedEntry = computed(() =>
  resolveCoreClinicalIconEntry(props.name, registeredExtendedIconsRef.value)
);
const resolvedSize = computed(() => normalizeClinicalIconSize(props.size));
const resolvedLabel = computed(() =>
  String(props.label || props.ariaLabel || "").trim()
);

const a11yAttrs = computed(() => {
  if (resolvedLabel.value) {
    return {
      role: "img",
      "aria-label": resolvedLabel.value,
    };
  }
  return {
    "aria-hidden": "true",
  };
});
</script>

<template>
  <component
    :is="resolvedEntry.component"
    :size="resolvedSize"
    :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
    :class="['clinical-icon', { 'clinical-icon--spin': spin }]"
    :data-clinical-icon="resolvedEntry.name"
    focusable="false"
    v-bind="a11yAttrs"
  />
</template>

<style scoped>
.clinical-icon {
  display: inline-block;
  vertical-align: -0.125em;
  flex-shrink: 0;
}

.clinical-icon--spin {
  animation: clinical-icon-spin 1s linear infinite;
}

@keyframes clinical-icon-spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

@media (prefers-reduced-motion: reduce) {
  .clinical-icon--spin {
    animation: none;
  }
}
</style>
