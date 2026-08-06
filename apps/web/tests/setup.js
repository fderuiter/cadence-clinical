import { vi, expect } from "vitest";
import { toBeAccessible } from "ui";
import { webcrypto } from "node:crypto";

// Polyfill Web Crypto API for JSDOM / Node environment
if (typeof globalThis !== "undefined") {
  if (!globalThis.crypto || !globalThis.crypto.subtle) {
    Object.defineProperty(globalThis, "crypto", {
      value: webcrypto,
      writable: true,
      configurable: true,
    });
  }
}

if (typeof window !== "undefined") {
  if (!window.crypto || !window.crypto.subtle) {
    Object.defineProperty(window, "crypto", {
      value: webcrypto,
      writable: true,
      configurable: true,
    });
  }
}

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
