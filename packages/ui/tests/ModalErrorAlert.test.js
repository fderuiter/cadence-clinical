// @vitest-environment jsdom
import { afterEach, describe, it, expect } from "vitest";
import { createApp, h, nextTick, ref } from "vue";
import fs from "fs";
import path from "path";
import * as rootUi from "../index.js";
import { ModalErrorAlert } from "../index.js";

describe("ModalErrorAlert (#4523)", () => {
  const alertPath = path.resolve(
    __dirname,
    "../src/components/ModalErrorAlert.vue"
  );
  const tokensPath = path.resolve(__dirname, "../tokens.css");
  const mounted = [];

  afterEach(() => {
    while (mounted.length > 0) {
      const { app, host, styleEls } = mounted.pop();
      app.unmount();
      host.remove();
      for (const s of styleEls) s.remove();
    }
  });

  it("exports ModalErrorAlert from package entry points and keeps retired useAsyncRetry unexported", () => {
    expect(ModalErrorAlert).toBeDefined();
    expect(rootUi.useAsyncRetry).toBeUndefined();
  });

  it("ModalErrorAlert component file exists and includes required ARIA attributes and explicit non-focus transitions", () => {
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
    expect(code).not.toMatch(/transition(-property)?\s*:\s*[^;]*\ball\b/);
  });

  it("preserves computed 2px solid --semantic-color-error outline when programmatic focus runs with tokens.css loaded", async () => {
    const tokensCss = fs.readFileSync(tokensPath, "utf-8");
    const sfcSource = fs.readFileSync(alertPath, "utf-8");
    const scopedCssMatch = sfcSource.match(/<style[^>]*>([\s\S]*?)<\/style>/i);
    expect(scopedCssMatch).not.toBeNull();

    const tokensStyleEl = document.createElement("style");
    tokensStyleEl.textContent = tokensCss;
    const componentStyleEl = document.createElement("style");
    componentStyleEl.textContent = scopedCssMatch[1];
    document.head.appendChild(tokensStyleEl);
    document.head.appendChild(componentStyleEl);

    const errorState = ref("");
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp({
      setup() {
        return () =>
          h(ModalErrorAlert, {
            id: "modal-validation-alert",
            title: "Submission Blocked",
            errorMessage: errorState.value,
          });
      },
    });
    app.mount(host);
    mounted.push({
      app,
      host,
      styleEls: [tokensStyleEl, componentStyleEl],
    });

    // Trigger error -> watch(hasError) runs alertRef.value.focus() on mount/update
    errorState.value = "Systolic blood pressure is required before sign-off.";
    await nextTick();
    const alertEl = host.querySelector("#modal-validation-alert");
    expect(alertEl).not.toBeNull();
    alertEl.focus();

    expect(document.activeElement).toBe(alertEl);
    const errorColorProbe = document.createElement("span");
    errorColorProbe.style.color = "var(--semantic-color-error)";
    host.appendChild(errorColorProbe);
    const expectedErrorColor = window.getComputedStyle(errorColorProbe).color;
    expect(expectedErrorColor).toBeTruthy();

    const computedStyle = window.getComputedStyle(alertEl);
    expect(computedStyle.outlineStyle).toBe("solid");
    expect(computedStyle.outlineWidth).toBe("2px");
    expect(computedStyle.outlineOffset).toBe("1px");
    expect(computedStyle.outlineColor).toBe(expectedErrorColor);
  });
});
