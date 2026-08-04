<template>
  <div class="comprehension-quiz-builder-container card">
    <div class="quiz-header">
      <h3>Comprehension Quiz Authoring</h3>
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

    <!-- Question List -->
    <div class="questions-list">
      <div v-if="econsentStore.quizQuestions.length === 0" class="no-questions">
        <p>
          No comprehension check questions added yet. Click "Add Question" below
          to draft one.
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
</template>

<script setup>
import { useEconsentStore } from "../../stores/econsent.js";

const econsentStore = useEconsentStore();

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
    // Adjust correct answer index if it was the removed one or shifted
    if (q.correctAnswerIndex >= q.options.length) {
      q.correctAnswerIndex = q.options.length - 1;
    }
    updateTranslation(q);
  }
};

const updateTranslation = (q) => {
  // Sync the current active language translation
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
</script>

<style scoped>
.comprehension-quiz-builder-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  background-color: var(--color-surface);
  padding: var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: 8px;
}

.quiz-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--color-border);
  padding-bottom: var(--spacing-sm);
}

.quiz-header h3 {
  margin: 0;
  font-size: 1.15rem;
  color: var(--color-text);
}

.threshold-control {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.threshold-control label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--color-text);
}

.threshold-input-wrapper {
  display: flex;
  align-items: center;
  position: relative;
}

.threshold-input {
  width: 75px;
  padding: var(--spacing-xs) var(--spacing-xl) var(--spacing-xs)
    var(--spacing-xs);
  text-align: right;
  font-weight: bold;
}

.percentage-symbol {
  position: absolute;
  right: var(--spacing-2xs);
  font-weight: bold;
  font-size: 0.85rem;
  pointer-events: none;
  color: var(--color-text-muted);
}

.questions-list {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.no-questions {
  text-align: center;
  color: var(--color-text-muted);
  font-style: italic;
  padding: var(--spacing-lg) 0;
}

.question-item {
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: var(--spacing-md);
  background-color: var(--color-surface-muted);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.question-item-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.question-item-header h4 {
  margin: 0;
  color: var(--color-primary);
  font-size: 0.95rem;
}

.btn-delete {
  background-color: transparent;
  color: var(--color-error);
  border: none;
  cursor: pointer;
  font-weight: bold;
  font-size: 0.8rem;
  padding: var(--spacing-2xs) var(--spacing-xs);
  border-radius: 4px;
}

.btn-delete:hover {
  background-color: var(--color-error-bg);
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-2xs);
}

.form-group label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--color-text);
}

.form-control {
  padding: var(--spacing-xs) var(--spacing-sm);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  font-size: 0.9rem;
}

.options-builder {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.options-builder label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--color-text);
}

.option-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.option-row input[type="radio"] {
  width: var(--touch-target-min, 44px);
  height: var(--touch-target-min, 44px);
  cursor: pointer;
}

.option-input {
  flex: 1;
}

.btn-remove-opt {
  background-color: transparent;
  color: var(--color-text-muted);
  border: none;
  font-size: 1.1rem;
  cursor: pointer;
  padding: var(--spacing-2xs) var(--spacing-xs);
}

.btn-remove-opt:hover:not(:disabled) {
  color: var(--color-error);
}

.btn-remove-opt:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.btn-small {
  align-self: flex-start;
  font-size: 0.8rem;
  padding: var(--spacing-2xs) var(--spacing-xs);
}

.hint-group {
  border-top: 1px dashed var(--color-border);
  padding-top: var(--spacing-sm);
}

.btn-add-question {
  align-self: flex-start;
}
</style>
