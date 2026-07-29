import {
  createClinicalInput,
  createClinicalRadioGrid,
  createCtmsMilestoneTable,
  createCtmsVisitTable,
  createClinicalLookupInput,
  generateGatewaySignature,
  sha256 as sharedSha256,
  validateField as sharedValidateField,
  debounce as sharedDebounce,
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
} from "./src/evaluator.js";

export { evaluateAST, compilerCache, getCompiledExpression };
export { sharedDebounce as debounce };

export function validateField(fieldMeta, val, context = {}) {
  return sharedValidateField(fieldMeta, val, context, evaluateAST);
}

// Re-export sha256 from the shared ui library to prevent duplicate implementations
export { sharedSha256 as sha256 };

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
          plannedDate: "2026-08-01", // deid-ignore
          actualDate: "2026-08-01", // deid-ignore
          status: "ACHIEVED",
        },
        {
          id: "M2",
          type: "INITIATION_VISIT",
          plannedDate: "2026-08-10", // deid-ignore
          actualDate: "2026-08-12", // deid-ignore
          status: "ACHIEVED",
        },
        {
          id: "M3",
          type: "SITE_ACTIVATION",
          plannedDate: "2026-08-15", // deid-ignore
          actualDate: "",
          status: "PLANNED",
        },
        {
          id: "M4",
          type: "FIRST_SUBJECT_ENROLLED",
          plannedDate: "2026-09-01", // deid-ignore
          actualDate: "",
          status: "PLANNED",
        },
      ],
      visits: [
        {
          id: "V1",
          type: "SIV",
          scheduledDate: "2026-08-10", // deid-ignore
          actualDate: "2026-08-12", // deid-ignore
          status: "SIGNED_OFF",
          cra: "cra_fderuiter",
        },
        {
          id: "V2",
          type: "IMV",
          scheduledDate: "2026-08-25", // deid-ignore
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
          value: "1980-05-12", // deid-ignore
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
          ? "0000000000000000000000000000000000000000000000000000000000000000" // deid-ignore
          : ledgerBlocks[index - 1].hash;

      const block = await buildLedgerBlock(
        index,
        timestamp,
        action,
        details,
        reason,
        prevHash
      );

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
    const tabLibrary = document.getElementById("tab-btn-library");
    const secLibrary = document.getElementById("section-library");

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
      [tabMdr, tabEcrf, tabCtms, tabRules, tabAudit, tabLibrary].forEach(
        (t) => {
          if (t) t.classList.remove("active");
        }
      );
      [secMdr, secEcrf, secCtms, secRules, secAudit, secLibrary].forEach(
        (s) => {
          if (s) s.classList.remove("active");
        }
      );

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
    if (tabLibrary && secLibrary) {
      tabLibrary.addEventListener("click", () => {
        switchTab(tabLibrary, secLibrary);
        renderLibrary();
      });
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

    // --- 9.7 GLOBAL LIBRARY MANAGEMENT SYSTEM (VANILLA-JS SANDBOX) ---
    let mockLibraryObjects = [
      {
        id: "lib-form-demographics",
        object_type: "FORM",
        version: "1.0.0",
        status: "PUBLISHED",
        sponsor_id: "SPONSOR-A",
        created_at: "2026-08-01T12:00:00Z",
        created_by: "usr_dm_fderuiter",
        reason_for_change:
          "Initial publication of core Demographics CDASH form template.",
        payload: {
          items: [
            {
              item_id: "brthdt",
              name: "DM.BRTHDT",
              question_text: "Date of Birth",
              data_type: "date",
              required: true,
            },
            {
              item_id: "sex",
              name: "DM.SEX",
              question_text: "Sex at Birth",
              data_type: "choice",
              required: true,
            },
          ],
        },
        history: [
          {
            version: "1.0.0",
            status: "PUBLISHED",
            change_reason:
              "Initial publication of core Demographics CDASH form template.",
            updated_by: "usr_dm_fderuiter",
            updated_at: "2026-08-01T12:00:00Z",
          },
          {
            version: "0.9.0",
            status: "IN_REVIEW",
            change_reason: "Sent for DM review.",
            updated_by: "usr_designer_alice",
            updated_at: "2026-07-28T10:00:00Z",
          },
          {
            version: "0.1.0",
            status: "DRAFT",
            change_reason: "Initial draft.",
            updated_by: "usr_designer_alice",
            updated_at: "2026-07-25T09:00:00Z",
          },
        ],
      },
      {
        id: "lib-elem-sbp",
        object_type: "DATA_ELEMENT",
        version: "1.1.0",
        status: "APPROVED",
        sponsor_id: "SPONSOR-A",
        created_at: "2026-08-02T14:30:00Z",
        created_by: "usr_dm_fderuiter",
        reason_for_change: "Added support for mmHg UCUM code verification.",
        payload: {
          data_type: "numeric",
          allowable_units: ["mm[Hg]"],
          default_unit: "mm[Hg]",
        },
        history: [
          {
            version: "1.1.0",
            status: "APPROVED",
            change_reason: "Added support for mmHg UCUM code verification.",
            updated_by: "usr_dm_fderuiter",
            updated_at: "2026-08-02T14:30:00Z",
          },
          {
            version: "1.0.0",
            status: "DRAFT",
            change_reason: "Initial baseline element definition.",
            updated_by: "usr_designer_alice",
            updated_at: "2026-07-26T15:00:00Z",
          },
        ],
      },
      {
        id: "lib-arm-placebo",
        object_type: "ARM",
        version: "1.0.0",
        status: "DRAFT",
        sponsor_id: "SPONSOR-A",
        created_at: "2026-08-03T11:00:00Z",
        created_by: "usr_designer_alice",
        reason_for_change: "Draft version for Placebo treatment arm metadata.",
        payload: {
          attributes: {
            arm_type: "PLACEBO",
            target_sample_size: 50,
            randomization_ratio: "1:1",
          },
        },
        history: [
          {
            version: "1.0.0",
            status: "DRAFT",
            change_reason: "Draft version for Placebo treatment arm metadata.",
            updated_by: "usr_designer_alice",
            updated_at: "2026-08-03T11:00:00Z",
          },
        ],
      },
      {
        id: "lib-visit-screening",
        object_type: "VISIT",
        version: "1.0.0",
        status: "PUBLISHED",
        sponsor_id: "SPONSOR-A",
        created_at: "2026-08-04T09:15:00Z",
        created_by: "usr_dm_fderuiter",
        reason_for_change:
          "Published standard screening visit metadata config.",
        payload: {
          attributes: {
            visit_type: "SCREENING",
            planned_day: -7,
            window_days: 2,
          },
        },
        history: [
          {
            version: "1.0.0",
            status: "PUBLISHED",
            change_reason:
              "Published standard screening visit metadata config.",
            updated_by: "usr_dm_fderuiter",
            updated_at: "2026-08-04T09:15:00Z",
          },
          {
            version: "0.1.0",
            status: "DRAFT",
            change_reason: "Initial screening visit skeleton.",
            updated_by: "usr_designer_alice",
            updated_at: "2026-08-01T14:00:00Z",
          },
        ],
      },
    ];

    const ALLOWED_LIBRARY_TRANSITIONS = {
      DRAFT: ["IN_REVIEW"],
      IN_REVIEW: ["APPROVED", "REJECTED"],
      APPROVED: ["PUBLISHED"],
      PUBLISHED: ["ARCHIVED"],
      REJECTED: ["DRAFT"],
      ARCHIVED: [],
    };

    const TRANSITION_ROLES_MAP = {
      IN_REVIEW: [
        "sponsor_designer",
        "sponsor_dm",
        "sponsor_admin",
        "sysadmin",
      ],
      APPROVED: ["sponsor_dm", "sponsor_admin", "sysadmin"],
      REJECTED: ["sponsor_dm", "sponsor_admin", "sysadmin"],
      PUBLISHED: ["sponsor_dm", "sponsor_admin", "sysadmin"],
      ARCHIVED: ["sponsor_admin", "sysadmin"],
      DRAFT: ["sponsor_designer", "sponsor_dm", "sponsor_admin", "sysadmin"],
    };

    let selectedLibraryObjectId = null;
    let pendingLibraryAction = null; // { type: 'transition'/'instantiate', id, targetStatus, targetStudyId }

    // API triggers
    async function apiTransitionLibraryObject(
      id,
      targetStatus,
      changeReason,
      role
    ) {
      const userId = "usr_9921a88b2c410";
      const timestamp = new Date().toISOString();
      const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret

      const signature = await generateGatewaySignature(
        userId,
        role,
        timestamp,
        "2",
        changeReason,
        secret
      );
      const headers = {
        "X-User-Id": userId,
        "X-User-Roles": role,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": changeReason,
        "X-Sponsor-Id": "SPONSOR-A",
      };

      const response = await fetch(
        `http://localhost:8000/api/v1/mdr/library/${id}/transition`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json", ...headers },
          body: JSON.stringify({
            status: targetStatus,
            change_reason: changeReason,
          }),
        }
      );
      if (!response.ok) {
        let errData = null;
        try {
          errData = await response.json();
        } catch {
          /* ignore */
        }
        throw new Error(errData?.detail || `API error ${response.status}`);
      }
      return await response.json();
    }

    async function apiInstantiateLibraryObject(
      studyId,
      libraryObjectId,
      changeReason,
      role
    ) {
      const userId = "usr_9921a88b2c410";
      const timestamp = new Date().toISOString();
      const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret

      const signature = await generateGatewaySignature(
        userId,
        role,
        timestamp,
        "2",
        changeReason,
        secret
      );
      const headers = {
        "X-User-Id": userId,
        "X-User-Roles": role,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": changeReason,
        "X-Sponsor-Id": "SPONSOR-A",
      };

      const response = await fetch(
        `http://localhost:8000/api/v1/studies/${studyId}/library-instances`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json", ...headers },
          body: JSON.stringify({ library_object_id: libraryObjectId }),
        }
      );
      if (!response.ok) {
        let errData = null;
        try {
          errData = await response.json();
        } catch {
          /* ignore */
        }
        throw new Error(errData?.detail || `API error ${response.status}`);
      }
      return await response.json();
    }

    // UI render helpers
    function displayLibraryError(msg) {
      const banner = document.getElementById("library-error-banner");
      if (banner) {
        banner.textContent = msg;
        banner.style.display = "block";
        if (banner.scrollIntoView) {
          banner.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
      } else {
        alert(msg);
      }
    }

    function clearLibraryError() {
      const banner = document.getElementById("library-error-banner");
      if (banner) {
        banner.style.display = "none";
        banner.textContent = "";
      }
    }

    function getPayloadHTML(obj) {
      if (!obj || !obj.payload) return "";
      const type = obj.object_type;
      const payload = obj.payload;

      let html = `<div class="library-payload-box" style="background-color: var(--neutral-light); border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-top: 8px;">`;
      html += `<h4 style="font-weight: 700; font-size: 0.85rem; text-transform: uppercase; color: var(--neutral-dark); margin-bottom: 8px;">Payload Definition Schema</h4>`;

      if (type === "FORM") {
        html += `<p style="font-size: 0.85rem; margin-bottom: 6px;"><strong>Questions / Fields (${payload.items?.length || 0}):</strong></p>`;
        html += `<ul style="font-size: 0.85rem; padding-left: 20px; line-height: 1.4;">`;
        (payload.items || []).forEach((item) => {
          html += `<li><code>${item.name}</code> (${item.data_type}${item.required ? ", required" : ""}): <em>"${item.question_text}"</em></li>`;
        });
        html += `</ul>`;
      } else if (type === "DATA_ELEMENT") {
        html += `<p style="font-size: 0.85rem;"><strong>Data Type:</strong> <code>${payload.data_type || "N/A"}</code></p>`;
        html += `<p style="font-size: 0.85rem;"><strong>Allowable Units:</strong> ${(payload.allowable_units || []).map((u) => `<code>${u}</code>`).join(", ") || "None"}</p>`;
        html += `<p style="font-size: 0.85rem;"><strong>Default Unit:</strong> <code>${payload.default_unit || "None"}</code></p>`;
      } else if (type === "ARM") {
        const attr = payload.attributes || {};
        html += `<p style="font-size: 0.85rem;"><strong>Arm Type:</strong> <code>${attr.arm_type || "N/A"}</code></p>`;
        html += `<p style="font-size: 0.85rem;"><strong>Target Sample Size:</strong> <code>${attr.target_sample_size || "N/A"}</code></p>`;
        html += `<p style="font-size: 0.85rem;"><strong>Allocation Ratio:</strong> <code>${attr.randomization_ratio || "N/A"}</code></p>`;
      } else if (type === "VISIT") {
        const attr = payload.attributes || {};
        html += `<p style="font-size: 0.85rem;"><strong>Visit Type:</strong> <code>${attr.visit_type || "N/A"}</code></p>`;
        html += `<p style="font-size: 0.85rem;"><strong>Planned Day:</strong> <code>${attr.planned_day !== undefined ? attr.planned_day : "N/A"}</code></p>`;
        html += `<p style="font-size: 0.85rem;"><strong>Window Days:</strong> <code>±${attr.window_days !== undefined ? attr.window_days : "N/A"} days</code></p>`;
      }

      html += `</div>`;
      return html;
    }

    function getHistoryHTML(obj) {
      if (!obj || !obj.history || obj.history.length === 0) return "";
      let rows = obj.history
        .map((hist) => {
          const badgeClass = `status-${hist.status.toLowerCase().replace(/[^a-z0-9]/g, "-")}`;
          return `
          <tr>
            <td><strong>v${hist.version}</strong></td>
            <td><span class="badge ${badgeClass}">${hist.status}</span></td>
            <td>${hist.updated_by || "system"}</td>
            <td style="font-size: 0.8rem; color: #64748b;">${hist.updated_at ? new Date(hist.updated_at).toLocaleString() : "N/A"}</td>
            <td style="font-size: 0.8rem; max-width: 250px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${hist.change_reason || ""}">
              ${hist.change_reason || "No comment."}
            </td>
          </tr>
        `;
        })
        .join("");

      return `
        <div style="margin-top: 16px;">
          <h4 style="font-weight: 700; font-size: 0.85rem; text-transform: uppercase; color: var(--neutral-dark); margin-bottom: 8px;">Version History & Audit Trail</h4>
          <table class="clinical-visit-matrix" style="font-size: 0.85rem; border: 1px solid var(--border);">
            <thead>
              <tr style="background-color: var(--neutral-light);">
                <th>Ver</th>
                <th>Status</th>
                <th>Author</th>
                <th>Timestamp</th>
                <th>Change Reason</th>
              </tr>
            </thead>
            <tbody>
              ${rows}
            </tbody>
          </table>
        </div>
      `;
    }

    function getGovernanceHTML(obj) {
      const allowedNext = ALLOWED_LIBRARY_TRANSITIONS[obj.status] || [];
      let html = `<div style="display: flex; flex-direction: column; gap: 12px; border-top: 1px solid var(--border); padding-top: 16px; margin-top: 16px;">`;
      html += `<h4 style="font-weight: 700; font-size: 0.85rem; text-transform: uppercase; color: var(--neutral-dark);">Governance Actions</h4>`;

      html += `<div style="display: flex; gap: 8px; flex-wrap: wrap;">`;
      if (allowedNext.length === 0) {
        html += `<span style="font-size: 0.85rem; color: #64748b; font-style: italic;">No further status transitions allowed in this state.</span>`;
      } else {
        allowedNext.forEach((nextState) => {
          html += `
            <button class="btn btn-secondary btn-library-transition" data-id="${obj.id}" data-target-status="${nextState}" style="padding: 6px 12px; font-size: 0.8rem;">
              Transition to ${nextState.replace("_", " ")}
            </button>
          `;
        });
      }
      html += `</div>`;

      html += `
        <div style="border-top: 1px dashed var(--border); padding-top: 12px; margin-top: 12px; display: flex; align-items: flex-end; gap: 12px; flex-wrap: wrap;">
          <div class="form-group" style="flex: 1; min-width: 150px; margin-bottom: 0;">
            <label for="library-instantiate-study-select" style="font-size: 0.75rem; font-weight: 700; color: #64748b; text-transform: uppercase;">Target Study ID</label>
            <input type="text" id="library-instantiate-study-select" value="${currentUsdm.studyId}" placeholder="Enter target study..." style="width: 100%; padding: 6px; border: 1px solid var(--border); border-radius: 4px; font-size: 0.85rem;" />
          </div>
          <button class="btn btn-primary" id="btn-library-instantiate" data-id="${obj.id}" style="padding: 8px 16px; font-size: 0.85rem;">
            ⚡ Instantiate into Study
          </button>
        </div>
      `;

      html += `</div>`;
      return html;
    }

    function renderLibrary() {
      const typeFilter =
        document.getElementById("library-filter-type")?.value || "";
      const statusFilter =
        document.getElementById("library-filter-status")?.value || "";

      const container = document.getElementById("library-objects-list");
      if (!container) return;

      const filtered = mockLibraryObjects.filter((obj) => {
        if (typeFilter && obj.object_type !== typeFilter) return false;
        if (statusFilter && obj.status !== statusFilter) return false;
        return true;
      });

      if (filtered.length === 0) {
        container.innerHTML = `<div style="text-align: center; padding: 24px; color: #64748b; font-style: italic;">No matching library objects found.</div>`;
        return;
      }

      container.innerHTML = filtered
        .map((obj) => {
          const isSelected =
            obj.id === selectedLibraryObjectId
              ? "border-color: var(--accent); background-color: var(--accent-bg);"
              : "border-color: var(--border);";
          const badgeClass = `status-${obj.status.toLowerCase().replace(/[^a-z0-9]/g, "-")}`;

          return `
          <div class="library-item-card" data-id="${obj.id}" style="border: 2px solid; border-radius: 8px; padding: 12px; cursor: pointer; transition: all 0.2s; ${isSelected}">
            <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 6px;">
              <strong style="color: var(--primary); font-size: 0.95rem;">${obj.id}</strong>
              <span class="badge ${badgeClass}">${obj.status}</span>
            </div>
            <div style="display: flex; justify-content: space-between; font-size: 0.8rem; color: #64748b;">
              <span>Type: <strong>${obj.object_type}</strong></span>
              <span>Version: <strong>v${obj.version}</strong></span>
            </div>
          </div>
        `;
        })
        .join("");

      container.querySelectorAll(".library-item-card").forEach((card) => {
        card.addEventListener("click", () => {
          selectedLibraryObjectId = card.getAttribute("data-id");
          renderLibrary();
          renderLibraryDetails();
        });
      });

      const typeSel = document.getElementById("library-filter-type");
      if (typeSel && !typeSel.hasAttribute("data-listener-bound")) {
        typeSel.setAttribute("data-listener-bound", "true");
        typeSel.addEventListener("change", () => {
          renderLibrary();
        });
      }
      const statusSel = document.getElementById("library-filter-status");
      if (statusSel && !statusSel.hasAttribute("data-listener-bound")) {
        statusSel.setAttribute("data-listener-bound", "true");
        statusSel.addEventListener("change", () => {
          renderLibrary();
        });
      }
    }

    function renderLibraryDetails() {
      const container = document.getElementById("library-details-content");
      if (!container) return;

      if (!selectedLibraryObjectId) {
        container.innerHTML = `<p style="color: #64748b; font-style: italic;">Select a library object from the catalog to inspect attributes, view audit trail, transition its lifecycle, or instantiate it into the active study version.</p>`;
        return;
      }

      const obj = mockLibraryObjects.find(
        (o) => o.id === selectedLibraryObjectId
      );
      if (!obj) {
        container.innerHTML = `<p style="color: var(--error); font-style: italic;">Selected object not found.</p>`;
        return;
      }

      const badgeClass = `status-${obj.status.toLowerCase().replace(/[^a-z0-9]/g, "-")}`;

      let html = `
        <div style="display: flex; flex-direction: column; gap: 12px;">
          <div style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: 8px;">
            <h3 style="font-weight: 700; font-size: 1.1rem; color: var(--primary);">${obj.id}</h3>
            <span class="badge ${badgeClass}" style="font-size: 0.8rem; padding: 4px 10px;">${obj.status}</span>
          </div>

          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 8px 16px; font-size: 0.85rem;">
            <div><span class="ledger-lbl">Object Type:</span> <strong style="color: var(--accent);">${obj.object_type}</strong></div>
            <div><span class="ledger-lbl">Version:</span> <strong>v${obj.version}</strong></div>
            <div><span class="ledger-lbl">Sponsor:</span> <code>${obj.sponsor_id}</code></div>
            <div><span class="ledger-lbl">Author:</span> <code>${obj.created_by}</code></div>
            <div style="grid-column: span 2;"><span class="ledger-lbl">Created At:</span> <code>${new Date(obj.created_at).toLocaleString()}</code></div>
          </div>
      `;

      html += getPayloadHTML(obj);
      html += getGovernanceHTML(obj);
      html += getHistoryHTML(obj);
      html += `</div>`;
      container.innerHTML = html;

      container.querySelectorAll(".btn-library-transition").forEach((btn) => {
        btn.addEventListener("click", () => {
          const targetStatus = btn.getAttribute("data-target-status");
          openLibraryReasonModal({
            type: "transition",
            id: obj.id,
            targetStatus,
          });
        });
      });

      const instBtn = document.getElementById("btn-library-instantiate");
      if (instBtn) {
        instBtn.addEventListener("click", () => {
          const studyInput = document.getElementById(
            "library-instantiate-study-select"
          );
          const targetStudyId = studyInput
            ? studyInput.value.trim()
            : currentUsdm.studyId;

          if (!targetStudyId) {
            displayLibraryError(
              "Validation Failure: Please enter a target Study ID."
            );
            return;
          }

          openLibraryReasonModal({
            type: "instantiate",
            id: obj.id,
            targetStudyId,
          });
        });
      }
    }

    async function handleTransitionLibraryConfirm(
      id,
      targetStatus,
      reason,
      role
    ) {
      const allowedRoles = TRANSITION_ROLES_MAP[targetStatus] || [];
      if (!allowedRoles.includes(role)) {
        displayLibraryError(
          `Authorization Failure: Role '${role}' is not authorized to transition object to '${targetStatus}'. Allowed roles: ${allowedRoles.join(", ")}`
        );
        return;
      }

      try {
        await apiTransitionLibraryObject(id, targetStatus, reason, role);
      } catch (err) {
        console.warn(
          "API transition failed, continuing with sandbox-offline mock fallback:",
          err.message
        );
      }

      const obj = mockLibraryObjects.find((o) => o.id === id);
      if (obj) {
        const priorStatus = obj.status;
        obj.status = targetStatus;

        let currentVer = obj.version.split(".").map(Number);
        if (targetStatus === "PUBLISHED") {
          currentVer[0]++;
          currentVer[1] = 0;
          currentVer[2] = 0;
        } else if (targetStatus === "APPROVED") {
          currentVer[1]++;
          currentVer[2] = 0;
        } else {
          currentVer[2]++;
        }
        const nextVer = currentVer.join(".");
        obj.version = nextVer;

        obj.history.unshift({
          version: nextVer,
          status: targetStatus,
          change_reason: reason,
          updated_by: `usr_${role}_signed`,
          updated_at: new Date().toISOString(),
        });

        clearLibraryError();
        renderLibrary();
        renderLibraryDetails();

        await addLedgerBlock(
          "LIBRARY_TRANSITION",
          {
            objectId: id,
            priorStatus,
            targetStatus,
            newVersion: nextVer,
            reason,
            roleSigned: role,
          },
          reason
        );
        alert(`Successfully transitioned ${id} to ${targetStatus}!`);
      }
    }

    async function handleInstantiateLibraryConfirm(
      id,
      targetStudyId,
      reason,
      role
    ) {
      try {
        await apiInstantiateLibraryObject(targetStudyId, id, reason, role);
      } catch (err) {
        console.warn(
          "API instantiation failed, continuing with sandbox-offline mock fallback:",
          err.message
        );
      }

      const obj = mockLibraryObjects.find((o) => o.id === id);
      if (!obj) {
        displayLibraryError(`Error: Object with ID ${id} not found.`);
        return;
      }

      if (targetStudyId !== currentUsdm.studyId) {
        displayLibraryError(
          `Validation Error: Target study '${targetStudyId}' does not match active sandbox study '${currentUsdm.studyId}'.`
        );
        return;
      }

      const type = obj.object_type;
      const payload = obj.payload;

      if (type === "FORM") {
        if (!currentUsdm.forms) currentUsdm.forms = [];
        if (currentUsdm.forms.some((f) => f.name === obj.id)) {
          displayLibraryError(
            `Governance Error: Form '${obj.id}' is already instantiated in study '${targetStudyId}'.`
          );
          return;
        }
        currentUsdm.forms.push({
          name: obj.id,
          statuses: currentUsdm.visits
            ? currentUsdm.visits.map(() => "Pending")
            : ["Pending"],
        });
      } else if (type === "ARM") {
        if (!currentUsdm.arms) currentUsdm.arms = [];
        if (currentUsdm.arms.some((a) => a.arm_id === obj.id)) {
          displayLibraryError(
            `Governance Error: Arm '${obj.id}' is already instantiated in study '${targetStudyId}'.`
          );
          return;
        }
        currentUsdm.arms.push({
          arm_id: obj.id,
          arm_name: `Arm: ${obj.id} (${payload.attributes?.arm_type || "TREATMENT"})`,
        });
      } else if (type === "VISIT") {
        if (!currentUsdm.encounters) currentUsdm.encounters = [];
        if (currentUsdm.encounters.some((e) => e.encounter_id === obj.id)) {
          displayLibraryError(
            `Governance Error: Visit/Encounter '${obj.id}' is already instantiated in study '${targetStudyId}'.`
          );
          return;
        }
        currentUsdm.encounters.push({
          encounter_id: obj.id,
          encounter_name: `Visit ${obj.id}`,
          epoch_id:
            currentUsdm.epochs && currentUsdm.epochs[0]
              ? currentUsdm.epochs[0].epoch_id
              : "EP-SCR",
          sequence: currentUsdm.encounters.length + 1,
        });

        if (currentUsdm.visits) {
          currentUsdm.visits.push(`Visit ${obj.id}`);
          (currentUsdm.forms || []).forEach((f) => {
            f.statuses.push("Pending");
          });
        }
      }

      renderMdr();
      clearLibraryError();
      renderLibrary();
      renderLibraryDetails();

      await addLedgerBlock(
        "LIBRARY_INSTANTIATE",
        {
          objectId: id,
          objectType: type,
          targetStudyId,
          version: obj.version,
          reason,
          roleSigned: role,
        },
        reason
      );

      alert(
        `Successfully instantiated library object '${id}' into study '${targetStudyId}'!`
      );
    }

    const libReasonModal = document.getElementById("library-reason-modal");
    const libReasonText = document.getElementById("library-change-reason");
    const libUserRole = document.getElementById("library-user-role");

    function openLibraryReasonModal(action) {
      pendingLibraryAction = action;
      if (libReasonText) libReasonText.value = "";
      if (libUserRole) libUserRole.value = "sponsor_dm";

      const modalTitle = document.getElementById("library-reason-modal-title");
      if (modalTitle) {
        if (action.type === "transition") {
          modalTitle.innerText = `Sign governed transition to ${action.targetStatus}`;
        } else {
          modalTitle.innerText = `Sign instantiation into study ${action.targetStudyId}`;
        }
      }

      if (libReasonModal) libReasonModal.style.display = "flex";
    }

    function closeLibraryReasonModal() {
      if (libReasonModal) libReasonModal.style.display = "none";
      pendingLibraryAction = null;
    }

    const btnLibCancel = document.getElementById("btn-library-cancel-action");
    if (btnLibCancel) {
      btnLibCancel.addEventListener("click", () => {
        closeLibraryReasonModal();
      });
    }

    const btnLibConfirm = document.getElementById("btn-library-confirm-action");
    if (btnLibConfirm) {
      btnLibConfirm.addEventListener("click", () => {
        const reason = libReasonText ? libReasonText.value.trim() : "";
        const role = libUserRole ? libUserRole.value : "sponsor_dm";

        if (!reason) {
          alert(
            "A valid justification / Change Reason is required under 21 CFR Part 11!"
          );
          return;
        }

        if (!pendingLibraryAction) return;

        const action = pendingLibraryAction;
        closeLibraryReasonModal();

        if (action.type === "transition") {
          handleTransitionLibraryConfirm(
            action.id,
            action.targetStatus,
            reason,
            role
          );
        } else if (action.type === "instantiate") {
          handleInstantiateLibraryConfirm(
            action.id,
            action.targetStudyId,
            reason,
            role
          );
        }
      });
    }

    // Export sandbox hooks to window for unified testability
    if (typeof window !== "undefined") {
      window.GlobalLibrarySandbox = {
        mockLibraryObjects,
        ALLOWED_LIBRARY_TRANSITIONS,
        TRANSITION_ROLES_MAP,
        renderLibrary,
        renderLibraryDetails,
        handleTransitionLibraryConfirm,
        handleInstantiateLibraryConfirm,
        displayLibraryError,
        clearLibraryError,
        getSelectedLibraryObjectId: () => selectedLibraryObjectId,
        setSelectedLibraryObjectId: (id) => {
          selectedLibraryObjectId = id;
        },
      };
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
          "IEC 62304", // deid-ignore
          "ISO 14155:2020", // deid-ignore
        ],
      },
      "System boot and cryptographic ledger initialized successfully."
    );
  });
}

