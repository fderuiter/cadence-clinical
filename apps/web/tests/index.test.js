import { describe, it, expect } from "vitest";
import { renderFormFromXML, renderFormFromJSON } from "../index.js";

describe("renderFormFromXML", () => {
  it("renders a form with inputs from XML payload", () => {
    const xml = `<form><field id="dob" label="Date of Birth" /><field id="bloodType" label="Blood Type" /></form>`;
    const html = renderFormFromXML(xml);
    expect(html).toContain("Date of Birth");
    expect(html).toContain("Blood Type");
    expect(html).toContain('class="clinical-input');
  });

  it("renders a form when the label attribute precedes the id attribute", () => {
    const xml = `<form><field label="Date of Birth" id="dob" /><field label="Blood Type" id="bloodType" /></form>`;
    const html = renderFormFromXML(xml);
    expect(html).toContain("Date of Birth");
    expect(html).toContain("Blood Type");
    expect(html).toContain('id="dob"');
    expect(html).toContain('id="bloodType"');
  });

  it("renders a form when the id attribute precedes the label attribute", () => {
    const xml = `<form><field id="dob" label="Date of Birth" /><field id="bloodType" label="Blood Type" /></form>`;
    const html = renderFormFromXML(xml);
    expect(html).toContain("Date of Birth");
    expect(html).toContain("Blood Type");
    expect(html).toContain('id="dob"');
    expect(html).toContain('id="bloodType"');
  });

  it("returns empty string if no form tag", () => {
    expect(renderFormFromXML("<root></root>")).toBe("");
  });
});

describe("renderFormFromJSON", () => {
  it("returns empty string on falsy input", () => {
    expect(renderFormFromJSON(null)).toBe("");
    expect(renderFormFromJSON("")).toBe("");
  });

  it("returns error message on malformed JSON string", () => {
    const html = renderFormFromJSON("{invalid: json}");
    expect(html).toContain("Invalid JSON payload configuration");
  });

  it("renders a dynamic grid form matching CDASH definitions (pre-parsed object)", () => {
    const payload = {
      formId: "VS",
      formTitle: "Vital Signs",
      layout: { columns: 12 },
      fields: [
        {
          id: "vssbp",
          label: "Systolic Blood Pressure",
          type: "numeric",
          gridSpan: 6,
          cdash: "VS.VSSBP",
          value: "120",
        },
        {
          id: "vsdpb",
          label: "Diastolic Blood Pressure",
          type: "numeric",
          gridSpan: 6,
          cdash: "VS.VSDPB",
          value: "80",
        },
      ],
    };

    const html = renderFormFromJSON(payload);
    expect(html).toContain(
      '<form class="clinical-form clinical-form-grid" id="form-VS"'
    );
    expect(html).toContain(
      'style="display: grid; grid-template-columns: repeat(12, 1fr);'
    );
    expect(html).toContain(
      '<h2 class="form-title" style="grid-column: span 12;">Vital Signs</h2>'
    );

    // Check first field
    expect(html).toContain('id="field-container-vssbp"');
    expect(html).toContain('style="grid-column: span 6;"');
    expect(html).toContain('data-cdash="VS.VSSBP"');
    expect(html).toContain('value="120"');

    // Check second field
    expect(html).toContain('id="field-container-vsdpb"');
    expect(html).toContain('style="grid-column: span 6;"');
    expect(html).toContain('data-cdash="VS.VSDPB"');
    expect(html).toContain('value="80"');
  });

  it("renders a dynamic form with choice_single / radio buttons from JSON string", () => {
    const payload = JSON.stringify({
      formId: "DM",
      formTitle: "Demographics",
      fields: [
        {
          id: "sex",
          label: "Sex",
          type: "radio",
          gridSpan: 12,
          options: [
            { value: "M", label: "Male" },
            { value: "F", label: "Female" },
          ],
          value: "M",
        },
      ],
    });

    const html = renderFormFromJSON(payload);
    expect(html).toContain(
      '<fieldset class="clinical-radio-grid grid-span-12"'
    );
    expect(html).toContain("<legend>Sex</legend>");
    expect(html).toContain('value="M" checked');
    expect(html).toContain('value="F"');
  });

  it("renders queries and their corresponding site interaction panels directly on the fields", () => {
    const payload = {
      formId: "AE",
      formTitle: "Adverse Events",
      fields: [
        {
          id: "aeterm",
          label: "Adverse Event Term",
          type: "text",
          gridSpan: 12,
          value: "Migraine",
          query: {
            status: "OPEN",
            message: "Please code according to MedDRA dictionary",
            createdBy: "CRA",
            createdAt: "2026-07-22",
          },
        },
      ],
    };

    const html = renderFormFromJSON(payload);
    expect(html).toContain('class="query-flag query-status-open"');
    expect(html).toContain('id="query-panel-aeterm"');
    expect(html).toContain("Please code according to MedDRA dictionary");
    expect(html).toContain('data-action="respond-query"');
  });
});
