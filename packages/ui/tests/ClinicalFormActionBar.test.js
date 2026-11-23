// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";
import fs from "fs";
import path from "path";
import { toBeAccessible } from "../accessibility-matcher.js";
import { ClinicalFieldLayout, ClinicalFormActionBar } from "../index.js";

describe("ClinicalFormActionBar & CSS variable reflow accessibility (ADR-2651)", () => {
  it("passes axe-core accessibility checks in collapsed, gated, and expanded tray states", async () => {
    const collapsedHtml = await renderToString(
      createSSRApp({
        render: () =>
          h(
            ClinicalFormActionBar,
            {
              ariaLabel: "eCRF form actions",
              statusHint: "Save all unsaved changes before signing.",
              statusTone: "gated",
              statusHintId: "ecrf-sign-hint",
              trayOpen: false,
            },
            {
              secondary: () =>
                h(
                  "button",
                  { type: "button", class: "btn-secondary" },
                  "Cancel"
                ),
              default: () => [
                h(
                  "button",
                  { type: "submit", class: "btn-primary" },
                  "Save page"
                ),
                h(
                  "button",
                  {
                    type: "button",
                    disabled: true,
                    "aria-describedby": "ecrf-sign-hint",
                  },
                  "Sign form"
                ),
              ],
            }
          ),
      })
    );
    expect(collapsedHtml).toContain("Save page");
    expect(collapsedHtml).toContain("Sign form");
    const collapsedResult = await toBeAccessible(collapsedHtml);
    expect(collapsedResult.pass, collapsedResult.message()).toBe(true);

    const expandedHtml = await renderToString(
      createSSRApp({
        render: () =>
          h(
            ClinicalFormActionBar,
            {
              ariaLabel: "eCRF form actions",
              trayOpen: true,
              trayLabel: "Reason for change",
            },
            {
              tray: () =>
                h("div", [
                  h("label", { for: "ecrf-save-reason" }, "Reason for change"),
                  h("input", {
                    id: "ecrf-save-reason",
                    type: "text",
                    value: "Initial vitals entry",
                  }),
                ]),
              default: () =>
                h(
                  "button",
                  { type: "submit", class: "btn-primary" },
                  "Save page"
                ),
            }
          ),
      })
    );
    expect(expandedHtml).toContain('id="ecrf-save-reason"');
    const expandedResult = await toBeAccessible(expandedHtml);
    expect(expandedResult.pass, expandedResult.message()).toBe(true);
  });

  it("binds --field-grid-span on ClinicalFieldLayout and defines descendant span collapse in responsive.css", async () => {
    const fieldHtml = await renderToString(
      createSSRApp({
        render: () =>
          h(
            ClinicalFieldLayout,
            {
              id: "hr-field",
              label: "Heart Rate (bpm)",
              gridSpan: 6,
            },
            {
              default: () => h("input", { id: "hr-field", type: "number" }),
            }
          ),
      })
    );
    expect(fieldHtml).toContain('id="hr-field"');
    const fieldResult = await toBeAccessible(fieldHtml);
    expect(fieldResult.pass, fieldResult.message()).toBe(true);

    const responsiveCss = fs.readFileSync(
      path.resolve(__dirname, "../responsive.css"),
      "utf-8"
    );
    expect(responsiveCss).toContain("var(--field-grid-span, 12)");
    expect(responsiveCss).toMatch(/@media\s*\(max-width:\s*1023px\)/);
    expect(responsiveCss).toContain(".responsive-grid fieldset > *");
    expect(responsiveCss).toContain(".grid-2-responsive .card");

    const actionBarVue = fs.readFileSync(
      path.resolve(
        __dirname,
        "../src/components/clinical/ClinicalFormActionBar.vue"
      ),
      "utf-8"
    );
    expect(actionBarVue).toContain("position: sticky;");
    expect(actionBarVue).toContain(
      "bottom: var(--form-action-bar-bottom, 0px);"
    );
  });
});