export function createClinicalInput(
  id,
  label,
  value = "",
  query = null,
  gridSpan = 12,
  attributes = {}
) {
  const extraAttrs = Object.entries(attributes)
    .map(([k, v]) => `${k}="${v}"`)
    .join(" ");

  const queryFlagHTML = createClinicalQueryFlag(id, query);
  const queryPanelHTML = createQueryPanel(id, query);

  return `
<div class="clinical-input grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}" ${extraAttrs}>
  <label for="${id}">${label}</label>
  <div class="input-wrapper">
    <input type="text" id="${id}" name="${id}" value="${value}" />
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
      <div class="radio-option">
        <input type="radio" id="${optionId}" name="${id}" value="${optVal}"${isChecked} />
        <label for="${optionId}">${optLabel}</label>
      </div>
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

export function createClinicalVisitMatrix(matrixData) {
  if (!matrixData) {
    return `<div class="clinical-visit-matrix-error">Invalid visit matrix data.</div>`;
  }

  if (matrixData.rows || matrixData.encounters || matrixData.epochs) {
    return createClinicalSoAMatrix(matrixData);
  }

  if (!matrixData.visits || !matrixData.forms) {
    return `<div class="clinical-visit-matrix-error">Invalid visit matrix data.</div>`;
  }

  const visitsHeaderHTML = matrixData.visits
    .map((visit) => `<th scope="col">${visit}</th>`)
    .join("");

  const rowsHTML = matrixData.forms
    .map((form) => {
      const cellsHTML = form.statuses
        .map((status) => {
          const statusClass = `status-${status.toLowerCase().replace(/[^a-z0-9]/g, "-")}`;
          return `<td class="${statusClass}">${status}</td>`;
        })
        .join("");

      return `
    <tr>
      <th scope="row">${form.name}</th>
      ${cellsHTML}
    </tr>
      `.trim();
    })
    .join("\n");

  return `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Form / Procedure</th>
      ${visitsHeaderHTML}
    </tr>
  </thead>
  <tbody>
    ${rowsHTML}
  </tbody>
</table>
  `.trim();
}

export function createClinicalSoAMatrix(soaData) {
  if (!soaData || !soaData.rows) {
    return `<div class="clinical-visit-matrix-error">Invalid SoA matrix data.</div>`;
  }

  const { epochs = [], encounters = [], rows = [], arms = [] } = soaData;

  const epochMap = {};
  epochs.forEach((ep) => {
    epochMap[ep.epoch_id] = ep;
  });

  const armMap = {};
  arms.forEach((arm) => {
    armMap[arm.arm_id] = arm;
  });

  const cols = encounters.map((enc) => {
    const epoch = epochMap[enc.epoch_id] || {
      epoch_id: enc.epoch_id,
      epoch_name: enc.epoch_id,
    };
    const armId = enc.arm_id || epoch.arm_id;
    const arm = armId
      ? armMap[armId] || { arm_id: armId, arm_name: armId }
      : null;

    return {
      arm,
      epoch,
      encounter: enc,
    };
  });

  if (cols.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

  const armGroups = [];
  let currentArmGroup = null;

  cols.forEach((col) => {
    const armId = col.arm ? col.arm.arm_id : "shared";
    const armName = col.arm ? col.arm.arm_name : "Common / Shared";

    if (!currentArmGroup || currentArmGroup.id !== armId) {
      if (currentArmGroup) {
        armGroups.push(currentArmGroup);
      }
      currentArmGroup = {
        id: armId,
        name: armName,
        colspan: 0,
      };
    }
    currentArmGroup.colspan++;
  });
  if (currentArmGroup) {
    armGroups.push(currentArmGroup);
  }

  const epochGroups = [];
  let currentEpochGroup = null;

  cols.forEach((col) => {
    const armId = col.arm ? col.arm.arm_id : "shared";
    const epochId = col.epoch.epoch_id;
    const epochName = col.epoch.epoch_name;
    const groupKey = `${armId}_${epochId}`;

    if (!currentEpochGroup || currentEpochGroup.key !== groupKey) {
      if (currentEpochGroup) {
        epochGroups.push(currentEpochGroup);
      }
      currentEpochGroup = {
        key: groupKey,
        name: epochName,
        colspan: 0,
      };
    }
    currentEpochGroup.colspan++;
  });
  if (currentEpochGroup) {
    epochGroups.push(currentEpochGroup);
  }

  const hasArms = cols.some((c) => c.arm !== null);
  const totalHeaderRows = hasArms ? 3 : 2;

  let headerRowsHTML = "";

  if (hasArms) {
    const armCellsHTML = armGroups
      .map(
        (g) =>
          `<th scope="col" colspan="${g.colspan}" class="grouped-header arm-header">${g.name}</th>`
      )
      .join("");
    headerRowsHTML += `
    <tr>
      <th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>
      ${armCellsHTML}
    </tr>
    `.trim();
  }

  const epochCellsHTML = epochGroups
    .map(
      (g) =>
        `<th scope="col" colspan="${g.colspan}" class="grouped-header epoch-header">${g.name}</th>`
    )
    .join("");

  if (hasArms) {
    headerRowsHTML += `
    <tr>
      ${epochCellsHTML}
    </tr>
    `.trim();
  } else {
    headerRowsHTML = `
    <tr>
      <th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>
      ${epochCellsHTML}
    </tr>
    `.trim();
  }

  const encounterCellsHTML = cols
    .map(
      (c) =>
        `<th scope="col" class="encounter-header">${c.encounter.encounter_name}</th>`
    )
    .join("");

  headerRowsHTML += `
    <tr>
      ${encounterCellsHTML}
    </tr>
  `.trim();

  const rowsHTML = rows
    .map((row) => {
      const cellsHTML = cols
        .map((col) => {
          const encId = col.encounter.encounter_id;
          const cell = row.cells.find((c) => c.encounter_id === encId) || {
            is_applicable: false,
          };

          let cellClass;
          let cellText;

          if (cell.is_applicable) {
            cellClass = "status-applicable";
            cellText = "✓";

            if (cell.details) {
              const detailsLower = cell.details.toLowerCase();
              if (detailsLower.includes("conditional")) {
                cellClass = "status-conditional";
              } else if (detailsLower.includes("optional")) {
                cellClass = "status-optional";
              }
              cellText += ` <span class="cell-details">${cell.details}</span>`;
            }
          } else {
            cellClass = "status-n-a";
            cellText = "-";
          }

          return `<td class="${cellClass}">${cellText}</td>`;
        })
        .join("");

      return `
    <tr>
      <th scope="row">${row.activity_name}</th>
      ${cellsHTML}
    </tr>
      `.trim();
    })
    .join("\n");

  return `
<table class="clinical-visit-matrix clinical-soa-matrix">
  <thead>
    ${headerRowsHTML}
  </thead>
  <tbody>
    ${rowsHTML}
  </tbody>
</table>
  `.trim();
}

export function createClinicalQueryFlag(fieldId, query) {
  const status = query && query.status ? query.status.toUpperCase() : "NONE";
  const statusClass = status.toLowerCase();
  const label =
    status === "NONE"
      ? "No active queries. Click to create."
      : `Query status: ${status}`;
  const icon = status === "NONE" ? "💬" : "⚠️";

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
    <button type="button" class="btn-close-panel" aria-label="Close query panel" onclick="document.getElementById('query-panel-${fieldId}').style.display='none'">×</button>
  </div>
  <div class="query-panel-body">
    ${bodyHTML}
  </div>
</div>
  `.trim();
}

