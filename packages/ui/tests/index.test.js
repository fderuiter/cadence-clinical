import { describe, it, expect } from "vitest";
import {
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalVisitMatrix,
  createClinicalSoAMatrix,
  createClinicalQueryFlag,
  createQueryPanel,
} from "../index.js";

describe("createClinicalInput", () => {
  it("renders an input with the given label and default arguments", () => {
    const html = createClinicalInput("patientName", "Patient Name");
    expect(html).toContain("Patient Name");
    expect(html).toContain('id="patientName"');
    expect(html).toContain('class="clinical-input grid-span-12"');
    expect(html).toContain('style="grid-column: span 12;"');
  });

  it("handles gridSpan and custom attributes", () => {
    const html = createClinicalInput("wt", "Weight", "70", null, 6, {
      "data-custom": "test",
    });
    expect(html).toContain('class="clinical-input grid-span-6"');
    expect(html).toContain('style="grid-column: span 6;"');
    expect(html).toContain('value="70"');
    expect(html).toContain('data-custom="test"');
  });

  it("renders query flags and panel when query object is provided", () => {
    const query = {
      status: "OPEN",
      message: "Check value range",
      createdBy: "CRA",
    };
    const html = createClinicalInput("bp", "Blood Pressure", "180/120", query);
    expect(html).toContain('class="query-flag query-status-open"');
    expect(html).toContain('id="query-panel-bp"');
    expect(html).toContain("Check value range");
  });
});

describe("createClinicalRadioGrid", () => {
  it("renders accessible fieldsets and legends for radio buttons", () => {
    const options = [
      { value: "M", label: "Male" },
      { value: "F", label: "Female" },
    ];
    const html = createClinicalRadioGrid(
      "gender",
      "Gender Selection",
      options,
      "F"
    );
    expect(html).toContain("<fieldset");
    expect(html).toContain("<legend>Gender Selection</legend>");
    expect(html).toContain('type="radio"');
    expect(html).toContain('value="M"');
    expect(html).toContain('value="F" checked');
    expect(html).toContain('id="gender_option_0"');
    expect(html).toContain('id="gender_option_1"');
  });

  it("handles simple string array options", () => {
    const html = createClinicalRadioGrid(
      "yesno",
      "Yes No",
      ["Yes", "No"],
      "Yes"
    );
    expect(html).toContain('value="Yes" checked');
    expect(html).toContain('value="No"');
  });
});

describe("createClinicalVisitMatrix", () => {
  it("renders a standard, accessible table representing visits vs forms", () => {
    const matrixData = {
      visits: ["Screening", "Week 2", "End of Study"],
      forms: [
        { name: "Demographics", statuses: ["Complete", "N/A", "N/A"] },
        { name: "Vital Signs", statuses: ["Complete", "Pending", "N/A"] },
      ],
    };
    const html = createClinicalVisitMatrix(matrixData);
    expect(html).toContain('<table class="clinical-visit-matrix"');
    expect(html).toContain('<th scope="col">Screening</th>');
    expect(html).toContain('<th scope="row">Demographics</th>');
    expect(html).toContain('<td class="status-complete">Complete</td>');
    expect(html).toContain('<td class="status-pending">Pending</td>');
    expect(html).toContain('<td class="status-n-a">N/A</td>');
  });

  it("renders a diagnostic message on invalid data", () => {
    const html = createClinicalVisitMatrix(null);
    expect(html).toContain("Invalid visit matrix data");
  });
});

