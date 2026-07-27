import {
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalVisitMatrix,
  createCtmsMilestoneTable,
  createCtmsVisitTable,
  createConditionRow,
  createRuleEditorContainer,
  generateGatewaySignature,
  createClinicalLookupInput,
  sha256 as sharedSha256,
  validateField as sharedValidateField,
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
  const fieldRegex = /<field\b([^>]*?)\/?>/gi;

  let html = `<form class="clinical-form">`;
  let match;
  while ((match = fieldRegex.exec(innerXML)) !== null) {
    const attrString = match[1];
    const idMatch = attrString.match(/\bid="([^"]*)"/);
    const labelMatch = attrString.match(/\blabel="([^"]*)"/);
    if (idMatch && labelMatch) {
      const id = idMatch[1];
      const label = labelMatch[1];
      html += createClinicalInput(id, label);
    }
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
    } else if (field.type === "concept_code") {
      const status = field.status || "none";
      const statusMessage = field.statusMessage || "";
      const attrs = {};
      if (field.cdash) {
        attrs["data-cdash"] = field.cdash;
      }
      html += createClinicalLookupInput(
        field.id,
        field.label,
        value,
        status,
        statusMessage,
        gridSpan,
        attrs
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
import {
  evaluateAST,
  compilerCache,
  getCompiledExpression,
  debounce,
} from "./src/evaluator.js";

export { evaluateAST, compilerCache, getCompiledExpression, debounce };

export function validateField(fieldMeta, val, context = {}) {
  return sharedValidateField(fieldMeta, val, context, evaluateAST);
}

// Re-export sha256 from the shared ui library to prevent duplicate implementations
export { sha256 };

/**
 * Renders the clinical trial management system (CTMS) dashboard view.
 *
 * @param {Object} data - The CTMS dashboard dataset
 */
export function renderCtms(data) {
  if (!data) return;
  const milestonesContainer = document.getElementById(
    "ctms-milestones-container"
  );
  const visitsContainer = document.getElementById("ctms-visits-container");
  const workloadContainer = document.getElementById("ctms-workload-container");
  const recruitmentContainer = document.getElementById(
    "ctms-recruitment-container"
  );

  if (milestonesContainer) {
    milestonesContainer.innerHTML = createCtmsMilestoneTable(data.milestones);
  }
  if (visitsContainer) {
    visitsContainer.innerHTML = createCtmsVisitTable(data.visits);
  }
  if (workloadContainer) {
    const rows = (data.allocations || [])
      .map(
        (a) => `
      <tr>
        <td><strong>${a.cra}</strong></td>
        <td>${a.activeAllocations}</td>
        <td>${(a.sites || []).join(", ")}</td>
        <td>${(a.studies || []).join(", ")}</td>
      </tr>
    `
      )
      .join("");
    workloadContainer.innerHTML = `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">CRA</th>
      <th scope="col">Active Allocations</th>
      <th scope="col">Allocated Sites</th>
      <th scope="col">Allocated Studies</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
    `.trim();
  }
  if (recruitmentContainer) {
    const rows = (data.recruitment || [])
      .map(
        (r) => `
      <tr>
        <td><strong>${r.siteId}</strong></td>
        <td>${r.screened}</td>
        <td>${r.enrolled}</td>
        <td>${r.target}</td>
        <td>${Math.round((r.enrolled / r.target) * 100)}%</td>
      </tr>
    `
      )
      .join("");
    recruitmentContainer.innerHTML = `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Site ID</th>
      <th scope="col">Screened</th>
      <th scope="col">Enrolled</th>
      <th scope="col">Target</th>
      <th scope="col">Progress</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
    `.trim();
  }
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

    // --- 1.5 CTMS MOCK DATA ---
    const defaultCtmsData = {
      milestones: [
        {
          id: "M1",
          type: "SITE_SELECTION",
          plannedDate: "2026-08-01",
          actualDate: "2026-08-01",
          status: "ACHIEVED",
        },
        {
          id: "M2",
          type: "INITIATION_VISIT",
          plannedDate: "2026-08-10",
          actualDate: "2026-08-12",
          status: "ACHIEVED",
        },
        {
          id: "M3",
          type: "SITE_ACTIVATION",
          plannedDate: "2026-08-15",
          actualDate: "",
          status: "PLANNED",
        },
        {
          id: "M4",
          type: "FIRST_SUBJECT_ENROLLED",
          plannedDate: "2026-09-01",
          actualDate: "",
          status: "PLANNED",
        },
      ],
      visits: [
        {
          id: "V1",
          type: "SIV",
          scheduledDate: "2026-08-10",
          actualDate: "2026-08-12",
          status: "SIGNED_OFF",
          cra: "cra_fderuiter",
        },
        {
          id: "V2",
          type: "IMV",
          scheduledDate: "2026-08-25",
          actualDate: "",
          status: "SCHEDULED",
          cra: "cra_fderuiter",
        },
      ],
      allocations: [
        {
          cra: "cra_fderuiter",
          activeAllocations: 3,
          sites: ["Site-01", "Site-02", "Site-09"],
          studies: ["STUDY-01", "STUDY-02"],
        },
        {
          cra: "cra_alice",
          activeAllocations: 1,
          sites: ["Site-03"],
          studies: ["STUDY-01"],
        },
      ],
      recruitment: [
        { siteId: "Site-01", screened: 15, enrolled: 8, target: 20 },
        { siteId: "Site-02", screened: 8, enrolled: 4, target: 15 },
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
    let currentCtmsData = JSON.parse(JSON.stringify(defaultCtmsData));
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
    const tabCtms = document.getElementById("tab-btn-ctms");
    const tabRules = document.getElementById("tab-btn-rules");
    const tabAudit = document.getElementById("tab-btn-audit");

    const secMdr = document.getElementById("section-mdr");
    const secEcrf = document.getElementById("section-ecrf");
    const secCtms = document.getElementById("section-ctms");
    const secRules = document.getElementById("section-rules");
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
      [tabMdr, tabEcrf, tabCtms, tabRules, tabAudit].forEach((t) => {
        if (t) t.classList.remove("active");
      });
      [secMdr, secEcrf, secCtms, secRules, secAudit].forEach((s) => {
        if (s) s.classList.remove("active");
      });

      if (activeTab) activeTab.classList.add("active");
      if (activeSec) activeSec.classList.add("active");
    }

    if (tabMdr && secMdr) {
      tabMdr.addEventListener("click", () => switchTab(tabMdr, secMdr));
    }
    if (tabEcrf && secEcrf) {
      tabEcrf.addEventListener("click", () => switchTab(tabEcrf, secEcrf));
    }
    if (tabCtms && secCtms) {
      tabCtms.addEventListener("click", () => switchTab(tabCtms, secCtms));
    }
    if (tabRules && secRules) {
      tabRules.addEventListener("click", () => switchTab(tabRules, secRules));
    }
    if (tabAudit && secAudit) {
      tabAudit.addEventListener("click", () => switchTab(tabAudit, secAudit));
    }

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

    // --- 9.5 CTMS ACTIONS & EVENT LISTENERS ---
    function renderWebCtms() {
      renderCtms(currentCtmsData);
    }

    const btnAchieveMilestone = document.getElementById(
      "btn-achieve-milestone"
    );
    if (btnAchieveMilestone) {
      btnAchieveMilestone.addEventListener("click", () => {
        // Find first PLANNED milestone and achieve it
        const nextM = currentCtmsData.milestones.find(
          (m) => m.status === "PLANNED"
        );
        if (nextM) {
          nextM.status = "ACHIEVED";
          nextM.actualDate = new Date().toISOString().slice(0, 10);
          renderWebCtms();
          addLedgerBlock(
            "CTMS_MILESTONE_ACHIEVED",
            {
              milestoneType: nextM.type,
              status: nextM.status,
              actualDate: nextM.actualDate,
            },
            `Site operational milestone '${nextM.type}' achieved and verified.`
          );
        } else {
          alert("All milestones have already been achieved!");
        }
      });
    }

    const btnScheduleVisit = document.getElementById("btn-schedule-visit");
    if (btnScheduleVisit) {
      btnScheduleVisit.addEventListener("click", () => {
        const newVisit = {
          id: "V" + (currentCtmsData.visits.length + 1),
          type: "IMV",
          scheduledDate: new Date(Date.now() + 5 * 24 * 3600 * 1000)
            .toISOString()
            .slice(0, 10),
          actualDate: "",
          status: "SCHEDULED",
          cra: "cra_fderuiter",
        };
        currentCtmsData.visits.push(newVisit);
        renderWebCtms();
        addLedgerBlock(
          "CTMS_VISIT_SCHEDULED",
          {
            visitId: newVisit.id,
            type: newVisit.type,
            scheduledDate: newVisit.scheduledDate,
          },
          `New Monitoring Visit scheduled for ${newVisit.scheduledDate}. Confirmation letter issued.`
        );
      });
    }

    const btnCompleteVisit = document.getElementById("btn-complete-visit");
    if (btnCompleteVisit) {
      btnCompleteVisit.addEventListener("click", () => {
        const scheduledVisit = currentCtmsData.visits.find(
          (v) => v.status === "SCHEDULED"
        );
        if (scheduledVisit) {
          scheduledVisit.status = "SIGNED_OFF";
          scheduledVisit.actualDate = new Date().toISOString().slice(0, 10);
          renderWebCtms();
          addLedgerBlock(
            "CTMS_VISIT_COMPLETED",
            {
              visitId: scheduledVisit.id,
              type: scheduledVisit.type,
              actualDate: scheduledVisit.actualDate,
            },
            `Monitoring Visit '${scheduledVisit.id}' completed and signed off. Follow-up letter issued.`
          );
        } else {
          alert("No scheduled visits to complete!");
        }
      });
    }

    const btnReallocateCra = document.getElementById("btn-reallocate-cra");
    if (btnReallocateCra) {
      btnReallocateCra.addEventListener("click", () => {
        const craAlice = currentCtmsData.allocations.find(
          (a) => a.cra === "cra_alice"
        );
        if (craAlice) {
          if (craAlice.activeAllocations === 1) {
            craAlice.activeAllocations = 2;
            craAlice.sites.push("Site-04");
          } else {
            craAlice.activeAllocations = 1;
            craAlice.sites = ["Site-03"];
          }
          renderWebCtms();
          addLedgerBlock(
            "CTMS_CRA_REALLOCATION",
            {
              cra: craAlice.cra,
              activeAllocations: craAlice.activeAllocations,
              sites: craAlice.sites,
            },
            `CRA allocations updated to balance workload.`
          );
        }
      });
    }

    const btnUpdateRecruitment = document.getElementById(
      "btn-update-recruitment"
    );
    if (btnUpdateRecruitment) {
      btnUpdateRecruitment.addEventListener("click", () => {
        const site1 = currentCtmsData.recruitment.find(
          (r) => r.siteId === "Site-01"
        );
        if (site1) {
          site1.screened += 2;
          site1.enrolled += 1;
          renderWebCtms();
          addLedgerBlock(
            "CTMS_RECRUITMENT_UPDATE",
            {
              siteId: site1.siteId,
              screened: site1.screened,
              enrolled: site1.enrolled,
            },
            `Logged enrollment of new subject at Site-01.`
          );
        }
      });
    }

    // --- 9.6 RULES DESIGNER SYSTEM ---
    let activeRules = [];
    let editingRuleId = null;
    let currentConditionRowsCount = 0;

    const mockStudyForms = [
      { id: "form_dm", name: "Demographics" },
      { id: "form_vs", name: "Vital Signs" },
      { id: "form_ae", name: "Adverse Events" },
    ];

    const mockStudyFields = [
      { id: "brthdt", name: "Date of Birth (brthdt)", formId: "form_dm" },
      { id: "sex", name: "Sex at Birth (sex)", formId: "form_dm" },
      { id: "vssbp", name: "Systolic BP (vssbp)", formId: "form_vs" },
      { id: "vsdpb", name: "Diastolic BP (vsdpb)", formId: "form_vs" },
      { id: "pulse", name: "Pulse Rate (pulse)", formId: "form_vs" },
      { id: "aeterm", name: "Adverse Event Term (aeterm)", formId: "form_ae" },
    ];

    const rulesListContainer = document.getElementById("rules-list-container");
    const rulesEditorWorkspace = document.getElementById(
      "rules-editor-workspace"
    );
    const rulesEditorContainer = document.getElementById(
      "rules-editor-container"
    );
    const btnNewRule = document.getElementById("btn-new-rule");

    function renderRulesList() {
      if (!rulesListContainer) return;
      if (activeRules.length === 0) {
        rulesListContainer.innerHTML = `<div style="color: #64748b; font-style: italic; padding: 12px 0;">No active rules configured. Click "Create New Rule" to get started.</div>`;
        return;
      }

      const listHTML = activeRules
        .map(
          (rule) => `
        <div class="rule-card" style="border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 8px; background-color: #f8fafc;">
          <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 4px;">
            <strong style="color: var(--primary); font-size: 0.9rem;">${rule.id} (${rule.type})</strong>
            <div style="display: flex; gap: 4px;">
              <button class="btn btn-secondary btn-edit-rule" data-id="${rule.id}" style="padding: 2px 6px; font-size: 0.75rem; cursor: pointer;">Edit</button>
              <button class="btn btn-error btn-delete-rule" data-id="${rule.id}" style="padding: 2px 6px; font-size: 0.75rem; background-color: var(--error); color: white; border: none; border-radius: 4px; cursor: pointer;">Delete</button>
            </div>
          </div>
          <div style="font-size: 0.8rem; color: #475569;">
            ${rule.type === "skip_logic" ? `<strong>Action:</strong> ${rule.action} field ${rule.target_field}` : ""}
            ${rule.type === "constraint" ? `<strong>Target:</strong> ${rule.target_field} <br/> <strong>Msg:</strong> ${rule.query_message}` : ""}
            ${rule.type === "cross_form_check" ? `<strong>Msg:</strong> ${rule.query_message}` : ""}
          </div>
          <div style="font-size: 0.75rem; color: #64748b; margin-top: 4px; word-break: break-all;">
            <strong>XPath:</strong> ${rule.compiled_xpath || "(Not compiled)"}
          </div>
        </div>
      `
        )
        .join("");

      rulesListContainer.innerHTML = listHTML;

      // Attach button event listeners
      rulesListContainer.querySelectorAll(".btn-edit-rule").forEach((btn) => {
        btn.addEventListener("click", () => {
          const ruleId = btn.getAttribute("data-id");
          const rule = activeRules.find((r) => r.id === ruleId);
          if (rule) openRuleEditor(rule);
        });
      });

      rulesListContainer.querySelectorAll(".btn-delete-rule").forEach((btn) => {
        btn.addEventListener("click", () => {
          const ruleId = btn.getAttribute("data-id");
          promptDeleteRule(ruleId);
        });
      });
    }

    function openRuleEditor(rule = null) {
      if (!rulesEditorContainer || !rulesEditorWorkspace) return;

      editingRuleId = rule ? rule.id : null;
      rulesEditorWorkspace.style.display = "block";
      rulesEditorContainer.innerHTML = createRuleEditorContainer(
        mockStudyForms,
        mockStudyFields
      );

      // DOM Elements inside editor
      const selectRuleType = document.getElementById("rule-type");
      const selectTargetField = document.getElementById("rule-target-field");
      const selectAction = document.getElementById("rule-action");
      const selectTargetForm = document.getElementById("rule-target-form");
      const inputMessage = document.getElementById("rule-message");
      const conditionsList = document.getElementById("conditions-list");
      const btnAddCondition = document.getElementById("btn-add-condition");
      const btnCancelRule = document.getElementById("btn-cancel-rule");
      const btnSaveRule = document.getElementById("btn-save-rule");

      currentConditionRowsCount = 0;

      // Toggle container visibility based on Rule Type
      function updateTypeVisibility() {
        const type = selectRuleType.value;
        const targetContainer = document.querySelector(
          ".rule-target-container"
        );
        const actionContainer = document.querySelector(
          ".rule-action-container"
        );
        const targetFormContainer = document.querySelector(
          ".rule-target-form-container"
        );
        const messageContainer = document.querySelector(
          ".rule-message-container"
        );

        if (type === "skip_logic") {
          targetContainer.style.display = "block";
          actionContainer.style.display = "block";
          targetFormContainer.style.display = "block";
          messageContainer.style.display = "none";
        } else if (type === "constraint") {
          targetContainer.style.display = "block";
          actionContainer.style.display = "none";
          targetFormContainer.style.display = "none";
          messageContainer.style.display = "block";
        } else if (type === "cross_form_check") {
          targetContainer.style.display = "none";
          actionContainer.style.display = "none";
          targetFormContainer.style.display = "none";
          messageContainer.style.display = "block";
        }
        triggerPreview();
      }

      selectRuleType.addEventListener("change", updateTypeVisibility);

      function addConditionRowToDOM(selectedValues = {}) {
        const index = currentConditionRowsCount++;
        const rowHTML = createConditionRow(
          index,
          mockStudyForms,
          mockStudyFields,
          selectedValues
        );

        // Wrap in a div element so we can easily remove/manage it
        const div = document.createElement("div");
        div.innerHTML = rowHTML;
        conditionsList.appendChild(div.firstElementChild);

        // Attach listeners to newly added elements
        const row = document.getElementById(`condition-row-${index}`);
        row
          .querySelector(".btn-remove-condition")
          .addEventListener("click", () => {
            row.remove();
            triggerPreview();
          });

        row
          .querySelector(".cond-operator-select")
          .addEventListener("change", (e) => {
            const op = e.target.value;
            const rightTypeContainer = row.querySelector(
              ".cond-right-type-container"
            );
            const rightValContainer = row.querySelector(
              ".cond-right-value-container"
            );
            const rightFieldContainer = row.querySelector(
              ".cond-right-field-container"
            );

            if (op === "is_empty" || op === "is_not_empty") {
              rightTypeContainer.style.display = "none";
              rightValContainer.style.display = "none";
              rightFieldContainer.style.display = "none";
            } else {
              rightTypeContainer.style.display = "block";
              const rightType = row.querySelector(
                ".cond-right-type-select"
              ).value;
              if (rightType === "constant") {
                rightValContainer.style.display = "block";
                rightFieldContainer.style.display = "none";
              } else {
                rightValContainer.style.display = "none";
                rightFieldContainer.style.display = "block";
              }
            }
            triggerPreview();
          });

        row
          .querySelector(".cond-right-type-select")
          .addEventListener("change", (e) => {
            const type = e.target.value;
            const rightValContainer = row.querySelector(
              ".cond-right-value-container"
            );
            const rightFieldContainer = row.querySelector(
              ".cond-right-field-container"
            );

            if (type === "constant") {
              rightValContainer.style.display = "block";
              rightFieldContainer.style.display = "none";
            } else {
              rightValContainer.style.display = "none";
              rightFieldContainer.style.display = "block";
            }
            triggerPreview();
          });

        row.querySelectorAll("select, input").forEach((el) => {
          el.addEventListener("change", triggerPreview);
        });

        triggerPreview();
      }

      btnAddCondition.addEventListener("click", () => {
        addConditionRowToDOM();
      });

      // Populate if editing
      if (rule) {
        selectRuleType.value = rule.type;
        selectTargetField.value = rule.target_field || "";
        selectAction.value = rule.action || "show";
        selectTargetForm.value = rule.target_form || "";
        inputMessage.value = rule.query_message || "";

        // Reconstruct condition tree nodes into rows
        if (rule.condition) {
          const node = rule.condition;
          if (node.type === "logical" && node.operands) {
            document.getElementById("rule-logical-operator").value =
              node.operator || "and";
            node.operands.forEach((operand) => {
              addConditionRowToDOM(deserializeNodeToRow(operand));
            });
          } else {
            addConditionRowToDOM(deserializeNodeToRow(node));
          }
        }
      } else {
        // Default with 1 empty condition row
        addConditionRowToDOM();
      }

      updateTypeVisibility();

      selectTargetField.addEventListener("change", triggerPreview);
      selectAction.addEventListener("change", triggerPreview);
      selectTargetForm.addEventListener("change", triggerPreview);
      inputMessage.addEventListener("input", triggerPreview);
      document
        .getElementById("rule-logical-operator")
        .addEventListener("change", triggerPreview);

      btnCancelRule.addEventListener("click", () => {
        rulesEditorWorkspace.style.display = "none";
        editingRuleId = null;
      });

      btnSaveRule.addEventListener("click", () => {
        promptSaveRule();
      });
    }

    function deserializeNodeToRow(node) {
      if (node.type === "comparison") {
        const left = node.operands[0];
        const right = node.operands[1];
        return {
          formId: left.field_ref ? left.field_ref.form_id : "",
          fieldId: left.field_ref ? left.field_ref.field_id : "",
          operator: node.operator,
          rightType: right.type === "field_ref" ? "field_ref" : "constant",
          rightValue: right.type === "constant" ? right.value : "",
          rightFieldId:
            right.type === "field_ref" ? right.field_ref.field_id : "",
        };
      } else if (node.type === "function") {
        const left = node.operands[0];
        return {
          formId: left.field_ref ? left.field_ref.form_id : "",
          fieldId: left.field_ref ? left.field_ref.field_id : "",
          operator: node.operator,
        };
      }
      return {};
    }

    function serializeRowsToExpressionTree() {
      const groupOp = document.getElementById("rule-logical-operator").value;
      const rows = document.querySelectorAll(".condition-row");

      const operands = [];
      rows.forEach((row) => {
        const index = row.getAttribute("data-index");
        const formId = document.getElementById(`cond-form-${index}`).value;
        const fieldId = document.getElementById(`cond-field-${index}`).value;
        const operator = document.getElementById(
          `cond-operator-${index}`
        ).value;

        if (!fieldId) return; // Skip incomplete

        const leftRef = {
          type: "field_ref",
          field_ref: {
            field_id: fieldId,
            form_id: formId || null,
          },
        };

        if (operator === "is_empty" || operator === "is_not_empty") {
          operands.push({
            type: "function",
            operator: operator,
            operands: [leftRef],
          });
        } else {
          const rightType = document.getElementById(
            `cond-right-type-${index}`
          ).value;
          let rightNode;

          if (rightType === "constant") {
            const rawVal = document.getElementById(
              `cond-right-value-${index}`
            ).value;
            // Guess type or default to string
            let val = rawVal;
            if (rawVal === "true") val = true;
            else if (rawVal === "false") val = false;
            else if (!isNaN(parseFloat(rawVal))) val = parseFloat(rawVal);

            rightNode = {
              type: "constant",
              value: val,
            };
          } else {
            const rightFieldId = document.getElementById(
              `cond-right-field-${index}`
            ).value;
            rightNode = {
              type: "field_ref",
              field_ref: {
                field_id: rightFieldId || "",
              },
            };
          }

          operands.push({
            type: "comparison",
            operator: operator,
            operands: [leftRef, rightNode],
          });
        }
      });

      if (operands.length === 0) {
        return { type: "constant", value: true };
      }

      if (operands.length === 1) {
        return operands[0];
      }

      return {
        type: "logical",
        operator: groupOp,
        operands: operands,
      };
    }

    function triggerPreview() {
      const type = document.getElementById("rule-type").value;
      const targetField = document.getElementById("rule-target-field").value;
      const action = document.getElementById("rule-action").value;
      const targetForm = document.getElementById("rule-target-form").value;
      const queryMessage = document.getElementById("rule-message").value;
      const condition = serializeRowsToExpressionTree();

      const payload = {
        type,
        condition,
        target_field: targetField || null,
        target_form: targetForm || null,
        action: type === "skip_logic" ? action : null,
        query_message: type !== "skip_logic" ? queryMessage : null,
      };

      // Inline local preview compilation
      const previewRes = compileMockPreview(payload);

      const xpathDiv = document.getElementById("rule-xpath-preview");
      const validationDiv = document.getElementById("rule-validation-failures");
      const cyclesDiv = document.getElementById("rule-circular-cycles");

      if (xpathDiv)
        xpathDiv.innerText = previewRes.xpath || "(No conditions added)";
      if (validationDiv)
        validationDiv.innerText = previewRes.failures.join(", ");
      if (cyclesDiv)
        cyclesDiv.innerText = previewRes.circular_cycles.join(", ");
    }

    function compileMockPreview(payload) {
      // Offline fallback compilation engine for instant local UI feedback
      const failures = [];
      const circular_cycles = [];
      let xpath;

      // 1. Compile XPath representation
      function compileNode(node) {
        if (!node) return "";
        if (node.type === "constant") {
          return typeof node.value === "string"
            ? `'${node.value}'`
            : String(node.value);
        }
        if (node.type === "field_ref") {
          return `/clinical_data/${node.field_ref.form_id ? node.field_ref.form_id + "/" : ""}${node.field_ref.field_id}`;
        }
        if (node.type === "function") {
          const fnName = node.operator === "is_empty" ? "empty" : "not(empty";
          const closing = node.operator === "is_not_empty" ? ")" : "";
          return `${fnName}(${compileNode(node.operands[0])})${closing}`;
        }
        if (node.type === "comparison") {
          return `(${compileNode(node.operands[0])} ${node.operator === "==" ? "=" : node.operator} ${compileNode(node.operands[1])})`;
        }
        if (node.type === "logical") {
          const ops = node.operands
            .map(compileNode)
            .join(` ${node.operator.toUpperCase()} `);
          return `(${ops})`;
        }
        return "";
      }

      xpath = compileNode(payload.condition);

      // 2. Validate fields
      function traverseRefs(node) {
        if (!node) return [];
        if (node.type === "field_ref") return [node.field_ref.field_id];
        let refs = [];
        if (node.operands) {
          node.operands.forEach((op) => {
            refs = refs.concat(traverseRefs(op));
          });
        }
        return refs;
      }

      const referencedFields = traverseRefs(payload.condition);
      referencedFields.forEach((fid) => {
        if (fid && !mockStudyFields.some((f) => f.id === fid)) {
          failures.push(`Unknown field reference: '${fid}'`);
        }
      });

      // 3. Detect Circular loops
      if (payload.type === "skip_logic" && payload.target_field) {
        if (referencedFields.includes(payload.target_field)) {
          circular_cycles.push(
            `Circular dependency: ${payload.target_field} -> ${payload.target_field}`
          );
        }
        // Check cross rules circular loop
        activeRules.forEach((rule) => {
          if (rule.type === "skip_logic" && rule.id !== editingRuleId) {
            const rRefs = traverseRefs(rule.condition);
            if (rule.target_field === payload.target_field) {
              // Same target, potential overlap
            }
            if (
              rRefs.includes(payload.target_field) &&
              referencedFields.includes(rule.target_field)
            ) {
              circular_cycles.push(
                `Circular cycle: ${payload.target_field} -> ${rule.target_field} -> ${payload.target_field}`
              );
            }
          }
        });
      }

      return { xpath, failures, circular_cycles };
    }

    function promptSaveRule() {
      const type = document.getElementById("rule-type").value;
      const targetField = document.getElementById("rule-target-field").value;
      const action = document.getElementById("rule-action").value;
      const targetForm = document.getElementById("rule-target-form").value;
      const queryMessage = document.getElementById("rule-message").value;
      const condition = serializeRowsToExpressionTree();

      // Check validation first
      if (type === "skip_logic" && !targetField) {
        alert("Please select a target field!");
        return;
      }
      if (type === "constraint" && (!targetField || !queryMessage)) {
        alert("Please provide a target field and auto-query message!");
        return;
      }
      if (type === "cross_form_check" && !queryMessage) {
        alert("Please provide an auto-query message!");
        return;
      }

      // We trigger the global Reason modal
      pendingValueChange = {
        isRuleSave: true,
        ruleData: {
          id:
            editingRuleId || `rule_${Math.random().toString(36).substr(2, 9)}`,
          study_id: currentUsdm.studyId,
          type,
          condition,
          target_field: targetField || null,
          target_form: targetForm || null,
          action: type === "skip_logic" ? action : null,
          query_message: type !== "skip_logic" ? queryMessage : null,
          compiled_xpath: compileMockPreview({
            type,
            condition,
            target_field: targetField,
          }).xpath,
        },
      };
      openReasonModal();
    }

    function promptDeleteRule(ruleId) {
      pendingValueChange = {
        isRuleDelete: true,
        ruleId: ruleId,
      };
      openReasonModal();
    }

    async function executeRuleSave(ruleData, changeReason) {
      // 21 CFR Part 11 signed API header authorization
      const userId = "usr_9921a88b2c410";
      const roles = "STUDY_DESIGNER";
      const timestamp = new Date().toISOString();
      const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret

      const signature = await generateGatewaySignature(
        userId,
        roles,
        timestamp,
        "2",
        changeReason,
        secret
      );

      const headers = {
        "X-User-Id": userId,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": changeReason,
      };

      // Store in activeRules
      const existingIdx = activeRules.findIndex((r) => r.id === ruleData.id);
      if (existingIdx > -1) {
        activeRules[existingIdx] = ruleData;
      } else {
        activeRules.push(ruleData);
      }

      renderRulesList();
      if (rulesEditorWorkspace) rulesEditorWorkspace.style.display = "none";
      editingRuleId = null;

      await addLedgerBlock(
        "RULE_SAVE",
        {
          ruleId: ruleData.id,
          type: ruleData.type,
          xpath: ruleData.compiled_xpath,
          headers,
        },
        changeReason
      );

      alert(`Rule successfully compiled and signed save verified!`);
    }

    async function executeRuleDelete(ruleId, changeReason) {
      const userId = "usr_9921a88b2c410";
      const roles = "STUDY_DESIGNER";
      const timestamp = new Date().toISOString();
      const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret

      const signature = await generateGatewaySignature(
        userId,
        roles,
        timestamp,
        "2",
        changeReason,
        secret
      );

      const headers = {
        "X-User-Id": userId,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": changeReason,
      };

      activeRules = activeRules.filter((r) => r.id !== ruleId);
      renderRulesList();
      if (rulesEditorWorkspace) rulesEditorWorkspace.style.display = "none";

      await addLedgerBlock("RULE_DELETE", { ruleId, headers }, changeReason);

      alert("Rule successfully soft-deleted!");
    }

    // Connect Reason for Change save button with Rules logic
    btnSaveChange.addEventListener("click", () => {
      if (pendingValueChange && pendingValueChange.isRuleSave) {
        const selReason = reasonSelect.value;
        const custText = reasonText.value.trim();
        const finalReason =
          selReason === "Other" && custText
            ? custText
            : `${selReason}${custText ? ": " + custText : ""}`;

        executeRuleSave(pendingValueChange.ruleData, finalReason);
        closeReasonModal();
      } else if (pendingValueChange && pendingValueChange.isRuleDelete) {
        const selReason = reasonSelect.value;
        const custText = reasonText.value.trim();
        const finalReason =
          selReason === "Other" && custText
            ? custText
            : `${selReason}${custText ? ": " + custText : ""}`;

        executeRuleDelete(pendingValueChange.ruleId, finalReason);
        closeReasonModal();
      }
    });

    if (btnNewRule) {
      btnNewRule.addEventListener("click", () => {
        openRuleEditor();
      });
    }

    // --- 10. INITIALIZATION BOOTSTRAP ---
    renderMdr();
    renderEcrf();
    renderWebCtms();
    renderRulesList();

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