export function createCtmsMilestoneTable(milestones) {
  if (!milestones || !Array.isArray(milestones)) {
    return `<div class="ctms-table-error">Invalid milestone data.</div>`;
  }
  const rows = milestones
    .map(
      (m) => `
    <tr>
      <td><strong>${m.type}</strong></td>
      <td>${m.plannedDate || "N/A"}</td>
      <td>${m.actualDate || "Pending"}</td>
      <td><span class="badge ${m.status === "ACHIEVED" ? "gxp" : ""}">${m.status}</span></td>
    </tr>
  `
    )
    .join("");

  return `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Milestone Type</th>
      <th scope="col">Planned Date</th>
      <th scope="col">Actual Date</th>
      <th scope="col">Status</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
  `.trim();
}

export function createCtmsVisitTable(visits) {
  if (!visits || !Array.isArray(visits)) {
    return `<div class="ctms-table-error">Invalid monitoring visit data.</div>`;
  }
  const rows = visits
    .map(
      (v) => `
    <tr>
      <td><strong>${v.type}</strong></td>
      <td>${v.scheduledDate || "N/A"}</td>
      <td>${v.actualDate || "Pending"}</td>
      <td>${v.cra || "N/A"}</td>
      <td><span class="badge ${v.status === "SIGNED_OFF" ? "gxp" : ""}">${v.status}</span></td>
    </tr>
  `
    )
    .join("");

  return `
<table class="clinical-visit-matrix">
  <thead>
    <tr>
      <th scope="col">Visit Type</th>
      <th scope="col">Scheduled Date</th>
      <th scope="col">Actual Date</th>
      <th scope="col">CRA Assigned</th>
      <th scope="col">Status</th>
    </tr>
  </thead>
  <tbody>
    ${rows}
  </tbody>
</table>
  `.trim();
}

