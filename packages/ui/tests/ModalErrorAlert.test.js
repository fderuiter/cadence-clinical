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
