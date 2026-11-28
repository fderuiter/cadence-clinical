import { describe, it, expect } from "vitest";
import {
  MISSING_REASONS,
  formatObservationValue,
  correctionUnit,
  isUnrecordedValue,
  parseCorrection,
  parseFormEntry,
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

describe("parseFormEntry (ADR-2622)", () => {
  it("keeps the kind of an item's current value", () => {
    expect(
      parseFormEntry("130", {
        value_kind: "Quantity",
        unit: "mm[Hg]",
        value: { type: "quantity", value: 120, unit: "mm[Hg]" },
      })
    ).toEqual({
      ok: true,
      value: {
        type: "quantity",
        value: "130",
        unit: "mm[Hg]",
        comparator: null,
      },
    });
  });

  it("reads a first value by the item's value kind", () => {
    const item = (value_kind: string | null, unit: string | null = null) => ({
      value_kind,
      unit,
      value: null,
    });
    expect(parseFormEntry("82", item("Quantity", "mm[Hg]"))).toMatchObject({
      ok: true,
      value: { type: "quantity", value: "82", unit: "mm[Hg]" },
    });
    expect(parseFormEntry("yes", item("Boolean"))).toEqual({
      ok: true,
      value: { type: "boolean", value: true },
    });
    expect(parseFormEntry("2027-03-14", item("DateTime"))).toEqual({
      ok: true,
      value: { type: "dateTime", value: "2027-03-14" },
    });
    expect(parseFormEntry("note", item(null))).toEqual({
      ok: true,
      value: { type: "text", value: "note" },
    });
    expect(parseFormEntry("82", item("Quantity"))).toEqual({
      ok: false,
      message: "This item names no unit.",
    });
    expect(parseFormEntry("C123", item("Coded")).ok).toBe(false);
  });

  it("parses initial and edited coded entries with hydrated code_system_version_id and codelist_options", () => {
    const codelistItem = {
      value_kind: "Coded",
      unit: null,
      code_system_version_id: CODE_SYSTEM,
      codelist: "C71148",
      codelist_options: [
        { code: "SITTING", display: "Sitting" },
        { code: "SUPINE", display: "Supine" },
      ],
      value: null,
    };

    expect(parseFormEntry("SITTING", codelistItem)).toEqual({
      ok: true,
      value: {
        type: "coded",
        code_system_version_id: CODE_SYSTEM,
        code: "SITTING",
      },
    });

    const invalid = parseFormEntry("STANDING", codelistItem);
    expect(invalid.ok).toBe(false);
    if (!invalid.ok) {
      expect(invalid.message).toContain("C71148");
    }

    // Pinned code_system_version_id with null codelist allows arbitrary codes for server-side LookupAsync check.
    expect(
      parseFormEntry("LLT_10019211", {
        value_kind: "Coded",
        unit: null,
        code_system_version_id: CODE_SYSTEM,
        codelist: null,
        codelist_options: null,
        value: null,
      })
    ).toEqual({
      ok: true,
      value: {
        type: "coded",
        code_system_version_id: CODE_SYSTEM,
        code: "LLT_10019211",
      },
    });
  });

  it("validates codelist_options in parseCorrection for missing and coded observations", () => {
    const spec = {
      valueKind: "Coded",
      codeSystemVersionId: CODE_SYSTEM,
      codelist: "C71148",
      codelistOptions: [
        { code: "SITTING", display: "Sitting" },
        { code: "SUPINE", display: "Supine" },
      ],
    };

    expect(
      parseCorrection("SUPINE", { type: "missing", reason: "NotAsked" }, spec)
    ).toEqual({
      ok: true,
      value: {
        type: "coded",
        code_system_version_id: CODE_SYSTEM,
        code: "SUPINE",
      },
    });
    expect(
      parseCorrection("STANDING", { type: "missing", reason: "NotAsked" }, spec)
        .ok
    ).toBe(false);

    expect(
      parseCorrection(
        "STANDING",
        {
          type: "coded",
          code_system_version_id: CODE_SYSTEM,
          code: "SITTING",
        },
        spec
      ).ok
    ).toBe(false);
  });

  it("offers the missing reasons a user may record, not the kernel's Masked or NotAsked", () => {
    expect(MISSING_REASONS.map((r) => r.reason)).not.toContain("Masked");
    expect(MISSING_REASONS.map((r) => r.reason)).not.toContain("NotAsked");
    expect(MISSING_REASONS).toContainEqual({
      reason: "NotDone",
      text: "Not done",
    });
    expect(isUnrecordedValue(null)).toBe(true);
    expect(isUnrecordedValue({ type: "missing", reason: "NotAsked" })).toBe(
      true
    );
    expect(
      isUnrecordedValue({
        type: "missing",
        reason: "not_asked" as unknown as "NotAsked",
      })
    ).toBe(true);
    expect(isUnrecordedValue({ type: "missing", reason: "NotDone" })).toBe(
      false
    );
    expect(
      isUnrecordedValue({ type: "quantity", value: 120, unit: "mm[Hg]" })
    ).toBe(false);
  });

  it("compares missing observation detail canonically (K-FACT-3)", () => {
    expect(
      sameObservationValue(
        { type: "missing", reason: "NotDone", detail: "   " },
        { type: "missing", reason: "NotDone" }
      )
    ).toBe(true);
    expect(
      sameObservationValue(
        { type: "missing", reason: "NotDone", detail: "Patient refused" },
        { type: "missing", reason: "NotDone" }
      )
    ).toBe(false);
    expect(
      sameObservationValue(
        { type: "missing", reason: "NotDone", detail: "Patient refused" },
        { type: "missing", reason: "NotDone", detail: "  Patient refused  " }
      )
    ).toBe(true);
    expect(
      sameObservationValue(
        { type: "missing", reason: "NotDone", detail: "Patient refused" },
        { type: "missing", reason: "NotDone", detail: "Equipment malfunction" }
      )
    ).toBe(false);
  });
});

describe("ExpectedItemSpec missing-value correction & quantity preservation (AC 5)", () => {
  it("enforces expected Quantity unit when correcting a NotAsked observation and preserves unit on later form edits", () => {
    const notAsked: KernelObservationValue = {
      type: "missing",
      reason: "NotAsked",
    };
    const spec = { valueKind: "Quantity", unit: "mm[Hg]" };

    expect(correctionUnit(notAsked, spec)).toBe("mm[Hg]");
    expect(parseCorrection("free text", notAsked, spec).ok).toBe(false);
    const corrected = parseCorrection("124", notAsked, spec);
    expect(corrected).toEqual({
      ok: true,
      value: {
        type: "quantity",
        value: "124",
        unit: "mm[Hg]",
        comparator: null,
      },
    });

    if (corrected.ok) {
      const nextEdit = parseFormEntry("128", {
        value_kind: "Quantity",
        unit: "mm[Hg]",
        value: corrected.value,
      });
      expect(nextEdit).toEqual({
        ok: true,
        value: {
          type: "quantity",
          value: "128",
          unit: "mm[Hg]",
          comparator: null,
        },
      });
    }

    expect(
      parseFormEntry("132", {
        value_kind: "Quantity",
        unit: "mm[Hg]",
        value: { type: "text", value: "124" },
      })
    ).toEqual({
      ok: true,
      value: {
        type: "quantity",
        value: "132",
        unit: "mm[Hg]",
        comparator: null,
      },
    });
  });
});
