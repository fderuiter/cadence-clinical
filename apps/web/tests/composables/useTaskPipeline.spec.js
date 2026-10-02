import { describe, it, expect, vi } from "vitest";
import { useTaskPipeline } from "@/composables/useTaskPipeline";

describe("useTaskPipeline composable", () => {
  it("initializes with default idle state", () => {
    const pipeline = useTaskPipeline();
    expect(pipeline.status.value).toBe("idle");
    expect(pipeline.isProcessing.value).toBe(false);
    expect(pipeline.progressPercent.value).toBe(0);
    expect(pipeline.currentStage.value).toBe("");
  });

  it("updates progress and stage descriptions", () => {
    const pipeline = useTaskPipeline();
    pipeline.startTask();
    expect(pipeline.isProcessing.value).toBe(true);

    pipeline.updateProgress(25, "Extracting records");
    expect(pipeline.progressPercent.value).toBe(25);
    expect(pipeline.currentStage.value).toBe("Extracting records");

    pipeline.updateProgress(150, "Capped percent");
    expect(pipeline.progressPercent.value).toBe(100);

    pipeline.completeTask("Finished");
    expect(pipeline.status.value).toBe("success");
    expect(pipeline.isCompleted.value).toBe(true);
  });

  it("handles cancellation via AbortSignal", () => {
    const pipeline = useTaskPipeline();
    const signal = pipeline.startTask();

    expect(signal.aborted).toBe(false);
    pipeline.cancelTask("User requested cancel");

    expect(signal.aborted).toBe(true);
    expect(pipeline.isCancelled.value).toBe(true);
    expect(pipeline.status.value).toBe("cancelled");
    expect(pipeline.errorMessage.value).toBe("User requested cancel");
  });

  it("tracks and cleans up Blob object URLs", () => {
    const pipeline = useTaskPipeline();
    pipeline.startTask();

    const revokeSpy = vi
      .spyOn(URL, "revokeObjectURL")
      .mockImplementation(() => {});

    pipeline.registerObjectUrl("blob:http://localhost/test-1");
    pipeline.registerObjectUrl("blob:http://localhost/test-2");

    expect(pipeline.activeObjectUrls.value.length).toBe(2);

    pipeline.cleanupObjectUrls();

    expect(revokeSpy).toHaveBeenCalledTimes(2);
    expect(pipeline.activeObjectUrls.value.length).toBe(0);

    revokeSpy.mockRestore();
  });

  it("executes runTask and handles success and error", async () => {
    const pipeline = useTaskPipeline();

    const result = await pipeline.runTask(
      async ({ updateProgress, signal }) => {
        updateProgress(50, "Halfway");
        expect(signal.aborted).toBe(false);
        return "SUCCESS_DATA";
      }
    );

    expect(result).toBe("SUCCESS_DATA");
    expect(pipeline.status.value).toBe("success");
    expect(pipeline.isCompleted.value).toBe(true);
  });
});
