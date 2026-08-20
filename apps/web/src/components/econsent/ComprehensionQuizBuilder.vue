<template>
  <div class="comprehension-quiz-builder-container card">
    <!-- Header with Mode Switcher & Threshold Control -->
    <div class="quiz-header">
      <div class="header-titles">
        <h3>Comprehension Check & Quiz Assessment</h3>
        <p class="subtitle">
          Draft questions and verify participant understanding before signature
          execution.
        </p>
      </div>

      <div class="header-actions">
        <!-- Mode Tabs -->
        <div class="mode-tabs" role="tablist">
          <button
            type="button"
            role="tab"
            :aria-selected="activeMode === 'authoring'"
            :class="['mode-tab-btn', { active: activeMode === 'authoring' }]"
            @click="setMode('authoring')"
          >
            ✏️ Authoring Mode
          </button>
          <button
            type="button"
            role="tab"
            :aria-selected="activeMode === 'interactive'"
            :class="['mode-tab-btn', { active: activeMode === 'interactive' }]"
            @click="setMode('interactive')"
          >
            🎯 Interactive Assessment
          </button>
        </div>

        <div class="threshold-control">
          <label for="threshold-select">Passing Score Threshold:</label>
          <div class="threshold-input-wrapper">
            <input
              id="threshold-select"
              v-model.number="econsentStore.passingThreshold"
              type="number"
              min="0"
              max="100"
              step="5"
              class="form-control threshold-input"
            />
            <span class="percentage-symbol">%</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ==================== AUTHORING MODE ==================== -->
    <div v-if="activeMode === 'authoring'" class="authoring-view">
      <div class="questions-list">
        <div
          v-if="econsentStore.quizQuestions.length === 0"
          class="no-questions"
        >
          <p>
            No comprehension check questions added yet. Click "Add Quiz
            Question" below to draft one.
          </p>
        </div>

        <div
          v-for="(q, qIndex) in econsentStore.quizQuestions"
          :key="q.id"
          class="question-item card"
        >
          <div class="question-item-header">
            <h4>Question #{{ qIndex + 1 }}</h4>
            <button
              type="button"
              class="btn-delete"
              title="Remove Question"
              @click="removeQuestion(qIndex)"
            >
              ✕ Remove
            </button>
          </div>

          <div class="form-group">
            <label :for="'q-text-' + q.id">Question Text:</label>
            <input
              :id="'q-text-' + q.id"
              v-model="q.text"
              type="text"
              placeholder="e.g. What is the main benefit of participating?"
              class="form-control"
              @input="updateTranslation(q)"
            />
          </div>

          <!-- Choices/Options Builder -->
          <div class="options-builder">
            <label
              >Answer Choices (Select the radio button for the correct
              choice):</label
            >
            <div
              v-for="(opt, optIndex) in q.options"
              :key="optIndex"
              class="option-row"
            >
              <input
                type="radio"
                :name="'correct-opt-' + q.id"
                :checked="q.correctAnswerIndex === optIndex"
                title="Mark as correct answer"
                @change="q.correctAnswerIndex = optIndex"
              />
              <input
                v-model="q.options[optIndex]"
                type="text"
                :placeholder="'Option ' + (optIndex + 1)"
                class="form-control option-input"
                @input="updateTranslation(q)"
              />
              <button
                type="button"
                class="btn-remove-opt"
                :disabled="q.options.length <= 2"
                title="Remove Option"
                @click="removeOption(qIndex, optIndex)"
              >
                ✕
              </button>
            </div>

            <button
              type="button"
              class="btn btn-secondary btn-small"
              @click="addOption(qIndex)"
            >
              ➕ Add Option Choice
            </button>
          </div>

          <div class="form-group hint-group">
            <label :for="'q-hint-' + q.id">Explanatory Feedback Hint:</label>
            <input
              :id="'q-hint-' + q.id"
              v-model="q.hint"
              type="text"
              placeholder="e.g. Hint: Refer to Section 2 of the consent document."
              class="form-control"
              @input="updateTranslation(q)"
            />
          </div>
        </div>
      </div>

      <button
        type="button"
        class="btn btn-primary btn-add-question"
        @click="addQuestion"
      >
        ➕ Add Quiz Question
      </button>
    </div>

    <!-- ==================== INTERACTIVE ASSESSMENT MODE ==================== -->
    <div v-else class="interactive-view">
      <div class="assessment-instructions">
        <p>
          Answer the questions below to verify your comprehension of the
          clinical consent document. Instant feedback will be provided upon
          checking your answers.
        </p>
      </div>

      <!-- Assessment Status / Score Banner -->
      <div
        v-if="assessmentSubmitted"
        id="assessment-result-banner"
        :class="[
          'assessment-result-card',
          evaluationResult.passed ? 'passed' : 'failed',
        ]"
        role="status"
        aria-live="polite"
      >
        <div class="result-icon">
          {{ evaluationResult.passed ? "🎉" : "⚠️" }}
        </div>
        <div class="result-details">
          <h4 class="result-title">
            {{
              evaluationResult.passed
                ? "Passing Threshold Met!"
                : "Passing Threshold Not Met"
            }}
          </h4>
          <p class="result-score">
            Score: <strong>{{ evaluationResult.score }}%</strong> ({{
              evaluationResult.correctCount
            }}/{{ evaluationResult.total }} correct). Required passing
            threshold: <strong>{{ econsentStore.passingThreshold }}%</strong>.
          </p>
          <p v-if="evaluationResult.passed" class="result-message">
            Congratulations! You have satisfied the comprehension requirements
            and may proceed to dual-credential signature capture.
          </p>
          <p v-else class="result-message">
            Please review the highlighted hints below and retry the
            comprehension assessment.
          </p>
        </div>
      </div>

      <!-- Interactive Questions List -->
      <div class="assessment-questions-list">
        <div
          v-for="(q, qIndex) in econsentStore.quizQuestions"
          :key="q.id"
          class="assessment-question-card card"
        >
          <fieldset class="question-fieldset">
            <legend class="question-legend">
              <span class="q-number">Question #{{ qIndex + 1 }}:</span>
              <span class="q-text">{{ q.text }}</span>
            </legend>

            <div class="assessment-options-list" role="radiogroup">
              <label
                v-for="(opt, optIndex) in q.options"
                :key="optIndex"
                :class="[
                  'assessment-option-label',
                  {
                    selected: selectedAnswers[q.id] === optIndex,
                    correct:
                      assessmentSubmitted && optIndex === q.correctAnswerIndex,
                    incorrect:
                      assessmentSubmitted &&
                      selectedAnswers[q.id] === optIndex &&
                      optIndex !== q.correctAnswerIndex,
                  },
                ]"
              >
                <input
                  type="radio"
                  :name="'quiz-ans-' + q.id"
                  :value="optIndex"
                  :checked="selectedAnswers[q.id] === optIndex"
                  @change="selectAnswer(q.id, optIndex)"
                />
                <span class="option-text">{{ opt }}</span>
                <span
                  v-if="
                    assessmentSubmitted && optIndex === q.correctAnswerIndex
                  "
                  class="feedback-badge correct-badge"
                  >✓ Correct</span
                >
                <span
                  v-else-if="
                    assessmentSubmitted &&
                    selectedAnswers[q.id] === optIndex &&
                    optIndex !== q.correctAnswerIndex
                  "
                  class="feedback-badge incorrect-badge"
                  >✕ Incorrect</span
                >
              </label>
            </div>

            <!-- Instant Explanatory Feedback Hint -->
            <div
              v-if="
                assessmentSubmitted &&
                selectedAnswers[q.id] !== undefined &&
                selectedAnswers[q.id] !== q.correctAnswerIndex &&
                q.hint
              "
              class="instant-hint-box"
            >
              <span class="hint-icon">💡</span>
              <span class="hint-text"
                ><strong>Feedback Hint:</strong> {{ q.hint }}</span
              >
            </div>
          </fieldset>
        </div>
      </div>

      <!-- Assessment Actions Footer -->
      <div class="assessment-footer">
        <button
          type="button"
          class="btn btn-secondary btn-reset-quiz"
          @click="resetAssessment"
        >
          🔄 Reset Answers
        </button>

        <button
          type="button"
          class="btn btn-primary btn-submit-answers"
          @click="submitAssessment"
        >
          ✅ Check Answers & Evaluate
        </button>

        <button
          v-if="assessmentSubmitted && evaluationResult.passed"
          type="button"
          id="btn-proceed-signature"
          class="btn btn-success btn-proceed-signature"
          @click="proceedToSignature"
        >
          ✍️ Proceed to eSignature
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive } from "vue";
import { useEconsentStore } from "../../stores/econsent.js";

