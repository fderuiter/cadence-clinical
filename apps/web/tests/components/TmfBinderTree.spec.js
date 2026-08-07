import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import TmfBinderTree from "../../src/components/etmf/TmfBinderTree.vue";
import { nextTick } from "vue";

describe("TmfBinderTree Keyboard Navigation & Roving Tabindex Tests", () => {
  const mockTree = [
    {
      id: "zone_1",
      name: "Trial Management",
      code: "1",
      type: "zone",
      children: [
        {
          id: "sec_01.01",
          name: "Trial Design",
          code: "01.01",
          type: "section",
          children: [
            {
              id: "art_01.01.01",
              name: "Clinical Trial Protocol",
              code: "01.01.01",
              type: "artifact",
            },
          ],
        },
      ],
    },
    {
      id: "zone_2",
      name: "Central Trial Documents",
      code: "2",
      type: "zone",
      children: [],
    },
  ];

  it("restricts container sequential navigation to exactly one stop (container tabindex=-1, active node tabindex=0)", async () => {
    const wrapper = mount(TmfBinderTree, {
      props: {
        tree: mockTree,
      },
    });

    // Root container must have tabindex="-1" to prevent the wrapper from being a tab stop
    const rootContainer = wrapper.find(".tree-root-nodes");
    expect(rootContainer.attributes("tabindex")).toBe("-1");

    // Initially, the first visible node (zone_1) should be active and have tabindex="0"
    const zone1Header = wrapper.find("#tree-node-zone_1");
    expect(zone1Header.attributes("tabindex")).toBe("0");

    // Other visible nodes (zone_2) must have tabindex="-1"
    const zone2Header = wrapper.find("#tree-node-zone_2");
    expect(zone2Header.attributes("tabindex")).toBe("-1");
  });

  it("updates internal activeFocusedNodeId and shifts tabindex dynamically upon native focus action", async () => {
    const wrapper = mount(TmfBinderTree, {
      props: {
        tree: mockTree,
      },
    });

    const zone1Header = wrapper.find("#tree-node-zone_1");
    const zone2Header = wrapper.find("#tree-node-zone_2");

    // Simulate focus event on zone_2 (e.g. via direct user action / click / keyboard tab + select)
    await zone2Header.trigger("focus");

    // After focus, zone_2 header gets tabindex="0" and zone_1 header gets tabindex="-1"
    expect(zone2Header.attributes("tabindex")).toBe("0");
    expect(zone1Header.attributes("tabindex")).toBe("-1");
  });

  it("traverses visible tree nodes using ArrowDown and ArrowUp, updating focused element", async () => {
    const wrapper = mount(TmfBinderTree, {
      props: {
        tree: mockTree,
      },
      attachTo: document.body, // Required for native element focus calls to succeed in JSDOM
    });

    const rootContainer = wrapper.find(".tree-root-nodes");
    const zone1Header = wrapper.find("#tree-node-zone_1");
    const zone2Header = wrapper.find("#tree-node-zone_2");

    // Set initial focus to Zone 1
    zone1Header.element.focus();
    expect(document.activeElement).toBe(zone1Header.element);

    // Trigger ArrowDown key event on container
    await rootContainer.trigger("keydown", { key: "ArrowDown" });
    await nextTick();

    // Focus must move to Zone 2
    expect(wrapper.vm.activeFocusedNodeId).toBe("zone_2");
    expect(document.activeElement).toBe(zone2Header.element);

    // Trigger ArrowUp key event on container
    await rootContainer.trigger("keydown", { key: "ArrowUp" });
    await nextTick();

    // Focus must move back to Zone 1
    expect(wrapper.vm.activeFocusedNodeId).toBe("zone_1");
    expect(document.activeElement).toBe(zone1Header.element);

    wrapper.unmount();
  });

  it("expands folder nodes on Enter or Space keydown events exactly once", async () => {
    const wrapper = mount(TmfBinderTree, {
      props: {
        tree: mockTree,
      },
    });

    const rootContainer = wrapper.find(".tree-root-nodes");

    // Ensure Zone 1 is initially collapsed
    expect(wrapper.vm.isExpanded("zone_1")).toBe(false);

    // Trigger Enter key on container (which bubbles up from the active focused node)
    await rootContainer.trigger("keydown", { key: "Enter" });
    await nextTick();

    // Zone 1 should now be expanded
    expect(wrapper.vm.isExpanded("zone_1")).toBe(true);

    // Trigger Space key on container
    await rootContainer.trigger("keydown", { key: " " });
    await nextTick();

    // Zone 1 should now be collapsed again
    expect(wrapper.vm.isExpanded("zone_1")).toBe(false);
  });
});
