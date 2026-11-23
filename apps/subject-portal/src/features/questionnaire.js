// 2. Questionnaire completion: compiling the form, preserving drafts on Back,
// confirming Cancel before discarding, and validating before signature.
import {
  ClinicalInput,
  ClinicalRadioGroup,
  ClinicalSlider,
  debounce,
  registerDraftSource,
  validateField,
} from "ui";
import { createApp, h, nextTick } from "vue";

import { fetchInstrument, isAuthenticatedSession } from "../api/client.js";
import { MOCK_INSTRUMENTS } from "../data/mock-data.js";
import { state } from "../stores/state.js";
import {
  deleteDraft,
  getDraft,
  getInstrumentFromDB,
  saveAssignmentsToDB,
  saveDraft,
  saveInstrumentsToDB,
} from "../sync-queue.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import {
  clearFieldInvalid,
  markFieldInvalid,
} from "../widgets/field-errors.js";
import { showToast } from "../widgets/toast.js";
import { registerViewLeaveHandler, showView } from "./navigation.js";
import { openSignatureModal } from "./signature.js";
import { renderTasks } from "./tasks.js";

let pendingDraftFlushPromise = null;
let standaloneQuestionnaireApp = null;
let standaloneQuestionnaireContainer = null;

function isSliderField(field) {
  return (
    field.type === "visual_analog" ||
    field.type === "vas" ||
    field.type === "slider" ||
    field.type === "range"
  );
}

export function buildQuestionnaireItemDescriptors(instrument) {
  if (!instrument?.items) return [];
  return Object.entries(instrument.items).map(([id, field]) => {
    if (isSliderField(field)) {
      const min = field.min !== undefined ? Number(field.min) : 0;
      const max = field.max !== undefined ? Number(field.max) : 100;
      const step = field.step !== undefined ? Number(field.step) : 1;
      return {
        id,
        kind: "slider",
        field,
        min,
        max,
        step,
        minLabel: field.min_label || String(min),
        maxLabel: field.max_label || String(max),
      };
    }
    if (field.type === "choice_single") {
      return {
        id,
        kind: "choice_single",
        field,
      };
    }
    const isNumeric = field.type === "numeric" || field.type === "number";
    return {
      id,
      kind: "input",
      field,
      inputType: isNumeric ? "number" : "text",
      inputAttributes: isNumeric ? { inputmode: "numeric" } : {},
    };
  });
}

function unmountStandaloneQuestionnaireApp() {
  if (standaloneQuestionnaireApp) {
    try {
      standaloneQuestionnaireApp.unmount();
    } catch {
      // Ignore unmount teardown errors
    }
    standaloneQuestionnaireApp = null;
  }
  standaloneQuestionnaireContainer = null;
}

