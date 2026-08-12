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
  let inputAttrs = "";

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

    inputAttrs += ` aria-describedby="lookup-status-${id}"`;
    if (status === "invalid") {
      inputAttrs += ` aria-invalid="true"`;
    }
  } else {
    statusHtml = `<div id="lookup-status-${id}" class="lookup-status-indicator" role="status" aria-live="polite" style="display: none"></div>`;
  }

  return `
  <div id="field-container-${id}" class="clinical-input clinical-lookup-container grid-span-12" style="grid-column: span 12;">
    <label for="${id}">${label}</label>
    <div class="input-wrapper">
      <input id="${id}" type="text" name="${id}" value="${value}" autocomplete="off"${inputAttrs} />
    </div>
    ${statusHtml}
  </div>`;
}

/**
 * Renders an arm-aware clinical Schedule of Activities (SoA) matrix.
 * Supports grouped column headers for Arms, Epochs, and Visits, and
 * rich per-cell details (such as applicability, timing, conditional/optional states).
 *
 * @param {Object} soaData - The structured SoA dataset.
 * @returns {string} Fully semantic, accessible HTML table representing the SoA.
 */
export function createSoaBuilderMatrix(soaData) {
  if (!soaData || !soaData.rows) {
    return `<div class="clinical-visit-matrix-error">Invalid SoA matrix data.</div>`;
  }

  const epochs = soaData.epochs || [];
  const encounters = soaData.encounters || [];
  const rows = soaData.rows || [];
  const arms = soaData.arms || [];

  if (encounters.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

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

  const hasArms = cols.some((c) => c.arm !== null);
  const totalHeaderRows = hasArms ? 3 : 2;

  // Arm groups
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

  // Epoch groups
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

  let tableHtml = `<table class="clinical-visit-matrix clinical-soa-matrix"><thead>`;

  // Arm row
  if (hasArms) {
    tableHtml += `<tr><th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>`;
    armGroups.forEach((g) => {
      tableHtml += `<th scope="col" colspan="${g.colspan}" class="grouped-header arm-header">${g.name}</th>`;
    });
    tableHtml += `</tr>`;
  }

  // Epoch row
  if (hasArms) {
    tableHtml += `<tr>`;
    epochGroups.forEach((g) => {
      tableHtml += `<th scope="col" colspan="${g.colspan}" class="grouped-header epoch-header">${g.name}</th>`;
    });
    tableHtml += `</tr>`;
  } else {
    tableHtml += `<tr><th scope="col" rowspan="${totalHeaderRows}" class="corner-header">Form / Procedure</th>`;
    epochGroups.forEach((g) => {
      tableHtml += `<th scope="col" colspan="${g.colspan}" class="grouped-header epoch-header">${g.name}</th>`;
    });
    tableHtml += `</tr>`;
  }

  // Encounter row
  tableHtml += `<tr>`;
  cols.forEach((c) => {
    tableHtml += `<th scope="col" class="encounter-header">${c.encounter.encounter_name}</th>`;
  });
  tableHtml += `</tr></thead><tbody>`;

  // Rows
  rows.forEach((row) => {
    tableHtml += `<tr><th scope="row">${row.activity_name}</th>`;
    cols.forEach((col) => {
      const encId = col.encounter.encounter_id;
      const cell = row.cells?.find((c) => c.encounter_id === encId) || {
        is_applicable: false,
      };
      let cellClass = "status-n-a";
      let cellContent = "-";
      let statusText = "Not Applicable";

      if (cell.is_applicable) {
        cellClass = "status-applicable";
        statusText = "Applicable";
        if (cell.details) {
          const detailsLower = cell.details.toLowerCase();
          if (detailsLower.includes("conditional")) {
            cellClass = "status-conditional";
            statusText = "Conditional";
          } else if (detailsLower.includes("optional")) {
            cellClass = "status-optional";
            statusText = "Optional";
          } else {
            statusText = cell.details;
          }
        }
        cellContent = `✓${
          cell.details
            ? ` <span class="cell-details">${cell.details}</span>`
            : ""
        }`;
      }

      const parts = [];
      if (col.arm && col.arm.arm_name) {
        parts.push(col.arm.arm_name);
      }
      if (col.epoch && col.epoch.epoch_name) {
        parts.push(col.epoch.epoch_name);
      }
      if (col.encounter && col.encounter.encounter_name) {
        parts.push(col.encounter.encounter_name);
      }
      const colDetailsStr = parts.join(" - ") || col.encounter.encounter_name;

      const srLabel = `Form: ${row.activity_name}, Visit: ${colDetailsStr}, Status: ${statusText}`;
      const srSpan = `<span class="sr-only" style="position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); border: 0; white-space: nowrap;">${srLabel}</span>`;

      tableHtml += `<td class="${cellClass}">${cellContent}${srSpan}</td>`;
    });
    tableHtml += `</tr>`;
  });

  tableHtml += `</tbody></table>`;
  return tableHtml;
}

/**
 * Backwards compatible clinical visit matrix function. Delegates to
 * `createSoaBuilderMatrix` if hierarchical input structure is detected,
 * or renders a simple 2D visit matrix for flat arrays.
 */
export function createClinicalVisitMatrix(visitsOrSoa, forms = []) {
  // If first argument is hierarchical soaData, delegate to createSoaBuilderMatrix
  if (
    visitsOrSoa &&
    (visitsOrSoa.rows || visitsOrSoa.encounters || visitsOrSoa.epochs)
  ) {
    return createSoaBuilderMatrix(visitsOrSoa);
  }

  // Simple, backwards-compatible 2D Visit × Form matrix renderer
  const visits = Array.isArray(visitsOrSoa) ? visitsOrSoa : [];
  if (visits.length === 0) {
    return `<div class="clinical-visit-matrix-error">No encounters defined for SoA matrix.</div>`;
  }

  let tableHtml = `<table class="clinical-visit-matrix"><thead><tr><th scope="col">Form / Procedure</th>`;
  visits.forEach((v) => {
    tableHtml += `<th scope="col">${v}</th>`;
  });
  tableHtml += `</tr></thead><tbody>`;

  forms.forEach((form) => {
    tableHtml += `<tr><th scope="row">${form.name}</th>`;
    visits.forEach((v, idx) => {
      const status = form.statuses?.[idx] || "N/A";
      let cellClass = "status-n-a";
      if (status === "Complete") {
        cellClass = "status-complete";
      } else if (status === "Pending") {
        cellClass = "status-pending";
      }
      const srLabel = `Form: ${form.name}, Visit: ${v}, Status: ${status}`;
      const srSpan = `<span class="sr-only" style="position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); border: 0; white-space: nowrap;">${srLabel}</span>`;
      tableHtml += `<td class="${cellClass}">${status}${srSpan}</td>`;
    });
    tableHtml += `</tr>`;
  });

  tableHtml += `</tbody></table>`;
  return tableHtml;
}
