// 5. My Consent: approved content, comprehension check and consent signing.
import { watch } from "vue";
import {
  handoffFocus,
  interpretComprehensionResult,
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  validateField,
} from "ui";

import { dispatchApi, isAuthenticatedSession } from "../api/client.js";
import {
  MOCK_APPROVED_CONTENT,
  MOCK_COMPREHENSION_CHECK,
} from "../data/mock-data.js";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import {
  formatConsentEditionLabel,
  formatConsentLanguageLabel,
  resolveFriendlyStudyTitle,
} from "../utils/participantCopy.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import { createClinicalRadioGrid } from "../widgets/clinical-fields.js";
import {
  clearFieldInvalid,
  markFieldInvalid,
} from "../widgets/field-errors.js";
import { showToast } from "../widgets/toast.js";

// 5. My Consent (eConsent ICF Review, Comprehension, and Signature)

const SUPPORTED_CONSENT_LANGUAGES = new Set(["en", "es", "nl", "fr"]);

export function resolveConsentLanguage(languageCode) {
  const normalized = String(languageCode || "en")
    .trim()
    .toLowerCase();
  return SUPPORTED_CONSENT_LANGUAGES.has(normalized) ? normalized : "en";
}

function applyConsentLanguageAttributes(languageCode) {
  if (typeof document === "undefined") return;
  const resolvedLang = resolveConsentLanguage(languageCode);
  const titleEl = document.getElementById("consent-template-title");
  if (titleEl) {
    titleEl.setAttribute("lang", resolvedLang);
  }
  const clausesEl = document.getElementById("consent-clauses-container");
  if (clausesEl) {
    clausesEl.setAttribute("lang", resolvedLang);
  }
}

export const LIVENESS_EXPIRED_MESSAGE =
  "Identity check expired after 15 minutes. Please verify your identity again with the camera check or study coordinator before signing.";

let livenessExpiryTimer = null;
let livenessWatcherStop = null;

function clearLivenessExpiryTimer() {
  if (livenessExpiryTimer) {
    clearTimeout(livenessExpiryTimer);
    livenessExpiryTimer = null;
  }
}

export function isConsentLivenessExpired(nowMs = Date.now()) {
  if (!state.consentLivenessExpiresAt) {
    return false;
  }
  const parsedMs = Date.parse(state.consentLivenessExpiresAt);
  if (Number.isNaN(parsedMs)) {
    return true;
  }
  return parsedMs <= nowMs;
}

export function expireConsentLiveness(message = LIVENESS_EXPIRED_MESSAGE) {
  clearLivenessExpiryTimer();
  state.livenessVerified = false;
  state.consentLivenessToken = null;

  if (typeof document !== "undefined") {
    const banner = document.getElementById("liveness-status-banner");
    if (banner) {
      banner.setAttribute("role", "alert");
      banner.setAttribute("aria-live", "assertive");
      banner.textContent = message;
      banner.style.display = "block";
      banner.style.backgroundColor = "var(--semantic-color-error-bg-soft)";
      banner.style.color = "var(--semantic-color-error-text-dark)";
      banner.style.border = "1px solid var(--semantic-color-error)";
    }
    const btnSign = document.getElementById("btn-trigger-consent-sign");
    if (btnSign) {
      btnSign.disabled = true;
    }
    updateConsentReadinessGuidance();
  }
}

export function ensureConsentLivenessFresh(nowMs = Date.now()) {
  if (isConsentLivenessExpired(nowMs)) {
    expireConsentLiveness();
    return false;
  }
  return Boolean(state.livenessVerified && state.consentLivenessToken);
}