const emit = defineEmits(["quiz-passed", "proceed-to-sign"]);

const econsentStore = useEconsentStore();

const activeMode = ref("authoring");
const selectedAnswers = reactive({});
const assessmentSubmitted = ref(false);
const evaluationResult = reactive({
  score: 0,
  passed: false,
  correctCount: 0,
  total: 0,
});

const setMode = (mode) => {
  activeMode.value = mode;
};

// --- Authoring Operations ---
const addQuestion = () => {
  const newQuestion = {
    id: `q-${Date.now()}`,
    text: "",
    options: ["", ""],
    correctAnswerIndex: 0,
    hint: "",
    translations: {
      en: { text: "", options: ["", ""], hint: "" },
    },
  };
  econsentStore.quizQuestions.push(newQuestion);
};

const removeQuestion = (index) => {
  econsentStore.quizQuestions.splice(index, 1);
};

const addOption = (qIndex) => {
  econsentStore.quizQuestions[qIndex].options.push("");
  updateTranslation(econsentStore.quizQuestions[qIndex]);
};

const removeOption = (qIndex, optIndex) => {
  const q = econsentStore.quizQuestions[qIndex];
  if (q.options.length > 2) {
    q.options.splice(optIndex, 1);
    if (q.correctAnswerIndex >= q.options.length) {
      q.correctAnswerIndex = q.options.length - 1;
    }
    updateTranslation(q);
  }
};