function mountStandaloneQuestionnaireFields(formContainer) {
  unmountStandaloneQuestionnaireApp();
  if (!formContainer) return;
  formContainer.innerHTML = "";

  const StandaloneForm = {
    name: "StandaloneParticipantQuestionnaire",
    render() {
      const active = state.activeQuestionnaire;
      const items = buildQuestionnaireItemDescriptors(active?.instrument);
      if (!items.length) return null;

      return h("div", { class: "clinical-form-grid" }, [
        h("h3", { class: "form-title" }, "Your Survey Questions"),
        ...items.map((item) => {
          const currentVal = active?.answers?.[item.id];
          const normalizedVal =
            currentVal === undefined || currentVal === null ? "" : currentVal;

          if (item.kind === "slider") {
            const touched =
              active?.touchedSliders && item.id in active.touchedSliders
                ? Boolean(active.touchedSliders[item.id])
                : normalizedVal !== "";
            return h(ClinicalSlider, {
              key: `${active.assignment?.id || "q"}-${item.id}`,
              id: item.id,
              label: item.field.label,
              min: item.min,
              max: item.max,
              step: item.step,
              minLabel: item.minLabel,
              maxLabel: item.maxLabel,
              gridSpan: 12,
              showQueryFlag: false,
              modelValue: normalizedVal,
              touched,
              "onUpdate:modelValue": (val) => {
                if (!state.activeQuestionnaire) return;
                state.activeQuestionnaire.answers[item.id] = val;
                debouncedSaveActiveDraft();
              },
              "onUpdate:touched": (nextTouched) => {
                if (!state.activeQuestionnaire) return;
                if (!state.activeQuestionnaire.touchedSliders) {
                  state.activeQuestionnaire.touchedSliders = {};
                }
                state.activeQuestionnaire.touchedSliders[item.id] =
                  Boolean(nextTouched);
                debouncedSaveActiveDraft();
              },
            });
          }

          if (item.kind === "choice_single") {
            return h(ClinicalRadioGroup, {
              key: `${active.assignment?.id || "q"}-${item.id}`,
              id: item.id,
              label: item.field.label,
              options: item.field.options || [],
              gridSpan: 12,
              showQueryFlag: false,
              modelValue: normalizedVal,
              "onUpdate:modelValue": (val) => {
                if (!state.activeQuestionnaire) return;
                state.activeQuestionnaire.answers[item.id] = val;
                debouncedSaveActiveDraft();
              },
            });
          }

          return h(ClinicalInput, {
            key: `${active.assignment?.id || "q"}-${item.id}`,
            id: item.id,
            label: item.field.label,
            inputType: item.inputType,
            inputAttributes: item.inputAttributes,
            gridSpan: 12,
            showQueryFlag: false,
            modelValue: normalizedVal,
            "onUpdate:modelValue": (val) => {
              if (!state.activeQuestionnaire) return;
              state.activeQuestionnaire.answers[item.id] = val;
              debouncedSaveActiveDraft();
            },
          });
        }),
      ]);
    },
  };

  standaloneQuestionnaireApp = createApp(StandaloneForm);
  standaloneQuestionnaireContainer = formContainer;
  standaloneQuestionnaireApp.mount(formContainer);
}

function readCurrentFormAnswers(instrument, existingAnswers = {}) {
  const collected = {};
  Object.entries(instrument.items || {}).forEach(([id, field]) => {
    if (field.type === "choice_single") {
      collected[id] =
        document.querySelector(`input[name="${id}"]:checked`)?.value || "";
      return;
    }

    const inputEl = document.getElementById(id);
    if (!inputEl) {
      collected[id] = existingAnswers[id] ?? "";
      return;
    }

    if (isSliderField(field)) {
      const wasTouched =
        inputEl.getAttribute("data-touched") === "true" ||
        (existingAnswers[id] !== undefined && existingAnswers[id] !== "");
      collected[id] = wasTouched ? String(inputEl.value).trim() : "";
      return;
    }

    collected[id] = inputEl.value?.trim() || "";
  });
  return collected;
}

function hasAnyRecordedAnswer(answers) {
  return Object.values(answers || {}).some(
    (val) => val !== "" && val !== null && val !== undefined
  );
}

function setDraftPresenceFlag(assignmentId, hasDraft) {
  const nextPresence = { ...(state.draftPresence || {}) };
  if (hasDraft) {
    nextPresence[assignmentId] = true;
  } else {
    delete nextPresence[assignmentId];
  }
  state.draftPresence = nextPresence;

  const targetAssignment = (state.assignments || []).find(
    (a) => a.id === assignmentId
  );
  if (targetAssignment) {
    targetAssignment.has_draft = Boolean(hasDraft);
  }
}

function persistQuestionnaireDraftSnapshot(assignmentId, collected) {
  setDraftPresenceFlag(assignmentId, true);
  renderTasks();

  const flushTask = (async () => {
    await saveDraft(assignmentId, collected);
    setDraftPresenceFlag(assignmentId, true);
    renderTasks();
    try {
      await saveAssignmentsToDB(state.assignments);
    } catch (err) {
      console.warn("Could not persist draft flag on assignment:", err);
    }
    return true;
  })();

  pendingDraftFlushPromise = flushTask.finally(() => {
    if (pendingDraftFlushPromise === flushTask) {
      pendingDraftFlushPromise = null;
    }
  });

  return pendingDraftFlushPromise;
}