describe("createClinicalSoAMatrix", () => {
  it("renders a multi-level grouped header with Arms, Epochs, and Encounters with correct colspans", () => {
    const soaData = {
      arms: [
        { arm_id: "ARM-A", arm_name: "Arm A" },
        { arm_id: "ARM-B", arm_name: "Arm B" },
      ],
      epochs: [
        { epoch_id: "EP-1", epoch_name: "Epoch 1", arm_id: "ARM-A" },
        { epoch_id: "EP-2", epoch_name: "Epoch 2", arm_id: "ARM-B" },
      ],
      encounters: [
        { encounter_id: "E1", encounter_name: "Encounter 1", epoch_id: "EP-1" },
        { encounter_id: "E2", encounter_name: "Encounter 2", epoch_id: "EP-1" },
        { encounter_id: "E3", encounter_name: "Encounter 3", epoch_id: "EP-2" },
      ],
      rows: [
        {
          activity_id: "ACT1",
          activity_name: "Procedure 1",
          cells: [
            { encounter_id: "E1", is_applicable: true, details: "Mandatory" },
            {
              encounter_id: "E2",
              is_applicable: true,
              details: "Conditional timing",
            },
            {
              encounter_id: "E3",
              is_applicable: true,
              details: "Optional check",
            },
          ],
        },
        {
          activity_id: "ACT2",
          activity_name: "Procedure 2",
          cells: [
            { encounter_id: "E1", is_applicable: false },
            { encounter_id: "E2", is_applicable: false },
            { encounter_id: "E3", is_applicable: false },
          ],
        },
      ],
    };

    const html = createClinicalSoAMatrix(soaData);

    // Verify accessible table structure
    expect(html).toContain('class="clinical-visit-matrix clinical-soa-matrix"');

    // Verify multi-level headers and colspans
    expect(html).toContain(
      '<th scope="col" colspan="2" class="grouped-header arm-header">Arm A</th>'
    );
    expect(html).toContain(
      '<th scope="col" colspan="1" class="grouped-header arm-header">Arm B</th>'
    );

    expect(html).toContain(
      '<th scope="col" colspan="2" class="grouped-header epoch-header">Epoch 1</th>'
    );
    expect(html).toContain(
      '<th scope="col" colspan="1" class="grouped-header epoch-header">Epoch 2</th>'
    );

    expect(html).toContain(
      '<th scope="col" class="encounter-header">Encounter 1</th>'
    );
    expect(html).toContain(
      '<th scope="col" class="encounter-header">Encounter 2</th>'
    );
    expect(html).toContain(
      '<th scope="col" class="encounter-header">Encounter 3</th>'
    );

    // Verify cell mapping and status classes
    expect(html).toContain(
      '<td class="status-applicable">✓ <span class="cell-details">Mandatory</span></td>'
    );
    expect(html).toContain(
      '<td class="status-conditional">✓ <span class="cell-details">Conditional timing</span></td>'
    );
    expect(html).toContain(
      '<td class="status-optional">✓ <span class="cell-details">Optional check</span></td>'
    );
    expect(html).toContain('<td class="status-n-a">-</td>');
  });

  it("handles missing arm or shared encounters gracefully without arm header row", () => {
    const soaData = {
      epochs: [{ epoch_id: "EP-1", epoch_name: "Epoch 1" }],
      encounters: [
        { encounter_id: "E1", encounter_name: "Encounter 1", epoch_id: "EP-1" },
      ],
      rows: [
        {
          activity_id: "ACT1",
          activity_name: "Procedure 1",
          cells: [{ encounter_id: "E1", is_applicable: true }],
        },
      ],
    };

    const html = createClinicalSoAMatrix(soaData);
    // Arm row should not be generated since hasArms is false
    expect(html).not.toContain('class="grouped-header arm-header"');
    expect(html).toContain(
      '<th scope="col" colspan="1" class="grouped-header epoch-header">Epoch 1</th>'
    );
    expect(html).toContain(
      '<th scope="col" class="encounter-header">Encounter 1</th>'
    );
  });
});

