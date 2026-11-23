export interface AccessibilityAuditOptions {
  strictLayoutMode?: boolean;
  runOnly?: Record<string, unknown>;
  rules?: Record<string, { enabled?: boolean; [key: string]: unknown }>;
  [key: string]: unknown;
}

export interface AccessibilityAuditResult {
  pass: boolean;
  message: () => string;
}

export function toBeAccessible(
  received: unknown,
  customOptions?: AccessibilityAuditOptions
): Promise<AccessibilityAuditResult>;
