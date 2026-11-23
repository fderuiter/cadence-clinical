// @vitest-environment jsdom
import { createApp, h, nextTick, ref } from "vue";
import { afterEach, describe, expect, it } from "vitest";
import { toBeAccessible } from "../accessibility-matcher.js";
import { ClinicalTabs } from "../index.js";

describe("ClinicalTabs WAI-ARIA Tabs primitive", () => {
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

  it("renders role=tablist, role=tab, role=tabpanel, roving tabindex, and passes axe-core both with inline panel slots and conditional external tabpanels", async () => {
    const activeTab = ref("overview");
    const tabs = [
      { id: "overview", label: "Overview", badge: 3 },
      { id: "versions", label: "Version History", badge: 2 },
      { id: "locked", label: "Locked Section", disabled: true },
      { id: "audit", label: "Audit Trail" },
    ];

    const host = mountHost(
      () => () =>
        h("div", [
          h(
            ClinicalTabs,
            {
              modelValue: activeTab.value,
              "onUpdate:modelValue": (val) => {
                activeTab.value = val;
              },
              tabs,
              ariaLabel: "SOP document sections",
              idPrefix: "sop-tabs",
            },
            {
              "panel-overview": () => h("p", "Overview prose body"),
              "panel-versions": () => h("p", "Immutable version snapshots"),
              "panel-locked": () => h("p", "Locked content"),
              "panel-audit": () => h("p", "21 CFR Part 11 audit ledger"),
            }
          ),
        ])
    );

    await nextTick();

    const tablist = host.querySelector('[role="tablist"]');
    expect(tablist).not.toBeNull();
    expect(tablist.getAttribute("aria-label")).toBe("SOP document sections");
    expect(tablist.getAttribute("aria-orientation")).toBe("horizontal");

    const tabButtons = Array.from(host.querySelectorAll('[role="tab"]'));
    expect(tabButtons).toHaveLength(4);
    expect(tabButtons[0].getAttribute("aria-selected")).toBe("true");
    expect(tabButtons[0].getAttribute("tabindex")).toBe("0");
    expect(tabButtons[1].getAttribute("aria-selected")).toBe("false");
    expect(tabButtons[1].getAttribute("tabindex")).toBe("-1");
    expect(tabButtons[2].disabled).toBe(true);
    expect(tabButtons[3].getAttribute("tabindex")).toBe("-1");

    const a11yInline = await toBeAccessible({ element: host });
    expect(a11yInline.pass, a11yInline.message()).toBe(true);

    // Keyboard ArrowRight -> versions
    tabButtons[0].focus();
    tabButtons[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("versions");
    expect(document.activeElement).toBe(tabButtons[1]);
    expect(tabButtons[1].getAttribute("aria-selected")).toBe("true");
    expect(tabButtons[1].getAttribute("tabindex")).toBe("0");
    expect(tabButtons[0].getAttribute("tabindex")).toBe("-1");

    // ArrowRight skips disabled "locked" tab -> "audit"
    tabButtons[1].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("audit");
    expect(document.activeElement).toBe(tabButtons[3]);

    // ArrowRight wraps around from last enabled ("audit") -> first enabled ("overview")
    tabButtons[3].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("overview");
    expect(document.activeElement).toBe(tabButtons[0]);

    // ArrowLeft wraps around from first ("overview") -> last enabled ("audit")
    tabButtons[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("audit");
    expect(document.activeElement).toBe(tabButtons[3]);

    // Home -> first enabled ("overview"), End -> last enabled ("audit")
    tabButtons[3].dispatchEvent(
      new KeyboardEvent("keydown", { key: "Home", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("overview");
    expect(document.activeElement).toBe(tabButtons[0]);

    tabButtons[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "End", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("audit");
    expect(document.activeElement).toBe(tabButtons[3]);
  });

  it("passes axe-core when external v-if tabpanels mount only the active panel and supports vertical + manual modes", async () => {
    const activeTab = ref("kanban");
    const tabs = [
      { id: "kanban", label: "Kanban" },
      { id: "table", label: "Table" },
    ];

    const externalHost = mountHost(
      () => () =>
        h("div", [
          h(ClinicalTabs, {
            modelValue: activeTab.value,
            "onUpdate:modelValue": (val) => {
              activeTab.value = val;
            },
            tabs,
            ariaLabel: "Issue display mode",
            idPrefix: "tickets-mode",
            orientation: "vertical",
            manual: true,
          }),
          activeTab.value === "kanban"
            ? h(
                "div",
                {
                  id: "tickets-mode-panel-kanban",
                  role: "tabpanel",
                  "aria-labelledby": "tickets-mode-tab-kanban",
                },
                "Kanban board content"
              )
            : h(
                "div",
                {
                  id: "tickets-mode-panel-table",
                  role: "tabpanel",
                  "aria-labelledby": "tickets-mode-tab-table",
                },
                "Table view content"
              ),
        ])
    );

    await nextTick();

    const a11yExternal = await toBeAccessible({ element: externalHost });
    expect(a11yExternal.pass, a11yExternal.message()).toBe(true);

    const tabButtons = Array.from(
      externalHost.querySelectorAll('[role="tab"]')
    );
    tabButtons[0].focus();

    // Vertical manual mode: ArrowDown moves roving focus without changing modelValue until Enter/Space
    tabButtons[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })
    );
    await nextTick();
    expect(document.activeElement).toBe(tabButtons[1]);
    expect(activeTab.value).toBe("kanban");

    tabButtons[1].dispatchEvent(
      new KeyboardEvent("keydown", { key: "Enter", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("table");

    // ArrowUp moves roving focus back to kanban, Space activates it
    tabButtons[1].dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowUp", bubbles: true })
    );
    await nextTick();
    expect(document.activeElement).toBe(tabButtons[0]);
    tabButtons[0].dispatchEvent(
      new KeyboardEvent("keydown", { key: " ", bubbles: true })
    );
    await nextTick();
    expect(activeTab.value).toBe("kanban");
  });

  it("omits aria-controls when controlsPanels is false for filter-only tab bars and passes axe-core", async () => {
    const activeFilter = ref("all");
    const tabs = [
      { id: "all", label: "All Records" },
      { id: "pending", label: "Pending Review" },
    ];

    const filterHost = mountHost(
      () => () =>
        h(ClinicalTabs, {
          modelValue: activeFilter.value,
          "onUpdate:modelValue": (val) => {
            activeFilter.value = val;
          },
          tabs,
          ariaLabel: "Record status filter",
          controlsPanels: false,
        })
    );

    await nextTick();

    const tabButtons = Array.from(filterHost.querySelectorAll('[role="tab"]'));
    expect(tabButtons[0].hasAttribute("aria-controls")).toBe(false);
    expect(tabButtons[1].hasAttribute("aria-controls")).toBe(false);

    const a11yFilter = await toBeAccessible({ element: filterHost });
    expect(a11yFilter.pass, a11yFilter.message()).toBe(true);
  });
});
