import { vi, expect } from "vitest";
import { config } from "@vue/test-utils";
import { toBeAccessible } from "ui";
import { i18n } from "../src/i18n/index.js";

config.global.plugins = [i18n];

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

const eventTargetProtos = new Set(
  [
    typeof EventTarget !== "undefined" ? EventTarget.prototype : null,
    typeof window !== "undefined" ? window.EventTarget?.prototype : null,
  ].filter((proto) => proto && typeof proto.dispatchEvent === "function")
);

for (const proto of eventTargetProtos) {
  const origDispatchEvent = proto.dispatchEvent;
  proto.dispatchEvent = function (event) {
    if (event && typeof event._vts === "number") {
      let node = this;
      while (node && typeof node === "object") {
        for (const sym of Object.getOwnPropertySymbols(node)) {
          if (sym.description === "_vei") {
            const invokers = node[sym];
            if (invokers && typeof invokers === "object") {
              for (const inv of Object.values(invokers)) {
                if (
                  inv &&
                  typeof inv.attached === "number" &&
                  event._vts <= inv.attached
                ) {
                  event._vts = inv.attached + 1;
                }
              }
            }
          }
        }
        node = node.parentNode || null;
      }
    }
    return origDispatchEvent.call(this, event);
  };
}

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
