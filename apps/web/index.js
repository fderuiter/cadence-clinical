import {
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalVisitMatrix,
} from "ui";

/**
 * Renders a clinical form HTML string from a backend XML payload.
 * It uses the shared design components (createClinicalInput) to generate each field.
 *
 * @param {string} xmlString - The XML payload containing the form definition.
 * @returns {string} The HTML string representing the rendered form.
 */
export function renderFormFromXML(xmlString) {
  if (!xmlString) return "";

  // Simple regex-based parser for demonstration (in reality, use DOMParser)
  const formMatch = xmlString.match(/<form[^>]*>(.*?)<\/form>/is);
  if (!formMatch) return "";

  const innerXML = formMatch[1];
  const fieldRegex = /<field id="([^"]+)" label="([^"]+)"\s*\/>/g;

  let html = `<form class="clinical-form">`;
  let match;
  while ((match = fieldRegex.exec(innerXML)) !== null) {
    const id = match[1];
    const label = match[2];
    html += createClinicalInput(id, label);
  }
  html += `</form>`;

  return html;
}

/**
 * Renders a clinical form HTML string dynamically based on a JSON layout definition.
 * Dynamically computes CSS grid columns and positions elements according to
 * CDASH metadata specifications.
 *
 * @param {string|Object} jsonPayload - The JSON payload or parsed object defining the form and its layout.
 * @returns {string} The HTML string representing the rendered form.
 */
export function renderFormFromJSON(jsonPayload) {
  if (!jsonPayload) return "";

  let payload = jsonPayload;
  if (typeof jsonPayload === "string") {
    try {
      payload = JSON.parse(jsonPayload);
    } catch {
      return `<div class="render-error">Invalid JSON payload configuration.</div>`;
    }
  }

  const formId = payload.formId || "unknown";
  const formTitle = payload.formTitle || "";
  const layout = payload.layout || { columns: 12 };
  const columns = layout.columns || 12;
  const fields = payload.fields || [];

  let html = `<form class="clinical-form clinical-form-grid" id="form-${formId}" style="display: grid; grid-template-columns: repeat(${columns}, 1fr); gap: 16px;">`;

  if (formTitle) {
    html += `<h2 class="form-title" style="grid-column: span ${columns};">${formTitle}</h2>`;
  }

  fields.forEach((field) => {
    const gridSpan = field.gridSpan || 12;
    const value = field.value || "";
    const query = field.query || null;

    if (field.type === "radio" || field.type === "choice_single") {
      const options = field.options || [];
      html += createClinicalRadioGrid(
        field.id,
        field.label,
        options,
        value,
        query,
        gridSpan
      );
    } else {
      const attrs = {};
      if (field.cdash) {
        attrs["data-cdash"] = field.cdash;
      }
      html += createClinicalInput(
        field.id,
        field.label,
        value,
        query,
        gridSpan,
        attrs
      );
    }
  });

  html += `</form>`;
  return html;
}

/**
 * Validates a value against the specified CDASH metadata rules.
 *
 * @param {Object} fieldMeta - The field definition containing validation configurations.
 * @param {string|number} val - The input value to validate.
 * @returns {Object} An object `{ valid: boolean, message?: string }`.
 */
export function validateField(fieldMeta, val) {
  if (!fieldMeta || !fieldMeta.validation) return { valid: true };

  const rules = fieldMeta.validation;

  // Required check
  if (
    rules.required &&
    (val === undefined || val === null || val.toString().trim() === "")
  ) {
    return { valid: false, message: "This field is required." };
  }

  // Only perform format/range validation if there is a value entered
  if (val !== undefined && val !== null && val.toString().trim() !== "") {
    // Pattern (Regex) check
    if (rules.pattern) {
      const regex = new RegExp(rules.pattern);
      if (!regex.test(val)) {
        return { valid: false, message: rules.message || "Invalid format." };
      }
    }

    // Min / Max (Numeric check)
    if (rules.min !== undefined || rules.max !== undefined) {
      const num = parseFloat(val);
      if (isNaN(num)) {
        return { valid: false, message: "Value must be a number." };
      }
      if (rules.min !== undefined && num < rules.min) {
        return {
          valid: false,
          message: rules.message || `Minimum value is ${rules.min}.`,
        };
      }
      if (rules.max !== undefined && num > rules.max) {
        return {
          valid: false,
          message: rules.message || `Maximum value is ${rules.max}.`,
        };
      }
    }
  }

  return { valid: true };
}

/**
 * Computes a standard SHA-256 hash using Web Crypto APIs.
 *
 * @param {string} message - The plaintext message to hash.
 * @returns {Promise<string>} The hexadecimal SHA-256 digest.
 */
export async function sha256(message) {
  const msgBuffer = new TextEncoder().encode(message);
  const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  const hashHex = hashArray
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
  return hashHex;
}

// --- STANDALONE INTERACTIVE WEB DEMO CLIENT-SIDE LOGIC ---
if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", () => {
    // --- 1. USDM MOCK DATA ---
    const defaultUsdm = {
      studyId: "STUDY-USDM-001",
      studyTitle: "Phase II Trial of Cadence-001 in Essential Hypertension",
      objectives: [
        {
          id: "OBJ-001",
          type: "Primary",
          description:
            "Evaluate the reduction of mean sitting Systolic Blood Pressure (SBP) from baseline.",
        },
        {
          id: "OBJ-002",
          type: "Secondary",
          description:
            "Evaluate safety and tolerability of daily oral administration of Cadence-001.",
        },
      ],
      visits: [
        "Screening",
        "Baseline (Day 1)",
        "Week 2",
        "Week 4",
        "End of Study",
      ],
      forms: [
        {
          name: "Demographics",
          statuses: ["Complete", "N/A", "N/A", "N/A", "N/A"],
        },
        {
          name: "Vital Signs (BP & Pulse)",
          statuses: ["Complete", "Pending", "Pending", "Pending", "Pending"],
        },
        {
          name: "Adverse Events Check",
          statuses: ["N/A", "Complete", "Pending", "Pending", "Complete"],
        },
        {
          name: "Study Medication Log",
          statuses: ["N/A", "Complete", "Complete", "Complete", "Complete"],
        },
      ],
    };

    // --- 2. eCRF CDASH METADATA DEFINITION ---
    const ecrfDefinition = {
      formId: "VS_DEMO",
      formTitle: "Vital Signs & Demographics Capture",
      layout: { columns: 12 },
      fields: [
        {
          id: "brthdt",
          label: "Date of Birth (YYYY-MM-DD)",
          type: "text",
          gridSpan: 6,
          cdash: "DM.BRTHDT",
          value: "1980-05-12",
          validation: {
            required: true,
            pattern: "^\\d{4}-\\d{2}-\\d{2}$",
            message: "Date must be in YYYY-MM-DD format",
          },
        },
        {
          id: "sex",
          label: "Sex at Birth",
          type: "radio",
          gridSpan: 6,
          options: [
            { value: "M", label: "Male" },
            { value: "F", label: "Female" },
            { value: "U", label: "Unknown" },
          ],
          cdash: "DM.SEX",
          value: "F",
        },
        {
          id: "vssbp",
          label: "Systolic Blood Pressure (mmHg)",
          type: "text",
          gridSpan: 4,
          cdash: "VS.VSSBP",
          value: "120",
          validation: {
            required: true,
            min: 50,
            max: 250,
            message: "Systolic Blood Pressure must be between 50 and 250 mmHg",
          },
        },
        {
          id: "vsdpb",
          label: "Diastolic Blood Pressure (mmHg)",
          type: "text",
          gridSpan: 4,
          cdash: "VS.VSDPB",
          value: "80",
          validation: {
            required: true,
            min: 30,
            max: 150,
            message: "Diastolic Blood Pressure must be between 30 and 150 mmHg",
          },
        },
        {
          id: "pulse",
          label: "Pulse Rate (bpm)",
          type: "text",
          gridSpan: 4,
          cdash: "VS.VSHR",
          value: "72",
          validation: {
            required: true,
            min: 30,
            max: 200,
            message: "Pulse Rate must be between 30 and 200 bpm",
          },
        },
      ],
    };

    // --- 3. STATE HOLDERS (IN-MEMORY BROWSER DB) ---
    let currentUsdm = JSON.parse(JSON.stringify(defaultUsdm));
    let formValues = {};
    let formQueries = {}; // fieldId -> queryObj
    let ledgerBlocks = [];
    let pendingValueChange = null; // Stores { fieldId, oldValue, newValue, element } during Reason Modal

    // Initialize Form State from definition
    ecrfDefinition.fields.forEach((f) => {
      formValues[f.id] = f.value;
      if (f.query) {
        formQueries[f.id] = f.query;
      }
    });

    async function addLedgerBlock(action, details, reason = "System Action") {
      const timestamp = new Date().toISOString();
      const index = ledgerBlocks.length;
      const prevHash =
        index === 0
          ? "0000000000000000000000000000000000000000000000000000000000000000"
          : ledgerBlocks[index - 1].hash;

      const payloadString = `${index}|${timestamp}|${action}|${JSON.stringify(details)}|${reason}|${prevHash}`;
      const hash = await sha256(payloadString);

      const block = {
        index,
        timestamp,
        action,
        details,
        reason,
        prevHash,
        hash,
      };

      ledgerBlocks.push(block);
      renderLedger();
      return block;
    }

    // --- 5. DOM ELEMENTS ---
    const tabMdr = document.getElementById("tab-btn-mdr");
    const tabEcrf = document.getElementById("tab-btn-ecrf");
    const tabAudit = document.getElementById("tab-btn-audit");

    const secMdr = document.getElementById("section-mdr");
    const secEcrf = document.getElementById("section-ecrf");
    const secAudit = document.getElementById("section-audit");

    const usdmTextarea = document.getElementById("usdm-json");
    const btnResetUsdm = document.getElementById("btn-reset-usdm");
    const btnUpdateSoa = document.getElementById("btn-update-soa");
    const soaContainer = document.getElementById("soa-matrix-container");

    const ecrfContainer = document.getElementById("ecrf-form-container");
    const btnClearEcrf = document.getElementById("btn-clear-ecrf");
    const btnSubmitEcrf = document.getElementById("btn-submit-ecrf");

    const ledgerContainer = document.getElementById(
      "ledger-timeline-container"
    );
    const btnClearLedger = document.getElementById("btn-clear-ledger");

    const reasonModal = document.getElementById("reason-modal");
    const reasonSelect = document.getElementById("change-reason-select");
    const reasonText = document.getElementById("change-reason-text");
    const btnCancelChange = document.getElementById("btn-cancel-change");
    const btnSaveChange = document.getElementById("btn-save-change");

    // --- 6. TAB NAVIGATION ---
    function switchTab(activeTab, activeSec) {
      [tabMdr, tabEcrf, tabAudit].forEach((t) => t.classList.remove("active"));
      [secMdr, secEcrf, secAudit].forEach((s) => s.classList.remove("active"));

      activeTab.classList.add("active");
      activeSec.classList.add("active");
    }

    tabMdr.addEventListener("click", () => switchTab(tabMdr, secMdr));
    tabEcrf.addEventListener("click", () => switchTab(tabEcrf, secEcrf));
    tabAudit.addEventListener("click", () => switchTab(tabAudit, secAudit));

    // --- 7. MDR VISUALIZER FUNCTIONS ---
    function renderMdr() {
      usdmTextarea.value = JSON.stringify(currentUsdm, null, 2);
      const matrixHtml = createClinicalVisitMatrix({
        visits: currentUsdm.visits || [],
        forms: currentUsdm.forms || [],
      });
      soaContainer.innerHTML = matrixHtml;
    }

    btnResetUsdm.addEventListener("click", () => {
      currentUsdm = JSON.parse(JSON.stringify(defaultUsdm));
      renderMdr();
      addLedgerBlock(
        "USDM_RESET",
        { studyId: currentUsdm.studyId },
        "User reset study protocol schema back to default USDM v3.0 specs."
      );
    });

    btnUpdateSoa.addEventListener("click", () => {
      try {
        const parsed = JSON.parse(usdmTextarea.value);
        if (!parsed.visits || !parsed.forms) {
          alert("Invalid USDM Structure! Must contain 'visits' and 'forms'.");
          return;
        }
        currentUsdm = parsed;
        renderMdr();
        addLedgerBlock(
          "USDM_UPDATE",
          { studyId: currentUsdm.studyId },
          "User modified and compiled a custom USDM study protocol."
        );
      } catch (err) {
        alert("Parsing Error: " + err.message);
      }
    });

    // --- 8. eCRF RUNTIME & VALIDATION ---
    function renderEcrf() {
      // Build fields with active queries merged
      const fieldsWithQueries = ecrfDefinition.fields.map((f) => {
        const fieldCopy = JSON.parse(JSON.stringify(f));
        fieldCopy.value = formValues[f.id] || "";
        if (formQueries[f.id]) {
          fieldCopy.query = formQueries[f.id];
        } else {
          fieldCopy.query = null;
        }
        return fieldCopy;
      });

      const payload = {
        formId: ecrfDefinition.formId,
        formTitle: ecrfDefinition.formTitle,
        layout: ecrfDefinition.layout,
        fields: fieldsWithQueries,
      };

      const formHtml = renderFormFromJSON(payload);
      ecrfContainer.innerHTML = formHtml;

      // Attach Event Listeners to rendered inputs
      ecrfDefinition.fields.forEach((fieldMeta) => {
        const inputEl = document.getElementById(fieldMeta.id);
        if (inputEl) {
          inputEl.addEventListener("change", (e) => {
            const newValue = e.target.value;
            const oldValue = formValues[fieldMeta.id] || "";

            if (newValue === oldValue) return; // No actual change

            // Comply with 21 CFR Part 11: Prompt for Reason for Change if old value was not empty
            if (
              oldValue !== "" &&
              oldValue !== null &&
              oldValue !== undefined
            ) {
              pendingValueChange = {
                fieldId: fieldMeta.id,
                oldValue,
                newValue,
                element: e.target,
              };
              openReasonModal();
            } else {
              // Direct save for initial entry
              saveFieldChange(
                fieldMeta.id,
                oldValue,
                newValue,
                "Initial Entry"
              );
            }
          });
        }

        // Handle Radios
        if (fieldMeta.type === "radio") {
          const radioOptions = document.getElementsByName(fieldMeta.id);
          radioOptions.forEach((opt) => {
            opt.addEventListener("change", (e) => {
              if (e.target.checked) {
                const newValue = e.target.value;
                const oldValue = formValues[fieldMeta.id] || "";

                if (newValue === oldValue) return;

                if (
                  oldValue !== "" &&
                  oldValue !== null &&
                  oldValue !== undefined
                ) {
                  pendingValueChange = {
                    fieldId: fieldMeta.id,
                    oldValue,
                    newValue,
                    element: e.target,
                  };
                  openReasonModal();
                } else {
                  saveFieldChange(
                    fieldMeta.id,
                    oldValue,
                    newValue,
                    "Initial Entry"
                  );
                }
              }
            });
          });
        }

        // Attach Query Button events
        const queryFlagBtn = document.getElementById(
          `query-flag-${fieldMeta.id}`
        );
        if (queryFlagBtn) {
          queryFlagBtn.addEventListener("click", () => {
            const panel = document.getElementById(
              `query-panel-${fieldMeta.id}`
            );
            if (panel) {
              const isHidden = panel.style.display === "none";
              panel.style.display = isHidden ? "block" : "none";
              queryFlagBtn.setAttribute(
                "aria-expanded",
                isHidden ? "true" : "false"
              );
            }
          });
        }
      });

      // Attach query action button click handlers inside panels
      const allActionButtons = ecrfContainer.querySelectorAll("[data-action]");
      allActionButtons.forEach((btn) => {
        btn.addEventListener("click", (e) => {
          const fieldId = e.target.getAttribute("data-field-id");
          const action = e.target.getAttribute("data-action");
          handleQueryAction(fieldId, action);
        });
      });

      // Run live validations on current values and draw error blocks
      ecrfDefinition.fields.forEach((fieldMeta) => {
        const val = formValues[fieldMeta.id] || "";
        const res = validateField(fieldMeta, val);
        const container = document.getElementById(
          `field-container-${fieldMeta.id}`
        );

        if (container) {
          // Clean existing error blocks
          const existingErr = container.querySelector(".validation-error-msg");
          if (existingErr) existingErr.remove();
          container.classList.remove("has-error");

          if (!res.valid && val !== "") {
            container.classList.add("has-error");
            const errDiv = document.createElement("div");
            errDiv.className = "validation-error-msg";
            errDiv.innerText = res.message;
            container.appendChild(errDiv);
          }
        }
      });
    }

    function openReasonModal() {
      reasonSelect.value = "Initial Entry";
      reasonText.value = "";
      reasonModal.style.display = "flex";
    }

    function closeReasonModal() {
      reasonModal.style.display = "none";
      pendingValueChange = null;
    }

    btnCancelChange.addEventListener("click", () => {
      // Revert input field value in the DOM
      if (pendingValueChange && pendingValueChange.element) {
        if (pendingValueChange.element.type === "radio") {
          // Re-check old radio option
          const radioOpts = document.getElementsByName(
            pendingValueChange.fieldId
          );
          radioOpts.forEach((opt) => {
            opt.checked = opt.value === pendingValueChange.oldValue;
          });
        } else {
          pendingValueChange.element.value = pendingValueChange.oldValue;
        }
      }
      closeReasonModal();
    });

    btnSaveChange.addEventListener("click", () => {
      if (!pendingValueChange) return;

      const selReason = reasonSelect.value;
      const custText = reasonText.value.trim();
      const finalReason =
        selReason === "Other" && custText
          ? custText
          : `${selReason}${custText ? ": " + custText : ""}`;

      saveFieldChange(
        pendingValueChange.fieldId,
        pendingValueChange.oldValue,
        pendingValueChange.newValue,
        finalReason
      );

      closeReasonModal();
    });

    function saveFieldChange(fieldId, oldValue, newValue, reason) {
      formValues[fieldId] = newValue;
      renderEcrf();

      const fieldMeta = ecrfDefinition.fields.find((f) => f.id === fieldId);
      const label = fieldMeta ? fieldMeta.label : fieldId;
      const cdash = fieldMeta ? fieldMeta.cdash : "";

      addLedgerBlock(
        "FIELD_CHANGE",
        {
          fieldId,
          label,
          cdash,
          oldValue,
          newValue,
        },
        reason
      );
    }

    async function handleQueryAction(fieldId, action) {
      if (action === "create-query") {
        const msgInput = document.getElementById(`query-message-${fieldId}`);
        const msg = msgInput ? msgInput.value.trim() : "";
        if (!msg) {
          alert("Please enter a discrepancy message!");
          return;
        }

        const queryObj = {
          status: "OPEN",
          message: msg,
          createdBy: "Data Monitor (Offline Client)",
          createdAt: new Date().toISOString().slice(0, 10),
        };

        formQueries[fieldId] = queryObj;
        renderEcrf();
        await addLedgerBlock(
          "QUERY_CREATE",
          { fieldId, query: queryObj },
          `Raised discrepancy: "${msg}"`
        );
      } else if (action === "respond-query") {
        const respInput = document.getElementById(`query-response-${fieldId}`);
        const resp = respInput ? respInput.value.trim() : "";
        if (!resp) {
          alert("Please enter a response!");
          return;
        }

        const queryObj = formQueries[fieldId];
        queryObj.status = "ANSWERED";
        queryObj.response = resp;
        queryObj.respondedBy = "Clinical Investigator (Offline Client)";
        queryObj.respondedAt = new Date().toISOString().slice(0, 10);

        renderEcrf();
        await addLedgerBlock(
          "QUERY_RESPOND",
          { fieldId, query: queryObj },
          `Responded to query: "${resp}"`
        );
      } else if (action === "close-query") {
        const queryObj = formQueries[fieldId];
        queryObj.status = "CLOSED";
        queryObj.closedBy = "Data Monitor (Offline Client)";
        queryObj.closedAt = new Date().toISOString().slice(0, 10);

        renderEcrf();
        await addLedgerBlock(
          "QUERY_CLOSE",
          { fieldId, query: queryObj },
          "Discrepancy resolved and closed permanently."
        );
      } else if (action === "reopen-query") {
        const queryObj = formQueries[fieldId];
        queryObj.status = "REOPENED";
        queryObj.message =
          queryObj.message + " [Reopened due to insufficient response]";

        renderEcrf();
        await addLedgerBlock(
          "QUERY_REOPEN",
          { fieldId, query: queryObj },
          "Investigator response was rejected. Query reopened."
        );
      }
    }

    btnClearEcrf.addEventListener("click", () => {
      ecrfDefinition.fields.forEach((f) => {
        formValues[f.id] = "";
        delete formQueries[f.id];
      });
      renderEcrf();
      addLedgerBlock(
        "FORM_CLEAR",
        { formId: ecrfDefinition.formId },
        "All eCRF form fields cleared by clinical staff."
      );
    });

    btnSubmitEcrf.addEventListener("click", () => {
      // Validate all fields first
      let allValid = true;
      let errMsgs = [];

      ecrfDefinition.fields.forEach((f) => {
        const val = formValues[f.id] || "";
        const res = validateField(f, val);
        if (!res.valid) {
          allValid = false;
          errMsgs.push(`${f.label}: ${res.message}`);
        }
      });

      if (!allValid) {
        alert(
          "Cannot submit eCRF! The form contains validation errors:\n\n" +
            errMsgs.join("\n")
        );
        return;
      }

      addLedgerBlock(
        "SESSION_SUBMIT",
        {
          formId: ecrfDefinition.formId,
          formValues,
          formQueries,
        },
        "eCRF successfully verified, finalized, and electronically submitted."
      );

      alert(
        "eCRF Session successfully submitted to secure cryptographic database!"
      );
    });

    // --- 9. LEDGER TIMELINE RENDERING ---
    function renderLedger() {
      if (ledgerBlocks.length === 0) {
        ledgerContainer.innerHTML = `<div class="empty-ledger">No ledger entries recorded yet. Make some changes on the tabs above!</div>`;
        return;
      }

      ledgerContainer.innerHTML = ledgerBlocks
        .map((block) => {
          const detailsHtml = Object.entries(block.details)
            .map(([k, v]) => {
              const valStr = typeof v === "object" ? JSON.stringify(v) : v;
              return `
              <span class="ledger-lbl">${k}:</span>
              <span class="ledger-val">${valStr}</span>
            `;
            })
            .join("");

          return `
          <div class="ledger-block signed">
            <span class="verified-stamp">Verified Ledger Block</span>
            <div class="ledger-block-header">
              <span class="ledger-block-index">BLOCK #${block.index}</span>
              <span class="ledger-block-timestamp">${block.timestamp}</span>
            </div>
            <div class="ledger-block-title">${block.action}</div>
            <div class="ledger-block-details">
              ${detailsHtml}
              <span class="ledger-lbl">reason:</span>
              <span class="ledger-val" style="color: var(--accent); font-weight: 600;">${block.reason}</span>
            </div>
            <div class="ledger-block-crypto">
              <div class="crypto-row">
                <span class="crypto-lbl">prevHash:</span>
                <span class="crypto-hash prev">${block.prevHash}</span>
              </div>
              <div class="crypto-row">
                <span class="crypto-lbl">blockHash:</span>
                <span class="crypto-hash">${block.hash}</span>
              </div>
            </div>
          </div>
        `;
        })
        .reverse()
        .join("");
    }

    btnClearLedger.addEventListener("click", () => {
      if (
        confirm(
          "Are you sure you want to purge the local cryptographic audit trail? This is a non-GxP compliance violation!"
        )
      ) {
        ledgerBlocks = [];
        renderLedger();
        alert("Audit trail purged!");
      }
    });

    // --- 10. INITIALIZATION BOOTSTRAP ---
    renderMdr();
    renderEcrf();

    // Create Genesis Block asynchronously
    addLedgerBlock(
      "GENESIS",
      {
        platform: "Cadence Clinical",
        environment: "Interactive Web Sandbox",
        compliantStandards: [
          "21 CFR Part 11",
          "GAMP 5 (Category 4/5)",
          "IEC 62304",
          "ISO 14155:2020",
        ],
      },
      "System boot and cryptographic ledger initialized successfully."
    );
  });
}