export function createConditionRow(
  index,
  forms = [],
  fields = [],
  selectedValues = {}
) {
  const {
    formId = "",
    fieldId = "",
    operator = "==",
    rightType = "constant",
    rightValue = "",
    rightFieldId = "",
  } = selectedValues;

  const formOptions = forms
    .map(
      (f) =>
        `<option value="${f.id || f}"${(f.id || f) === formId ? " selected" : ""}>${f.name || f.id || f}</option>`
    )
    .join("");

  const fieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id || f}"${(f.id || f) === fieldId ? " selected" : ""}>${f.name || f.id || f}</option>`
    )
    .join("");

  const rightFieldOptions = fields
    .map(
      (f) =>
        `<option value="${f.id || f}"${(f.id || f) === rightFieldId ? " selected" : ""}>${f.name || f.id || f}</option>`
    )
    .join("");

  const operators = [
    { value: "==", label: "equals" },
    { value: "!=", label: "does not equal" },
    { value: "<", label: "is less than" },
    { value: "<=", label: "is less than or equal to" },
    { value: ">", label: "is greater than" },
    { value: ">=", label: "is greater than or equal to" },
    { value: "is_empty", label: "is empty" },
    { value: "is_not_empty", label: "is not empty" },
  ];

  const operatorOptions = operators
    .map(
      (op) =>
        `<option value="${op.value}"${op.value === operator ? " selected" : ""}>${op.label}</option>`
    )
    .join("");

  return `
<fieldset class="condition-row" data-index="${index}" id="condition-row-${index}">
  <legend>Condition Element #${index + 1}</legend>

  <div class="condition-row-grid" style="display: flex; gap: 12px; align-items: flex-end; flex-wrap: wrap;">

    <div class="form-group" style="flex: 1; min-width: 120px;">
      <label for="cond-form-${index}">Left Form</label>
      <select id="cond-form-${index}" class="cond-form-select" data-index="${index}">
        <option value="">-- Select Form --</option>
        ${formOptions}
      </select>
    </div>

    <div class="form-group" style="flex: 1; min-width: 120px;">
      <label for="cond-field-${index}">Left Field</label>
      <select id="cond-field-${index}" class="cond-field-select" data-index="${index}">
        <option value="">-- Select Field --</option>
        ${fieldOptions}
      </select>
    </div>

    <div class="form-group" style="flex: 1; min-width: 120px;">
      <label for="cond-operator-${index}">Operator</label>
      <select id="cond-operator-${index}" class="cond-operator-select" data-index="${index}">
        ${operatorOptions}
      </select>
    </div>

    <div class="form-group cond-right-type-container" style="flex: 1; min-width: 120px; ${operator === "is_empty" || operator === "is_not_empty" ? "display: none;" : ""}">
      <label for="cond-right-type-${index}">Right Value Type</label>
      <select id="cond-right-type-${index}" class="cond-right-type-select" data-index="${index}">
        <option value="constant"${rightType === "constant" ? " selected" : ""}>Constant Value</option>
        <option value="field_ref"${rightType === "field_ref" ? " selected" : ""}>Field Reference</option>
      </select>
    </div>

    <div class="form-group cond-right-value-container" style="flex: 1; min-width: 120px; ${operator === "is_empty" || operator === "is_not_empty" || rightType !== "constant" ? "display: none;" : ""}">
      <label for="cond-right-value-${index}">Constant Value</label>
      <input type="text" id="cond-right-value-${index}" class="cond-right-value-input" value="${rightValue}" data-index="${index}" placeholder="Value..." />
    </div>

    <div class="form-group cond-right-field-container" style="flex: 1; min-width: 120px; ${operator === "is_empty" || operator === "is_not_empty" || rightType !== "field_ref" ? "display: none;" : ""}">
      <label for="cond-right-field-${index}">Right Field</label>
      <select id="cond-right-field-${index}" class="cond-right-field-select" data-index="${index}">
        <option value="">-- Select Field --</option>
        ${rightFieldOptions}
      </select>
    </div>

    <div class="form-group" style="flex-grow: 0;">
      <button type="button" class="btn btn-error btn-remove-condition" data-index="${index}" style="background-color: var(--error, #ef4444); color: white; border: none; padding: 8px 12px; border-radius: 4px; cursor: pointer;">Remove</button>
    </div>

  </div>
</fieldset>
  `.trim();
}