function scheduleLivenessExpiryCheck(expiresAtIso) {
  clearLivenessExpiryTimer();
  if (!expiresAtIso) return;
  const expiresMs = Date.parse(expiresAtIso);
  if (Number.isNaN(expiresMs)) {
    expireConsentLiveness();
    return;
  }
  const delayMs = expiresMs - Date.now();
  if (delayMs <= 0) {
    expireConsentLiveness();
    return;
  }
  livenessExpiryTimer = setTimeout(() => {
    livenessExpiryTimer = null;
    if (isConsentLivenessExpired()) {
      expireConsentLiveness();
    }
  }, delayMs);
  if (livenessExpiryTimer && typeof livenessExpiryTimer.unref === "function") {
    livenessExpiryTimer.unref();
  }
}

function ensureLivenessExpiryWatcher() {
  if (livenessWatcherStop) return;
  livenessWatcherStop = watch(
    () => state.consentLivenessExpiresAt,
    (nextExpiresAt) => {
      if (!nextExpiresAt) {
        clearLivenessExpiryTimer();
        return;
      }
      if (isConsentLivenessExpired()) {
        expireConsentLiveness();
      } else {
        scheduleLivenessExpiryCheck(nextExpiresAt);
      }
    }
  );
}
export async function loadConsentDetails() {
  const loadingEl = document.getElementById("consent-loading");
  const failureEl = document.getElementById("consent-failure");
  const contentEl = document.getElementById("consent-content-wrapper");
  const errorMsgEl = document.getElementById("consent-error-msg");
  const langSelector = document.getElementById("consent-lang-selector");

  if (loadingEl) loadingEl.style.display = "block";
  if (failureEl) failureEl.style.display = "none";
  if (contentEl) contentEl.style.display = "none";

  state.consentLanguage = resolveConsentLanguage(
    langSelector && langSelector.value
      ? langSelector.value
      : state.consentLanguage || "en"
  );
  applyConsentLanguageAttributes(state.consentLanguage);

  try {
    if (isAuthenticatedSession()) {
      // Fetch dynamic approved composed content from secure microservice
      state.consentContent = await dispatchApi(
        `api/v1/econsent/templates/template-icf/approved-content?language_code=${state.consentLanguage}`
      );
      state.consentCheck = await dispatchApi(
        `api/v1/econsent/templates/template-icf/versions/1/comprehension-checks`
      );
    } else if (isDemoModeActive()) {
      // Explicit demo mode fallback using language matched synthetic structures
      const localApproved = JSON.parse(JSON.stringify(MOCK_APPROVED_CONTENT));
      localApproved.language_code = state.consentLanguage;
      if (state.consentLanguage === "es") {
        localApproved.template_name =
          "Formulario de Consentimiento Informado Principal";
        localApproved.clauses = [
          {
            clause_id: "clause-risk",
            title: "Riesgos y Efectos Secundarios",
            text: "Existe el riesgo de dolor de cabeza leve y temporal.",
            version_index: 1,
          },
          {
            clause_id: "clause-benefit",
            title: "Beneficios Esperados",
            text: "Recibirá monitoreo médico experto y una posible mejoría de los síntomas.",
            version_index: 1,
          },
        ];
      } else if (state.consentLanguage === "nl") {
        localApproved.template_name =
          "Hoofdformulier voor Geïnformeerde Toestemming";
        localApproved.clauses = [
          {
            clause_id: "clause-risk",
            title: "Risico's en Bijwerkingen",
            text: "Er bestaat een risico op lichte en tijdelijke hoofdpijn.",
            version_index: 1,
          },
          {
            clause_id: "clause-benefit",
            title: "Verwachte Voordelen",
            text: "U ontvangt deskundige medische controle en mogelijke verlichting van symptomen.",
            version_index: 1,
          },
        ];
      } else if (state.consentLanguage === "fr") {
        localApproved.template_name =
          "Formulaire Principal de Consentement Éclairé";
        localApproved.clauses = [
          {
            clause_id: "clause-risk",
            title: "Risques et Effets Secondaires",
            text: "Il existe un risque de maux de tête légers et temporaires.",
            version_index: 1,
          },
          {
            clause_id: "clause-benefit",
            title: "Bénéfices Attendus",
            text: "Vous bénéficierez d'un suivi médical spécialisé et d'une amélioration potentielle des symptômes.",
            version_index: 1,
          },
        ];
      }
      state.consentContent = localApproved;
      state.consentCheck = JSON.parse(JSON.stringify(MOCK_COMPREHENSION_CHECK));
    } else {
      state.consentContent = null;
      state.consentCheck = null;
      throw new Error(
        "Please sign in with an active study connection to access your electronic consent documents."
      );
    }

    renderConsentUI();
  } catch (err) {
    if (loadingEl) loadingEl.style.display = "none";
    if (failureEl) {
      failureEl.style.display = "block";
      if (errorMsgEl) errorMsgEl.textContent = err.message || err;
    }
  }
}

