// HTML-string widget factories for generated clinical form fields (CDASH
// inputs, radio grids, visual analog sliders) and their query flag and panel.
import { renderClinicalIconSvg } from "ui";

const SLIDER_TOUCHED_BADGE_STYLE =
  "background: var(--semantic-color-interactive-primary); color: var(--semantic-color-text-on-primary); border: 1px solid var(--semantic-color-interactive-primary); padding: 2px 10px; border-radius: 12px; font-weight: bold; font-size: 14px; font-variant-numeric: var(--font-variant-numeric-tabular);";
const SLIDER_UNTOUCHED_BADGE_STYLE =
  "background: var(--semantic-color-surface); color: var(--semantic-color-text-secondary); border: 1px solid var(--semantic-color-border-control); padding: 2px 10px; border-radius: 12px; font-weight: 600; font-size: 14px; font-variant-numeric: var(--font-variant-numeric-tabular);";

const SLIDER_INTERACTION_KEYS = new Set([
  "ArrowLeft",
  "ArrowRight",
  "ArrowUp",
  "ArrowDown",
  "Home",
  "End",
  "PageUp",
  "PageDown",
  " ",
]);

export function markClinicalSliderTouched(inputEl, explicitValue = undefined) {
  if (!inputEl) return;
  if (explicitValue !== undefined && explicitValue !== null) {
    inputEl.value = String(explicitValue);
  }
  inputEl.setAttribute("data-touched", "true");
  inputEl.removeAttribute("aria-valuetext");
  if (inputEl.style) {
    inputEl.style.accentColor = "var(--semantic-color-interactive-primary)";
  }
  const doc = inputEl.ownerDocument || document;
  const disp = inputEl.id ? doc.getElementById(`${inputEl.id}_display`) : null;
  if (disp) {
    disp.textContent = `Value: ${inputEl.value}`;
    disp.setAttribute("data-state", "touched");
    disp.setAttribute("style", SLIDER_TOUCHED_BADGE_STYLE);
  }
}

export function createClinicalSlider(
  id,
  label,
  value = "",
  query = null,
  gridSpan = 12,
  attributes = {}
) {
  const minVal = attributes.min !== undefined ? attributes.min : 0;
  const maxVal = attributes.max !== undefined ? attributes.max : 100;
  const stepVal = attributes.step !== undefined ? attributes.step : 1;
  const hasExplicitValue =
    value !== "" && value !== null && value !== undefined;
  const initialValue = hasExplicitValue ? value : minVal;
  const initialTouched = hasExplicitValue ? "true" : "false";
  const initialDisplay = hasExplicitValue
    ? `Value: ${initialValue}`
    : "Not answered";
  const initialBadgeState = hasExplicitValue ? "touched" : "untouched";
  const initialBadgeStyle = hasExplicitValue
    ? SLIDER_TOUCHED_BADGE_STYLE
    : SLIDER_UNTOUCHED_BADGE_STYLE;
  const initialAccentColor = hasExplicitValue
    ? "var(--semantic-color-interactive-primary)"
    : "var(--semantic-color-border-control)";
  const ariaValueTextAttr = hasExplicitValue
    ? ""
    : '\n        aria-valuetext="Not answered"';
  const minLabel = attributes.min_label || attributes.minLabel || `${minVal}`;
  const maxLabel = attributes.max_label || attributes.maxLabel || `${maxVal}`;

  const queryFlagHTML = createClinicalQueryFlag(id, query);
  const queryPanelHTML = createQueryPanel(id, query);

  return `
<div class="clinical-input vas-slider-container grid-span-${gridSpan}" style="grid-column: span ${gridSpan}; margin-bottom: 20px;" id="field-container-${id}">
  <label for="${id}" class="vas-slider-label" style="font-weight: 600; display: block; margin-bottom: 8px;">
    ${label}
  </label>
  <div class="vas-slider-wrapper" style="background: var(--semantic-color-surface-muted); padding: 16px; border: 1px solid var(--semantic-color-border-control); border-radius: 8px;">
    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px;">
      <span class="vas-min-label tabular-nums" style="font-size: 12px; color: var(--semantic-color-text-secondary); font-weight: 500;">${minLabel}</span>
      <span class="vas-value-badge tabular-nums" id="${id}_display" data-state="${initialBadgeState}" style="${initialBadgeStyle}">
        ${initialDisplay}
      </span>
      <span class="vas-max-label tabular-nums" style="font-size: 12px; color: var(--semantic-color-text-secondary); font-weight: 500;">${maxLabel}</span>
    </div>
    <div class="input-wrapper" style="position: relative;">
      <input
        type="range"
        id="${id}"
        name="${id}"
        min="${minVal}"
        max="${maxVal}"
        step="${stepVal}"
        value="${initialValue}"
        data-tabular-nums="true"
        data-touched="${initialTouched}"${ariaValueTextAttr}
        class="vas-slider-input"
        style="width: 100%; cursor: pointer; accent-color: ${initialAccentColor}; min-height: var(--touch-target-min); height: var(--touch-target-min);"
      />
      ${queryFlagHTML}
    </div>
  </div>
  ${queryPanelHTML}
</div>
  `.trim();
}

