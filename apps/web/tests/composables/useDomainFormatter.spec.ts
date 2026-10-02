import { describe, it, expect } from "vitest";
import { useDomainFormatter } from "@/composables/useDomainFormatter";

describe("useDomainFormatter composable", () => {
  const { formatLabel, formatDatabaseKey, formatSubjectId, formatFacilityCode } =
    useDomainFormatter();

  it("translates raw database column keys into clinical terminology", () => {
    expect(formatLabel("subject_id")).toBe("Subject Identifier");
    expect(formatLabel("collection_site_id")).toBe("Collection Site");
    expect(formatLabel("manufacturing_facility_id")).toBe("Manufacturing Facility");
    expect(formatLabel("study_id")).toBe("Study Protocol");
    expect(formatLabel("site_id")).toBe("Site Identifier");
    expect(formatLabel("batch_id")).toBe("Manufacturing Batch");
    expect(formatLabel("Subject ID / UUID")).toBe("Subject Identifier");
    expect(formatLabel("Subject UUID")).toBe("Subject Identifier");
  });

  it("converts unmapped snake_case keys to Title Case", () => {
    expect(formatLabel("cryo_storage_temperature")).toBe("Cryo Storage Temperature");
    expect(formatDatabaseKey("lowest_temp_celsius")).toBe("Lowest Temp Celsius");
  });

  it("handles empty or null values gracefully", () => {
    expect(formatLabel("")).toBe("");
    expect(formatSubjectId(null)).toBe("N/A");
    expect(formatFacilityCode(undefined)).toBe("N/A");
  });
});
