import { describe, it, expect } from "vitest";
import { createClinicalInput } from "../index.js";

describe("createClinicalInput", () => {
  it("renders an input with the given label", () => {
    const html = createClinicalInput("patientName", "Patient Name");
    expect(html).toContain("Patient Name");
    expect(html).toContain('id="patientName"');
  });
});
