import { describe, it, expect } from "vitest";
import {
  parseCryoLoggerFile,
  formatDuration,
  CRYO_TEMP_SAFE_MIN,
  CRYO_TEMP_SAFE_MAX,
} from "@/utils/cryoLoggerParser";

describe("cryoLoggerParser.ts Unit Tests", () => {
  it("formats durations correctly", () => {
    expect(formatDuration(30)).toBe("30s");
    expect(formatDuration(90)).toBe("1m 30s");
    expect(formatDuration(3600)).toBe("1h");
    expect(formatDuration(3750)).toBe("1h 2m");
  });

  it("parses Sensitech CSV telemetry files with safe cryogenic temperatures", async () => {
    const csvContent = `
Sensitech Cold Chain Telemetry Report
Serial Number: SENS-99201

Timestamp,Temperature (°C),Status
2026-10-12T10:00:00Z,-195.0,OK
2026-10-12T10:05:00Z,-194.5,OK
2026-10-12T10:10:00Z,-190.2,OK
2026-10-12T10:15:00Z,-185.0,OK
    `.trim();

    const result = await parseCryoLoggerFile(csvContent, "Sensitech_Log_001.csv");

    expect(result.vendor).toBe("Sensitech");
    expect(result.totalSamples).toBe(4);
    expect(result.minTemperature).toBe(-195.0);
    expect(result.maxTemperature).toBe(-185.0);
    expect(result.excursionCount).toBe(0);
    expect(result.excursionEvents).toHaveLength(0);
    expect(result.isDownsampled).toBe(false);
  });

  it("parses TempTale CSV files and detects warm temperature excursions (> -70.0°C)", async () => {
    const csvContent = `
TempTale 16 Direct Telemetry Export
Device ID: TT-88210

Date Time,Temp (°C)
2026-10-12T08:00:00Z,-190.0
2026-10-12T08:15:00Z,-180.0
2026-10-12T08:30:00Z,-65.0
2026-10-12T08:45:00Z,-55.0
2026-10-12T09:00:00Z,-185.0
    `.trim();

    const result = await parseCryoLoggerFile(csvContent, "TempTale_Export.csv");

    expect(result.vendor).toBe("TempTale");
    expect(result.totalSamples).toBe(5);
    expect(result.minTemperature).toBe(-190.0);
    expect(result.maxTemperature).toBe(-55.0);
    expect(result.excursionCount).toBe(2);
    expect(result.excursionEvents).toHaveLength(1);

    const exc = result.excursionEvents[0];
    expect(exc.type).toBe("WARM_EXCURSION");
    expect(exc.peakTemperature).toBe(-55.0);
    expect(exc.startTime).toBe(new Date("2026-10-12T08:30:00Z").toISOString());
    expect(exc.endTime).toBe(new Date("2026-10-12T08:45:00Z").toISOString());
    expect(exc.durationSeconds).toBe(900);
    expect(exc.durationFormatted).toBe("15m");
  });

  it("parses JSON telemetry payloads and identifies cold excursions (< -196.0°C)", async () => {
    const jsonContent = JSON.stringify({
      device: "Sensitech-LN2",
      readings: [
        { timestamp: "2026-10-12T12:00:00Z", temperature: -180.0 },
        { timestamp: "2026-10-12T12:01:00Z", temperature: -198.5 },
        { timestamp: "2026-10-12T12:02:00Z", temperature: -180.0 },
      ],
    });

    const result = await parseCryoLoggerFile(jsonContent, "sensitech_data.json");

    expect(result.vendor).toBe("Sensitech");
    expect(result.totalSamples).toBe(3);
    expect(result.minTemperature).toBe(-198.5);
    expect(result.excursionCount).toBe(1);
    expect(result.excursionEvents).toHaveLength(1);

    const exc = result.excursionEvents[0];
    expect(exc.type).toBe("COLD_EXCURSION");
    expect(exc.peakTemperature).toBe(-198.5);
  });

  it("handles downsampling for large telemetry files (> 50,000 data points)", async () => {
    const rawReadings: string[] = ["Timestamp,Temperature (°C)"];
    const baseMs = new Date("2026-10-01T00:00:00Z").getTime();

    for (let i = 0; i < 50010; i++) {
      const timeStr = new Date(baseMs + i * 1000).toISOString();
      let temp = -180.0;
      if (i === 10000) temp = -60.0; // WARM EXCURSION
      if (i === 20000) temp = -200.0; // COLD EXCURSION
      rawReadings.push(`${timeStr},${temp}`);
    }

    const csvContent = rawReadings.join("\n");
    const result = await parseCryoLoggerFile(csvContent, "Large_Sensitech_50k.csv");

    expect(result.totalSamples).toBe(50010);
    expect(result.isDownsampled).toBe(true);
    expect(result.keypoints.length).toBeLessThanOrEqual(1000 + 10);
    expect(result.minTemperature).toBe(-200.0);
    expect(result.maxTemperature).toBe(-60.0);
    expect(result.excursionEvents).toHaveLength(2);
  });

  it("throws descriptive error for invalid or empty files", async () => {
    await expect(parseCryoLoggerFile("", "empty.csv")).rejects.toThrow(
      "Invalid or corrupted logger file: File content is empty."
    );

    await expect(
      parseCryoLoggerFile("No telemetry data here\nJust random text", "random.txt")
    ).rejects.toThrow("No valid time-series temperature readings found.");
  });
});
