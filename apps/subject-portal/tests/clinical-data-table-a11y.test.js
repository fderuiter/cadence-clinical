import { createApp, h, nextTick } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ClinicalDataTable } from "ui";

describe("Subject Portal shared ClinicalDataTable accessibility", () => {
  let app = null;
  let host = null;

  afterEach(() => {
    if (app) {
      app.unmount();
      app = null;
    }
    if (host) {
      host.remove();
      host = null;
    }
  });

  it("renders accessible sortable headers, keyboard row activation, and polite pagination status from shared ui package", async () => {
    const onRowClick = vi.fn();
    host = document.createElement("div");
    document.body.appendChild(host);

    const columns = [
      { key: "visit", label: "Visit", sortable: true },
      { key: "summary", label: "Summary", sortable: false },
    ];
    const rows = [
      { id: "VIS-02", visit: "Visit 2", summary: "Completed" },
      { id: "VIS-01", visit: "Visit 1", summary: "Completed" },
      { id: "VIS-03", visit: "Visit 3", summary: "Scheduled" },
    ];

    app = createApp({
      render: () =>
        h(ClinicalDataTable, {
          title: "My Study Visits",
          columns,
          data: rows,
          pageSize: 2,
          selectable: false,
          "onRow-click": onRowClick,
        }),
    });
    app.mount(host);
    await nextTick();

    const ths = host.querySelectorAll("thead th");
    expect(ths[0].getAttribute("aria-sort")).toBe("none");
    expect(ths[1].hasAttribute("aria-sort")).toBe(false);

    const sortBtn = ths[0].querySelector('button[type="button"]');
    sortBtn.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Enter",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(ths[0].getAttribute("aria-sort")).toBe("ascending");

    const firstRow = host.querySelector("tbody tr.data-row");
    expect(firstRow.getAttribute("tabindex")).toBe("0");
    expect(firstRow.hasAttribute("aria-selected")).toBe(false);
    firstRow.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: " ",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(onRowClick).toHaveBeenCalledTimes(1);

    const paginationStatus = host.querySelector(".pagination-info");
    expect(paginationStatus.getAttribute("role")).toBe("status");
    expect(paginationStatus.getAttribute("aria-live")).toBe("polite");

    await expect(host).toBeAccessible();
  });
});
