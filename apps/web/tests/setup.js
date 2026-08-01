import { vi, expect } from "vitest";
import axe from "axe-core";

expect.extend({
  /**
   * Asserts that a standard DOM Element, raw HTML string, or Vue test wrapper is WCAG accessible.
   * Runs an asynchronous accessibility audit using axe-core and reports violations clearly.
   *
   * @param {Element|string|Object} received - The target DOM Element, raw HTML string, or Vue Wrapper.
   * @param {Object} [customOptions={}] - Optional options to override the default rules and configuration.
   * @returns {Promise<{pass: boolean, message: function}>} Result indicating pass/fail status and description.
   */
  async toBeAccessible(received, customOptions = {}) {
    let element = received;
    let wrapperToCleanup = null;

    // Normalizing the received parameter into a standard DOM Element
    if (received && typeof received === "object" && received.element) {
      element = received.element;
    } else if (typeof received === "string") {
      const container = document.createElement("div");
      container.innerHTML = received;
      element = container;
    }

    if (!element) {
      return {
        pass: false,
        message: () =>
          "Expected a valid DOM element, HTML string, or Vue test wrapper, but received null or undefined.",
      };
    }

    // Default fragment-level bypass rules to support isolated component testing
    const defaultRules = {
      "document-title": { enabled: false },
      "html-has-lang": { enabled: false },
      "landmark-one-main": { enabled: false },
      "page-has-heading-one": { enabled: false },
      region: { enabled: false },
    };

    // Merge default rules with user-provided options
    const finalOptions = {
      runOnly: customOptions.runOnly || {
        type: "tag",
        values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"],
      },
      rules: {
        ...defaultRules,
        ...(customOptions.rules || {}),
      },
      ...customOptions,
    };

    try {
      // Temporarily attach to body if not already in the document to ensure axe-core can inspect it correctly
      if (!document.body.contains(element)) {
        document.body.appendChild(element);
        wrapperToCleanup = element;
      }

      const results = await axe.run(element, finalOptions);
      const pass = results.violations.length === 0;

      if (pass) {
        return {
          pass: true,
          message: () =>
            "Expected element not to be accessible, but no accessibility violations were found.",
        };
      } else {
        const report = results.violations
          .map((violation, index) => {
            const nodeDetails = violation.nodes
              .map((node, nodeIdx) => {
                return `  ${nodeIdx + 1}) Target: ${node.target.join(", ")}\n     Snippet: ${node.html}\n     Fix: ${node.failureSummary}`;
              })
              .join("\n\n");

            return (
              `[Violation ${index + 1}] ID: ${violation.id} (${violation.impact})\n` +
              `Description: ${violation.description}\n` +
              `Help: ${violation.help} (${violation.helpUrl})\n` +
              `Offending Nodes:\n${nodeDetails}`
            );
          })
          .join("\n\n--------------------------------------------------\n\n");

        return {
          pass: false,
          message: () =>
            `Accessibility audit failed with ${results.violations.length} violation(s):\n\n${report}`,
        };
      }
    } catch (error) {
      return {
        pass: false,
        message: () => `Accessibility audit failed to run: ${error.message}`,
      };
    } finally {
      if (wrapperToCleanup && wrapperToCleanup.parentNode) {
        wrapperToCleanup.parentNode.removeChild(wrapperToCleanup);
      }
    }
  },
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
