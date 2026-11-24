import "vitest";
import type { AccessibilityAuditOptions } from "../accessibility-matcher.js";

interface CustomMatchers<R = unknown> {
  toBeAccessible(customOptions?: AccessibilityAuditOptions): Promise<R>;
}

declare module "vitest" {
  interface Assertion<T = any> extends CustomMatchers<T> {}
  interface AsymmetricMatchersContaining extends CustomMatchers {}
}