export function renderConsentUI() {
  const loadingEl = document.getElementById("consent-loading");
  const contentEl = document.getElementById("consent-content-wrapper");
  const statusBadge = document.getElementById("consent-status-badge");

  if (loadingEl) loadingEl.style.display = "none";
  if (contentEl) contentEl.style.display = "block";

  const tpl = state.consentContent;
  const check = state.consentCheck;

  if (!tpl) return;

  // Render Status Badge
  if (statusBadge) {
    if (state.consentSigned) {
      statusBadge.textContent = "Signed & Archived";
      statusBadge.className = "status-pill completed";
    } else if (state.consentPassed) {
      statusBadge.textContent = "Check Passed";
      statusBadge.className = "status-pill pending";
    } else {
      statusBadge.textContent = "Pending Check";
      statusBadge.className = "status-pill overdue";
    }
  }

  // 1. Render Metadata and Clauses (Informed Consent document)
  const resolvedLang = resolveConsentLanguage(
    tpl.language_code || state.consentLanguage
  );
  const titleEl = document.getElementById("consent-template-title");
  if (titleEl) {
    titleEl.setAttribute("lang", resolvedLang);
    titleEl.textContent = tpl.template_name;
  }

  const metaDisplay = document.getElementById("consent-metadata-display");
  const friendlyStudy = resolveFriendlyStudyTitle(
    tpl.study_id || state.session?.studyId
  );
  const friendlyEdition = formatConsentEditionLabel(
    tpl.document_version ?? tpl.protocol_version ?? tpl.version_index
  );
  const friendlyLanguage = formatConsentLanguageLabel(tpl.language_code);
  metaDisplay.innerHTML = `
    <span><strong>Study:</strong> ${friendlyStudy}</span> |
    <span><strong>Document Version:</strong> ${friendlyEdition}</span> |
    <span><strong>Language:</strong> ${friendlyLanguage}</span>
  `;

  // Parse and order display sections using ui normalizer
  const sections = normalizeApprovedConsent(tpl);
  const clausesContainer = document.getElementById("consent-clauses-container");
  clausesContainer.setAttribute("lang", resolvedLang);
  clausesContainer.innerHTML = "";

  const clauseSections = sections.filter((s) => s.type === "clause");
  clausesContainer.innerHTML = clauseSections
    .map(
      (sec) => `
    <div class="clause-view-box" style="border: 1px solid var(--semantic-color-border); padding: 16px; border-radius: 6px; background: var(--semantic-color-surface);">
      <h4 style="margin-top: 0; color: var(--semantic-color-primary); border-bottom: 1px dashed var(--semantic-color-border); padding-bottom: 6px;">${sec.title}</h4>
      <p style="margin: 0; font-size: 14px;">${sec.content}</p>
    </div>
  `
    )
    .join("");

  // 2. Render Comprehension checks (Step 1)
  const qContainer = document.getElementById("consent-questions-container");
  qContainer.innerHTML = "";

  const signedCard = document.getElementById("consent-signed-card");

  if (state.consentSigned) {
    const compCard = document.getElementById("consent-comprehension-card");
    const livenessCard = document.getElementById("consent-liveness-card");
    const signCard = document.getElementById("consent-signature-card");
    const activeInsideHiddenStep =
      typeof document !== "undefined" &&
      Boolean(
        compCard?.contains(document.activeElement) ||
        livenessCard?.contains(document.activeElement) ||
        signCard?.contains(document.activeElement) ||
        document.activeElement === document.body
      );
    if (compCard) compCard.style.display = "none";
    if (livenessCard) livenessCard.style.display = "none";
    if (signCard) signCard.style.display = "none";
    if (signedCard) signedCard.style.display = "block";
    if (activeInsideHiddenStep && state.currentView === "view-consent") {
      void handoffFocus([
        "#consent-signed-heading",
        "#consent-view-heading",
        "#main-content",
      ]);
    }
    return;
  } else {
    if (signedCard) signedCard.style.display = "none";
    document.getElementById("consent-comprehension-card").style.display =
      "block";
    const livenessCard = document.getElementById("consent-liveness-card");
    if (livenessCard) livenessCard.style.display = "block";
    document.getElementById("consent-signature-card").style.display = "block";
  }

  if (check && check.questions && check.questions.length > 0) {
    let formHtml = `<div class="clinical-form-grid" style="gap: 16px;">`;
    check.questions.forEach((q) => {
      formHtml += createClinicalRadioGrid(
        q.id,
        q.text,
        q.options || ["Yes", "No"],
        "",
        null,
        12
      );
    });
    formHtml += `</div>`;
    qContainer.innerHTML = formHtml;
  } else {
    qContainer.innerHTML = `<div class="loading-state">No comprehension questions configured for this template.</div>`;
  }

  // Update signing button state - requires BOTH comprehension check pass AND non-expired liveness verification
  ensureLivenessExpiryWatcher();
  if (isConsentLivenessExpired()) {
    expireConsentLiveness();
  }
  const btnSign = document.getElementById("btn-trigger-consent-sign");
  if (btnSign) {
    btnSign.disabled = !(
      state.consentPassed &&
      state.livenessVerified &&
      !isConsentLivenessExpired()
    );
  }
  updateConsentReadinessGuidance();
}

