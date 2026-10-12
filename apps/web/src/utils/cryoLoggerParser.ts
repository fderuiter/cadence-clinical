export interface TelemetryReading {
  timestamp: string;
  temperatureCelsius: number;
  isExcursion: boolean;
}

export interface ExcursionEvent {
  id: string;
  startTime: string;
  endTime: string;
  durationSeconds: number;
  durationFormatted: string;
  peakTemperature: number;
  type: "WARM_EXCURSION" | "COLD_EXCURSION";
  sampleCount: number;
  startSampleIndex: number;
  endSampleIndex: number;
}

export interface ParsedCryoLoggerResult {
  fileName: string;
  vendor: "Sensitech" | "TempTale" | "Generic";
  totalSamples: number;
  minTemperature: number;
  maxTemperature: number;
  excursionCount: number;
  excursionEvents: ExcursionEvent[];
  isDownsampled: boolean;
  rawSampleCount: number;
  keypoints: TelemetryReading[];
  sampleReadings: TelemetryReading[];
}

export const CRYO_TEMP_SAFE_MIN = -196.0;
export const CRYO_TEMP_SAFE_MAX = -70.0;

export function formatDuration(seconds: number): string {
  if (isNaN(seconds) || seconds <= 0) return "0s";
  if (seconds < 60) return `${Math.round(seconds)}s`;
  const minutes = Math.floor(seconds / 60);
  const remainingSec = Math.round(seconds % 60);
  if (minutes < 60) {
    return remainingSec > 0 ? `${minutes}m ${remainingSec}s` : `${minutes}m`;
  }
  const hours = Math.floor(minutes / 60);
  const remainingMin = minutes % 60;
  return remainingMin > 0 ? `${hours}h ${remainingMin}m` : `${hours}h`;
}

function parseTemperatureValue(rawVal: any): number | null {
  if (typeof rawVal === "number") {
    return isFinite(rawVal) ? rawVal : null;
  }
  if (typeof rawVal === "string") {
    const cleaned = rawVal.replace(/[^\d.-]/g, "").trim();
    if (!cleaned) return null;
    const num = parseFloat(cleaned);
    return isNaN(num) ? null : num;
  }
  return null;
}

function parseTimestampValue(rawVal: any): string | null {
  if (!rawVal) return null;
  if (typeof rawVal === "number") {
    const d = new Date(rawVal);
    return isNaN(d.getTime()) ? null : d.toISOString();
  }
  if (typeof rawVal === "string") {
    const str = rawVal.trim();
    if (!str) return null;
    const d = new Date(str);
    if (!isNaN(d.getTime())) return d.toISOString();
    
    // Try parsing US format MM/DD/YYYY HH:mm:ss or DD-MMM-YYYY
    const parts = str.split(/[\s,T]+/);
    if (parts.length >= 2) {
      const datePart = parts[0];
      const timePart = parts[1];
      const dateSub = datePart.split(/[/.-]/);
      if (dateSub.length === 3) {
        // Assume MM/DD/YYYY if slash or hyphen
        let year = parseInt(dateSub[2], 10);
        let month = parseInt(dateSub[0], 10) - 1;
        let day = parseInt(dateSub[1], 10);
        if (year < 100) year += 2000;
        const timeSub = timePart.split(":");
        if (timeSub.length >= 2) {
          const hh = parseInt(timeSub[0], 10);
          const mm = parseInt(timeSub[1], 10);
          const ss = timeSub.length > 2 ? parseInt(timeSub[2], 10) : 0;
          const d2 = new Date(Date.UTC(year, month, day, hh, mm, ss));
          if (!isNaN(d2.getTime())) return d2.toISOString();
        }
      }
    }
  }
  return null;
}

