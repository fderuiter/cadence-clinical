// 5. My Consent: approved content, comprehension check and consent signing.
import {
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
import { createClinicalRadioGrid } from "../widgets/clinical-fields.js";
import {
  clearFieldInvalid,
  markFieldInvalid,
} from "../widgets/field-errors.js";

// 5. My Consent (eConsent ICF Review, Comprehension, and Signature)

export async function loadConsentDetails() {
  const loadingEl = document.getElementById("consent-loading");
  const failureEl = document.getElementById("consent-failure");
  const contentEl = document.getElementById("consent-content-wrapper");
  const errorMsgEl = document.getElementById("consent-error-msg");
  const langSelector = document.getElementById("consent-lang-selector");

  if (loadingEl) loadingEl.style.display = "block";
  if (failureEl) failureEl.style.display = "none";
  if (contentEl) contentEl.style.display = "none";

  state.consentLanguage = langSelector ? langSelector.value : "en";

  try {
    if (isAuthenticatedSession()) {
      // Fetch dynamic approved composed content from secure microservice
      state.consentContent = await dispatchApi(
        `api/v1/econsent/templates/template-icf/approved-content?language_code=${state.consentLanguage}`
      );
      state.consentCheck = await dispatchApi(
        `api/v1/econsent/templates/template-icf/versions/1/comprehension-checks`
      );
    } else {
      // Offline fallback using language matched mock structures
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
      }
      state.consentContent = localApproved;
      state.consentCheck = JSON.parse(JSON.stringify(MOCK_COMPREHENSION_CHECK));
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
  document.getElementById("consent-template-title").textContent =
    tpl.template_name;

  const metaDisplay = document.getElementById("consent-metadata-display");
  metaDisplay.innerHTML = `
    <span><strong>Study ID:</strong> ${tpl.study_id}</span> |
    <span><strong>Protocol Version:</strong> ${tpl.protocol_version}</span> |
    <span><strong>Version Index:</strong> ${tpl.version_index}</span> |
    <span><strong>Language:</strong> ${tpl.language_code.toUpperCase()}</span>
  `;

  // Parse and order display sections using ui normalizer
  const sections = normalizeApprovedConsent(tpl);
  const clausesContainer = document.getElementById("consent-clauses-container");
  clausesContainer.innerHTML = "";

  const clauseSections = sections.filter((s) => s.type === "clause");
  clausesContainer.innerHTML = clauseSections
    .map(
      (sec) => `
    <div class="clause-view-box" style="border: 1px solid var(--border-color); padding: 16px; border-radius: 6px; background: rgba(255,255,255,0.01);">
      <h4 style="margin-top: 0; color: var(--primary-color); border-bottom: 1px dashed var(--border-color); padding-bottom: 6px;">${sec.title}</h4>
      <p style="margin: 0; font-size: 14px;">${sec.content}</p>
    </div>
  `
    )
    .join("");

  // 2. Render Comprehension checks (Step 1)
  const qContainer = document.getElementById("consent-questions-container");
  qContainer.innerHTML = "";

  if (state.consentSigned) {
    document.getElementById("consent-comprehension-card").style.display =
      "none";
    const livenessCard = document.getElementById("consent-liveness-card");
    if (livenessCard) livenessCard.style.display = "none";
    document.getElementById("consent-signature-card").style.display = "none";
    return;
  } else {
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

  // Update signing button state - requires BOTH comprehension check pass AND liveness verification
  const btnSign = document.getElementById("btn-trigger-consent-sign");
  if (btnSign) {
    btnSign.disabled = !(state.consentPassed && state.livenessVerified);
  }
}

export async function verifyBiometricLiveness(isFallback = false) {
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
    } else {
      // Sandbox / local fallback
      const token = `mock_liveness_token_${state.session.userId}_${isFallback ? "fallback" : "camera"}_${Date.now()}`;
      response = {
        liveness_token: token,
        is_live: true,
        anti_spoof_score: isFallback ? 0.92 : 0.98,
        expires_at: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
        status: "VERIFIED",
      };
    }

    if (response && response.is_live && response.liveness_token) {
      state.consentLivenessToken = response.liveness_token;
      state.livenessVerified = true;

      if (banner) {
        banner.textContent = `Liveness Verification Successful (${isFallback ? "Coordinator Fallback Mode" : "Camera Anti-Spoof Score: " + (response.anti_spoof_score * 100).toFixed(0) + "%"}). Liveness token issued for 15 minutes.`;
        banner.style.display = "block";
        banner.style.backgroundColor = "#ecfdf5";
        banner.style.color = "#047857"; // deid: ignore
        banner.style.border = "1px solid #a7f3d0"; // deid: ignore
      }
    } else {
      state.livenessVerified = false;
      state.consentLivenessToken = null;
      if (banner) {
        banner.textContent =
          "Biometric liveness verification failed. Please retry or select Study Coordinator Video Fallback.";
        banner.style.display = "block";
        banner.style.backgroundColor = "#fef2f2";
        banner.style.color = "#b91c1c";
        banner.style.border = "1px solid #fecaca";
      }
    }

    renderConsentUI();
  } catch (err) {
    state.livenessVerified = false;
    state.consentLivenessToken = null;
    if (banner) {
      banner.textContent =
        "Liveness verification error: " + (err.message || err);
      banner.style.display = "block";
      banner.style.backgroundColor = "#fef2f2";
      banner.style.color = "#b91c1c";
      banner.style.border = "1px solid #fecaca";
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
    alert("Please answer all comprehension questions before submitting.");
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
    } else {
      // Sandbox local evaluation
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
    }

    // Interpret using ui helper
    const decision = interpretComprehensionResult(resultResponse);
    state.consentPassed = decision.canSign;

    // Display banner
    if (banner) {
      banner.textContent = decision.message;
      banner.style.display = "block";
      if (decision.canSign) {
        banner.style.backgroundColor = "#ecfdf5";
        banner.style.color = "#047857"; // deid: ignore
        banner.style.border = "1px solid #a7f3d0"; // deid: ignore
      } else {
        banner.style.backgroundColor = "#fef2f2";
        banner.style.color = "#b91c1c";
        banner.style.border = "1px solid #fecaca";
      }
    }

    // Refresh UI triggers
    renderConsentUI();
  } catch (err) {
    alert("Answers submission failed: " + err.message);
  }
}
