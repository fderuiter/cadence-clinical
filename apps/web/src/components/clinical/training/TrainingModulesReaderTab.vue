<template>
  <div
    id="training-console-panel-modules"
    role="tabpanel"
    aria-labelledby="training-console-tab-modules"
    class="tab-content modules-attestation-grid"
  >
    <div class="modules-quiz-column">
      <div class="modules-layout">
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
                      'badge-mod-' +
                      (
                        mod.module_type ||
                        (mod.section_ref
                          ? 'procedure_delta'
                          : 'protocol_overview')
                      ).toLowerCase()
                    "
                  >
                    {{
                      mod.section_ref ||
                      formatClinicalEnum(mod.module_type || "PROTOCOL_OVERVIEW")
                    }}
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
                <span class="badge badge-delta">
                  {{
                    currentModule.section_ref ||
                    formatClinicalEnum(
                      currentModule.module_type || "PROCEDURE_DELTA"
                    )
                  }}
                </span>
                <h3 class="reader-title" tabindex="-1">
                  {{ currentModule.title }}
                </h3>
              </div>
              <div class="reader-timer reader-guidance-pill tabular-nums">
                <ClinicalIcon name="check-circle" :size="14" />
                <span>
                  Minimum reading guidance:
                  <strong class="tabular-nums">{{
                    readingGuidanceLabel
                  }}</strong>
                </span>
              </div>
            </div>

            <div class="markdown-body">
              <pre class="content-text">{{ moduleSummaryText }}</pre>
              <ul
                v-if="currentModule.key_changes?.length"
                class="key-changes-list"
              >
                <li
                  v-for="(delta, dIdx) in currentModule.key_changes"
                  :key="dIdx"
                >
                  {{ delta }}
                </li>
              </ul>
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
                <span class="badge badge-delta">
                  v{{ activeCurriculum.protocol_version }}
                </span>
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

      <section
        v-if="quizQuestions.length > 0"
        data-testid="training-quiz-card"
        class="quiz-verification-card"
        aria-labelledby="training-quiz-heading"
      >
        <div class="quiz-card-header">
          <div>
            <h4 id="training-quiz-heading" class="quiz-card-title">
              Knowledge Verification Quiz
            </h4>
            <p class="quiz-card-subtitle">
              Select the single best response for each protocol verification
              item before signing your TMF 05.02.04 attestation.
            </p>
          </div>
          <span class="quiz-progress-pill tabular-nums">
            {{ answeredCount }} / {{ quizQuestions.length }} answered · Passing
            &gt;= {{ passingThresholdPercent }}%
          </span>
        </div>

        <div class="quiz-question-stack">
          <fieldset
            v-for="(q, qIdx) in quizQuestions"
            :key="q.id"
            class="quiz-question-fieldset form-fieldset"
          >
            <legend class="quiz-question-prompt tabular-nums">
              {{ qIdx + 1 }}. {{ q.prompt }}
            </legend>
            <div class="quiz-options-list">
              <label
                v-for="opt in q.options"
                :key="opt.id"
                class="quiz-option-card"
                :class="{ selected: quizAnswers[q.id] === opt.id }"
                :for="`quiz-${q.id}-${opt.id}`"
              >
                <input
                  :id="`quiz-${q.id}-${opt.id}`"
                  type="radio"
                  :name="`quiz-question-${q.id}`"
                  :value="opt.id"
                  :checked="quizAnswers[q.id] === opt.id"
                  :disabled="isCompleted || submittingCompletion"
                  @change="emit('answerQuiz', q.id, opt.id)"
                />
                <span class="quiz-option-label">{{ opt.label }}</span>
              </label>
            </div>
          </fieldset>
        </div>
      </section>
    </div>

    <TrainingAttestationPanel
      :active-assignment="activeAssignment"
      :completion-result="completionResult"
      :completion-error="completionError"
      :submitting-completion="submittingCompletion"
      :quiz-ready="quizReady"
      :total-questions="quizQuestions.length"
      :meaning="attestationMeaning"
      :reason-for-change="attestationReason"
      :signer-credential="attestationCredential"
      @update:meaning="emit('update:attestationMeaning', $event)"
      @update:reason-for-change="emit('update:attestationReason', $event)"
      @update:signer-credential="emit('update:attestationCredential', $event)"
      @submit="emit('submitAttestation')"
    />
  </div>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalIcon, handoffFocus } from "ui";
import { formatClinicalEnum } from "../../../utils/clinicalLabels";
import TrainingAttestationPanel from "./TrainingAttestationPanel.vue";
import { normalizePercentScore } from "./useProtocolTrainingConsole";

const props = defineProps({
  activeCurriculum: {
    type: Object,
    required: true,
  },
  activeAssignment: {
    type: Object,
    default: null,
  },
  selectedModuleIndex: {
    type: Number,
    default: 0,
  },
  quizQuestions: {
    type: Array,
    default: () => [],
  },
  quizAnswers: {
    type: Object,
    default: () => ({}),
  },
  quizReady: {
    type: Boolean,
    default: true,
  },
  attestationMeaning: {
    type: String,
    default: "",
  },
  attestationReason: {
    type: String,
    default: "",
  },
  attestationCredential: {
    type: String,
    default: "",
  },
  submittingCompletion: {
    type: Boolean,
    default: false,
  },
  completionResult: {
    type: Object,
    default: null,
  },
  completionError: {
    type: String,
    default: "",
  },
});

const emit = defineEmits([
  "update:selectedModuleIndex",
  "answerQuiz",
  "update:attestationMeaning",
  "update:attestationReason",
  "update:attestationCredential",
  "submitAttestation",
]);

const currentModule = computed(() => {
  if (!props.activeCurriculum?.modules) return null;
  return props.activeCurriculum.modules[props.selectedModuleIndex] || null;
});

const moduleSummaryText = computed(() => {
  if (!currentModule.value) return "";
  return (
    currentModule.value.summary ||
    currentModule.value.content_text ||
    currentModule.value.content ||
    ""
  );
});

const readingGuidanceLabel = computed(() => {
  const mod = currentModule.value;
  if (!mod) return "2 min";
  if (mod.estimated_minutes != null && Number(mod.estimated_minutes) > 0) {
    return `${mod.estimated_minutes} min`;
  }
  return `${mod.min_reading_time_seconds || 30}s`;
});

const answeredCount = computed(
  () =>
    props.quizQuestions.filter((q) => Boolean(props.quizAnswers[q.id])).length
);

const passingThresholdPercent = computed(
  () => normalizePercentScore(props.activeCurriculum?.passing_score) ?? 80
);

const isCompleted = computed(
  () =>
    props.activeAssignment?.status === "COMPLETED" ||
    Boolean(props.completionResult)
);

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
