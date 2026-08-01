import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import { defineComponent } from "vue";

describe("toBeAccessible Custom Vitest Matcher", () => {
  it("passes when given an accessible, compliant HTML fragment", async () => {
    // Standard input with associated label is accessible
    const html = `
      <div>
        <label for="username">Username</label>
        <input id="username" type="text" />
      </div>
    `;
    await expect(html).toBeAccessible();
  });

  it("fails with a descriptive report when given an HTML fragment with violations", async () => {
    // Missing form labels on input elements is a WCAG violation
    const html = `
      <div>
        <input id="bad-input" type="text" />
      </div>
    `;

    let error = null;
    try {
      await expect(html).toBeAccessible();
    } catch (e) {
      error = e;
    }

    expect(error).not.toBeNull();
    expect(error.message).toContain("Accessibility audit failed");
    expect(error.message).toContain("bad-input"); // Should target/contain the offending element's id/info
  });

  it("automatically bypasses document-level and page landmark rules for isolated fragments", async () => {
    // Isolated fragment doesn't have a main landmark, region, or document-title.
    // However, it should pass because these rules are disabled by default.
    const html = `
      <div>
        <label for="email">Email</label>
        <input id="email" type="email" />
      </div>
    `;
    await expect(html).toBeAccessible();
  });

  it("supports standard HTMLElements", async () => {
    const div = document.createElement("div");
    const label = document.createElement("label");
    label.setAttribute("for", "first-name");
    label.textContent = "First Name";
    const input = document.createElement("input");
    input.id = "first-name";

    div.appendChild(label);
    div.appendChild(input);

    await expect(div).toBeAccessible();
  });

  it("supports Vue Test Utils wrappers", async () => {
    const TestComponent = defineComponent({
      template: `
        <div>
          <label for="last-name">Last Name</label>
          <input id="last-name" type="text" />
        </div>
      `,
    });

    const wrapper = mount(TestComponent);
    await expect(wrapper).toBeAccessible();
  });

  it("merges and respects custom rules options", async () => {
    // Image without alt tag normally fails, but if we override the rule to be disabled:
    const html = `<img src="dummy.png" />`;

    // Without override, it should fail
    let error = null;
    try {
      await expect(html).toBeAccessible();
    } catch (e) {
      error = e;
    }
    expect(error).not.toBeNull();

    // With custom rule override, it should pass
    await expect(html).toBeAccessible({
      rules: {
        "image-alt": { enabled: false },
      },
    });
  });
});