export function createClinicalInput(
  id,
  label,
  value = "",
  query = null,
  gridSpan = 12,
  attributes = {}
) {
  const isNumeric =
    attributes.type === "numeric" || attributes.type === "number";
  const inputType = isNumeric ? "number" : attributes.type || "text";
  const resolvedInputMode =
    attributes.inputmode || (isNumeric ? "numeric" : "");
  const inputModeAttr = resolvedInputMode
    ? ` inputmode="${resolvedInputMode}"`
    : "";
  const explicitTabular = attributes["data-tabular-nums"];
  const shouldUseTabularNums =
    explicitTabular === false || explicitTabular === "false"
      ? false
      : explicitTabular !== undefined && explicitTabular !== null
        ? true
        : isNumeric ||
          /^(date|datetime-local|time|month|week|tel|range)$/.test(inputType) ||
          /^(numeric|decimal)$/.test(resolvedInputMode);
  const tabularAttrValue =
    explicitTabular !== undefined &&
    explicitTabular !== null &&
    explicitTabular !== "" &&
    explicitTabular !== true
      ? String(explicitTabular)
      : "true";
  const tabularNumsAttr = shouldUseTabularNums
    ? ` data-tabular-nums="${tabularAttrValue}"`
    : "";

  const containerAttrs = Object.entries(attributes)
    .filter(([k]) => k !== "type" && k !== "inputmode")
    .map(([k, v]) => `${k}="${v}"`)
    .join(" ");

  const queryFlagHTML = createClinicalQueryFlag(id, query);
  const queryPanelHTML = createQueryPanel(id, query);

  return `
<div class="clinical-input grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}" ${containerAttrs}>
  <label for="${id}">${label}</label>
  <div class="input-wrapper">
    <input type="${inputType}" id="${id}" name="${id}" value="${value}"${inputModeAttr}${tabularNumsAttr} />
    ${queryFlagHTML}
  </div>
  ${queryPanelHTML}
</div>
  `.trim();
}

export function createClinicalRadioGrid(
  id,
  label,
  options = [],
  selectedValue = "",
  query = null,
  gridSpan = 12
) {
  const optionsHTML = options
    .map((opt, idx) => {
      const optVal = typeof opt === "string" ? opt : opt.value;
      const optLabel = typeof opt === "string" ? opt : opt.label;
      const isChecked = optVal === selectedValue ? " checked" : "";
      const optionId = `${id}_option_${idx}`;
      return `
      <label class="radio-option" for="${optionId}">
        <input type="radio" id="${optionId}" name="${id}" value="${optVal}"${isChecked} />
        <span class="radio-option-label">${optLabel}</span>
      </label>
      `.trim();
    })
    .join("\n");

  const queryFlagHTML = createClinicalQueryFlag(id, query);
  const queryPanelHTML = createQueryPanel(id, query);

  return `
<fieldset class="clinical-radio-grid grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}">
  <legend>${label}</legend>
  <div class="radio-options-wrapper">
    <div class="radio-options">
      ${optionsHTML}
    </div>
    ${queryFlagHTML}
  </div>
  ${queryPanelHTML}
</fieldset>
  `.trim();
}

