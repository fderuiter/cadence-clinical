import { describe, it, expect, vi } from "vitest";
import { debounce, createClinicalLookupInput } from "../index.js";

describe("createClinicalLookupInput", () => {
  it("generates correct HTML structure for default status none", () => {
    const html = createClinicalLookupInput("test-id", "Test Label", "C123");
    expect(html).toContain('id="field-container-test-id"');
    expect(html).toContain('label for="test-id"');
    expect(html).toContain('value="C123"');
    expect(html).toContain('id="lookup-status-test-id"');
    expect(html).toContain('style="display: none"');
  });

  it("generates loading status correctly", () => {
    const html = createClinicalLookupInput("test-id", "Test Label", "C123", "loading");
    expect(html).toContain("lookup-loading");
    expect(html).toContain("⏳");
    expect(html).toContain("Searching terminology database...");
  });

  it("generates valid status with custom message", () => {
    const html = createClinicalLookupInput("test-id", "Test Label", "C123", "valid", "Code is VALID");
    expect(html).toContain("lookup-valid");
    expect(html).toContain("✅");
    expect(html).toContain("Code is VALID");
  });

  it("generates invalid status correctly", () => {
    const html = createClinicalLookupInput("test-id", "Test Label", "C123", "invalid");
    expect(html).toContain("lookup-invalid");
    expect(html).toContain("❌");
    expect(html).toContain("Invalid code.");
  });

  it("generates degraded status correctly", () => {
    const html = createClinicalLookupInput("test-id", "Test Label", "C123", "degraded");
    expect(html).toContain("lookup-degraded");
    expect(html).toContain("⚠️");
    expect(html).toContain("Terminology service degraded.");
  });
});

describe("debounce", () => {
  it("delays execution and bounds rapid invocations to a single call", () => {
    vi.useFakeTimers();
    const callback = vi.fn();
    const debounced = debounce(callback, 200);

    debounced("first");
    debounced("second");
    debounced("third");

    expect(callback).not.toHaveBeenCalled();

    vi.advanceTimersByTime(199);
    expect(callback).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(callback).toHaveBeenCalledTimes(1);
    expect(callback).toHaveBeenCalledWith("third");

    vi.useRealTimers();
  });
});
