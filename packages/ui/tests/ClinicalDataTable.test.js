// @vitest-environment jsdom
import { createApp, h, nextTick, ref } from "vue";
import { afterEach, describe, expect, it, vi } from "vitest";
import { toBeAccessible } from "../accessibility-matcher.js";
import { ClinicalDataTable } from "../index.js";

describe("ClinicalDataTable WCAG 2.2 keyboard & ARIA primitive", () => {
  const mountedApps = [];

  const columns = [
    { key: "id", label: "Subject ID", sortable: true },
    { key: "glucose", label: "Glucose (mg/dL)", sortable: true },
    { key: "notes", label: "Clinical Notes", sortable: false },
    { key: "actions", label: "Actions" },
  ];

  const sampleRows = [
    { id: "SUBJ-003", glucose: 112, notes: "Fasting" },
    { id: "SUBJ-001", glucose: 89, notes: "Baseline" },
    { id: "SUBJ-002", glucose: 104, notes: "Follow-up" },
  ];

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

  it("links table region and caption to a unique heading ID, exposes conditional aria-sort buttons, and passes axe-core", async () => {
    const host = mountHost(
      () => () =>
        h(ClinicalDataTable, {
          title: "Laboratory Observations",
          columns,
          data: sampleRows,
          pageSize: 2,
          selectable: true,
        })
    );

    await nextTick();

    const heading = host.querySelector("h3.table-title");
    const region = host.querySelector('.table-wrapper[role="region"]');
    const table = host.querySelector("table.clinical-table");
    const caption = table.querySelector("caption.sr-only");

    expect(heading).not.toBeNull();
    expect(heading.id).toMatch(/^clinical-data-table-title-/);
    expect(region.getAttribute("aria-labelledby")).toBe(heading.id);
    expect(region.hasAttribute("aria-label")).toBe(false);
    expect(table.getAttribute("aria-labelledby")).toBe(heading.id);
    expect(caption).not.toBeNull();
    expect(caption.textContent.trim()).toBe("Laboratory Observations");

    const headers = Array.from(table.querySelectorAll("thead th"));
    expect(headers).toHaveLength(5);

    // Selection header and non-sortable headers must omit aria-sort and sort buttons
    expect(headers[0].hasAttribute("aria-sort")).toBe(false);
    expect(headers[0].querySelector("button")).toBeNull();
    expect(headers[3].hasAttribute("aria-sort")).toBe(false);
    expect(headers[3].querySelector("button")).toBeNull();
    expect(headers[4].hasAttribute("aria-sort")).toBe(false);
    expect(headers[4].querySelector("button")).toBeNull();

    // Sortable headers render native type="button" controls and expose aria-sort
    const idSortBtn = headers[1].querySelector('button[type="button"]');
    const glucoseSortBtn = headers[2].querySelector('button[type="button"]');
    expect(idSortBtn).not.toBeNull();
    expect(glucoseSortBtn).not.toBeNull();
    expect(headers[1].getAttribute("aria-sort")).toBe("none");
    expect(headers[2].getAttribute("aria-sort")).toBe("none");

    // Keyboard Enter sorts ascending
    glucoseSortBtn.focus();
    const enterEvt = new KeyboardEvent("keydown", {
      key: "Enter",
      bubbles: true,
      cancelable: true,
    });
    glucoseSortBtn.dispatchEvent(enterEvt);
    await nextTick();

    expect(enterEvt.defaultPrevented).toBe(true);
    expect(headers[2].getAttribute("aria-sort")).toBe("ascending");
    expect(headers[1].getAttribute("aria-sort")).toBe("none");
    expect(
      Array.from(table.querySelectorAll("tbody tr td:nth-child(2)")).map((td) =>
        td.textContent.trim()
      )
    ).toEqual(["SUBJ-001", "SUBJ-002"]);

    // Keyboard Space reverses to descending
    const spaceEvt = new KeyboardEvent("keydown", {
      key: " ",
      bubbles: true,
      cancelable: true,
    });
    glucoseSortBtn.dispatchEvent(spaceEvt);
    await nextTick();

    expect(spaceEvt.defaultPrevented).toBe(true);
    expect(headers[2].getAttribute("aria-sort")).toBe("descending");
    expect(
      Array.from(table.querySelectorAll("tbody tr td:nth-child(2)")).map((td) =>
        td.textContent.trim()
      )
    ).toEqual(["SUBJ-003", "SUBJ-002"]);

    // Mouse click on Subject ID button sorts ascending by ID
    idSortBtn.click();
    await nextTick();
    expect(headers[1].getAttribute("aria-sort")).toBe("ascending");
    expect(headers[2].getAttribute("aria-sort")).toBe("none");

    const audit = await toBeAccessible({ element: host });
    expect(audit.pass, audit.message()).toBe(true);
  });

  it("activates focusable rows with Enter/Space, guards checkboxes and slotted buttons from row-click, and toggles aria-selected", async () => {
    const onRowClick = vi.fn();
    const onSelect = vi.fn();
    const onInspect = vi.fn();

    const host = mountHost(
      () => () =>
        h(
          ClinicalDataTable,
          {
            title: "Enrolled Subjects",
            columns,
            data: sampleRows,
            pageSize: 10,
            selectable: true,
            onSelect,
            "onRow-click": onRowClick,
          },
          {
            "cell-actions": ({ row }) =>
              h(
                "button",
                {
                  type: "button",
                  class: "btn-inspect-row",
                  onClick: () => onInspect(row.id),
                },
                [h("span", "Inspect "), h("span", { class: "sr-only" }, row.id)]
              ),
          }
        )
    );

    await nextTick();

    const dataRows = Array.from(host.querySelectorAll("tbody tr.data-row"));
    expect(dataRows).toHaveLength(3);
    expect(dataRows[0].getAttribute("tabindex")).toBe("0");
    expect(dataRows[0].getAttribute("aria-selected")).toBe("false");

    // Keyboard Enter and Space directly on focused <tr> emit row-click
    dataRows[0].focus();
    const rowEnter = new KeyboardEvent("keydown", {
      key: "Enter",
      bubbles: true,
      cancelable: true,
    });
    dataRows[0].dispatchEvent(rowEnter);
    expect(rowEnter.defaultPrevented).toBe(true);
    expect(onRowClick).toHaveBeenCalledTimes(1);
    expect(onRowClick).toHaveBeenLastCalledWith(sampleRows[0]);

    const rowSpace = new KeyboardEvent("keydown", {
      key: " ",
      bubbles: true,
      cancelable: true,
    });
    dataRows[1].dispatchEvent(rowSpace);
    expect(rowSpace.defaultPrevented).toBe(true);
    expect(onRowClick).toHaveBeenCalledTimes(2);
    expect(onRowClick).toHaveBeenLastCalledWith(sampleRows[1]);

    // Clicking a plain data cell emits row-click
    const plainCell = dataRows[2].querySelector("td.data-cell");
    plainCell.click();
    expect(onRowClick).toHaveBeenCalledTimes(3);
    expect(onRowClick).toHaveBeenLastCalledWith(sampleRows[2]);

    // Checkbox click, change, and keyboard Space must NOT double-fire row-click
    const firstCheckbox = dataRows[0].querySelector('input[type="checkbox"]');
    firstCheckbox.focus();
    firstCheckbox.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: " ",
        bubbles: true,
        cancelable: true,
      })
    );
    firstCheckbox.click();
    await nextTick();

    expect(onSelect).toHaveBeenCalledWith(["SUBJ-003"]);
    expect(dataRows[0].getAttribute("aria-selected")).toBe("true");
    expect(dataRows[1].getAttribute("aria-selected")).toBe("false");
    expect(onRowClick).toHaveBeenCalledTimes(3);

    // Slotted button click and keyboard Enter/Space must NOT double-fire row-click
    const inspectBtn = dataRows[0].querySelector(".btn-inspect-row");
    inspectBtn.focus();
    inspectBtn.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Enter",
        bubbles: true,
        cancelable: true,
      })
    );
    inspectBtn.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: " ",
        bubbles: true,
        cancelable: true,
      })
    );
    inspectBtn.click();
    await nextTick();

    expect(onInspect).toHaveBeenCalledTimes(1);
    expect(onInspect).toHaveBeenCalledWith("SUBJ-003");
    expect(onRowClick).toHaveBeenCalledTimes(3);

    const audit = await toBeAccessible({ element: host });
    expect(audit.pass, audit.message()).toBe(true);
  });

  it("omits aria-selected when selectable is false, announces polite pagination status, clamps currentPage on filter shrink, and passes axe-core in empty state", async () => {
    const reactiveRows = ref([
      { id: "SUBJ-001", glucose: 88, notes: "Visit 1" },
      { id: "SUBJ-002", glucose: 94, notes: "Visit 1" },
      { id: "SUBJ-003", glucose: 101, notes: "Visit 2" },
      { id: "SUBJ-004", glucose: 108, notes: "Visit 2" },
      { id: "SUBJ-005", glucose: 115, notes: "Visit 3" },
    ]);

    const host = mountHost(
      () => () =>
        h(ClinicalDataTable, {
          title: "Filtered Cohort",
          columns: columns.slice(0, 3),
          data: reactiveRows.value,
          pageSize: 2,
          selectable: false,
        })
    );

    await nextTick();

    const firstRow = host.querySelector("tbody tr.data-row");
    expect(firstRow.hasAttribute("aria-selected")).toBe(false);

    const paginationBtns = Array.from(
      host.querySelectorAll(".table-pagination button")
    );
    expect(paginationBtns).toHaveLength(2);
    expect(paginationBtns[0].getAttribute("type")).toBe("button");
    expect(paginationBtns[1].getAttribute("type")).toBe("button");
    expect(paginationBtns[0].disabled).toBe(true);
    expect(paginationBtns[1].disabled).toBe(false);

    const status = host.querySelector(".pagination-info");
    expect(status.getAttribute("role")).toBe("status");
    expect(status.getAttribute("aria-live")).toBe("polite");
    expect(status.textContent.trim()).toBe("Page 1 of 3");

    // Navigate to page 3
    paginationBtns[1].click();
    await nextTick();
    paginationBtns[1].click();
    await nextTick();
    expect(status.textContent.trim()).toBe("Page 3 of 3");
    expect(host.textContent).toContain("SUBJ-005");

    // Shrink filtered data to 2 rows (1 page) -> currentPage clamps to 1 instead of rendering empty page 3
    reactiveRows.value = reactiveRows.value.slice(0, 2);
    await nextTick();

    expect(host.querySelector(".empty-cell")).toBeNull();
    expect(host.textContent).toContain("SUBJ-001");
    expect(host.textContent).toContain("SUBJ-002");

    // Empty dataset renders accessible empty message
    reactiveRows.value = [];
    await nextTick();
    expect(host.querySelector(".empty-cell")?.textContent).toContain(
      "No clinical records found."
    );

    const emptyAudit = await toBeAccessible({ element: host });
    expect(emptyAudit.pass, emptyAudit.message()).toBe(true);
  });
});