export function createClinicalQueryFlag(fieldId, query) {
  const status = query && query.status ? query.status.toUpperCase() : "NONE";
  const statusClass = status.toLowerCase();
  const label =
    status === "NONE"
      ? "No active queries. Click to create."
      : `Query status: ${status}`;
  const icon = renderClinicalIconSvg(
    status === "NONE" ? "message-square" : "alert-triangle",
    { size: 16 }
  );

  return `
<button class="query-flag query-status-${statusClass}"
        id="query-flag-${fieldId}"
        type="button"
        aria-expanded="false"
        aria-controls="query-panel-${fieldId}"
        aria-label="${label}">
  ${icon}
</button>
  `.trim();
}

export function createQueryPanel(fieldId, query) {
  const status = query && query.status ? query.status.toUpperCase() : "NONE";
  let bodyHTML = "";

  if (status === "NONE") {
    bodyHTML = `
      <div class="query-create-section">
        <p class="query-panel-instruction">Raise a query for this field:</p>
        <div class="form-group">
          <label for="query-message-${fieldId}">Discrepancy Message</label>
          <textarea id="query-message-${fieldId}" placeholder="Enter clinical discrepancy details..." required></textarea>
        </div>
        <button type="button" class="btn-submit-query" data-field-id="${fieldId}" data-action="create-query">Submit Query</button>
      </div>
    `.trim();
  } else if (status === "OPEN" || status === "REOPENED") {
    bodyHTML = `
      <div class="query-details">
        <div class="query-status-badge badge-${status.toLowerCase()}">Status: ${status}</div>
        <p class="query-current-msg"><strong>Discrepancy:</strong> ${query.message}</p>
        <p class="query-meta">Raised by: ${query.createdBy || "System"} on ${query.createdAt || "N/A"}</p>
      </div>
      <div class="query-respond-section">
        <div class="form-group">
          <label for="query-response-${fieldId}">Your Response</label>
          <textarea id="query-response-${fieldId}" placeholder="Enter clinical justification or resolution explanation..." required></textarea>
        </div>
        <button type="button" class="btn-respond-query" data-field-id="${fieldId}" data-action="respond-query">Submit Response</button>
      </div>
    `.trim();
  } else if (status === "ANSWERED") {
    bodyHTML = `
      <div class="query-details">
        <div class="query-status-badge badge-answered">Status: ANSWERED</div>
        <p class="query-current-msg"><strong>Discrepancy:</strong> ${query.message}</p>
        <p class="query-response-msg"><strong>Response:</strong> ${query.response || "No response provided"}</p>
        <p class="query-meta">Responded by: ${query.respondedBy || "Investigator"} on ${query.respondedAt || "N/A"}</p>
      </div>
      <div class="query-actions-section">
        <button type="button" class="btn-close-query" data-field-id="${fieldId}" data-action="close-query">Close Query (Resolve)</button>
        <button type="button" class="btn-reopen-query" data-field-id="${fieldId}" data-action="reopen-query">Reopen Query</button>
      </div>
    `.trim();
  } else if (status === "CLOSED") {
    bodyHTML = `
      <div class="query-details">
        <div class="query-status-badge badge-closed">Status: CLOSED</div>
        <p class="query-current-msg"><strong>Discrepancy:</strong> ${query.message}</p>
        <p class="query-response-msg"><strong>Response:</strong> ${query.response || "N/A"}</p>
        <p class="query-meta">Closed by: ${query.closedBy || "CRA/DM"} on ${query.closedAt || "N/A"}</p>
        <p class="query-history-info">This query is permanently resolved and closed.</p>
      </div>
    `.trim();
  }

  return `
<div class="query-panel" id="query-panel-${fieldId}" style="display: none;" role="region" aria-labelledby="query-flag-${fieldId}">
  <div class="query-panel-header">
    <span class="query-panel-title">Query Manager - ${fieldId}</span>
    <button type="button" class="btn-close-panel" data-field-id="${fieldId}" aria-label="Close query panel">×</button>
  </div>
  <div class="query-panel-body">
    ${bodyHTML}
  </div>
</div>
  `.trim();
}