describe("createClinicalQueryFlag", () => {
  it("renders flag for NONE status", () => {
    const html = createClinicalQueryFlag("testField", null);
    expect(html).toContain('class="query-flag query-status-none"');
    expect(html).toContain("💬");
  });

  it("renders flag for OPEN status", () => {
    const html = createClinicalQueryFlag("testField", { status: "OPEN" });
    expect(html).toContain('class="query-flag query-status-open"');
    expect(html).toContain("⚠️");
  });

  it("renders flag for CLOSED status", () => {
    const html = createClinicalQueryFlag("testField", { status: "CLOSED" });
    expect(html).toContain('class="query-flag query-status-closed"');
    expect(html).toContain("⚠️");
  });
});

describe("createQueryPanel", () => {
  it("renders query creation panel for NONE status", () => {
    const html = createQueryPanel("fieldX", null);
    expect(html).toContain("Raise a query for this field");
    expect(html).toContain('data-action="create-query"');
    expect(html).toContain('id="query-message-fieldX"');
  });

  it("renders response panel for OPEN/REOPENED status", () => {
    const query = {
      status: "OPEN",
      message: "Inconsistent data entry",
      createdBy: "CRA",
    };
    const html = createQueryPanel("fieldX", query);
    expect(html).toContain("Inconsistent data entry");
    expect(html).toContain('data-action="respond-query"');
    expect(html).toContain('id="query-response-fieldX"');
  });

  it("renders action buttons (Close/Reopen) for ANSWERED status", () => {
    const query = {
      status: "ANSWERED",
      message: "Value high",
      response: "Patient had coffee before reading",
    };
    const html = createQueryPanel("fieldX", query);
    expect(html).toContain('data-action="close-query"');
    expect(html).toContain('data-action="reopen-query"');
    expect(html).toContain("Patient had coffee before reading");
  });

  it("renders summary message and no actions for CLOSED status", () => {
    const query = {
      status: "CLOSED",
      message: "Value high",
      response: "Resolved",
      closedBy: "Monitor",
      closedAt: "2026-07-22",
    };
    const html = createQueryPanel("fieldX", query);
    expect(html).toContain("This query is permanently resolved and closed.");
    expect(html).not.toContain('data-action="close-query"');
  });
});

import { createConditionRow, createRuleEditorContainer } from "../index.js";

describe("createConditionRow", () => {
  const mockForms = [{ id: "form_dm", name: "Demographics" }];
  const mockFields = [{ id: "brthdt", name: "Date of Birth" }];

  it("renders accessible fieldset and legends for condition rows", () => {
    const html = createConditionRow(0, mockForms, mockFields);
    expect(html).toContain("<fieldset");
    expect(html).toContain("Condition Element #1");
    expect(html).toContain('id="cond-form-0"');
    expect(html).toContain('id="cond-field-0"');
    expect(html).toContain('id="cond-operator-0"');
    expect(html).toContain("form_dm");
    expect(html).toContain("brthdt");
  });

  it("supports pre-populating values", () => {
    const html = createConditionRow(1, mockForms, mockFields, {
      formId: "form_dm",
      fieldId: "brthdt",
      operator: "<",
      rightType: "constant",
      rightValue: "2026-01-01",
    });
    expect(html).toContain("Condition Element #2");
    expect(html).toContain('value="form_dm" selected');
    expect(html).toContain('value="brthdt" selected');
    expect(html).toContain('value="<" selected');
    expect(html).toContain('value="2026-01-01"');
  });
});

describe("createRuleEditorContainer", () => {
  const mockForms = [{ id: "form_dm", name: "Demographics" }];
  const mockFields = [{ id: "brthdt", name: "Date of Birth" }];

  it("renders correct rule editor container with dropdown fields and accessibility specs", () => {
    const html = createRuleEditorContainer(mockForms, mockFields);
    expect(html).toContain('id="rule-editor-form"');
    expect(html).toContain('id="rule-type"');
    expect(html).toContain('id="rule-target-field"');
    expect(html).toContain('id="rule-action"');
    expect(html).toContain('id="rule-message"');
    expect(html).toContain('id="conditions-list"');
    expect(html).toContain('id="btn-add-condition"');
    expect(html).toContain('id="btn-save-rule"');
  });
});