export async function saveActiveDraftProgress() {
  if (!state.activeQuestionnaire) return false;
  const { instrument, assignment } = state.activeQuestionnaire;
  const collected = readCurrentFormAnswers(
    instrument,
    state.activeQuestionnaire.answers
  );
  state.activeQuestionnaire.answers = collected;

  if (!hasAnyRecordedAnswer(collected)) {
    return false;
  }

  return await persistQuestionnaireDraftSnapshot(assignment.id, collected);
}

export const debouncedSaveActiveDraft = debounce(saveActiveDraftProgress, 1500);

export async function flushActiveQuestionnaireDraft(options = {}) {
  const { clearActive = false } = options;
  debouncedSaveActiveDraft.cancel();

  if (!state.activeQuestionnaire) {
    if (pendingDraftFlushPromise) {
      return await pendingDraftFlushPromise;
    }
    return false;
  }

  const { instrument, assignment } = state.activeQuestionnaire;
  const collected = readCurrentFormAnswers(
    instrument,
    state.activeQuestionnaire.answers
  );
  state.activeQuestionnaire.answers = collected;

  if (clearActive) {
    state.questionnaireCancelConfirmOpen = false;
    state.activeQuestionnaire = null;
    if (standaloneQuestionnaireContainer) {
      unmountStandaloneQuestionnaireApp();
    }
  }

  if (!hasAnyRecordedAnswer(collected)) {
    if (clearActive) {
      renderTasks();
    }
    return false;
  }

  return await persistQuestionnaireDraftSnapshot(assignment.id, collected);
}

registerViewLeaveHandler("view-questionnaire", () => {
  void flushActiveQuestionnaireDraft({ clearActive: true });
});

export function hasUnsavedQuestionnaireDrafts() {
  if (state.activeQuestionnaire?.instrument) {
    const currentAnswers = readCurrentFormAnswers(
      state.activeQuestionnaire.instrument,
      state.activeQuestionnaire.answers
    );
    if (hasAnyRecordedAnswer(currentAnswers)) {
      return true;
    }
  }
  return Object.values(state.draftPresence || {}).some(Boolean);
}

registerDraftSource({
  flush: flushActiveQuestionnaireDraft,
  hasDrafts: hasUnsavedQuestionnaireDrafts,
});

