import { describe, it, expect, vi } from "vitest";
import { debounce } from "../index.js";

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
