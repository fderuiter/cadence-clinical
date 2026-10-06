import { vi, expect, afterEach, beforeAll, afterAll } from "vitest";
import { toBeAccessible } from "ui";
import { setupServer } from "msw/node";
import { gatewayHandlers } from "../src/mocks/handlers.js";

export const server = setupServer(...gatewayHandlers);

beforeAll(() => server.listen({ onUnhandledRequest: "bypass" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

expect.extend({
  toBeAccessible,
});

// Guard console logging during environment teardown to prevent vitest RPC closing errors
let isTeardown = false;
afterEach(() => {
  isTeardown = true;
});

const wrapConsole = (method) => {
  const orig = console[method];
  console[method] = (...args) => {
    if (isTeardown) return;
    try {
      orig.apply(console, args);
    } catch {
      // Suppress logging failures when RPC is closing
    }
  };
};

["log", "warn", "error", "info"].forEach(wrapConsole);

// In-memory Storage mock for jsdom environment
class LocalStorageMock {
  constructor() {
    this.store = {};
  }

  clear() {
    this.store = {};
  }

  getItem(key) {
    return this.store[key] !== undefined ? this.store[key] : null;
  }

  setItem(key, value) {
    this.store[key] = String(value);
  }

  removeItem(key) {
    delete this.store[key];
  }

  get length() {
    return Object.keys(this.store).length;
  }

  key(index) {
    const keys = Object.keys(this.store);
    return keys[index] || null;
  }
}

const localStorageInstance = new LocalStorageMock();

if (typeof window !== "undefined") {
  Object.defineProperty(window, "localStorage", {
    value: localStorageInstance,
    writable: true,
    configurable: true,
  });

  if (!window.alert) {
    window.alert = vi.fn();
  }

  if (!window.matchMedia) {
    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));
  }
}
