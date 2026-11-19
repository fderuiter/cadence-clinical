// 2. Questionnaire completion: compiling the form, drafts and validation.
import { debounce, validateField } from "ui";

import { fetchInstrument, isAuthenticatedSession } from "../api/client.js";
import { MOCK_INSTRUMENTS } from "../data/mock-data.js";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import {
  deleteDraft,
  getDraft,
  getInstrumentFromDB,
  saveDraft,
  saveInstrumentsToDB,
} from "../sync-queue.js";
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

export async function saveActiveDraftProgress() {
  if (!state.activeQuestionnaire) return;
  const { instrument } = state.activeQuestionnaire;
  Object.entries(instrument.items).forEach(([id, field]) => {
    const val =
      field.type === "choice_single"
        ? document.querySelector(`input[name="${id}"]:checked`)?.value || ""
        : document.getElementById(id)?.value?.trim() || "";
    state.activeQuestionnaire.answers[id] = val;
  });

  await saveDraft(
    state.activeQuestionnaire.assignment.id,
    state.activeQuestionnaire.answers
  );
}

export const debouncedSaveActiveDraft = debounce(saveActiveDraftProgress, 1500);

// 2. Questionnaire Completion
export async function startQuestionnaire(assignmentId) {
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
          alert(
            "Failed to retrieve questionnaire definition: " +
              (err.message || err)
          );
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
    alert("Questionnaire specification not found.");
    return;
  }

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
    const isSlider =
      field.type === "visual_analog" ||
      field.type === "vas" ||
      field.type === "slider" ||
      field.type === "range";

    if (isSlider) {
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

  // Set up listeners for real-time background auto-saving
  formContainer.addEventListener("input", debouncedSaveActiveDraft);
  formContainer.addEventListener("change", debouncedSaveActiveDraft);

  // Render and navigate synchronously
  showView("view-questionnaire");

  // Load and apply draft answers asynchronously
  try {
    const draftAnswers = await getDraft(assignmentId);
    if (
      draftAnswers &&
      Object.keys(draftAnswers).length > 0 &&
      state.activeQuestionnaire &&
      state.activeQuestionnaire.assignment.id === assignmentId
    ) {
      state.activeQuestionnaire.answers = draftAnswers;
      Object.entries(draftAnswers).forEach(([id, val]) => {
        if (!val) return;
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
              inputEl.value = val;
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
      field.type === "visual_analog" ||
      field.type === "vas" ||
      field.type === "slider" ||
      field.type === "range";

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

  return allValid;
}

// Back / Cancel: drops the draft and returns to the task list.
export async function leaveQuestionnaire() {
  if (state.activeQuestionnaire && state.activeQuestionnaire.assignment) {
    try {
      await deleteDraft(state.activeQuestionnaire.assignment.id);
    } catch (err) {
      console.warn("Could not delete draft:", err);
    }
  }
  state.activeQuestionnaire = null;
  showView("view-tasks");
}

// Sign and Submit: validates, then asks for the electronic signature.
export function requestQuestionnaireSignature() {
  if (validateActiveQuestionnaire()) {
    openSignatureModal("epro");
  } else {
    alert("Please fix all form errors before signing.");
  }
}