export function updateConsentReadinessGuidance() {
  const hintEl = document.getElementById("consent-sign-readiness-hint");
  if (!hintEl) return;

  if (state.consentSigned) {
    hintEl.textContent = "Your signed consent form is saved.";
    return;
  }

  if (state.consentPassed && state.livenessVerified) {
    hintEl.textContent =
      "All checks are complete. You can now sign your consent form.";
    return;
  }

  if (!state.consentPassed && !state.livenessVerified) {
    hintEl.innerHTML = `Before you can sign, please finish both quick checks above: <a href="#consent-comprehension-card" id="link-jump-comprehension" class="consent-step-link">Step 1: Quick Understanding Check</a> and <a href="#consent-liveness-card" id="link-jump-liveness" class="consent-step-link">Step 2: Quick Camera Check</a>.`;
  } else if (!state.consentPassed) {
    hintEl.innerHTML = `Almost ready to sign! Please finish <a href="#consent-comprehension-card" id="link-jump-comprehension" class="consent-step-link">Step 1: Quick Understanding Check</a>.`;
  } else {
    hintEl.innerHTML = `Almost ready to sign! Please finish <a href="#consent-liveness-card" id="link-jump-liveness" class="consent-step-link">Step 2: Quick Camera Check</a>.`;
  }

  const bindStepJump = (linkId, targetId) => {
    const linkEl = document.getElementById(linkId);
    if (!linkEl) return;
    linkEl.addEventListener("click", (event) => {
      event.preventDefault();
      const targetEl = document.getElementById(targetId);
      if (targetEl) {
        targetEl.scrollIntoView?.({ behavior: "smooth", block: "start" });
        targetEl.focus?.();
      }
    });
  };

  bindStepJump("link-jump-comprehension", "consent-comprehension-card");
  bindStepJump("link-jump-liveness", "consent-liveness-card");
}

