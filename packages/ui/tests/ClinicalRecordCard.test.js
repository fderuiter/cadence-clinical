// @vitest-environment jsdom
import fs from "fs";
import path from "path";
import { createApp, h, nextTick, ref } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import { toBeAccessible } from "../accessibility-matcher.js";
import { ClinicalRecordCard, EntityLink } from "../index.js";

describe("ClinicalRecordCard shared clinical primitive", () => {
  const mountedApps = [];

  afterEach(() => {
    while (mountedApps.length > 0) {
      const { app, host } = mountedApps.pop();
      app.unmount();
      host.remove();
    }
  });

  function mountHost(setupFn) {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp({ setup: setupFn });
    app.mount(host);
    mountedApps.push({ app, host });
    return host;
  }

  const sampleFields = [
    { key: "site", label: "Site", value: "104 - Mass General", mono: true },
    { key: "visit", label: "Visit Window", value: "Day 28 (±2d)" },
    {
      key: "notes",
      label: "Monitor Summary",
      value: "Source verification completed for primary efficacy endpoints.",
      fullWidth: true,
    },
  ];

  it("renders monospace or EntityLink identifier header, semantic status slot, tabular key-value dl, relative action footer, and compact/touch densities", async () => {
    const density = ref("compact");
    const onCardClick = vi.fn();
    const onFooterAction = vi.fn();
    const onSelect = vi.fn();

    const sfcSource = fs.readFileSync(
      path.resolve(
        __dirname,
        "../src/components/clinical/ClinicalRecordCard.vue"
      ),
      "utf-8"
    );
    const scopedMatch = sfcSource.match(/<style[^>]*>([\s\S]*?)<\/style>/i);
    expect(scopedMatch).not.toBeNull();
    const styleEl = document.createElement("style");
    styleEl.textContent = scopedMatch[1];
    document.head.appendChild(styleEl);

    try {
      const host = mountHost(
        () => () =>
          h(
            ClinicalRecordCard,
            {
              identifier: "MV-2026-014",
              title: "Interim Monitoring Visit",
              fields: sampleFields,
              density: density.value,
              interactive: true,
              selectable: true,
              selected: false,
              onSelect,
              "onCard-click": onCardClick,
            },
            {
              identifier: () =>
                h(EntityLink, {
                  nodeRef: { kind: "MonitoringVisit", id: "MV-2026-014" },
                  label: "MV-2026-014",
                }),
              status: () =>
                h(
                  "span",
                  { class: "clinical-badge clinical-badge--verified" },
                  "VERIFIED"
                ),
              footer: () =>
                h(
                  "button",
                  {
                    type: "button",
                    class: "btn btn-secondary card-inspect-btn",
                    onClick: onFooterAction,
                  },
                  [
                    h("span", "Inspect"),
                    h("span", { class: "sr-only" }, " visit MV-2026-014"),
                  ]
                ),
            }
          )
      );

      await nextTick();

      const card = host.querySelector(".clinical-record-card");
      expect(card).not.toBeNull();
      expect(card.classList.contains("clinical-record-card--compact")).toBe(
        true
      );
      expect(card.getAttribute("tabindex")).toBe("0");

      const dlItems = host.querySelectorAll(".clinical-record-card__field");
      expect(dlItems).toHaveLength(3);
      expect(
        dlItems[2].classList.contains("clinical-record-card__field--full")
      ).toBe(true);

      const footer = host.querySelector(".clinical-record-card__footer");
      expect(footer).not.toBeNull();
      expect(window.getComputedStyle(footer).position).toBe("relative");

      // Footer action button does not double-fire card-click
      const btn = footer.querySelector(".card-inspect-btn");
      btn.click();
      expect(onFooterAction).toHaveBeenCalledTimes(1);
      expect(onCardClick).not.toHaveBeenCalled();

      // Keyboard Enter on card emits card-click
      card.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "Enter",
          bubbles: true,
          cancelable: true,
        })
      );
      expect(onCardClick).toHaveBeenCalledTimes(1);

      // Switch to touch density
      density.value = "touch";
      await nextTick();
      expect(card.classList.contains("clinical-record-card--touch")).toBe(true);

      const audit = await toBeAccessible({ element: host });
      expect(audit.pass, audit.message()).toBe(true);
    } finally {
      styleEl.remove();
    }
  });

  it("renders non-interactive masked state via #masked slot without firing card-click", async () => {
    const onCardClick = vi.fn();
    const host = mountHost(
      () => () =>
        h(
          ClinicalRecordCard,
          {
            identifier: "SUBJ-REDACTED",
            masked: true,
            interactive: true,
            "onCard-click": onCardClick,
          },
          {
            masked: () =>
              h("span", { class: "masked-copy" }, "Blinded treatment record"),
          }
        )
    );

    await nextTick();
    const card = host.querySelector(".clinical-record-card");
    expect(card.classList.contains("clinical-record-card--masked")).toBe(true);
    expect(card.hasAttribute("tabindex")).toBe(false);
    expect(card.textContent).toContain("Blinded treatment record");
    card.click();
    expect(onCardClick).not.toHaveBeenCalled();
  });
});