const updateTranslation = (q) => {
  const lang = econsentStore.activeLanguage || "en";
  if (!q.translations) {
    q.translations = {};
  }
  q.translations[lang] = {
    text: q.text,
    options: [...q.options],
    hint: q.hint,
  };
};

// --- Interactive Assessment Operations ---
const selectAnswer = (questionId, optionIndex) => {
  selectedAnswers[questionId] = optionIndex;
};

const submitAssessment = () => {
  const result = econsentStore.evaluateQuiz(selectedAnswers);
  evaluationResult.score = result.score;
  evaluationResult.passed = result.passed;
  evaluationResult.correctCount = result.correctCount;
  evaluationResult.total = result.total;
  assessmentSubmitted.value = true;

  if (result.passed) {
    emit("quiz-passed", result);
  }
};

const resetAssessment = () => {
  Object.keys(selectedAnswers).forEach((k) => delete selectedAnswers[k]);
  assessmentSubmitted.value = false;
  evaluationResult.score = 0;
  evaluationResult.passed = false;
  evaluationResult.correctCount = 0;
  evaluationResult.total = 0;
  econsentStore.resetQuiz();
};

const proceedToSignature = () => {
  emit("proceed-to-sign");
};

// Expose state for unit testing
defineExpose({
  activeMode,
  selectedAnswers,
  assessmentSubmitted,
  evaluationResult,
  submitAssessment,
  resetAssessment,
  setMode,
});
</script>

<style scoped>
.comprehension-quiz-builder-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
  background-color: white;
  padding: 20px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.quiz-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  border-bottom: 1px solid var(--border);
  padding-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.header-titles h3 {
  margin: 0 0 4px 0;
  font-size: 1.2rem;
  color: var(--neutral-dark);
}

.subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: #64748b;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.mode-tabs {
  display: flex;
  background-color: var(--neutral-light);
  padding: 4px;
  border-radius: 6px;
  border: 1px solid var(--border);
  gap: 4px;
}

.mode-tab-btn {
  padding: 6px 12px;
  font-size: 0.85rem;
  font-weight: 600;
  border: none;
  background: transparent;
  border-radius: 4px;
  cursor: pointer;
  color: var(--neutral-dark);
  transition: all 0.2s;
}

