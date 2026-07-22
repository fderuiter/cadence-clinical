import { describe, it, expect } from "vitest";
import { renderFormFromXML } from "../index.js";

describe("renderFormFromXML", () => {
  it("renders a form with inputs from XML payload", () => {
    const xml = `<form><field id="dob" label="Date of Birth" /><field id="bloodType" label="Blood Type" /></form>`;
    const html = renderFormFromXML(xml);
    expect(html).toContain("Date of Birth");
    expect(html).toContain("Blood Type");
    expect(html).toContain('class="clinical-input"');
  });

  it("returns empty string if no form tag", () => {
    expect(renderFormFromXML("<root></root>")).toBe("");
  });
});
