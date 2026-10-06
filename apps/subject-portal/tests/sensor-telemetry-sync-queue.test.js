import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import {
  openDatabase,
  queueSensorTelemetryBatch,
  saveSensorTelemetryBatch,
  getQueuedSensorTelemetryBatches,
  getQueuedSensorBatches,
  updateSensorTelemetryBatchStatus,
  clearAllSensorTelemetryBatches,
} from "../src/sync-queue.js";

describe("Subject Portal Sensor Telemetry Offline Sync Queue", () => {
  beforeEach(async () => {
    await clearAllSensorTelemetryBatches();
  });

  it("should queue a sensor telemetry batch into IndexedDB", async () => {
    const batchPayload = {
      study_id: "STUDY-901",
      subject_id: "PAT-101",
      device_type: "CGM",
      vendor: "Dexcom",
      model_name: "G7",
      device_token: "mock-token-123",
      readings: [
        { metric_name: "glucose", value: 110, unit: "mg/dL", timestamp: new Date().toISOString() },
        { metric_name: "glucose", value: 125, unit: "mg/dL", timestamp: new Date().toISOString() },
      ],
    };

    const queued = await queueSensorTelemetryBatch(batchPayload);

    expect(queued).toBeDefined();
    expect(queued.id).toBeDefined();
    expect(queued.study_id).toBe("STUDY-901");
    expect(queued.status).toBe("QUEUED");

    const queuedList = await getQueuedSensorTelemetryBatches();
    expect(queuedList.length).toBe(1);
    expect(queuedList[0].subject_id).toBe("PAT-101");
    expect(queuedList[0].readings.length).toBe(2);
  });

  it("should alias saveSensorTelemetryBatch and getQueuedSensorBatches", async () => {
    const batchPayload = {
      batch_id: "BATCH-456",
      study_id: "STUDY-901",
      subject_id: "PAT-102",
      device_type: "ACTIGRAPHY",
      readings: [{ metric_name: "steps", value: 1200, unit: "steps" }],
    };

    const saved = await saveSensorTelemetryBatch(batchPayload);
    expect(saved.batch_id).toBe("BATCH-456");

    const list = await getQueuedSensorBatches();
    expect(list.length).toBe(1);
    expect(list[0].batch_id).toBe("BATCH-456");
  });

  it("should update sensor batch status", async () => {
    const batch = await queueSensorTelemetryBatch({
      id: "sensor-batch-test-1",
      study_id: "STUDY-901",
      subject_id: "PAT-103",
      device_type: "SPIROMETRY",
    });

    const updated = await updateSensorTelemetryBatchStatus("sensor-batch-test-1", "SYNCED", {
      sync_response_id: "resp-789",
    });

    expect(updated.status).toBe("SYNCED");
    expect(updated.sync_response_id).toBe("resp-789");

    const queuedList = await getQueuedSensorTelemetryBatches();
    expect(queuedList.length).toBe(0);
  });
});