export async function verifyBiometricLiveness(isFallback = false) {
  ensureLivenessExpiryWatcher();
  clearLivenessExpiryTimer();
  const banner = document.getElementById("liveness-status-banner");
  if (banner) banner.style.display = "none";

  const frames = isFallback
    ? []
    : ["simulated_frame_base64_1", "simulated_frame_base64_2"];
  const payload = {
    subject_pseudonym: state.session.userId,
    video_frames: frames,
    fallback_mode: isFallback,
  };

  try {
    let response;
    if (isAuthenticatedSession()) {
      response = await dispatchApi("api/v1/ai/biometrics/verify-liveness", {
        method: "POST",
        body: JSON.stringify(payload),
      });
    } else if (allowUnbackedAction("Biometric liveness verification")) {
      // Explicit demo mode fallback
      const token = `mock_liveness_token_${state.session.userId}_${isFallback ? "fallback" : "camera"}_${Date.now()}`;
      response = {
        liveness_token: token,
        is_live: true,
        anti_spoof_score: isFallback ? 0.92 : 0.98,
        expires_at: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
        status: "VERIFIED",
      };
    } else {
      throw new Error(
        "Biometric liveness verification service is unavailable while unauthenticated or offline."
      );
    }

    if (response && response.is_live && response.liveness_token) {
      const resolvedExpiresAt =
        response.expires_at ||
        new Date(Date.now() + 15 * 60 * 1000).toISOString();
      state.consentLivenessToken = response.liveness_token;
      state.consentLivenessExpiresAt = resolvedExpiresAt;
      state.livenessVerified = !isConsentLivenessExpired();
      scheduleLivenessExpiryCheck(resolvedExpiresAt);

      if (state.livenessVerified && banner) {
        banner.setAttribute("role", "status");
        banner.setAttribute("aria-live", "polite");
        banner.textContent = isFallback
          ? "Identity confirmed with your study coordinator (valid for 15 minutes)."
          : "Camera check complete — identity confirmed for the next 15 minutes.";
        banner.style.display = "block";
        banner.style.backgroundColor = "var(--semantic-color-success-bg-soft)";
        banner.style.color = "var(--semantic-color-success-dark)";
        banner.style.border = "1px solid var(--semantic-color-success)";
      }
    } else {
      state.livenessVerified = false;
      state.consentLivenessToken = null;
      state.consentLivenessExpiresAt = null;
      if (banner) {
        banner.setAttribute("role", "alert");
        banner.setAttribute("aria-live", "assertive");
        banner.textContent =
          "We could not complete the camera check. Please try again or verify with your study coordinator instead.";
        banner.style.display = "block";
        banner.style.backgroundColor = "var(--semantic-color-error-bg-soft)";
        banner.style.color = "var(--semantic-color-error-text-dark)";
        banner.style.border = "1px solid var(--semantic-color-error)";
      }
    }

    const btnSign =
      typeof document !== "undefined"
        ? document.getElementById("btn-trigger-consent-sign")
        : null;
    if (btnSign) {
      btnSign.disabled = !(
        state.consentPassed &&
        state.livenessVerified &&
        !isConsentLivenessExpired()
      );
    }

    renderConsentUI();
  } catch (err) {
    state.livenessVerified = false;
    state.consentLivenessToken = null;
    state.consentLivenessExpiresAt = null;
    if (banner) {
      banner.setAttribute("role", "alert");
      banner.setAttribute("aria-live", "assertive");
      banner.textContent = "Camera check unavailable: " + (err.message || err);
      banner.style.display = "block";
      banner.style.backgroundColor = "var(--semantic-color-error-bg-soft)";
      banner.style.color = "var(--semantic-color-error-text-dark)";
      banner.style.border = "1px solid var(--semantic-color-error)";
    }
    renderConsentUI();
  }
}

