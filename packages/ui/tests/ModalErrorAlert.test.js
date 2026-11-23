// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from "vitest";
import { createApp, h, nextTick, ref } from "vue";
import fs from "fs";
import path from "path";
import { ModalErrorAlert, useAsyncRetry } from "../index.js";

describe("ModalErrorAlert & useAsyncRetry (#4523)", () => {
  const mountedApps = [];

  afterEach(() => {
    while (mountedApps.length > 0) {
      const { app, host } = mountedApps.pop();
      app.unmount();
      host.remove();
    }
  });

  function mountHost(setupFn) {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp({ setup: setupFn });
    app.mount(host);
    mountedApps.push({ app, host });
    return host;
  }

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
    expect(fs.existsSync(composablePath)).toBe(true);
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

  it("does not steal focus from inputs on mount or error update unless autoFocus is true", async () => {
    const errorVal = ref("Initial authentication error");
    const host = mountHost(
      () => () =>
        h("div", [
          h("input", { id: "active-modal-input", type: "password" }),
          h(ModalErrorAlert, {
            id: "test-alert",
            error: errorVal.value,
          }),
        ])
    );

    const input = host.querySelector("#active-modal-input");
    input.focus();
    expect(document.activeElement).toBe(input);

    // Clear and re-trigger error with default autoFocus=false: input keeps focus
    errorVal.value = "";
    await nextTick();
    errorVal.value = "Updated authentication error";
    await nextTick();
    expect(document.activeElement).toBe(input);

    const alertEl = host.querySelector("#test-alert");
    expect(alertEl).not.toBeNull();
    expect(alertEl.getAttribute("role")).toBe("alert");
    expect(alertEl.getAttribute("aria-live")).toBe("assertive");
    expect(alertEl.getAttribute("tabindex")).toBe("-1");

    // When autoFocus is explicitly enabled, post-flush focus moves to the alert
    const autoFocusError = ref("");
    const autoHost = mountHost(
      () => () =>
        h("div", [
          h(ModalErrorAlert, {
            id: "autofocus-alert",
            error: autoFocusError.value,
            autoFocus: true,
          }),
        ])
    );
    autoFocusError.value = "Focus me explicitly";
    await nextTick();
    expect(document.activeElement).toBe(
      autoHost.querySelector("#autofocus-alert")
    );
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