export function createRuleEditorContainer(forms = [], fields = []) {
  const formOptions = forms
    .map((f) => `<option value="${f.id || f}">${f.name || f.id || f}</option>`)
    .join("");

  const fieldOptions = fields
    .map((f) => `<option value="${f.id || f}">${f.name || f.id || f}</option>`)
    .join("");

  return `
<div class="rule-editor-container" id="rule-editor-form">
  <fieldset class="rule-meta-fieldset" style="border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px;">
    <legend style="padding: 0 8px; font-weight: bold; color: var(--primary);">Rule Type & Target Definition</legend>
    <div style="display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 12px;">
      <div class="form-group" style="flex: 1; min-width: 200px;">
        <label for="rule-type" style="display: block; margin-bottom: 4px; font-weight: 600;">Rule Classification Type</label>
        <select id="rule-type" class="rule-type-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="skip_logic">Skip Logic (Show/Hide fields)</option>
          <option value="constraint">Field Constraint (Single field query validation)</option>
          <option value="cross_form_check">Cross-Form / Longitudinal Check</option>
        </select>
      </div>

      <div class="form-group rule-target-container" style="flex: 1; min-width: 200px;">
        <label for="rule-target-field" style="display: block; margin-bottom: 4px; font-weight: 600;">Target Field</label>
        <select id="rule-target-field" class="rule-target-field-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="">-- Select Target Field --</option>
          ${fieldOptions}
        </select>
      </div>
    </div>

    <div style="display: flex; gap: 16px; flex-wrap: wrap;" id="rule-extra-fields-grid">
      <div class="form-group rule-action-container" style="flex: 1; min-width: 200px;">
        <label for="rule-action" style="display: block; margin-bottom: 4px; font-weight: 600;">Action on Target</label>
        <select id="rule-action" class="rule-action-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="show">Show target field</option>
          <option value="hide">Hide target field</option>
        </select>
      </div>

      <div class="form-group rule-target-form-container" style="flex: 1; min-width: 200px;">
        <label for="rule-target-form" style="display: block; margin-bottom: 4px; font-weight: 600;">Target Form (Optional)</label>
        <select id="rule-target-form" class="rule-target-form-select" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;">
          <option value="">-- Select Target Form --</option>
          ${formOptions}
        </select>
      </div>

      <div class="form-group rule-message-container" style="flex: 1; min-width: 200px; display: none;">
        <label for="rule-message" style="display: block; margin-bottom: 4px; font-weight: 600;">Auto-Query Discrepancy Message</label>
        <input type="text" id="rule-message" class="rule-message-input" placeholder="e.g. Systolic BP is out of logical range" style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;" />
      </div>
    </div>
  </fieldset>

  <fieldset class="rule-conditions-fieldset" style="border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px;">
    <legend style="padding: 0 8px; font-weight: bold; color: var(--primary);">Rule Conditions (Logical Expression Tree)</legend>
    <div class="form-group" style="margin-bottom: 12px;">
      <label for="rule-logical-operator" style="display: block; margin-bottom: 4px; font-weight: 600;">Match Conditions Group Operator</label>
      <select id="rule-logical-operator" class="rule-logical-operator-select" style="padding: 6px; border: 1px solid var(--border); border-radius: 4px;">
        <option value="and">All conditions must be met (AND)</option>
        <option value="or">Any condition can be met (OR)</option>
      </select>
    </div>

    <div id="conditions-list" style="display: flex; flex-direction: column; gap: 12px;">
      <!-- Dynamic Condition Rows go here -->
    </div>

    <div style="margin-top: 12px; display: flex; gap: 8px;">
      <button type="button" id="btn-add-condition" class="btn btn-secondary" style="padding: 8px 12px; cursor: pointer;">➕ Add Condition Row</button>
    </div>
  </fieldset>

  <fieldset class="rule-preview-fieldset" style="border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px;">
    <legend style="padding: 0 8px; font-weight: bold; color: var(--primary);">Live Compilation & GxP Verification Preview</legend>
    <div class="preview-output" style="background-color: #f1f5f9; padding: 12px; border-radius: 6px; font-family: monospace; font-size: 0.85rem; border: 1px solid var(--border);"> // deid-ignore
      <div><strong>Compiled XPath Expression:</strong></div>
      <div id="rule-xpath-preview" style="color: #0369a1; margin-bottom: 8px; word-break: break-all;">(No conditions added)</div> // deid-ignore

      <div><strong>Validation Feedback:</strong></div>
      <div id="rule-validation-failures" style="color: #b91c1c; margin-bottom: 4px; font-weight: 600;"></div>
      <div id="rule-circular-cycles" style="color: #b91c1c; font-weight: 600;"></div>
    </div>
  </fieldset>

  <div style="margin-top: 16px; display: flex; justify-content: flex-end; gap: 8px;">
    <button type="button" id="btn-cancel-rule" class="btn" style="padding: 8px 16px; cursor: pointer;">Cancel</button>
    <button type="button" id="btn-save-rule" class="btn btn-primary" style="padding: 8px 16px; cursor: pointer; background-color: var(--primary); color: white; border: none; border-radius: 4px;">Save Signed Rule</button>
  </div>
</div>
  `.trim();
}

