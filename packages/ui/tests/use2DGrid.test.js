// @vitest-environment jsdom
import { createApp, h, nextTick, ref } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import { use2DGrid } from "../index.js";

describe("use2DGrid shared 2D roving tabindex composable", () => {
  const mountedApps = [];

  afterEach(() => {
    while (mountedApps.length > 0) {
      const { app, host } = mountedApps.pop();
      app.unmount();
      host.remove();
    }
  });

  it("manages roving tabindex (0 active, -1 inactive), 4-way arrow keys, Home/End, Ctrl+Home/End, containerRef DOM focus, and inline edit Escape", async () => {
    const rowCount = ref(3);
    const colCount = ref(4);
    const onCellAction = vi.fn();
    let gridApi;

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp({
      setup() {
        const containerRef = ref(null);
        gridApi = use2DGrid(rowCount, colCount, {
          containerRef,
          onCellAction,
        });
        return () =>
          h("div", { ref: containerRef, class: "matrix-host" }, [
            h(
              "table",
              { role: "grid" },
              h(
                "tbody",
                Array.from({ length: rowCount.value }, (_, r) =>
                  h(
                    "tr",
                    { key: r },
                    Array.from({ length: colCount.value }, (__, c) =>
                      h(
                        "td",
                        {
                          key: c,
                          role: "gridcell",
                          "data-row": r,
                          "data-col": c,
                          tabindex: gridApi.getCellTabindex(r, c),
                          onClick: () => gridApi.selectCell(r, c, false),
                          onKeydown: (e) => gridApi.handleCellKeyDown(e, r, c),
                        },
                        `R${r}C${c}`
                      )
                    )
                  )
                )
              )
            ),
          ]);
      },
    });
    app.mount(host);
    mountedApps.push({ app, host });

    await nextTick();

    const cell = (r, c) =>
      host.querySelector(`td[data-row="${r}"][data-col="${c}"]`);

    expect(cell(0, 0).getAttribute("tabindex")).toBe("0");
    expect(cell(0, 1).getAttribute("tabindex")).toBe("-1");
    expect(cell(1, 0).getAttribute("tabindex")).toBe("-1");

    // ArrowRight -> (0, 1) and focuses DOM cell inside containerRef
    cell(0, 0).focus();
    cell(0, 0).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowRight",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.activeRowIndex.value).toBe(0);
    expect(gridApi.activeColIndex.value).toBe(1);
    expect(cell(0, 1).getAttribute("tabindex")).toBe("0");
    expect(cell(0, 0).getAttribute("tabindex")).toBe("-1");
    expect(document.activeElement).toBe(cell(0, 1));

    // End -> (0, 3)
    cell(0, 1).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "End",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.activeColIndex.value).toBe(3);

    // ArrowDown -> (1, 3)
    cell(0, 3).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowDown",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.activeRowIndex.value).toBe(1);
    expect(gridApi.activeColIndex.value).toBe(3);

    // Home -> (1, 0)
    cell(1, 3).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Home",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.activeColIndex.value).toBe(0);

    // Ctrl+End -> (2, 3) and Ctrl+Home -> (0, 0)
    cell(1, 0).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "End",
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.activeRowIndex.value).toBe(2);
    expect(gridApi.activeColIndex.value).toBe(3);

    cell(2, 3).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Home",
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.activeRowIndex.value).toBe(0);
    expect(gridApi.activeColIndex.value).toBe(0);

    // Enter invokes onCellAction
    cell(0, 0).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Enter",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(onCellAction).toHaveBeenCalledWith(
      0,
      0,
      undefined,
      expect.any(KeyboardEvent)
    );

    // Inline editing ignores arrow navigation and exits on Escape
    gridApi.startEditing(1, 2);
    cell(1, 2).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowLeft",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(gridApi.activeColIndex.value).toBe(2);

    cell(1, 2).dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(gridApi.isEditing.value).toBe(false);
    expect(document.activeElement).toBe(cell(1, 2));
  });
});
