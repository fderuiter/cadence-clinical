import { evaluateAST } from "./src/evaluator.js";
import { validateField as uiValidateField } from "ui";

/**
 * Pure evaluation function for dynamic forms.
 * Determines field visibility, performs cascading dependent nullification,
 * and tracks field values and audit logs.
 *
 * @param {Array} fields - Array of field metadata objects
 * @param {Object} currentValues - Current state of form values
 * @param {Function} logPurge - Callback function to log field purges in the audit trail
 * @returns {Object} { visibleFields, updatedValues }
 */
export function renderFormFromJSON(fields, currentValues, logPurge = null) {
  const updatedValues = { ...currentValues };
  const visibleFields = {};

  let changed = true;
  let passes = 0;
  // Support cascading up to 10 levels deep
  while (changed && passes < 10) {
    changed = false;
    passes++;

    for (const field of fields) {
      const isRelevant = field.relevant
        ? evaluateAST(field.relevant, updatedValues) !== false
        : true;

      const wasVisible = visibleFields[field.id] !== false;
      if (visibleFields[field.id] === undefined || isRelevant !== wasVisible) {
        visibleFields[field.id] = isRelevant;
        changed = true;
      }

      if (!isRelevant) {
        const val = updatedValues[field.id];
        if (val !== undefined && val !== "" && val !== null) {
          updatedValues[field.id] = "";
          if (logPurge) {
            logPurge(
              field.id,
              val,
              "System-initiated purge of inactive child variable due to parent value mutation"
            );
          }
          changed = true;
        }
      }
    }
  }

  return { visibleFields, updatedValues };
}

/**
 * Wrapper for field validation that supports AST constraints.
 *
 * @param {Object} fieldMeta - Field metadata configuration
 * @param {any} val - Field value
 * @param {Object} context - Optional form context for constraint evaluation
 * @returns {Object} { valid: boolean, message: string }
 */
export function validateField(fieldMeta, val, context = {}) {
  return uiValidateField(fieldMeta, val, context, evaluateAST);
}

export { createClinicalVisitMatrix, createSoaBuilderMatrix } from "ui";

/**
 * Vanilla JS renderer for the interactive Schedule of Activities (SoA) Builder.
 * Manages in-memory builder state, mounts to a container, and supports full re-rendering on edits.
 *
 * @param {HTMLElement} container - The DOM element where the SoA builder is rendered.
 * @param {Object} soaData - The current structured SoA dataset.
 * @param {Function} [onUpdate=null] - Optional callback triggered on state updates.
 */
