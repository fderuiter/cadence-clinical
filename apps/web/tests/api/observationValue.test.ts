import { describe, it, expect } from "vitest";
import {
  formatObservationValue,
  parseCorrection,
  sameObservationValue,
  type KernelObservationValue,
} from "@/api/observationValue";

const CODE_SYSTEM = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72";

describe("observation values (ADR-2623)", () => {
  it.each<[KernelObservationValue, string]>([
    [{ type: "quantity", value: 182, unit: "mm[Hg]" }, "182 mm[Hg]"],
    [
      { type: "quantity", value: 1, unit: "mg/dL", comparator: "<" },
      "<1 mg/dL",
    ],
    [
      { type: "coded", code_system_version_id: CODE_SYSTEM, code: "C25301" },
      "C25301",
    ],
    [{ type: "text", value: "Mild headache" }, "Mild headache"],
    [{ type: "boolean", value: false }, "No"],
    [{ type: "dateTime", value: "2026-10" }, "2026-10"],
    [
      { type: "missing", reason: "NotDone", detail: "Patient refused" },
      "Not done: Patient refused",
    ],
    [{ type: "missing", reason: "AskedButUnknown" }, "Asked but unknown"],
  ])("shows %j as %s", (value, text) => {
    expect(formatObservationValue(value)).toBe(text);
  });

  it("corrects a quantity in its unit and keeps the precision typed", () => {
    expect(
      parseCorrection(" >= 1.50 ", {
        type: "quantity",
        value: 2,
        unit: "mmol/L",
      })
    ).toEqual({
      ok: true,
      value: {
        type: "quantity",
        value: "1.50",
        unit: "mmol/L",
        comparator: ">=",
      },
    });
    expect(
      parseCorrection("1,5", { type: "quantity", value: 2, unit: "mmol/L" }).ok
    ).toBe(false);
  });

  it("corrects a code in its code system version, and a missing value as text", () => {
    expect(
      parseCorrection("C25302", {
        type: "coded",
        code_system_version_id: CODE_SYSTEM,
        code: "C25301",
      })
    ).toEqual({
      ok: true,
      value: {
        type: "coded",
        code_system_version_id: CODE_SYSTEM,
        code: "C25302",
      },
    });
    expect(parseCorrection("yes", { type: "boolean", value: false })).toEqual({
      ok: true,
      value: { type: "boolean", value: true },
    });
    expect(parseCorrection("maybe", { type: "boolean", value: false }).ok).toBe(
      false
    );
    expect(
      parseCorrection("72", { type: "missing", reason: "NotDone" })
    ).toEqual({
      ok: true,
      value: { type: "text", value: "72" },
    });
  });

  it("treats equal quantities and reordered fields as the same value", () => {
    expect(
      sameObservationValue(
        { type: "quantity", value: "182.0", unit: "mm[Hg]", comparator: null },
        { type: "quantity", value: 182, unit: "mm[Hg]" }
      )
    ).toBe(true);
    expect(
      sameObservationValue(
        { code: "C1", type: "coded", code_system_version_id: CODE_SYSTEM },
        { type: "coded", code_system_version_id: CODE_SYSTEM, code: "C1" }
      )
    ).toBe(true);
    expect(
      sameObservationValue(
        { type: "text", value: "a" },
        { type: "text", value: "b" }
      )
    ).toBe(false);
  });
});