const delegatedContainers = new WeakMap();

export function bindDelegatedClinicalWidgets(container, options = {}) {
  if (!container || typeof container.addEventListener !== "function") {
    return;
  }

  const alreadyBound = delegatedContainers.has(container);
  delegatedContainers.set(container, options);
  if (alreadyBound) {
    return;
  }

  const isSliderInput = (target) =>
    Boolean(
      target &&
      target.tagName === "INPUT" &&
      (target.type === "range" ||
        target.classList?.contains("vas-slider-input"))
    );

  const notifyFieldInput = (event) => {
    const currentOptions = delegatedContainers.get(container);
    if (typeof currentOptions?.onFieldInput === "function") {
      currentOptions.onFieldInput(event);
    }
  };

  const handleInputOrChange = (event) => {
    const target = event?.target;
    if (isSliderInput(target)) {
      markClinicalSliderTouched(target);
    }
    notifyFieldInput(event);
  };

  const handlePointerDown = (event) => {
    const target = event?.target;
    if (isSliderInput(target)) {
      markClinicalSliderTouched(target);
      notifyFieldInput(event);
    }
  };

  const handleKeyDown = (event) => {
    const target = event?.target;
    if (isSliderInput(target) && SLIDER_INTERACTION_KEYS.has(event.key)) {
      markClinicalSliderTouched(target);
      notifyFieldInput(event);
    }
  };

  const handleClick = (event) => {
    const rawTarget = event?.target;
    const elementTarget =
      rawTarget && typeof rawTarget.closest === "function"
        ? rawTarget
        : rawTarget?.parentElement;
    if (!elementTarget || typeof elementTarget.closest !== "function") {
      return;
    }

    const doc = container.ownerDocument || document;

    const flagBtn = elementTarget.closest(".query-flag");
    if (flagBtn && container.contains(flagBtn)) {
      const panelId = flagBtn.getAttribute("aria-controls");
      const panel = panelId ? doc.getElementById(panelId) : null;
      if (panel) {
        const isClosed =
          panel.style.display === "none" ||
          panel.hidden ||
          flagBtn.getAttribute("aria-expanded") !== "true";
        panel.style.display = isClosed ? "block" : "none";
        flagBtn.setAttribute("aria-expanded", isClosed ? "true" : "false");
      }
      return;
    }

    const closeBtn = elementTarget.closest(".btn-close-panel");
    if (closeBtn && container.contains(closeBtn)) {
      const panel = closeBtn.closest(".query-panel");
      if (panel) {
        panel.style.display = "none";
        const flagId = panel.getAttribute("aria-labelledby");
        const linkedFlag = flagId ? doc.getElementById(flagId) : null;
        if (linkedFlag) {
          linkedFlag.setAttribute("aria-expanded", "false");
          linkedFlag.focus?.();
        }
      }
    }
  };

  container.addEventListener("input", handleInputOrChange, true);
  container.addEventListener("change", handleInputOrChange, true);
  container.addEventListener("pointerdown", handlePointerDown, true);
  container.addEventListener("keydown", handleKeyDown, true);
  container.addEventListener("click", handleClick);
}
