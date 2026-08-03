import { vi, expect } from "vitest";
import { toBeAccessible } from "ui";

expect.extend({
  toBeAccessible,
});

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
const sessionStorageInstance = new LocalStorageMock();

if (typeof window !== "undefined") {
  Object.defineProperty(window, "localStorage", {
    value: localStorageInstance,
    writable: true,
    configurable: true,
  });

  Object.defineProperty(window, "sessionStorage", {
    value: sessionStorageInstance,
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