export async function submitConsentAnswers() {
  const check = state.consentCheck;
  if (!check) return;

  const banner = document.getElementById("comprehension-status-banner");
  if (banner) banner.style.display = "none";

  let allValid = true;
  const answers = {};

  check.questions.forEach((q) => {
    const val =
      document.querySelector(`input[name="${q.id}"]:checked`)?.value || "";

    // Clear previous errors
    clearFieldInvalid(q.id);

    const fieldMeta = {
      id: q.id,
      label: q.text,
      validation: { required: true },
    };

    const res = validateField(fieldMeta, val);
    if (!res.valid) {
      allValid = false;
      markFieldInvalid(q.id, res.message);
    }
    answers[q.id] = val;
  });

  if (!allValid) {
    const incompleteMsg =
      "Please answer all comprehension questions before submitting.";
    if (banner) {
      banner.setAttribute("role", "alert");
      banner.setAttribute("aria-live", "assertive");
      banner.textContent = incompleteMsg;
      banner.style.display = "block";
      banner.style.backgroundColor = "var(--semantic-color-error-bg-soft)";
      banner.style.color = "var(--semantic-color-error-text-dark)";
      banner.style.border = "1px solid var(--semantic-color-error)";
    }
    showToast(incompleteMsg, { variant: "error" });
    return;
  }

  // Shape submission payload using ui helper
  const payload = shapeComprehensionAnswers(
    state.session.userId,
    answers,
    "Comprehension verification check submission"
  );

  try {
    let resultResponse;

    if (isAuthenticatedSession()) {
      resultResponse = await dispatchApi(
        "api/v1/econsent/templates/template-icf/versions/1/submit-answers",
        {
          method: "POST",
          body: JSON.stringify(payload),
        }
      );
    } else if (allowUnbackedAction("Consent comprehension verification")) {
      // Explicit demo mode local evaluation
      let correct = 0;
      Object.entries(check.expected_answers).forEach(([qid, expected]) => {
        if (answers[qid] === expected) {
          correct += 1;
        }
      });
      const minRequired = check.threshold_policy.min_correct;
      const passed = correct >= minRequired;
      resultResponse = {
        passed,
        score: (correct / check.questions.length) * 100,
        next_step: passed ? "sign_consent" : "retry_checks",
        message: passed
          ? "Congratulations! You have passed the comprehension check and can proceed to sign the consent form."
          : `You got ${correct} out of ${check.questions.length} questions correct. The passing threshold is ${minRequired}. Please review the material and try again.`,
      };
    } else {
      throw new Error(
        "Consent comprehension verification service is unavailable while unauthenticated or offline."
      );
    }

    // Interpret using ui helper
    const decision = interpretComprehensionResult(resultResponse);
    state.consentPassed = decision.canSign;

    // Display banner
    if (banner) {
      banner.textContent = decision.message;
      banner.style.display = "block";
      if (decision.canSign) {
        banner.setAttribute("role", "status");
        banner.setAttribute("aria-live", "polite");
        banner.style.backgroundColor = "var(--semantic-color-success-bg-soft)";
        banner.style.color = "var(--semantic-color-success-dark)";
        banner.style.border = "1px solid var(--semantic-color-success)";
      } else {
        banner.setAttribute("role", "alert");
        banner.setAttribute("aria-live", "assertive");
        banner.style.backgroundColor = "var(--semantic-color-error-bg-soft)";
        banner.style.color = "var(--semantic-color-error-text-dark)";
        banner.style.border = "1px solid var(--semantic-color-error)";
      }
    }

    // Refresh UI triggers
    renderConsentUI();
  } catch (err) {
    state.consentPassed = false;
    const msg = "Answers submission failed: " + (err.message || err);
    if (banner) {
      banner.setAttribute("role", "alert");
      banner.setAttribute("aria-live", "assertive");
      banner.textContent = msg;
      banner.style.display = "block";
      banner.style.backgroundColor = "var(--semantic-color-error-bg-soft)";
      banner.style.color = "var(--semantic-color-error-text-dark)";
      banner.style.border = "1px solid var(--semantic-color-error)";
    }
    showToast(msg, { variant: "error" });
    renderConsentUI();
  }
}
