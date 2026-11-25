// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import * as rootUi from "../index.js";
import { ModalErrorAlert } from "../index.js";

describe("ModalErrorAlert (#4523)", () => {
  const alertPath = path.resolve(
    __dirname,
    "../src/components/ModalErrorAlert.vue"
  );

  it("exports ModalErrorAlert from package entry points and keeps retired useAsyncRetry unexported", () => {
    expect(ModalErrorAlert).toBeDefined();
    expect(rootUi.useAsyncRetry).toBeUndefined();
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
});

describe("ModalErrorAlert focus preservation (ADR-2679)", () => {
  it("preserves tabindex=-1, role=alert, aria-live=assertive without stealing focus on mount or update", async () => {
    const { createApp, h, nextTick, ref } = await import("vue");
    const host = document.createElement("div");
    const activeInput = document.createElement("input");
    activeInput.id = "sig-password";
    const mountTarget = document.createElement("div");
    host.appendChild(activeInput);
    host.appendChild(mountTarget);
    document.body.appendChild(host);
    activeInput.focus();
    expect(document.activeElement).toBe(activeInput);

    const errorRef = ref("Invalid credentials");
    const app = createApp({
      render: () =>
        h(ModalErrorAlert, { error: errorRef.value, canRetry: true }),
    });
    app.mount(mountTarget);
    await nextTick();

    const alertEl = mountTarget.querySelector('[role="alert"]');
    expect(alertEl).not.toBeNull();
    expect(alertEl.getAttribute("tabindex")).toBe("-1");
    expect(alertEl.getAttribute("aria-live")).toBe("assertive");
    expect(document.activeElement).toBe(activeInput);

    errorRef.value = "Updated step-up failure";
    await nextTick();
    expect(document.activeElement).toBe(activeInput);

    app.unmount();
    host.remove();
  });
});