// 2. Questionnaire Completion
export async function startQuestionnaire(assignmentId) {
  state.taskActionError = null;
  const assignment = state.assignments.find((a) => a.id === assignmentId);
  if (!assignment) return;

  let instrument = state.instruments[assignment.instrument_id];
  if (!instrument && isDemoModeActive()) {
    instrument = MOCK_INSTRUMENTS[assignment.instrument_id];
  }

  if (!instrument) {
    if (isAuthenticatedSession()) {
      try {
        instrument = await fetchInstrument(assignment.instrument_id);
        state.instruments[assignment.instrument_id] = instrument;
        if (instrument) {
          try {
            await saveInstrumentsToDB([instrument]);
          } catch (dbErr) {
            console.warn("Could not cache instrument to IndexedDB:", dbErr);
          }
        }
      } catch (err) {
        try {
          instrument = await getInstrumentFromDB(assignment.instrument_id);
        } catch (dbErr) {
          console.warn("Could not read instrument from IndexedDB:", dbErr);
        }
        if (instrument) {
          state.instruments[assignment.instrument_id] = instrument;
        } else {
          const msg =
            "Failed to retrieve questionnaire definition: " +
            (err.message || err);
          state.taskActionError = msg;
          showToast(msg, { variant: "error" });
          return;
        }
      }
    } else {
      try {
        instrument = await getInstrumentFromDB(assignment.instrument_id);
      } catch (dbErr) {
        console.warn("Could not read instrument from IndexedDB:", dbErr);
      }
      if (instrument) {
        state.instruments[assignment.instrument_id] = instrument;
      }
    }
  }

  if (!instrument) {
    const missingMsg = "Questionnaire specification not found.";
    state.taskActionError = missingMsg;
    showToast(missingMsg, { variant: "error" });
    return;
  }

  const formErrBanner = document.getElementById("questionnaire-form-error");
  if (formErrBanner) {
    formErrBanner.style.display = "none";
    formErrBanner.textContent = "";
  }

  state.questionnaireCancelConfirmOpen = false;
  state.activeQuestionnaire = {
    assignment,
    instrument,
    answers: {},
    touchedSliders: {},
    errors: {},
  };

  // Set HTML headers synchronously
  const titleEl = document.getElementById("questionnaire-title");
  if (titleEl) titleEl.textContent = instrument.name;
  const descEl = document.getElementById("questionnaire-desc");
  if (descEl) descEl.textContent = instrument.description;

  const formContainer = document.getElementById("questionnaire-form-container");
  const usesDeclarativeVue = Boolean(
    formContainer?.hasAttribute("data-vue-questionnaire")
  );
  if (formContainer && !usesDeclarativeVue) {
    mountStandaloneQuestionnaireFields(formContainer);
    if (!formContainer.hasAttribute("data-draft-listeners-bound")) {
      formContainer.setAttribute("data-draft-listeners-bound", "true");
      formContainer.addEventListener("input", () => debouncedSaveActiveDraft());
      formContainer.addEventListener("change", () =>
        debouncedSaveActiveDraft()
      );
    }
  }

  // Render and navigate synchronously
  showView("view-questionnaire");

  // Load and apply draft answers asynchronously
  try {
    if (usesDeclarativeVue) {
      await nextTick();
    }
    if (pendingDraftFlushPromise) {
      await pendingDraftFlushPromise.catch(() => {});
    }
    const draftAnswers = await getDraft(assignmentId);
    if (
      draftAnswers &&
      hasAnyRecordedAnswer(draftAnswers) &&
      state.activeQuestionnaire &&
      state.activeQuestionnaire.assignment.id === assignmentId
    ) {
      const nextTouched = {};
      Object.entries(draftAnswers).forEach(([id, val]) => {
        if (val === "" || val === null || val === undefined) return;
        const field = instrument.items[id];
        if (field && isSliderField(field)) {
          nextTouched[id] = true;
        }
      });
      state.activeQuestionnaire.touchedSliders = nextTouched;
      state.activeQuestionnaire.answers = { ...draftAnswers };
      setDraftPresenceFlag(assignmentId, true);
      await nextTick();
      showToast("Progress restored");
    }
  } catch (err) {
    console.warn("Could not retrieve draft:", err);
  }
}

export function validateActiveQuestionnaire() {
  if (!state.activeQuestionnaire) return false;

  const { instrument } = state.activeQuestionnaire;
  let allValid = true;
  const errors = [];
  const nextErrors = {};
  const formContainer = document.getElementById("questionnaire-form-container");
  const usesDeclarativeVue = Boolean(
    formContainer?.hasAttribute("data-vue-questionnaire")
  );

  Object.entries(instrument.items).forEach(([id, field]) => {
    let val;
    if (field.type === "choice_single") {
      val = document.querySelector(`input[name="${id}"]:checked`)?.value || "";
    } else if (isSliderField(field)) {
      const sliderEl = document.getElementById(id);
      val =
        sliderEl && sliderEl.getAttribute("data-touched") === "true"
          ? sliderEl.value.trim()
          : "";
    } else {
      val = document.getElementById(id)?.value.trim() || "";
    }

    // Clean previous error markers
    if (!usesDeclarativeVue) {
      clearFieldInvalid(id);
    }

    const isNumericField =
      field.type === "numeric" ||
      field.type === "number" ||
      isSliderField(field);

    const fieldMeta = {
      id: id,
      label: field.label,
      validation: {
        required: field.required,
        min: isNumericField ? field.min : undefined,
        max: isNumericField ? field.max : undefined,
      },
    };

    const res = validateField(fieldMeta, val);
    if (!res.valid) {
      allValid = false;
      const errorMsg = res.message;
      errors.push(`${field.label}: ${errorMsg}`);
      if (usesDeclarativeVue) {
        nextErrors[id] = errorMsg;
      } else {
        markFieldInvalid(id, errorMsg);
      }
    }

    state.activeQuestionnaire.answers[id] =
      isNumericField && val !== "" && !isNaN(Number(val)) ? Number(val) : val;
  });

  if (usesDeclarativeVue) {
    state.activeQuestionnaire.errors = nextErrors;
  }

  const formErrBanner = document.getElementById("questionnaire-form-error");
  if (allValid && formErrBanner) {
    formErrBanner.style.display = "none";
    formErrBanner.textContent = "";
  }

  return allValid;
}

