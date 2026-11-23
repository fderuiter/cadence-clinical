import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: safety-labs-dct", () => {
  const api = useDemoSliceServer();

  it("evaluates lab ranges, LOINC unit conversion, CTCAE toxicity grades, sensor telemetry, and DCT metrics", async () => {
    const ranges = await api.requestJson("/api/v1/execution/lab-ranges");
    expect(ranges.status).toBe(200);
    expect(ranges.data[0].loinc_code).toBe("1742-6");

    const converted = await api.requestJson(
      "/api/v1/execution/labs/convert-unit",
      {
        method: "POST",
        body: JSON.stringify({ value: 1.2, to_unit: "umol/L" }),
      }
    );
    expect(converted.status).toBe(200);
    expect(converted.data.converted_value).toBe(106.08);

    const grade3 = await api.requestJson(
      "/api/v1/execution/labs/toxicity/evaluate",
      {
        method: "POST",
        body: JSON.stringify({ value: 240, ref_high: 40 }),
      }
    );
    expect(grade3.status).toBe(200);
    expect(grade3.data.ctcae_grade).toBe(3);
    expect(grade3.data.is_critical_ae_trigger).toBe(true);

    const sensors = await api.requestJson(
      "/api/v1/interop/sensors/metrics/DEMO-01"
    );
    expect(sensors.status).toBe(200);
    expect(sensors.data.data.metrics.active_devices).toBe(2);

    const dct = await api.requestJson("/api/v1/interop/dct/metrics/DEMO-01");
    expect(dct.status).toBe(200);
    expect(dct.data.total_dtp_shipments).toBe(2);
  });
});
