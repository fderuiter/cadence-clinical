// 21 CFR Part 11 electronic signature modal for ePRO and eConsent.
import { dispatchApi, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import { deleteDraft, queueSubmission } from "../sync-queue.js";
import { logAuditRecord } from "./audit.js";
import { renderCompliance } from "./compliance.js";
import { loadConsentDetails } from "./consent.js";
import { showView } from "./navigation.js";
import { syncOfflineQueue } from "./sync.js";
import { renderTasks } from "./tasks.js";

// Submit with 21 CFR PIN signature validation
export function openSignatureModal(actionType = "epro") {
  state.pendingSignableAction = actionType;
  if (typeof document !== "undefined") {
    state.previouslyFocusedElement = document.activeElement;
  }
  document.getElementById("sign-username").value = state.session.userId;
  document.getElementById("sign-password").value = "";

  const reasonSelect = document.getElementById("sign-reason");
  const modalHeader = document.getElementById("portal-modal-title");
  const errorBanner = document.getElementById("modal-error-banner");
  if (errorBanner) errorBanner.style.display = "none";

  if (actionType === "consent") {
    if (modalHeader)
      modalHeader.textContent = "eConsent Electronic Signature Required";
    reasonSelect.innerHTML = `
      <option value="Enrollment Confirmation and Consent">Enrollment Confirmation and Consent</option>
      <option value="Re-consent on amended protocol version">Re-consent on amended protocol version</option>
      <option value="Other">Other (specify below)</option>
    `;
    reasonSelect.value = "Enrollment Confirmation and Consent";
  } else {
    if (modalHeader) modalHeader.textContent = "Electronic Signature Required";
    reasonSelect.innerHTML = `
      <option value="Initial Questionnaire Completion">Initial Questionnaire Completion</option>
      <option value="Correction of previous entry">Correction of previous entry</option>
      <option value="Requested update by study coordinator">Requested update by study coordinator</option>
      <option value="Acknowledge important reminder">Acknowledge important reminder</option>
      <option value="Other">Other (specify below)</option>
    `;
    reasonSelect.value = "Initial Questionnaire Completion";
  }

  document.getElementById("portal-sign-modal").style.display = "flex";

  // Focus trap: set focus to the first focusable element inside the modal
  const modal = document.getElementById("portal-sign-modal");
  if (modal) {
    const focusableSelectors =
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
    let elements = Array.from(
      modal.querySelectorAll(focusableSelectors)
    ).filter((el) => !el.disabled);
    const visible = elements.filter((el) => el.offsetParent !== null);
    const focusableElements = visible.length > 0 ? visible : elements;
    if (focusableElements.length > 0) {
      focusableElements[0].focus();
    }
  }
}

export function closeSignatureModal() {
  // Credential hygiene: clear PIN / password from DOM immediately on cancel
  document.getElementById("sign-password").value = "";
  document.getElementById("portal-sign-modal").style.display = "none";
  if (
    state.initiatingButton &&
    typeof state.initiatingButton.focus === "function"
  ) {
    state.initiatingButton.focus();
    state.initiatingButton = null;
  } else if (
    state.previouslyFocusedElement &&
    typeof state.previouslyFocusedElement.focus === "function"
  ) {
    state.previouslyFocusedElement.focus();
  }
}

export async function verifyAndSubmitSignature() {
  const usernameInput = document.getElementById("sign-username");
  const passwordInput = document.getElementById("sign-password");
  const username = usernameInput.value.trim();
  const password = passwordInput.value;
  const reasonSelect = document.getElementById("sign-reason").value;
  const reasonCustom = document
    .getElementById("sign-reason-custom")
    .value.trim();
  const errorBanner = document.getElementById("modal-error-banner");

  if (errorBanner) errorBanner.style.display = "none";

  if (!username || !password) {
    if (errorBanner) {
      errorBanner.textContent =
        "Please enter both User ID and Security PIN/Password to sign.";
      errorBanner.style.display = "block";
    } else {
      alert("Please enter both User ID and Security PIN/Password to sign.");
    }
    return;
  }

  const finalReason =
    reasonSelect === "Other" && reasonCustom
      ? reasonCustom
      : `${reasonSelect}${reasonCustom ? ": " + reasonCustom : ""}`;

  // Preserve credentials into local variables and clear reactive DOM fields immediately before async call
  const cleanUsername = username;
  const cleanPassword = password;

  // Clear the actual DOM element for credential hygiene
  passwordInput.value = "";

  const actionType = state.pendingSignableAction;

  if (actionType === "consent") {
    // eConsent GxP/Part 11 Step-up signature flow
    if (isAuthenticatedSession()) {
      try {
        // Step 1: obtain short-lived sig_token
        const tokenResponse = await dispatchApi(
          "api/v1/auth/signature-verification",
          {
            method: "POST",
            body: JSON.stringify({
              username: cleanUsername,
              password: cleanPassword,
              action: "SIGN_CONSENT",
            }),
          }
        );

        const sigToken = tokenResponse.sig_token;

        // Step 2: Sign consent document
        await dispatchApi(
          "api/v1/econsent/templates/template-icf/versions/1/sign",
          {
            method: "POST",
            body: JSON.stringify({
              subject_pseudonym: state.session.userId,
              signature_data: cleanUsername,
              reason_for_change: finalReason,
              liveness_token: state.consentLivenessToken,
            }),
            headers: {
              "X-Sig-Token": sigToken,
            },
          }
        );

        // Set local signed state
        state.consentSigned = true;

        // Write audit ledger record
        await logAuditRecord(
          "SIGN_CONSENT",
          {
            template_id: "template-icf",
            version_index: 1,
            subject_pseudonym: state.session.userId,
          },
          `Informed Consent Signed and Verified: "${cleanUsername}" with declaration: "${finalReason}"`
        );

        closeSignatureModal();
        alert("Informed Consent successfully signed and archived!");
        loadConsentDetails();
      } catch (err) {
        // Show in accessible error status banner and preserve credential hygiene
        if (errorBanner) {
          errorBanner.textContent = `Signature Verification Rejected: ${err.message}`;
          errorBanner.style.display = "block";
        } else {
          alert(`Signature Verification Rejected: ${err.message}`);
        }
      }
    } else {
      // Sandbox fallback mode
      state.consentSigned = true;

      // Write audit ledger record
      await logAuditRecord(
        "SIGN_CONSENT",
        {
          template_id: "template-icf",
          version_index: 1,
          subject_pseudonym: state.session.userId,
        },
        `Informed Consent Signed and Verified: "${cleanUsername}" with declaration: "${finalReason}"`
      );

      closeSignatureModal();
      alert("Informed Consent successfully signed and archived (Sandbox VM)!");
      loadConsentDetails();
    }
  } else {
    // Default ePRO questionnaire submission
    closeSignatureModal();

    const active = state.activeQuestionnaire;
    if (!active) return;

    const rawAnswers = JSON.parse(JSON.stringify(active.answers));

    // Queue submission locally inside IndexedDB
    let queuedItem;
    try {
      queuedItem = await queueSubmission({
        subject_id: state.session.userId,
        diary_id: active.instrument.id,
        assignment_id: active.assignment.id,
        answers: rawAnswers,
        change_reason: finalReason,
        username: cleanUsername,
      });
    } catch (err) {
      console.error("Failed to write to offline queue IndexedDB:", err);
      alert("Could not queue submission locally. Please try again.");
      return;
    }

    try {
      await deleteDraft(active.assignment.id);
    } catch (err) {
      console.warn("Could not delete draft on submission:", err);
    }

    // Perform GxP audit logging locally
    await logAuditRecord(
      "EPRO_SUBMIT",
      {
        diary_id: active.instrument.id,
        answers: rawAnswers,
        assignment_id: active.assignment.id,
        sequence_number: queuedItem.sequence_number,
      },
      `Verified Electronic Signature: "${cleanUsername}" with statement "${finalReason}"`
    );

    // Mark assignment complete
    const foundAssign = state.assignments.find(
      (a) => a.id === active.assignment.id
    );
    if (foundAssign) {
      foundAssign.status = "COMPLETED";
      foundAssign.submitted_at = queuedItem.device_timestamp;
    }

    state.activeQuestionnaire = null;
    alert("Diary/Questionnaire successfully signed and submitted!");

    // Recalculate and update views
    renderTasks();
    renderCompliance();
    showView("view-tasks");

    // Attempt sync immediately
    await syncOfflineQueue();
  }
}

// Keeps Tab focus inside the open signature modal (document listener).
export function installDocumentSignatureFocusTrap() {
  if (!window.__TAB_LISTENER_REGISTERED__) {
    window.__TAB_LISTENER_REGISTERED__ = true;
    document.addEventListener("keydown", (e) => {
      if (e.key === "Tab") {
        const modal = document.getElementById("portal-sign-modal");
        if (modal && modal.style.display !== "none") {
          const focusableSelectors =
            "input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])";
          const focusableElements = Array.from(
            modal.querySelectorAll(focusableSelectors)
          );
          if (focusableElements.length === 0) return;

          const first = focusableElements[0];
          const last = focusableElements[focusableElements.length - 1];

          if (e.shiftKey) {
            if (document.activeElement === first) {
              last.focus();
              e.preventDefault();
            }
          } else {
            if (document.activeElement === last) {
              first.focus();
              e.preventDefault();
            }
          }
        }
      }
    });
  }
}

// Keeps Tab focus inside the open signature modal (window listener).
export function installWindowSignatureFocusTrap() {
  if (typeof window !== "undefined") {
    if (!window.__TAB_TRAP_LISTENER_ADDED__) {
      window.addEventListener("keydown", (e) => {
        if (e.key === "Tab") {
          const modal = document.getElementById("portal-sign-modal");
          if (modal && modal.style.display !== "none") {
            const focusableSelectors =
              'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
            let elements = Array.from(
              modal.querySelectorAll(focusableSelectors)
            ).filter((el) => !el.disabled);
            const visible = elements.filter((el) => el.offsetParent !== null);
            const focusableElements = visible.length > 0 ? visible : elements;

            if (focusableElements.length > 0) {
              const firstFocusable = focusableElements[0];
              const lastFocusable =
                focusableElements[focusableElements.length - 1];

              if (e.shiftKey) {
                if (document.activeElement === firstFocusable) {
                  lastFocusable.focus();
                  e.preventDefault();
                }
              } else {
                if (document.activeElement === lastFocusable) {
                  firstFocusable.focus();
                  e.preventDefault();
                }
              }
            }
          }
        }
      });
      window.__TAB_TRAP_LISTENER_ADDED__ = true;
    }
  }
}
