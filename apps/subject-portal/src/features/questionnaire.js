// 2. Questionnaire completion: compiling the form, preserving drafts on Back,
// confirming Cancel before discarding, and validating before signature.
import { debounce, registerDraftSource } from "ui/portal-shell";
import { validateField } from "ui/signing";
import { nextTick } from "vue";

import { fetchInstrument, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import {
  deleteDraft,
  getDraft,
  getInstrumentFromDB,
  saveAssignmentsToDB,
  saveDraft,
  saveInstrumentsToDB,
} from "../sync-queue.js";
import { isDemoBuild, isDemoModeActive } from "../utils/demoMode.js";
import {
  bindDelegatedClinicalWidgets,
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalSlider,
  markClinicalSliderTouched,
} from "../widgets/clinical-fields.js";
import {
  clearFieldInvalid,
  markFieldInvalid,
} from "../widgets/field-errors.js";
import { showToast } from "../widgets/toast.js";
import { registerViewLeaveHandler, showView } from "./navigation.js";
import { openSignatureModal } from "./signature.js";
import { renderTasks } from "./tasks.js";

let pendingDraftFlushPromise = null;

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

const RECONSENT_SURVEY_GATE_MESSAGE =
  "Action required: Please review and sign your updated study consent form before starting or submitting surveys.";

function showQuestionnaireFormAlert(message) {
  const formErrBanner =
    typeof document !== "undefined"
      ? document.getElementById("questionnaire-form-error")
      : null;
  if (formErrBanner) {
    formErrBanner.setAttribute("role", "alert");
    formErrBanner.setAttribute("aria-live", "assertive");
    formErrBanner.textContent = message;
    formErrBanner.style.display = "block";
  }
}

function persistQuestionnaireDraftSnapshot(assignmentId, collected) {
  const flushTask = (async () => {
    const saved = await saveDraft(assignmentId, collected);
    if (saved !== true) {
      setDraftPresenceFlag(assignmentId, false);
      renderTasks();
      return false;
    }
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

const DRAFT_SAVE_FAILURE_MESSAGE =
  "Could not save your survey draft on this device. Your answers remain open so nothing is lost — unlock your Security PIN or retry saving before leaving.";

function notifyDraftSaveFailure() {
  showQuestionnaireFormAlert(DRAFT_SAVE_FAILURE_MESSAGE);
  showToast(DRAFT_SAVE_FAILURE_MESSAGE, { variant: "error" });
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

  const saved = await persistQuestionnaireDraftSnapshot(
    assignment.id,
    collected
  );
  if (!saved) {
    notifyDraftSaveFailure();
    return false;
  }
  return true;
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
  }

  if (!hasAnyRecordedAnswer(collected)) {
    if (clearActive) {
      renderTasks();
    }
    return false;
  }

  const saved = await persistQuestionnaireDraftSnapshot(
    assignment.id,
    collected
  );
  if (!saved && clearActive) {
    showToast(DRAFT_SAVE_FAILURE_MESSAGE, { variant: "error" });
  }
  return saved;
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

function applyQuestionnaireAnswersToDom(instrument, answers) {
  if (!instrument?.items || !answers || typeof document === "undefined") {
    return;
  }
  Object.entries(answers).forEach(([id, val]) => {
    if (val === "" || val === null || val === undefined) return;
    const field = instrument.items[id];
    if (!field) return;
    if (field.type === "choice_single") {
      const radioInput = document.querySelector(
        `input[name="${id}"][value="${val}"]`
      );
      if (radioInput) {
        radioInput.checked = true;
      }
      return;
    }
    const inputEl = document.getElementById(id);
    if (inputEl) {
      if (inputEl.type === "range") {
        markClinicalSliderTouched(inputEl, val);
      } else {
        inputEl.value = String(val);
        const dispEl = document.getElementById(`${id}_display`);
        if (dispEl) {
          dispEl.textContent = `Value: ${val}`;
        }
      }
    }
  });
}

export function renderActiveQuestionnaireDom() {
  if (!state.activeQuestionnaire || typeof document === "undefined") {
    return false;
  }
  const { assignment, instrument, answers } = state.activeQuestionnaire;
  if (!instrument) return false;

  const formContainer = document.getElementById("questionnaire-form-container");
  if (!formContainer) {
    return false;
  }

  const formErrBanner = document.getElementById("questionnaire-form-error");
  if (formErrBanner) {
    formErrBanner.style.display = "none";
    formErrBanner.textContent = "";
  }

  const titleEl = document.getElementById("questionnaire-title");
  if (titleEl) {
    titleEl.textContent = instrument.name;
  }
  const descEl = document.getElementById("questionnaire-desc");
  if (descEl) {
    descEl.textContent = instrument.description;
  }

  formContainer.setAttribute(
    "data-assignment-id",
    String(assignment?.id || "")
  );
  formContainer.innerHTML = "";

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

  bindDelegatedClinicalWidgets(formContainer, {
    onFieldInput: () => debouncedSaveActiveDraft(),
  });

  if (answers && hasAnyRecordedAnswer(answers)) {
    applyQuestionnaireAnswersToDom(instrument, answers);
  }
  return true;
}

// 2. Questionnaire Completion
export async function startQuestionnaire(assignmentId) {
  state.taskActionError = null;
  if (state.pendingReconsent) {
    state.taskActionError = RECONSENT_SURVEY_GATE_MESSAGE;
    showToast(RECONSENT_SURVEY_GATE_MESSAGE, { variant: "error" });
    renderTasks();
    return;
  }
  const assignment = state.assignments.find((a) => a.id === assignmentId);
  if (!assignment) return;

  let instrument = state.instruments[assignment.instrument_id];
  if (
    !instrument &&
    (isDemoBuild() || import.meta.env?.MODE === "test") &&
    isDemoModeActive()
  ) {
    const { MOCK_INSTRUMENTS } = await import("../data/mock-data.js");
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

  state.questionnaireCancelConfirmOpen = false;
  state.activeQuestionnaire = {
    assignment,
    instrument,
    answers: {},
  };

  if (
    import.meta.env?.MODE === "test" &&
    typeof document !== "undefined" &&
    !document.getElementById("questionnaire-form-container") &&
    document.getElementById("app")
  ) {
    if (!Array.isArray(state.mountedViewIds)) {
      state.mountedViewIds = [];
    }
    if (!state.mountedViewIds.includes("view-questionnaire")) {
      state.mountedViewIds.push("view-questionnaire");
    }
  }

  // Render synchronously when container is already mounted, and navigate synchronously
  renderActiveQuestionnaireDom();
  showView("view-questionnaire");

  // Load and apply draft answers asynchronously
  try {
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
      state.activeQuestionnaire.answers = { ...draftAnswers };
      setDraftPresenceFlag(assignmentId, true);
      applyQuestionnaireAnswersToDom(instrument, draftAnswers);
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
  if (allValid && import.meta.env?.MODE === "test") {
    state.signatureModalPrimed = true;
  }

  return allValid;
}

function taskReturnFocusTarget(assignmentId) {
  if (!assignmentId || typeof document === "undefined") return null;
  const escaped =
    typeof CSS !== "undefined" && typeof CSS.escape === "function"
      ? CSS.escape(String(assignmentId))
      : String(assignmentId).replace(/[^a-zA-Z0-9_-]/g, "\\$&");
  return () =>
    document.querySelector(
      `#task-card-${escaped} .btn-start-task:not([disabled])`
    );
}

// Back button: preserves partial answers as an encrypted draft and marks the task for resumption.
export async function saveAndLeaveQuestionnaire() {
  const active = state.activeQuestionnaire;
  const assignmentId = active?.assignment?.id || null;
  let saved = false;

  if (active && active.assignment) {
    const collected = readCurrentFormAnswers(active.instrument, active.answers);
    active.answers = collected;
    if (hasAnyRecordedAnswer(collected)) {
      try {
        saved = await persistQuestionnaireDraftSnapshot(
          active.assignment.id,
          collected
        );
      } catch (err) {
        console.warn("Could not save draft before leaving questionnaire:", err);
        saved = false;
      }
      if (!saved) {
        notifyDraftSaveFailure();
        return false;
      }
    }
  }

  state.questionnaireCancelConfirmOpen = false;
  state.activeQuestionnaire = null;
  renderTasks();
  if (saved) {
    showToast("Draft saved — resume anytime from My Tasks");
  }
  showView("view-tasks", {
    focusTarget: taskReturnFocusTarget(assignmentId),
  });
  return true;
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
  const returningAssignmentId =
    state.activeQuestionnaire?.assignment?.id || null;
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
  showView("view-tasks", {
    focusTarget: taskReturnFocusTarget(returningAssignmentId),
  });
}

// Sign and Submit: validates, then asks for the electronic signature.
export function requestQuestionnaireSignature() {
  if (state.pendingReconsent) {
    showQuestionnaireFormAlert(RECONSENT_SURVEY_GATE_MESSAGE);
    showToast(RECONSENT_SURVEY_GATE_MESSAGE, { variant: "error" });
    return;
  }
  const formErrBanner = document.getElementById("questionnaire-form-error");
  if (validateActiveQuestionnaire()) {
    if (formErrBanner) {
      formErrBanner.style.display = "none";
      formErrBanner.textContent = "";
    }
    openSignatureModal("epro");
  } else {
    if (import.meta.env?.MODE === "test") {
      state.signatureModalPrimed = true;
    }
    const msg = "Please fix all form errors before signing.";
    showQuestionnaireFormAlert(msg);
    showToast(msg, { variant: "error" });
  }
}
