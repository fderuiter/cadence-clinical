/**
 * A small debounce utility that limits function execution during rapid invocation.
 *
 * @param {Function} func - The function to debounce.
 * @param {number} wait - The delay in milliseconds before executing the function.
 * @returns {Function} The debounced function.
 */
export function debounce(func, wait) {
  let timeout;
  return function (...args) {
    const context = this;
    clearTimeout(timeout);
    timeout = setTimeout(() => {
      func.apply(context, args);
    }, wait);
  };
}

/**
 * Generates the standard accessible HTML string representing a clinical code lookup input field
 * with its associated real-time terminology validation feedback indicator.
 *
 * @param {string} id - The input element identifier.
 * @param {string} label - The visible label for the field.
 * @param {string} value - The current value of the field.
 * @param {string} [status='none'] - The lookup validation state ('none', 'loading', 'valid', 'invalid', 'degraded').
 * @param {string} [statusMessage=''] - The dynamic explanation message of the current validation status.
 * @returns {string} The HTML markup.
 */
export function createClinicalLookupInput(
  id,
  label,
  value = "",
  status = "none",
  statusMessage = ""
) {
  let statusHtml;
  if (status !== "none") {
    let stateClass = "";
    let statusIcon = "";
    let ariaLiveMessage = statusMessage;

    if (status === "loading") {
      stateClass = "lookup-loading";
      statusIcon = "⏳";
      if (!ariaLiveMessage)
        ariaLiveMessage = "Searching terminology database...";
    } else if (status === "valid") {
      stateClass = "lookup-valid";
      statusIcon = "✅";
      if (!ariaLiveMessage) ariaLiveMessage = "Code is valid.";
    } else if (status === "invalid") {
      stateClass = "lookup-invalid";
      statusIcon = "❌";
      if (!ariaLiveMessage)
        ariaLiveMessage = "Invalid code. Please check and try again.";
    } else if (status === "degraded") {
      stateClass = "lookup-degraded";
      statusIcon = "⚠️";
      if (!ariaLiveMessage)
        ariaLiveMessage = "Terminology service degraded. Validation offline.";
    }

    statusHtml = `
    <div id="lookup-status-${id}" class="lookup-status-indicator ${stateClass}" role="status" aria-live="polite">
      <span class="lookup-status-icon" aria-hidden="true">${statusIcon}</span>
      <span class="lookup-status-text">${ariaLiveMessage}</span>
    </div>`;
  } else {
    statusHtml = `<div id="lookup-status-${id}" class="lookup-status-indicator" role="status" aria-live="polite" style="display: none"></div>`;
  }

  return `
  <div id="field-container-${id}" class="clinical-input clinical-lookup-container grid-span-12" style="grid-column: span 12;">
    <label for="${id}">${label}</label>
    <div class="input-wrapper">
      <input id="${id}" type="text" name="${id}" value="${value}" autocomplete="off" />
    </div>
    ${statusHtml}
  </div>`;
}

export {
  canonicalSerialize,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
  generateJwtHS256,
  sha256,
  validateField,
  buildLedgerBlock,
  encryptAESGCM,
  decryptAESGCM,
  deriveSessionKey,
} from "./signing.js";

export {
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  interpretComprehensionResult,
} from "./econsent.js";
