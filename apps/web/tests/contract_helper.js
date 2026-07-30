import { expect } from "vitest";

export const forbiddenHeaders = [
  "X-User-Id",
  "X-User-Roles",
  "X-Gateway-Timestamp",
  "X-Gateway-Signature",
  "X-Signature-Version",
];

export function assertSecureOptions(options) {
  if (!options) return;

  // Verify headers
  if (options.headers) {
    for (const header of forbiddenHeaders) {
      expect(options.headers[header]).toBeUndefined();
      expect(options.headers[header.toLowerCase()]).toBeUndefined();
    }
  }

  // Double check that options itself does not carry signature elements or hardcoded identities
  for (const header of forbiddenHeaders) {
    expect(options[header]).toBeUndefined();
    expect(options[header.toLowerCase()]).toBeUndefined();
  }
}