export async function parseCryoLoggerFile(
  fileContent: string,
  fileName: string = "logger_export.csv"
): Promise<ParsedCryoLoggerResult> {
  if (!fileContent || !fileContent.trim()) {
    throw new Error("Invalid or corrupted logger file: File content is empty.");
  }

  const trimmed = fileContent.trim();
  let vendor: "Sensitech" | "TempTale" | "Generic" = "Generic";
  if (/sensitech/i.test(trimmed) || /sensitech/i.test(fileName)) {
    vendor = "Sensitech";
  } else if (/temptale/i.test(trimmed) || /temptale/i.test(fileName)) {
    vendor = "TempTale";
  }

  let readings: TelemetryReading[] = [];

  // 1. Try parsing as JSON first
  const isJsonCandidate =
    fileName.toLowerCase().endsWith(".json") ||
    trimmed.startsWith("{") ||
    trimmed.startsWith("[");

  if (isJsonCandidate) {
    try {
      const parsedJson = JSON.parse(trimmed);
      readings = await parseJsonTelemetry(parsedJson);
    } catch (err: any) {
      if (fileName.toLowerCase().endsWith(".json")) {
        throw new Error(`Invalid or corrupted JSON logger file: ${err.message || err}`, {
          cause: err,
        });
      }
    }
  }

  // 2. If no readings extracted from JSON, try CSV parsing
  if (readings.length === 0) {
    readings = await parseCsvTelemetry(trimmed, vendor);
  }

  if (readings.length === 0) {
    throw new Error(
      `Invalid or corrupted logger file '${fileName}': No valid time-series temperature readings found.`
    );
  }

  // Calculate statistics
  let minTemperature = Infinity;
  let maxTemperature = -Infinity;
  let excursionCount = 0;

  for (let i = 0; i < readings.length; i++) {
    const temp = readings[i].temperatureCelsius;
    if (temp < minTemperature) minTemperature = temp;
    if (temp > maxTemperature) maxTemperature = temp;
    if (readings[i].isExcursion) excursionCount++;
  }

  // Detect contiguous excursion events
  const excursionEvents: ExcursionEvent[] = [];
  let currentEventStart: number | null = null;
  let currentEventType: "WARM_EXCURSION" | "COLD_EXCURSION" | null = null;

  for (let i = 0; i < readings.length; i++) {
    const r = readings[i];
    if (r.isExcursion) {
      const type: "WARM_EXCURSION" | "COLD_EXCURSION" =
        r.temperatureCelsius > CRYO_TEMP_SAFE_MAX ? "WARM_EXCURSION" : "COLD_EXCURSION";

      if (currentEventStart === null) {
        currentEventStart = i;
        currentEventType = type;
      } else if (currentEventType !== type) {
        // Finalize previous excursion event
        excursionEvents.push(
          buildExcursionEvent(
            readings,
            currentEventStart,
            i - 1,
            currentEventType!,
            excursionEvents.length + 1
          )
        );
        currentEventStart = i;
        currentEventType = type;
      }
    } else if (currentEventStart !== null) {
      excursionEvents.push(
        buildExcursionEvent(
          readings,
          currentEventStart,
          i - 1,
          currentEventType!,
          excursionEvents.length + 1
        )
      );
      currentEventStart = null;
      currentEventType = null;
    }
  }

  if (currentEventStart !== null) {
    excursionEvents.push(
      buildExcursionEvent(
        readings,
        currentEventStart,
        readings.length - 1,
        currentEventType!,
        excursionEvents.length + 1
      )
    );
  }

  // Downsampling logic for large datasets (> 50,000 readings)
  const rawSampleCount = readings.length;
  const isDownsampled = rawSampleCount > 50000;
  const keypoints = isDownsampled
    ? downsampleKeypoints(readings, excursionEvents, 1000)
    : [...readings];

  return {
    fileName,
    vendor,
    totalSamples: rawSampleCount,
    minTemperature,
    maxTemperature,
    excursionCount,
    excursionEvents,
    isDownsampled,
    rawSampleCount,
    keypoints,
    sampleReadings: readings.slice(0, 50),
  };
}