.mode-tab-btn.active {
  background-color: white;
  color: var(--primary);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.threshold-control {
  display: flex;
  align-items: center;
  gap: 8px;
}

.threshold-control label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.threshold-input-wrapper {
  display: flex;
  align-items: center;
  position: relative;
}

.threshold-input {
  width: 75px;
  padding: 6px 16px 6px 8px;
  text-align: right;
  font-weight: bold;
}

.percentage-symbol {
  position: absolute;
  right: 6px;
  font-weight: bold;
  font-size: 0.85rem;
  pointer-events: none;
  color: #64748b;
}

/* Authoring Mode Styles */
.authoring-view,
.interactive-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.questions-list,
.assessment-questions-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.no-questions {
  text-align: center;
  color: #64748b;
  font-style: italic;
  padding: 20px 0;
}

.question-item,
.assessment-question-card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  background-color: var(--neutral-light);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.question-item-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.question-item-header h4 {
  margin: 0;
  color: var(--primary);
  font-size: 0.95rem;
}

.btn-delete {
  background-color: transparent;
  color: var(--error);
  border: none;
  cursor: pointer;
  font-weight: bold;
  font-size: 0.8rem;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-delete:hover {
  background-color: #fef2f2;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.form-control {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.9rem;
}

.options-builder {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.options-builder label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.option-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.option-row input[type="radio"] {
  width: 18px;
  height: 18px;
  cursor: pointer;
}

.option-input {
  flex: 1;
}

.btn-remove-opt {
  background-color: transparent;
  color: #94a3b8;
  border: none;
  font-size: 1.1rem;
  cursor: pointer;
  padding: 4px 8px;
}

.btn-remove-opt:hover:not(:disabled) {
  color: var(--error);
}

.btn-remove-opt:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.btn-small {
  align-self: flex-start;
  font-size: 0.8rem;
  padding: 4px 8px;
}

.hint-group {
  border-top: 1px dashed var(--border);
  padding-top: 12px;
}

.btn-add-question {
  align-self: flex-start;
}

/* Interactive Assessment Styles */
.assessment-instructions {
  background-color: #eff6ff;
  border-left: 4px solid var(--primary);
  padding: 12px 16px;
  border-radius: 4px;
  font-size: 0.9rem;
  color: #1e3a8a;
}

.assessment-instructions p {
  margin: 0;
}

.assessment-result-card {
  display: flex;
  gap: 16px;
  padding: 16px;
  border-radius: 8px;
  align-items: flex-start;
}

.assessment-result-card.passed {
  background-color: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #166534;
}

.assessment-result-card.failed {
  background-color: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
}

.result-icon {
  font-size: 2rem;
}

.result-details {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.result-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: bold;
}

.result-score,
.result-message {
  margin: 0;
  font-size: 0.9rem;
  line-height: 1.4;
}

.question-fieldset {
  border: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.question-legend {
  font-weight: 600;
  font-size: 1rem;
  color: var(--neutral-dark);
  margin-bottom: 8px;
  display: flex;
  gap: 8px;
}

.q-number {
  color: var(--primary);
}

.assessment-options-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.assessment-option-label {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  background-color: white;
  border: 1px solid var(--border);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s;
  font-size: 0.9rem;
}

.assessment-option-label:hover {
  background-color: #f8fafc;
  border-color: #cbd5e1;
}

.assessment-option-label.selected {
  border-color: var(--primary);
  background-color: #f0f9ff;
}

.assessment-option-label.correct {
  border-color: #22c55e;
  background-color: #f0fdf4;
  font-weight: 600;
}

.assessment-option-label.incorrect {
  border-color: #ef4444;
  background-color: #fef2f2;
}

.assessment-option-label input[type="radio"] {
  width: 18px;
  height: 18px;
  cursor: pointer;
}

.option-text {
  flex: 1;
}

.feedback-badge {
  font-size: 0.75rem;
  font-weight: bold;
  padding: 2px 8px;
  border-radius: 4px;
}

.correct-badge {
  background-color: #dcfce7;
  color: #15803d;
}

.incorrect-badge {
  background-color: #fee2e2;
  color: #b91c1c;
}

.instant-hint-box {
  display: flex;
  gap: 8px;
  align-items: center;
  background-color: #fffbeb;
  border: 1px solid #fef3c7;
  color: #92400e;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
  margin-top: 4px;
}

.assessment-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  border-top: 1px solid var(--border);
  padding-top: 16px;
  flex-wrap: wrap;
}

.btn-success {
  background-color: #16a34a;
  color: white;
  border: none;
}

.btn-success:hover {
  background-color: #15803d;
}
</style>