// Back button: preserves partial answers as an encrypted draft and marks the task for resumption.
export async function saveAndLeaveQuestionnaire() {
  let saved = false;
  if (state.activeQuestionnaire && state.activeQuestionnaire.assignment) {
    try {
      saved = await saveActiveDraftProgress();
    } catch (err) {
      console.warn("Could not save draft before leaving questionnaire:", err);
    }
  }
  state.questionnaireCancelConfirmOpen = false;
  state.activeQuestionnaire = null;
  if (standaloneQuestionnaireContainer) {
    unmountStandaloneQuestionnaireApp();
  }
  renderTasks();
  if (saved) {
    showToast("Draft saved — resume anytime from My Tasks");
  }
  showView("view-tasks");
}

export async function leaveQuestionnaire() {
  await saveAndLeaveQuestionnaire();
}

// Cancel button: prompts confirmation before discarding the draft.
export function requestCancelQuestionnaire() {
  if (!state.activeQuestionnaire) {
    showView("view-tasks");
    return;
  }
  state.questionnaireCancelConfirmOpen = true;
  nextTick(() => {
    const keepBtn = document.getElementById("btn-cancel-keep-editing");
    if (keepBtn) keepBtn.focus();
  });
}

export function dismissCancelQuestionnaire() {
  state.questionnaireCancelConfirmOpen = false;
  nextTick(() => {
    const cancelBtn = document.getElementById("btn-cancel-questionnaire");
    if (cancelBtn) cancelBtn.focus();
  });
}

export async function confirmCancelQuestionnaire() {
  debouncedSaveActiveDraft.cancel();
  if (state.activeQuestionnaire && state.activeQuestionnaire.assignment) {
    const assignmentId = state.activeQuestionnaire.assignment.id;
    try {
      await deleteDraft(assignmentId);
    } catch (err) {
      console.warn("Could not delete draft on cancel:", err);
    }
    setDraftPresenceFlag(assignmentId, false);
    try {
      await saveAssignmentsToDB(state.assignments);
    } catch (err) {
      console.warn("Could not update assignment draft flag:", err);
    }
  }
  state.questionnaireCancelConfirmOpen = false;
  state.activeQuestionnaire = null;
  if (standaloneQuestionnaireContainer) {
    unmountStandaloneQuestionnaireApp();
  }
  renderTasks();
  showView("view-tasks");
}

// Sign and Submit: validates, then asks for the electronic signature.
export function requestQuestionnaireSignature() {
  const formErrBanner = document.getElementById("questionnaire-form-error");
  if (validateActiveQuestionnaire()) {
    if (formErrBanner) {
      formErrBanner.style.display = "none";
      formErrBanner.textContent = "";
    }
    openSignatureModal("epro");
  } else {
    const msg = "Please fix all form errors before signing.";
    if (formErrBanner) {
      formErrBanner.setAttribute("role", "alert");
      formErrBanner.setAttribute("aria-live", "assertive");
      formErrBanner.textContent = msg;
      formErrBanner.style.display = "block";
    }
    showToast(msg, { variant: "error" });
  }
}