function buildExcursionEvent(
  readings: TelemetryReading[],
  startIdx: number,
  endIdx: number,
  type: "WARM_EXCURSION" | "COLD_EXCURSION",
  eventNum: number
): ExcursionEvent {
  const startTime = readings[startIdx].timestamp;
  const endTime = readings[endIdx].timestamp;
  const startMs = new Date(startTime).getTime();
  const endMs = new Date(endTime).getTime();
  const durationSeconds = Math.max(0, (endMs - startMs) / 1000);

  let peakTemp = readings[startIdx].temperatureCelsius;
  for (let i = startIdx + 1; i <= endIdx; i++) {
    const temp = readings[i].temperatureCelsius;
    if (type === "WARM_EXCURSION") {
      if (temp > peakTemp) peakTemp = temp;
    } else {
      if (temp < peakTemp) peakTemp = temp;
    }
  }

  return {
    id: `EXC-${eventNum}`,
    startTime,
    endTime,
    durationSeconds,
    durationFormatted: formatDuration(durationSeconds),
    peakTemperature: peakTemp,
    type,
    sampleCount: endIdx - startIdx + 1,
    startSampleIndex: startIdx,
    endSampleIndex: endIdx,
  };
}

async function parseJsonTelemetry(jsonObj: any): Promise<TelemetryReading[]> {
  const readings: TelemetryReading[] = [];
  let rawItems: any[] = [];

  if (Array.isArray(jsonObj)) {
    rawItems = jsonObj;
  } else if (typeof jsonObj === "object" && jsonObj !== null) {
    const possibleKeys = [
      "readings",
      "data",
      "dataPoints",
      "timeSeries",
      "telemetry",
      "logs",
      "samples",
    ];
    for (const k of possibleKeys) {
      if (Array.isArray(jsonObj[k])) {
        rawItems = jsonObj[k];
        break;
      }
    }
  }

  const batchSize = 5000;
  for (let i = 0; i < rawItems.length; i++) {
    if (i > 0 && i % batchSize === 0) {
      await new Promise((r) => setTimeout(r, 0));
    }
    const item = rawItems[i];
    if (typeof item !== "object" || item === null) continue;

    const timeVal =
      item.timestamp ??
      item.time ??
      item.date ??
      item.dateTime ??
      item.recordedAt ??
      item.readingDate ??
      item.recorded_at;

    const tempVal =
      item.temperatureCelsius ??
      item.temperature ??
      item.temp ??
      item.tempC ??
      item.celsius ??
      item.value ??
      item.temp_celsius;

    const parsedTime = parseTimestampValue(timeVal);
    const parsedTemp = parseTemperatureValue(tempVal);

    if (parsedTime && parsedTemp !== null) {
      readings.push({
        timestamp: parsedTime,
        temperatureCelsius: parsedTemp,
        isExcursion: parsedTemp > CRYO_TEMP_SAFE_MAX || parsedTemp < CRYO_TEMP_SAFE_MIN,
      });
    }
  }

  return readings;
}

