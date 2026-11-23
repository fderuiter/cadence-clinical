import { describe, expect, it } from "vitest";

describe("apps/web Vitest node project", () => {
  it("runs pure Node specs without creating a jsdom window or document", () => {
    expect(typeof window).toBe("undefined");
    expect(typeof document).toBe("undefined");
  });
});
