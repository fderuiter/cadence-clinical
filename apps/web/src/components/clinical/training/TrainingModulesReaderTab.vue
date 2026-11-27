<template>
  <div
    id="training-console-panel-modules"
    role="tabpanel"
    aria-labelledby="training-console-tab-modules"
    class="tab-content modules-layout"
  >
    <div class="module-sidebar">
      <h4 class="sidebar-heading">Curriculum Sections</h4>
      <ul class="module-list">
        <li v-for="(mod, idx) in activeCurriculum.modules" :key="mod.id">
          <button
            type="button"
            class="module-item"
            :class="{ active: selectedModuleIndex === idx }"
            :aria-current="selectedModuleIndex === idx ? 'true' : undefined"
            @click="selectModule(idx)"
          >
            <span class="module-num tabular-nums">{{ idx + 1 }}</span>
            <span class="module-meta">
              <span class="module-title">{{ mod.title }}</span>
              <span
                class="badge badge-sm"
                :class="
                  'badge-mod-' + (mod.module_type || 'default').toLowerCase()
                "
              >
                {{ formatClinicalEnum(mod.module_type) }}
              </span>
            </span>
          </button>
        </li>
      </ul>
    </div>

    <div class="module-reader">
      <div v-if="currentModule" class="reader-content">
        <div class="reader-header">
          <div class="reader-title-group">
            <span class="badge badge-delta">{{
              formatClinicalEnum(currentModule.module_type)
            }}</span>
            <h3 class="reader-title" tabindex="-1">
              {{ currentModule.title }}
            </h3>
          </div>
          <div class="reader-timer tabular-nums">
            Min Reading Guidance:
            <strong class="tabular-nums"
              >{{ currentModule.min_reading_time_seconds || 30 }}s</strong
            >
          </div>
        </div>

        <div class="markdown-body">
          <pre class="content-text">{{ currentModule.content_text }}</pre>
        </div>

        <div class="reader-footer">
          <button
            id="btn-prev-training-module"
            type="button"
            class="btn btn-secondary"
            :disabled="selectedModuleIndex <= 0"
            @click="selectModule(selectedModuleIndex - 1)"
          >
            Previous Section
          </button>
          <button
            v-if="
              activeCurriculum.modules &&
              selectedModuleIndex < activeCurriculum.modules.length - 1
            "
            id="btn-next-training-module"
            type="button"
            class="btn btn-primary"
            @click="selectModule(selectedModuleIndex + 1)"
          >
            Next Section
          </button>
        </div>
      </div>
      <div v-else class="reader-content">
        <div class="reader-header">
          <div class="reader-title-group">
            <span class="badge badge-delta"
              >v{{ activeCurriculum.protocol_version }}</span
            >
            <h3 class="reader-title" tabindex="-1">
              {{ activeCurriculum.title }}
            </h3>
          </div>
        </div>
        <div class="markdown-body">
          <pre class="content-text">{{
            activeCurriculum.description ||
            "No procedural modules attached to this curriculum."
          }}</pre>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { handoffFocus } from "ui";
import { formatClinicalEnum } from "../../../utils/clinicalLabels";

const props = defineProps({
  activeCurriculum: {
    type: Object,
    required: true,
  },
  selectedModuleIndex: {
    type: Number,
    default: 0,
  },
});

const emit = defineEmits(["update:selectedModuleIndex"]);

const currentModule = computed(() => {
  if (!props.activeCurriculum?.modules) return null;
  return props.activeCurriculum.modules[props.selectedModuleIndex] || null;
});

function selectModule(index) {
  const activeEl =
    typeof document !== "undefined" ? document.activeElement : null;
  const fromFooterNav = Boolean(activeEl?.closest?.(".reader-footer"));
  emit("update:selectedModuleIndex", index);
  const total = props.activeCurriculum?.modules?.length ?? 0;
  const prevDisabled = index <= 0;
  const nextUnmounted = index >= total - 1;
  if (
    fromFooterNav &&
    ((activeEl?.id === "btn-next-training-module" && nextUnmounted) ||
      (activeEl?.id === "btn-prev-training-module" && prevDisabled))
  ) {
    void handoffFocus([
      ".reader-title",
      "#btn-next-training-module",
      "#btn-prev-training-module",
    ]);
  }
}
</script>

<style scoped src="../protocolTrainingConsole.css"></style>
