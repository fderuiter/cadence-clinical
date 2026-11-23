// Protocol amendment re-consent: the pending requirement and its modal.
import { DEMO_STUDY } from "demo-fixtures";
import {
  SEMANTIC_ACTIONS,
  buildStepUpVerificationPayload,
  requiresCustomSignatureReason,
  resolveSignatureReason,
} from "ui";
import { nextTick } from "vue";

import { dispatchApi, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import { showToast } from "../widgets/toast.js";
import { logAuditRecord } from "./audit.js";
import { renderTasks } from "./tasks.js";

export const DEFAULT_RECONSENT_COMPREHENSION_QUESTIONS = Object.freeze([
  {
    id: "reconsent_q_changes",
    text: "Have you reviewed the highlighted clause updates in this amended study consent?",
    options: [
      "Yes, I reviewed the updated clauses",
      "No, I have not reviewed them yet",
    ],
    correct_option: "Yes, I reviewed the updated clauses",
  },
  {
    id: "reconsent_q_voluntary",
    text: "Does your study participation remain voluntary with the right to withdraw at any time?",
    options: [
      "Yes, I can withdraw at any time",
      "No, withdrawal is no longer permitted",
    ],
    correct_option: "Yes, I can withdraw at any time",
  },
]);

function normalizeDiffChangeType(rawType, previousText, updatedText) {
  const normalized = String(rawType || "")
    .trim()
    .toLowerCase();
  if (normalized === "added" || normalized === "inserted") return "added";
  if (normalized === "removed" || normalized === "deleted") return "removed";
  if (normalized === "modified" || normalized === "updated") return "modified";
  if (!previousText && updatedText) return "added";
  if (previousText && !updatedText) return "removed";
  return "modified";
}

export function resolveReconsentClauseDiffs(pendingReconsent) {
  const rawClauses =
    pendingReconsent?.clause_diffs ||
    pendingReconsent?.amended_clauses ||
    pendingReconsent?.clauses;

  if (Array.isArray(rawClauses) && rawClauses.length > 0) {
    return rawClauses.map((clause, index) => {
      const previousText = String(
        clause?.previous_text ?? clause?.old_text ?? clause?.before ?? ""
      ).trim();
      const updatedText = String(
        clause?.updated_text ??
          clause?.new_text ??
          clause?.after ??
          clause?.content ??
          ""
      ).trim();
      const changeType = normalizeDiffChangeType(
        clause?.change_type ?? clause?.status,
        previousText,
        updatedText
      );
      return {
        id: clause?.id || clause?.clause_id || `clause_diff_${index + 1}`,
        clause_title:
          clause?.clause_title ||
          clause?.title ||
          `Amended Clause ${index + 1}`,
        change_type: changeType,
        previous_text: previousText,
        updated_text: updatedText,
      };
    });
  }

  const summaryText =
    pendingReconsent?.change_summary ||
    pendingReconsent?.summary_of_changes ||
    "Updated study procedures and consent terms requiring participant review.";

  return [
    {
      id: "clause_diff_schedule",
      clause_title: "Updated Study Procedures and Visit Schedule",
      change_type: "modified",
      previous_text:
        "Prior consent version covered baseline study procedures before this update.",
      updated_text: summaryText,
    },
    {
      id: "clause_diff_rights",
      clause_title: "Participant Rights and Voluntary Continuation",
      change_type: "added",
      previous_text: "",
      updated_text:
        "Your continued participation remains voluntary, and new ePRO diary submissions stay paused until you review and sign this amended consent.",
    },
  ];
}

export function resolveReconsentComprehensionQuestions(pendingReconsent) {
  const explicitQuestions =
    pendingReconsent?.comprehension_check?.questions ||
    pendingReconsent?.comprehension_questions ||
    pendingReconsent?.questions;
  const expectedMap =
    pendingReconsent?.comprehension_check?.expected_answers ||
    pendingReconsent?.expected_answers ||
    {};

  if (Array.isArray(explicitQuestions) && explicitQuestions.length > 0) {
    return explicitQuestions.map((q, index) => {
      const id = q?.id || `reconsent_q_${index + 1}`;
      const options =
        Array.isArray(q?.options) && q.options.length > 0
          ? q.options
          : ["Yes", "No"];
      const correctOption =
        expectedMap[id] ??
        q?.correct_option ??
        q?.expected_answer ??
        options[0];
      return {
        id,
        text: q?.text || q?.prompt || `Comprehension Check ${index + 1}`,
        options,
        correct_option: correctOption,
      };
    });
  }

  return DEFAULT_RECONSENT_COMPREHENSION_QUESTIONS.map((q) => ({
    ...q,
    options: [...q.options],
  }));
}

function hasExplicitReconsentComprehensionQuestions(pendingReconsent) {
  return Boolean(
    (Array.isArray(pendingReconsent?.comprehension_check?.questions) &&
      pendingReconsent.comprehension_check.questions.length > 0) ||
    (Array.isArray(pendingReconsent?.comprehension_questions) &&
      pendingReconsent.comprehension_questions.length > 0) ||
    (Array.isArray(pendingReconsent?.questions) &&
      pendingReconsent.questions.length > 0)
  );
}

function resolveReconsentStudyId() {
  return (
    state.pendingReconsent?.study_id ||
    state.session?.studyId ||
    (isDemoModeActive() ? DEMO_STUDY?.code || null : null) ||
    "STUDY-01"
  );
}

export function openReconsentModal() {
  if (typeof document !== "undefined" && !state.reconsentModalOpen) {
    state.reconsentPreviouslyFocusedElement = document.activeElement;
  }
  if (!state.pendingReconsent) {
    state.pendingReconsent = {
      id: "req_default",
      study_id: resolveReconsentStudyId(),
      protocol_version: "2.0",
      change_summary:
        "Protocol amendment updates requiring active subject re-consent.",
    };
  }
  state.reconsentModalError = "";
  state.reconsentForm.username = state.session.userId || "";
  state.reconsentForm.password = "";
  state.reconsentForm.reason = "Protocol Amendment Re-Consent Acknowledgment";
  state.reconsentForm.customReason = "";
  state.reconsentComprehensionAnswers = {};
  state.reconsentModalOpen = true;
  renderTasks();

  nextTick(() => {
    const pwdInput = document.getElementById("reconsent-sign-password");
    if (pwdInput) pwdInput.focus();
  });
}

export function closeReconsentModal() {
  state.reconsentForm.password = "";
  if (typeof document !== "undefined") {
    const pwdInput = document.getElementById("reconsent-sign-password");
    if (pwdInput) pwdInput.value = "";
  }
  state.reconsentModalOpen = false;
  state.reconsentModalError = "";
  // Keep state.pendingReconsent intact so survey starts and ePRO signing remain gated
  renderTasks();
  const prev = state.reconsentPreviouslyFocusedElement;
  if (prev && typeof prev.focus === "function" && !prev.closest?.("[inert]")) {
    prev.focus();
    state.reconsentPreviouslyFocusedElement = null;
  }
}

export async function checkPendingReconsent() {
  if (!state.session.userId) return;

  try {
    if (isAuthenticatedSession()) {
      const studyId = resolveReconsentStudyId();
      const pendingReqs = await dispatchApi(
        `api/v1/econsent/reconsent/pending/${encodeURIComponent(studyId)}?subject_pseudonym=${encodeURIComponent(state.session.userId)}`
      );
      if (Array.isArray(pendingReqs) && pendingReqs.length > 0) {
        state.pendingReconsent = pendingReqs[0];
        openReconsentModal();
        return;
      }
    }
  } catch (err) {
    console.warn(
      "Could not query pending re-consent requirements from server:",
      err
    );
  }

  // Fallback check: check if state.notifications contains a reconsent alert
  const reconsentNotif = state.notifications.find(
    (n) =>
      n.related_entity_type === "RECONSENT_REQUIRED" ||
      (n.message_content &&
        n.message_content.toLowerCase().includes("re-consent required")) ||
      (n.message && n.message.toLowerCase().includes("re-consent required"))
  );

  if (reconsentNotif) {
    state.pendingReconsent = {
      id: reconsentNotif.related_entity_id || "req_notif",
      study_id: reconsentNotif.study_id || resolveReconsentStudyId(),
      protocol_version: "2.0",
      change_summary:
        reconsentNotif.message_content ||
        reconsentNotif.message ||
        "Protocol amendment re-consent required.",
    };
    openReconsentModal();
  }
}

function wipeReconsentPasswordField() {
  state.reconsentForm.password = "";
  if (typeof document !== "undefined") {
    const pwdInput = document.getElementById("reconsent-sign-password");
    if (pwdInput) pwdInput.value = "";
  }
}

export async function submitReconsentSignature() {
  const cleanUsername = (state.reconsentForm.username || "").trim();
  const cleanPassword = state.reconsentForm.password || "";

  if (!cleanUsername) {
    wipeReconsentPasswordField();
    state.reconsentModalError = "Please enter your User ID / Username.";
    return;
  }
  if (!cleanPassword) {
    state.reconsentModalError = isAuthenticatedSession()
      ? "Please enter your Account Password."
      : "Please enter your Security PIN / Password.";
    return;
  }

  const customReasonTrimmed = (state.reconsentForm.customReason || "").trim();
  const reasonCheck = resolveSignatureReason(
    state.reconsentForm.reason,
    customReasonTrimmed
  );
  if (!reasonCheck.valid) {
    wipeReconsentPasswordField();
    state.reconsentModalError = reasonCheck.error;
    if (
      typeof document !== "undefined" &&
      requiresCustomSignatureReason(state.reconsentForm.reason)
    ) {
      const customEl = document.getElementById("reconsent-sign-reason-custom");
      if (customEl && typeof customEl.focus === "function") {
        customEl.focus();
      }
    }
    return;
  }

  // Enforce signer username match against active session identity
  const expectedUserId = String(state.session.userId || "").trim();
  if (
    (isAuthenticatedSession() && !expectedUserId) ||
    (expectedUserId &&
      cleanUsername.toLowerCase() !== expectedUserId.toLowerCase())
  ) {
    wipeReconsentPasswordField();
    state.reconsentModalError =
      "Signature Verification Rejected: User ID / Username must match the signed-in participant account.";
    if (typeof document !== "undefined") {
      const userEl = document.getElementById("reconsent-sign-username");
      if (userEl && typeof userEl.focus === "function") {
        userEl.focus();
      }
    }
    return;
  }

  // Validate comprehension checks when rendered in ReconsentModal or explicitly configured on pendingReconsent
  const comprehensionMounted =
    typeof document !== "undefined" &&
    Boolean(document.getElementById("reconsent-comprehension-section"));
  if (
    comprehensionMounted ||
    hasExplicitReconsentComprehensionQuestions(state.pendingReconsent)
  ) {
    const questions = resolveReconsentComprehensionQuestions(
      state.pendingReconsent
    );
    for (const q of questions) {
      const domChecked =
        typeof document !== "undefined"
          ? document.querySelector(`input[name="reconsent_${q.id}"]:checked`)
              ?.value
          : undefined;
      const recordedVal =
        state.reconsentComprehensionAnswers?.[q.id] ?? domChecked ?? "";
      if (!recordedVal) {
        wipeReconsentPasswordField();
        state.reconsentModalError =
          "Please answer all comprehension check questions to confirm you understand the updated consent terms before signing.";
        return;
      }
      if (q.correct_option && recordedVal !== q.correct_option) {
        wipeReconsentPasswordField();
        state.reconsentModalError =
          "Comprehension check not passed: please review the redlined consent clauses above and select the correct answers before signing.";
        return;
      }
    }
  }

  // Credential hygiene: clear password immediately before async network work
  wipeReconsentPasswordField();
  state.reconsentModalError = "";

  const studyId = resolveReconsentStudyId();
  const protocolVersion = state.pendingReconsent?.protocol_version || "2.0";
  const requirementId = state.pendingReconsent?.id;
  const declReason = requiresCustomSignatureReason(state.reconsentForm.reason)
    ? reasonCheck.reason
    : `${reasonCheck.reason}${customReasonTrimmed ? ": " + customReasonTrimmed : ""}`;

  try {
    if (isAuthenticatedSession()) {
      const reconsentActionPath = "/api/v1/execution/amendments/reconsent";
      const verificationBody = buildStepUpVerificationPayload({
        username: cleanUsername,
        password: cleanPassword,
        action: reconsentActionPath,
        semanticAction: SEMANTIC_ACTIONS.ECONSENT_RECONSENT_SIGN,
      });
      const tokenResponse = await dispatchApi(
        "api/v1/auth/signature-verification",
        {
          method: "POST",
          body: JSON.stringify(verificationBody),
        }
      );

      const sigToken = tokenResponse?.sig_token;
      if (!sigToken || typeof sigToken !== "string") {
        throw new Error(
          "Signature verification service is unavailable or did not issue a signature token. Nothing was saved."
        );
      }

      const persistRes = await dispatchApi(
        "api/v1/execution/amendments/reconsent",
        {
          method: "POST",
          body: JSON.stringify({
            subject_id: cleanUsername,
            study_id: studyId,
            protocol_version: protocolVersion,
            version_index: state.pendingReconsent?.new_version_index || 2,
            icf_signed: true,
            signature_type: "ECONSENT",
          }),
          headers: {
            "X-Sig-Token": sigToken,
          },
          change_reason: declReason,
        }
      );

      if (
        !persistRes ||
        typeof persistRes !== "object" ||
        persistRes.error ||
        persistRes.persisted === false
      ) {
        throw new Error(
          "Re-consent persistence service is unavailable. Nothing was saved."
        );
      }

      if (requirementId && !requirementId.startsWith("req_")) {
        await dispatchApi(
          `api/v1/econsent/reconsent/complete/${requirementId}`,
          {
            method: "POST",
            headers: {
              "X-Sig-Token": sigToken,
            },
            change_reason: declReason,
          }
        );
      }
    } else if (!allowUnbackedAction("Protocol amendment re-consent")) {
      state.reconsentModalError =
        "Protocol amendment re-consent cannot be completed while unauthenticated or when signature verification services are unavailable. Nothing was saved.";
      return;
    }

    // 21 CFR Part 11 Audit Log entry
    await logAuditRecord(
      "RECONSENT_EXECUTED",
      {
        subject_id: cleanUsername,
        study_id: studyId,
        protocol_version: protocolVersion,
        signature_type: "ECONSENT",
      },
      `Subject e-signature executed for Protocol Amendment Re-Consent (${declReason})`
    );

    state.pendingReconsent = null;
    state.consentSigned = true;
    state.reconsentModalOpen = false;
    state.reconsentModalError = "";
    renderTasks();
    const prev = state.reconsentPreviouslyFocusedElement;
    if (
      prev &&
      typeof prev.focus === "function" &&
      !prev.closest?.("[inert]")
    ) {
      prev.focus();
      state.reconsentPreviouslyFocusedElement = null;
    }

    showToast(
      "Re-Consent signed and submitted successfully. Submissions unlocked."
    );
  } catch (err) {
    state.reconsentModalError = `Re-Consent submission failed: ${err.message || err}`;
  }
}