export function renderMdr(container, soaData, onUpdate = null) {
  if (!container) return;

  // Render the core matrix HTML using our packages/ui component
  const matrixHtml = createSoaBuilderMatrix(soaData);

  // Build interactive controls panel
  const controlsHtml = `
    <div class="soa-builder-controls" style="margin-top: 16px; padding: 16px; border: 1px solid var(--border); border-radius: 8px; background-color: #f8fafc;">
      <h3 style="margin-bottom: 12px; font-weight: bold; color: var(--primary);">Interactive SoA Entity Editor</h3>

      <!-- Arm Editor -->
      <fieldset style="border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 12px;">
        <legend style="padding: 0 6px; font-weight: bold;">Add Study Arm</legend>
        <div style="display: flex; gap: 8px; margin-bottom: 8px;">
          <input id="vanilla-arm-id" type="text" placeholder="Arm ID (e.g. ARM-C)" style="padding: 6px; flex: 1;" />
          <input id="vanilla-arm-name" type="text" placeholder="Arm Name (e.g. Arm C)" style="padding: 6px; flex: 2;" />
          <button id="vanilla-btn-add-arm" class="btn btn-primary" style="padding: 6px 12px;">Add Arm</button>
        </div>
      </fieldset>

      <!-- Epoch Editor -->
      <fieldset style="border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 12px;">
        <legend style="padding: 0 6px; font-weight: bold;">Add Epoch</legend>
        <div style="display: flex; gap: 8px; margin-bottom: 8px;">
          <input id="vanilla-epoch-id" type="text" placeholder="Epoch ID" style="padding: 6px; flex: 1;" />
          <input id="vanilla-epoch-name" type="text" placeholder="Epoch Name" style="padding: 6px; flex: 2;" />
          <select id="vanilla-epoch-arm" style="padding: 6px; flex: 1;">
            <option value="">-- Shared / None --</option>
            ${(soaData?.arms || []).map((arm) => `<option value="${arm.arm_id}">${arm.arm_name}</option>`).join("")}
          </select>
          <button id="vanilla-btn-add-epoch" class="btn btn-primary" style="padding: 6px 12px;">Add Epoch</button>
        </div>
      </fieldset>

      <!-- Visit Editor -->
      <fieldset style="border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 12px;">
        <legend style="padding: 0 6px; font-weight: bold;">Add Visit</legend>
        <div style="display: flex; gap: 8px; margin-bottom: 8px;">
          <input id="vanilla-visit-id" type="text" placeholder="Visit ID" style="padding: 6px; flex: 1;" />
          <input id="vanilla-visit-name" type="text" placeholder="Visit Name" style="padding: 6px; flex: 2;" />
          <select id="vanilla-visit-epoch" style="padding: 6px; flex: 1;">
            <option value="">-- Select Epoch --</option>
            ${(soaData?.epochs || []).map((ep) => `<option value="${ep.epoch_id}">${ep.epoch_name}</option>`).join("")}
          </select>
          <button id="vanilla-btn-add-visit" class="btn btn-primary" style="padding: 6px 12px;">Add Visit</button>
        </div>
      </fieldset>

      <!-- Procedure Editor -->
      <fieldset style="border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-bottom: 12px;">
        <legend style="padding: 0 6px; font-weight: bold;">Add Procedure</legend>
        <div style="display: flex; gap: 8px; margin-bottom: 8px;">
          <input id="vanilla-proc-id" type="text" placeholder="Proc ID" style="padding: 6px; flex: 1;" />
          <input id="vanilla-proc-name" type="text" placeholder="Procedure Name" style="padding: 6px; flex: 2;" />
          <button id="vanilla-btn-add-proc" class="btn btn-primary" style="padding: 6px 12px;">Add Proc</button>
        </div>
      </fieldset>

      <!-- Cell Window & Applicability Editor -->
      <fieldset style="border: 1px solid var(--border); border-radius: 6px; padding: 12px;">
        <legend style="padding: 0 6px; font-weight: bold;">Set Applicability &amp; Timing</legend>
        <div style="display: flex; gap: 8px; flex-wrap: wrap;">
          <select id="vanilla-link-proc" style="padding: 6px; flex: 1; min-width: 150px;">
            <option value="">-- Select Procedure --</option>
            ${(soaData?.rows || []).map((row) => `<option value="${row.activity_id}">${row.activity_name}</option>`).join("")}
          </select>
          <select id="vanilla-link-visit" style="padding: 6px; flex: 1; min-width: 150px;">
            <option value="">-- Select Visit --</option>
            ${(soaData?.encounters || []).map((enc) => `<option value="${enc.encounter_id}">${enc.encounter_name}</option>`).join("")}
          </select>
          <input id="vanilla-link-timing" type="text" placeholder="Timing (e.g. Conditional)" style="padding: 6px; flex: 2; min-width: 150px;" />
          <button id="vanilla-btn-set-link" class="btn btn-primary" style="padding: 6px 12px;">Apply</button>
          <button id="vanilla-btn-remove-link" class="btn btn-danger" style="padding: 6px 12px; background-color: var(--error); color: white;">Remove</button>
        </div>
      </fieldset>
    </div>
  `;

  // Inject into the container
  container.innerHTML = `
    <div id="soa-matrix-container">${matrixHtml}</div>
    ${controlsHtml}
  `;

  // Helper to re-render with updated data
  const reRender = (updatedData) => {
    if (onUpdate) onUpdate(updatedData);
    renderMdr(container, updatedData, onUpdate);
  };

  // Add Arm Click
  const addArmBtn = container.querySelector("#vanilla-btn-add-arm");
  if (addArmBtn) {
    addArmBtn.addEventListener("click", () => {
      const armId = container.querySelector("#vanilla-arm-id")?.value?.trim();
      const armName = container
        .querySelector("#vanilla-arm-name")
        ?.value?.trim();
      if (!armId || !armName) {
        alert("Please enter Arm ID and Name");
        return;
      }
      const updatedData = { ...soaData };
      if (!updatedData.arms) updatedData.arms = [];
      updatedData.arms.push({ arm_id: armId, arm_name: armName });
      reRender(updatedData);
    });
  }

  // Add Epoch Click
  const addEpochBtn = container.querySelector("#vanilla-btn-add-epoch");
  if (addEpochBtn) {
    addEpochBtn.addEventListener("click", () => {
      const epochId = container
        .querySelector("#vanilla-epoch-id")
        ?.value?.trim();
      const epochName = container
        .querySelector("#vanilla-epoch-name")
        ?.value?.trim();
      const armId = container.querySelector("#vanilla-epoch-arm")?.value;
      if (!epochId || !epochName) {
        alert("Please enter Epoch ID and Name");
        return;
      }
      const updatedData = { ...soaData };
      if (!updatedData.epochs) updatedData.epochs = [];
      updatedData.epochs.push({
        epoch_id: epochId,
        epoch_name: epochName,
        sequence: updatedData.epochs.length + 1,
        arm_id: armId || undefined,
      });
      reRender(updatedData);
    });
  }

  // Add Visit Click
  const addVisitBtn = container.querySelector("#vanilla-btn-add-visit");
  if (addVisitBtn) {
    addVisitBtn.addEventListener("click", () => {
      const visitId = container
        .querySelector("#vanilla-visit-id")
        ?.value?.trim();
      const visitName = container
        .querySelector("#vanilla-visit-name")
        ?.value?.trim();
      const epochId = container.querySelector("#vanilla-visit-epoch")?.value;
      if (!visitId || !visitName || !epochId) {
        alert("Please populate all fields");
        return;
      }
      const updatedData = { ...soaData };
      if (!updatedData.encounters) updatedData.encounters = [];
      updatedData.encounters.push({
        encounter_id: visitId,
        encounter_name: visitName,
        epoch_id: epochId,
        sequence: updatedData.encounters.length + 1,
      });
      reRender(updatedData);
    });
  }

  // Add Proc Click
  const addProcBtn = container.querySelector("#vanilla-btn-add-proc");
  if (addProcBtn) {
    addProcBtn.addEventListener("click", () => {
      const procId = container.querySelector("#vanilla-proc-id")?.value?.trim();
      const procName = container
        .querySelector("#vanilla-proc-name")
        ?.value?.trim();
      if (!procId || !procName) {
        alert("Please enter Procedure ID and Name");
        return;
      }
      const updatedData = { ...soaData };
      if (!updatedData.rows) updatedData.rows = [];
      updatedData.rows.push({
        activity_id: procId,
        activity_name: procName,
        cells: (updatedData.encounters || []).map((enc) => ({
          encounter_id: enc.encounter_id,
          is_applicable: false,
        })),
      });
      reRender(updatedData);
    });
  }

  // Set / Remove Link click
  const setLinkBtn = container.querySelector("#vanilla-btn-set-link");
  if (setLinkBtn) {
    setLinkBtn.addEventListener("click", () => {
      const procId = container.querySelector("#vanilla-link-proc")?.value;
      const visitId = container.querySelector("#vanilla-link-visit")?.value;
      const timing = container
        .querySelector("#vanilla-link-timing")
        ?.value?.trim();
      if (!procId || !visitId) {
        alert("Select both a Procedure and a Visit");
        return;
      }
      const updatedData = { ...soaData };
      const row = updatedData.rows?.find((r) => r.activity_id === procId);
      if (row) {
        if (!row.cells) row.cells = [];
        let cell = row.cells.find((c) => c.encounter_id === visitId);
        if (!cell) {
          cell = { encounter_id: visitId, is_applicable: true };
          row.cells.push(cell);
        }
        cell.is_applicable = true;
        cell.details = timing || undefined;
      }
      reRender(updatedData);
    });
  }

  const removeLinkBtn = container.querySelector("#vanilla-btn-remove-link");
  if (removeLinkBtn) {
    removeLinkBtn.addEventListener("click", () => {
      const procId = container.querySelector("#vanilla-link-proc")?.value;
      const visitId = container.querySelector("#vanilla-link-visit")?.value;
      if (!procId || !visitId) {
        alert("Select both a Procedure and a Visit");
        return;
      }
      const updatedData = { ...soaData };
      const row = updatedData.rows?.find((r) => r.activity_id === procId);
      if (row) {
        if (!row.cells) row.cells = [];
        let cell = row.cells.find((c) => c.encounter_id === visitId);
        if (cell) {
          cell.is_applicable = false;
          cell.details = undefined;
        }
      }
      reRender(updatedData);
    });
  }
}
