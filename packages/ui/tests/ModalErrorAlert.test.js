import { describe, it, expect, vi } from "vitest";
import fs from "fs";
import path from "path";
import { ModalErrorAlert, useAsyncRetry } from "../index.js";

describe("ModalErrorAlert & useAsyncRetry (#4523)", () => {
  const alertPath = path.resolve(
    __dirname,
    "../src/components/ModalErrorAlert.vue"
  );
  const composablePath = path.resolve(
    __dirname,
    "../src/composables/useAsyncRetry.js"
  );

  it("exports ModalErrorAlert and useAsyncRetry from package entry points", () => {
    expect(ModalErrorAlert).toBeDefined();
    expect(useAsyncRetry).toBeDefined();
  });

  it("ModalErrorAlert component file exists and includes required ARIA attributes and buttons", () => {
    expect(fs.existsSync(alertPath)).toBe(true);
    const code = fs.readFileSync(alertPath, "utf-8");

    expect(code).toContain('role="alert"');
    expect(code).toContain('aria-live="assertive"');
    expect(code).toContain('tabindex="-1"');
    expect(code).toContain('data-testid="modal-error-retry-btn"');
    expect(code).toContain('data-testid="modal-error-dismiss-btn"');
    expect(code).toContain("handleRetry");
    expect(code).toContain("handleDismiss");
    expect(code).toContain('emit("retry"');
    expect(code).toContain('emit("dismiss"');
  });

  it("useAsyncRetry composable manages isLoading, error, execute, retry, and clearError", async () => {
    let callCount = 0;
    const asyncOperation = vi.fn(async (shouldFail) => {
      callCount++;
      if (shouldFail) {
        throw new Error(`Failure on call ${callCount}`);
      }
      return `Success on call ${callCount}`;
    });

    const { isLoading, error, execute, retry, clearError } =
      useAsyncRetry(asyncOperation);

    expect(isLoading.value).toBe(false);
    expect(error.value).toBe(null);

    // Execute failing call
    await expect(execute(true)).rejects.toThrow("Failure on call 1");
    expect(isLoading.value).toBe(false);
    expect(error.value).toBeInstanceOf(Error);
    expect(error.value.message).toBe("Failure on call 1");

    // Retry succeeding call
    const result = await retry(false);
    expect(result).toBe("Success on call 2");
    expect(error.value).toBe(null);
    expect(isLoading.value).toBe(false);

    // Test clearError
    error.value = new Error("Manual error");
    clearError();
    expect(error.value).toBe(null);
  });
});