export function createClinicalLookupInput(
  id,
  label,
  value = "",
  status = "none",
  statusMessage = "",
  gridSpan = 12,
  attributes = {}
) {
  const extraAttrs = Object.entries(attributes)
    .map(([k, v]) => `${k}="${v}"`)
    .join(" ");

  let stateClass = "";
  let ariaLiveMessage = statusMessage;
  let statusIcon = "";

  if (status === "loading") {
    stateClass = "lookup-loading";
    statusIcon = "⏳";
    if (!ariaLiveMessage) ariaLiveMessage = "Searching terminology database...";
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

  const statusHTML =
    status !== "none"
      ? `
<div class="lookup-status-indicator ${stateClass}" id="lookup-status-${id}" role="status" aria-live="polite">
  <span class="lookup-status-icon" aria-hidden="true">${statusIcon}</span>
  <span class="lookup-status-text">${ariaLiveMessage}</span>
</div>
  `.trim()
      : `
<div class="lookup-status-indicator" id="lookup-status-${id}" role="status" aria-live="polite" style="display: none;"></div>
  `.trim();

  return `
<div class="clinical-input clinical-lookup-container grid-span-${gridSpan}" style="grid-column: span ${gridSpan};" id="field-container-${id}" ${extraAttrs}>
  <label for="${id}">${label}</label>
  <div class="input-wrapper">
    <input type="text" id="${id}" name="${id}" value="${value}" autocomplete="off" />
  </div>
  ${statusHTML}
</div>
  `.trim();
}