async function parseCsvTelemetry(
  content: string,
  _detectedVendor: "Sensitech" | "TempTale" | "Generic"
): Promise<TelemetryReading[]> {
  const lines = content.split(/\r?\n/);
  const readings: TelemetryReading[] = [];

  let headerRowIndex = -1;
  let timeColIdx = -1;
  let tempColIdx = -1;
  let delimiter = ",";

  const batchSize = 5000;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (!line) continue;

    // Test delimiter
    let cols = line.split(",");
    if (cols.length < 2) cols = line.split("\t");
    if (cols.length < 2) cols = line.split(";");

    const colLower = cols.map((c) => c.trim().toLowerCase());

    const hasTimeHeader = colLower.some(
      (c) =>
        c.includes("time") ||
        c.includes("date") ||
        c.includes("timestamp") ||
        c.includes("recorded")
    );
    const hasTempHeader = colLower.some(
      (c) =>
        c.includes("temp") ||
        c.includes("celsius") ||
        c.includes("°c") ||
        c.includes("degree") ||
        c === "c"
    );

    if (hasTimeHeader && hasTempHeader) {
      headerRowIndex = i;
      delimiter = line.includes("\t") ? "\t" : line.includes(";") ? ";" : ",";
      timeColIdx = colLower.findIndex(
        (c) =>
          c.includes("time") ||
          c.includes("date") ||
          c.includes("timestamp") ||
          c.includes("recorded")
      );
      tempColIdx = colLower.findIndex(
        (c) =>
          c.includes("temp") ||
          c.includes("celsius") ||
          c.includes("°c") ||
          c.includes("degree") ||
          c === "c"
      );
      break;
    }
  }

  // Fallback if no explicit header row found: scan for rows containing date + numeric float
  const startRow = headerRowIndex >= 0 ? headerRowIndex + 1 : 0;

  for (let i = startRow; i < lines.length; i++) {
    if (i > startRow && i % batchSize === 0) {
      await new Promise((r) => setTimeout(r, 0));
    }

    const line = lines[i].trim();
    if (!line || line.startsWith("#") || line.startsWith("//")) continue;

    const cols = line.split(delimiter).map((c) => c.trim());

    if (headerRowIndex >= 0 && timeColIdx >= 0 && tempColIdx >= 0) {
      if (cols.length > Math.max(timeColIdx, tempColIdx)) {
        const timeVal = parseTimestampValue(cols[timeColIdx]);
        const tempVal = parseTemperatureValue(cols[tempColIdx]);

        if (timeVal && tempVal !== null) {
          readings.push({
            timestamp: timeVal,
            temperatureCelsius: tempVal,
            isExcursion: tempVal > CRYO_TEMP_SAFE_MAX || tempVal < CRYO_TEMP_SAFE_MIN,
          });
        }
      }
    } else {
      // Unstructured line parsing fallback
      const timeVal = cols.map(parseTimestampValue).find((v) => v !== null);
      const tempVal = cols.map(parseTemperatureValue).find((v) => v !== null);

      if (timeVal && tempVal !== null && tempVal !== undefined) {
        readings.push({
          timestamp: timeVal,
          temperatureCelsius: tempVal,
          isExcursion: tempVal > CRYO_TEMP_SAFE_MAX || tempVal < CRYO_TEMP_SAFE_MIN,
        });
      }
    }
  }

  return readings;
}

function downsampleKeypoints(
  readings: TelemetryReading[],
  excursionEvents: ExcursionEvent[],
  targetMax: number = 1000
): TelemetryReading[] {
  const keyIndexSet = new Set<number>();

  // 1. Always include first and last points
  keyIndexSet.add(0);
  keyIndexSet.add(readings.length - 1);

  // 2. Global min and max points
  let minIdx = 0;
  let maxIdx = 0;
  for (let i = 1; i < readings.length; i++) {
    if (readings[i].temperatureCelsius < readings[minIdx].temperatureCelsius) minIdx = i;
    if (readings[i].temperatureCelsius > readings[maxIdx].temperatureCelsius) maxIdx = i;
  }
  keyIndexSet.add(minIdx);
  keyIndexSet.add(maxIdx);

  // 3. Excursion boundary and peak points
  for (const event of excursionEvents) {
    keyIndexSet.add(event.startSampleIndex);
    keyIndexSet.add(event.endSampleIndex);

    // Add peak point in event
    let peakIdx = event.startSampleIndex;
    for (let i = event.startSampleIndex + 1; i <= event.endSampleIndex; i++) {
      if (event.type === "WARM_EXCURSION") {
        if (readings[i].temperatureCelsius > readings[peakIdx].temperatureCelsius) peakIdx = i;
      } else {
        if (readings[i].temperatureCelsius < readings[peakIdx].temperatureCelsius) peakIdx = i;
      }
    }
    keyIndexSet.add(peakIdx);
  }

  // 4. Uniform sampling for remainder
  const step = Math.max(1, Math.floor(readings.length / targetMax));
  for (let i = 0; i < readings.length; i += step) {
    keyIndexSet.add(i);
  }

  const sortedIndices = Array.from(keyIndexSet).sort((a, b) => a - b);
  return sortedIndices.map((idx) => readings[idx]);
}
