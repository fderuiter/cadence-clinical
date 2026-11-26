<template>
  <component
    :is="resolvedIcon"
    :size="normalizedSize"
    :stroke-width="CLINICAL_ICON_STROKE_WIDTH"
    aria-hidden="true"
    focusable="false"
    class="clinical-icon"
  />
</template>

<script>
import { defineAsyncComponent, shallowRef } from "vue";
import { shouldThrowOnUnknownIcon } from "../../icons/iconNames.js";

const loadedRouteIcons = shallowRef(null);
let routeIconsPromise = null;
const asyncRouteIconCache = new Map();

function loadRouteIconsModule() {
  if (!routeIconsPromise) {
    routeIconsPromise = import("../../icons/routeIcons.js").then((mod) => {
      loadedRouteIcons.value = mod.CLINICAL_ROUTE_ICONS;
      return mod;
    });
  }
  return routeIconsPromise;
}

if (shouldThrowOnUnknownIcon()) {
  void loadRouteIconsModule();
}

function getLazyRouteIcon(name) {
  if (loadedRouteIcons.value && loadedRouteIcons.value[name]) {
    return loadedRouteIcons.value[name];
  }
  let cached = asyncRouteIconCache.get(name);
  if (!cached) {
    cached = defineAsyncComponent(() =>
      loadRouteIconsModule().then((mod) => mod.CLINICAL_ROUTE_ICONS[name])
    );
    asyncRouteIconCache.set(name, cached);
  }
  return cached;
}
</script>

<script setup>
import { computed } from "vue";
import {
  CLINICAL_ICON_STROKE_WIDTH,
  isClinicalRouteIconName,
  isClinicalShellIconName,
  normalizeClinicalIconSize,
} from "../../icons/iconNames.js";
import { CLINICAL_SHELL_ICONS } from "../../icons/shellIcons.js";

const props = defineProps({
  name: {
    type: String,
    default: "info",
  },
  size: {
    type: [Number, String],
    default: 16,
  },
});

const resolvedIcon = computed(() => {
  const iconName = props.name ?? "info";
  if (isClinicalShellIconName(iconName)) {
    return CLINICAL_SHELL_ICONS[iconName];
  }
  if (isClinicalRouteIconName(iconName)) {
    return getLazyRouteIcon(iconName);
  }
  if (shouldThrowOnUnknownIcon()) {
    throw new Error(
      `Unknown ClinicalIcon name "${String(iconName)}". Register a curated lucide-vue-next icon in packages/ui/src/icons/ or pass a valid ClinicalIconName.`
    );
  }
  return CLINICAL_SHELL_ICONS.info;
});

const normalizedSize = computed(() => normalizeClinicalIconSize(props.size));
</script>

<style scoped>
.clinical-icon {
  display: inline-block;
  vertical-align: middle;
  flex-shrink: 0;
}
</style>
