import axe from "axe-core";

/**
 * Asserts that a standard DOM Element, raw HTML string, or Vue test wrapper is WCAG accessible.
 * Runs an asynchronous accessibility audit using axe-core and reports violations clearly.
 * This helper is centralized in packages/ui to serve both apps/web and apps/subject-portal.
 *
 * @param {Element|string|Object} received - The target DOM Element, raw HTML string, or Vue Wrapper.
 * @param {Object} [customOptions={}] - Optional options to override the default rules and configuration.
 * @returns {Promise<{pass: boolean, message: function}>} Result indicating pass/fail status and description.
 */
export async function toBeAccessible(received, customOptions = {}) {
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

  // Strict Layout Mode validation checks (WCAG 2.4.1 Bypass Blocks & Landmark requirements)
  if (customOptions.strictLayoutMode || (customOptions.rules && customOptions.rules["landmark-one-main"]?.enabled)) {
    const hasMain = element.querySelector("main#main-content");
    const hasSkipLink = element.querySelector(".skip-link[href='#main-content']");
    if (!hasMain || !hasSkipLink) {
      return {
        pass: false,
        message: () =>
          `Accessibility strict validation failed:\n` +
          (!hasMain ? ` - Missing main landmark element (<main>) with id="main-content"\n` : "") +
          (!hasSkipLink ? ` - Missing skip-to-content link (.skip-link) with href="#main-content"\n` : ""),
      };
    }
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
}
