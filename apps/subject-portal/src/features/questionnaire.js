// 2. Questionnaire completion: compiling the form, preserving drafts on Back,
// confirming Cancel before discarding, and validating before signature.
import { debounce, registerDraftSource, validateField } from "ui";
import { nextTick } from "vue";

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
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalSlider,
} from "../widgets/clinical-fields.js";
import {
  clearFieldInvalid,
  markFieldInvalid,
} from "../widgets/field-errors.js";
import { showToast } from "../widgets/toast.js";
import { showView } from "./navigation.js";
import { openSignatureModal } from "./signature.js";
import { renderTasks } from "./tasks.js";

function isSliderField(field) {
  return (
    field.type === "visual_analog" ||
    field.type === "vas" ||
    field.type === "slider" ||
    field.type === "range"
  );
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
      const defaultMin = String(field.min !== undefined ? field.min : 0);
      const wasTouched =
        inputEl.getAttribute("data-touched") === "true" ||
        (existingAnswers[id] !== undefined && existingAnswers[id] !== "") ||
        String(inputEl.value) !== defaultMin;
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

  await saveDraft(assignment.id, collected);
  setDraftPresenceFlag(assignment.id, true);
  try {
    await saveAssignmentsToDB(state.assignments);
  } catch (err) {
    console.warn("Could not persist draft flag on assignment:", err);
  }
  return true;
}

export const debouncedSaveActiveDraft = debounce(saveActiveDraftProgress, 1500);

export async function flushActiveQuestionnaireDraft() {
  debouncedSaveActiveDraft.cancel();
  if (!state.activeQuestionnaire) {
    return false;
  }
  return await saveActiveDraftProgress();
}

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
  };

  // Set HTML headers synchronously
  document.getElementById("questionnaire-title").textContent = instrument.name;
  document.getElementById("questionnaire-desc").textContent =
    instrument.description;

  // Build questionnaire fields using raw HTML string widgets to remain compatible with packages/ui index.js primitives
  const formContainer = document.getElementById("questionnaire-form-container");
  formContainer.innerHTML = "";

  // Dynamic compiler
  let formHtml = `<div class="clinical-form-grid">`;
  formHtml += `<h2 class="form-title">Enter Clinical Findings</h2>`;

  Object.entries(instrument.items).forEach(([id, field]) => {
    if (isSliderField(field)) {
      formHtml += createClinicalSlider(id, field.label, "", null, 12, field);
    } else if (field.type === "choice_single") {
      formHtml += createClinicalRadioGrid(
        id,
        field.label,
        field.options,
        "",
        null,
        12
      );
    } else {
      const isNumeric = field.type === "numeric" || field.type === "number";
      const inputAttrs = {
        type: isNumeric ? "number" : "text",
        ...(isNumeric ? { inputmode: "numeric" } : {}),
      };
      formHtml += createClinicalInput(
        id,
        field.label,
        "",
        null,
        12,
        inputAttrs
      );
    }
  });

  formHtml += `</div>`;
  formContainer.innerHTML = formHtml;

  // Set up listeners for real-time background auto-saving and slider interaction tracking
  const markSliderTouchedAndSave = (event) => {
    if (event?.target?.type === "range") {
      event.target.setAttribute("data-touched", "true");
    }
    debouncedSaveActiveDraft();
  };
  formContainer.addEventListener("input", markSliderTouchedAndSave);
  formContainer.addEventListener("change", markSliderTouchedAndSave);

  // Render and navigate synchronously
  showView("view-questionnaire");

  // Load and apply draft answers asynchronously
  try {
    const draftAnswers = await getDraft(assignmentId);
    if (
      draftAnswers &&
      hasAnyRecordedAnswer(draftAnswers) &&
      state.activeQuestionnaire &&
      state.activeQuestionnaire.assignment.id === assignmentId
    ) {
      state.activeQuestionnaire.answers = { ...draftAnswers };
      setDraftPresenceFlag(assignmentId, true);
      Object.entries(draftAnswers).forEach(([id, val]) => {
        if (val === "" || val === null || val === undefined) return;
        const field = instrument.items[id];
        if (field) {
          if (field.type === "choice_single") {
            const radioInput = document.querySelector(
              `input[name="${id}"][value="${val}"]`
            );
            if (radioInput) {
              radioInput.checked = true;
            }
          } else {
            const inputEl = document.getElementById(id);
            if (inputEl) {
              inputEl.value = String(val);
              if (inputEl.type === "range") {
                inputEl.setAttribute("data-touched", "true");
              }
              const dispEl = document.getElementById(`${id}_display`);
              if (dispEl) {
                dispEl.textContent = `Value: ${val}`;
              }
            }
          }
        }
      });
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

  Object.entries(instrument.items).forEach(([id, field]) => {
    const val =
      field.type === "choice_single"
        ? document.querySelector(`input[name="${id}"]:checked`)?.value || ""
        : document.getElementById(id)?.value.trim() || "";

    // Clean previous error markers
    clearFieldInvalid(id);

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
      markFieldInvalid(id, errorMsg);
    }

    state.activeQuestionnaire.answers[id] =
      isNumericField && val !== "" && !isNaN(Number(val)) ? Number(val) : val;
  });

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
