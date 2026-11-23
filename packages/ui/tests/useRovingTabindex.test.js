// @vitest-environment jsdom
import { createApp, h, nextTick, ref } from "vue";
import { afterEach, describe, expect, it } from "vitest";
import { toBeAccessible } from "../accessibility-matcher.js";
import { syncRovingTabindex, useRovingTabindex } from "../index.js";

describe("useRovingTabindex shared WAI-ARIA tablist composable", () => {
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
    const app = createApp({
      setup: setupFn,
    });
    app.mount(host);
    mountedApps.push({ app, host });
    return host;
  }

  it("synchronizes roving tabindex and handles ArrowRight, ArrowLeft, Home, End while skipping disabled tabs", async () => {
    const activeId = ref("alpha");
    const items = [
      { id: "alpha", label: "Alpha" },
      { id: "beta", label: "Beta", disabled: true },
      { id: "gamma", label: "Gamma" },
      { id: "delta", label: "Delta" },
    ];

    const host = mountHost(() => {
      const tablistRef = ref(null);
      useRovingTabindex(tablistRef, (idx) => {
        const nextItem = items[idx];
        if (nextItem && !nextItem.disabled) {
          activeId.value = nextItem.id;
        }
      });

      return () =>
        h(
          "div",
          {
            ref: tablistRef,
            role: "tablist",
            "aria-label": "Study governance views",
          },
          items.map((item) =>
            h(
              "button",
              {
                key: item.id,
                type: "button",
                role: "tab",
                "aria-selected": activeId.value === item.id ? "true" : "false",
                disabled: Boolean(item.disabled),
                onClick: () => {
                  if (!item.disabled) activeId.value = item.id;
                },
              },
              item.label
            )
          )
        );
    });

    await nextTick();

    const tabs = Array.from(host.querySelectorAll('[role="tab"]'));
    expect(tabs).toHaveLength(4);
    expect(tabs[0].getAttribute("tabindex")).toBe("0");
    expect(tabs[1].getAttribute("tabindex")).toBe("-1");
    expect(tabs[2].getAttribute("tabindex")).toBe("-1");
    expect(tabs[3].getAttribute("tabindex")).toBe("-1");

    const a11y = await toBeAccessible({ element: host });
    expect(a11y.pass, a11y.message()).toBe(true);

    // ArrowRight skips disabled index 1 -> index 2 ("gamma")
    tabs[0].focus();
    tabs[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("gamma");
    expect(document.activeElement).toBe(tabs[2]);
    expect(tabs[2].getAttribute("tabindex")).toBe("0");
    expect(tabs[0].getAttribute("tabindex")).toBe("-1");

    // End -> index 3 ("delta")
    tabs[2].dispatchEvent(
      new KeyboardEvent("keydown", { key: "End", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("delta");
    expect(document.activeElement).toBe(tabs[3]);
    expect(tabs[3].getAttribute("tabindex")).toBe("0");

    // ArrowRight wraps -> index 0 ("alpha")
    tabs[3].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("alpha");
    expect(document.activeElement).toBe(tabs[0]);

    // ArrowLeft wraps -> index 3 ("delta")
    tabs[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("delta");
    expect(document.activeElement).toBe(tabs[3]);

    // Home -> index 0 ("alpha")
    tabs[3].dispatchEvent(
      new KeyboardEvent("keydown", { key: "Home", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("alpha");
    expect(document.activeElement).toBe(tabs[0]);
  });

  it("re-attaches when a v-if drawer mounts and supports vertical manual activation", async () => {
    const isOpen = ref(false);
    const activeId = ref("first");
    const items = [
      { id: "first", label: "First" },
      { id: "second", label: "Second" },
    ];

    const host = mountHost(() => {
      const tablistRef = ref(null);
      useRovingTabindex(
        () => tablistRef.value,
        (idx) => {
          activeId.value = items[idx].id;
        },
        { orientation: "vertical", manual: true }
      );

      return () =>
        isOpen.value
          ? h(
              "div",
              {
                ref: tablistRef,
                role: "tablist",
                "aria-label": "Drawer sections",
                "aria-orientation": "vertical",
              },
              items.map((item) =>
                h(
                  "button",
                  {
                    key: item.id,
                    type: "button",
                    role: "tab",
                    "aria-selected":
                      activeId.value === item.id ? "true" : "false",
                  },
                  item.label
                )
              )
            )
          : h("div", "Closed");
    });

    await nextTick();
    expect(syncRovingTabindex(null)).toBeNull();

    isOpen.value = true;
    await nextTick();
    await nextTick();

    const tabs = Array.from(host.querySelectorAll('[role="tab"]'));
    expect(tabs[0].getAttribute("tabindex")).toBe("0");
    expect(tabs[1].getAttribute("tabindex")).toBe("-1");

    tabs[0].focus();
    tabs[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })
    );
    await nextTick();
    expect(document.activeElement).toBe(tabs[1]);
    expect(activeId.value).toBe("first");
    expect(tabs[1].getAttribute("tabindex")).toBe("0");

    tabs[1].dispatchEvent(
      new KeyboardEvent("keydown", { key: "Enter", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("second");

    tabs[1].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowUp", bubbles: true })
    );
    await nextTick();
    expect(document.activeElement).toBe(tabs[0]);
    tabs[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: " ", bubbles: true })
    );
    await nextTick();
    expect(activeId.value).toBe("first");

    isOpen.value = false;
    await nextTick();
    expect(host.querySelectorAll('[role="tab"]')).toHaveLength(0);
  });
});
