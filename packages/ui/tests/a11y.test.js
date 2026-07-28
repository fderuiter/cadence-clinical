// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import axe from "axe-core";
import {
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalVisitMatrix,
  createClinicalSoAMatrix,
  createClinicalQueryFlag,
  createQueryPanel,
  createConditionRow,
  createRuleEditorContainer,
  createClinicalLookupInput,
} from "../index.js";

// Helper function to render HTML string into a container and run axe
async function runAxe(htmlString) {
  const container = document.createElement("div");
  container.innerHTML = htmlString;
  document.body.appendChild(container);

  try {
    const results = await axe.run(container, {
      rules: {
        // Disable page-level rules that do not make sense for single components in-memory
        "document-title": { enabled: false },
        "html-has-lang": { enabled: false },
        "landmark-one-main": { enabled: false },
        "page-has-heading-one": { enabled: false },
        region: { enabled: false },
        "color-contrast": { enabled: false }, // jsdom does not support style/layout calculation
      },
    });
    return results.violations;
  } finally {
    document.body.removeChild(container);
  }
}

describe("In-Memory accessibility audits for shared UI components", () => {
  it("createClinicalInput has zero accessibility violations", async () => {
    const html = createClinicalInput("patientName", "Patient Name");
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createClinicalRadioGrid has zero accessibility violations", async () => {
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
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createClinicalVisitMatrix has zero accessibility violations", async () => {
    const matrixData = {
      visits: ["Screening", "Week 2"],
      forms: [{ name: "Demographics", statuses: ["Complete", "N/A"] }],
    };
    const html = createClinicalVisitMatrix(matrixData);
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createClinicalSoAMatrix has zero accessibility violations", async () => {
    const soaData = {
      arms: [{ arm_id: "ARM-A", arm_name: "Arm A" }],
      epochs: [{ epoch_id: "EP-1", epoch_name: "Epoch 1", arm_id: "ARM-A" }],
      encounters: [
        { encounter_id: "E1", encounter_name: "Encounter 1", epoch_id: "EP-1" },
      ],
      rows: [
        {
          activity_id: "ACT1",
          activity_name: "Procedure 1",
          cells: [
            { encounter_id: "E1", is_applicable: true, details: "Mandatory" },
          ],
        },
      ],
    };
    const html = createClinicalSoAMatrix(soaData);
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createClinicalQueryFlag has zero accessibility violations", async () => {
    const html = createClinicalQueryFlag("testField", { status: "OPEN" });
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createQueryPanel has zero accessibility violations", async () => {
    const query = {
      status: "OPEN",
      message: "Discrepancy message",
      createdBy: "CRA",
    };
    const html = createQueryPanel("fieldX", query);
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createConditionRow has zero accessibility violations", async () => {
    const mockForms = [{ id: "form_dm", name: "Demographics" }];
    const mockFields = [{ id: "brthdt", name: "Date of Birth" }];
    const html = createConditionRow(0, mockForms, mockFields);
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createRuleEditorContainer has zero accessibility violations", async () => {
    const mockForms = [{ id: "form_dm", name: "Demographics" }];
    const mockFields = [{ id: "brthdt", name: "Date of Birth" }];
    const html = createRuleEditorContainer(mockForms, mockFields);
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });

  it("createClinicalLookupInput has zero accessibility violations", async () => {
    const html = createClinicalLookupInput(
      "meddraCode",
      "MedDRA Code",
      "",
      "loading"
    );
    const violations = await runAxe(html);
    expect(violations).toEqual([]);
  });
});
