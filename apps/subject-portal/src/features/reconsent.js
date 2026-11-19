// Protocol amendment re-consent: the pending requirement and its modal.
import { nextTick } from "vue";

import { dispatchApi, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import { showToast } from "../widgets/toast.js";
import { logAuditRecord } from "./audit.js";

export function openReconsentModal() {
  if (!state.pendingReconsent) {
    state.pendingReconsent = {
      id: "req_default",
      study_id: "STUDY-01",
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
  state.reconsentModalOpen = true;

  nextTick(() => {
    const pwdInput = document.getElementById("reconsent-sign-password");
    if (pwdInput) pwdInput.focus();
  });
}

export function closeReconsentModal() {
  state.reconsentForm.password = "";
  state.reconsentModalOpen = false;
  state.reconsentModalError = "";
}

export async function checkPendingReconsent() {
  if (!state.session.userId) return;

  try {
    if (isAuthenticatedSession()) {
      const pendingReqs = await dispatchApi(
        `api/v1/econsent/reconsent/pending/STUDY-01?subject_pseudonym=${state.session.userId}`
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
      study_id: "STUDY-01",
      protocol_version: "2.0",
      change_summary:
        reconsentNotif.message_content ||
        reconsentNotif.message ||
        "Protocol amendment re-consent required.",
    };
    openReconsentModal();
  }
}

export async function submitReconsentSignature() {
  const cleanUsername = (state.reconsentForm.username || "").trim();
  const cleanPassword = state.reconsentForm.password || "";

  if (!cleanUsername) {
    state.reconsentModalError = "Please enter your User ID / Username.";
    return;
  }
  if (!cleanPassword) {
    state.reconsentModalError = "Please enter your Security PIN / Password.";
    return;
  }

  // Credential hygiene: clear password immediately before async network work
  state.reconsentForm.password = "";
  state.reconsentModalError = "";

  const studyId = state.pendingReconsent?.study_id || "STUDY-01";
  const protocolVersion = state.pendingReconsent?.protocol_version || "2.0";
  const requirementId = state.pendingReconsent?.id;
  const declReason =
    state.reconsentForm.reason === "Other" && state.reconsentForm.customReason
      ? state.reconsentForm.customReason
      : state.reconsentForm.reason;

  try {
    if (isAuthenticatedSession()) {
      const reconsentActionPath = "/api/v1/execution/amendments/reconsent";
      const tokenResponse = await dispatchApi(
        "api/v1/auth/signature-verification",
        {
          method: "POST",
          body: JSON.stringify({
            username: cleanUsername,
            password: cleanPassword,
            action: reconsentActionPath,
            semantic_action: "econsent.reconsent.sign",
          }),
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

    showToast(
      "Re-Consent signed and submitted successfully. Submissions unlocked."
    );
  } catch (err) {
    state.reconsentModalError = `Re-Consent submission failed: ${err.message || err}`;
  }
}
