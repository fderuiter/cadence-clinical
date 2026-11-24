import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import {
  USDMStudySchema,
  USDMV3StudySchema,
  WrapperSchema,
  validateUsdmGraph,
} from "../src/index";

const cdiscUsdm4Full = JSON.parse(
  readFileSync(
    resolve(__dirname, "../../../tests/fixtures/usdm/cdisc_usdm4_full_1.json"),
    "utf8"
  )
);

const sampleUsdmV3 = JSON.parse(
  readFileSync(
    resolve(__dirname, "../../../tests/fixtures/sample_usdm_v3.json"),
    "utf8"
  )
);

describe("USDM v4 WrapperSchema and USDMStudySchema (v3 + v4)", () => {
  it("validates official CDISC USDM v4 Wrapper payloads that UsdmDesignReader.Select imports", () => {
    const wrapperResult = WrapperSchema.safeParse(cdiscUsdm4Full);
    expect(wrapperResult.success).toBe(true);

    const studyResult = USDMStudySchema.safeParse(cdiscUsdm4Full);
    expect(studyResult.success).toBe(true);

    const graphResult = validateUsdmGraph(cdiscUsdm4Full);
    expect(graphResult.valid).toBe(true);
  });

  it("preserves USDM v3 root-study support in USDMV3StudySchema and USDMStudySchema", () => {
    const v3Direct = USDMV3StudySchema.safeParse(sampleUsdmV3);
    expect(v3Direct.success).toBe(true);

    const v3ViaUnion = USDMStudySchema.safeParse(sampleUsdmV3);
    expect(v3ViaUnion.success).toBe(true);

    // A v3 root-study object is not a v4 Wrapper.
    expect(WrapperSchema.safeParse(sampleUsdmV3).success).toBe(false);
  });

  it("rejects version/wrapper mismatches and incomplete USDM v4 wrappers", () => {
    const invalidCases = [
      { usdmVersion: "3.0.0", study: {} },
      { study: {} },
      { usdmVersion: "4.0.0" },
      { id: "STUDY-4", name: "Root study claiming v4", usdmVersion: "4.0.0" },
      {
        usdmVersion: "4.0.0",
        study: { id: "S1", name: "Empty versions", versions: [] },
      },
      {
        usdmVersion: "4.0.0",
        study: {
          id: "S1",
          name: "Empty designs",
          versions: [{ id: "SV1", studyDesigns: [] }],
        },
      },
    ];

    for (const candidate of invalidCases) {
      expect(WrapperSchema.safeParse(candidate).success).toBe(false);
      expect(USDMStudySchema.safeParse(candidate).success).toBe(false);
    }
  });
});
