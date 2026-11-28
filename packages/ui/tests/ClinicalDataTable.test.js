// @vitest-environment jsdom
import fs from "fs";
import path from "path";
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

    expect(region.classList.contains("focus-ring-inset")).toBe(true);

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
    expect(idSortBtn.classList.contains("focus-ring-inset")).toBe(true);
    expect(glucoseSortBtn.classList.contains("focus-ring-inset")).toBe(true);
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
    expect(dataRows[0].classList.contains("focus-ring-inset")).toBe(true);
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

  it("supports externalPagination, load-more, page-change, and masked-row slots without firing row-click on masked records", async () => {
    const onRowClick = vi.fn();
    const onLoadMore = vi.fn();
    const onPageChange = vi.fn();
    const mixedRows = [
      { id: "SITE-01", name: "Academic Medical Center", masked: false },
      { id: "SITE-99", masked: true, maskReason: "Outside assigned sites" },
    ];

    const host = mountHost(
      () => () =>
        h(
          ClinicalDataTable,
          {
            title: "Trial Sites",
            tableId: "study-sites-table",
            columns: [
              { key: "id", label: "Site Code", sortable: true },
              { key: "name", label: "Site Name", sortable: true },
            ],
            data: mixedRows,
            externalPagination: true,
            totalRecords: 5,
            hasMore: true,
            loadMoreLabel: "Show more sites",
            isRowMasked: (row) => Boolean(row.masked),
            "onRow-click": onRowClick,
            "onLoad-more": onLoadMore,
            "onPage-change": onPageChange,
          },
          {
            "masked-row": ({ row }) =>
              h(
                "span",
                { class: "masked-note" },
                `Restricted: ${row.maskReason}`
              ),
          }
        )
    );

    await nextTick();

    const table = host.querySelector("#study-sites-table");
    expect(table).not.toBeNull();
    const maskedRow = table.querySelector("tbody tr.masked-row");
    expect(maskedRow).not.toBeNull();
    expect(maskedRow.hasAttribute("tabindex")).toBe(false);
    expect(maskedRow.textContent).toContain(
      "Restricted: Outside assigned sites"
    );

    maskedRow.click();
    expect(onRowClick).not.toHaveBeenCalled();

    const activeRow = table.querySelector("tbody tr.data-row");
    activeRow.querySelector("td").click();
    expect(onRowClick).toHaveBeenCalledTimes(1);
    expect(onRowClick).toHaveBeenCalledWith(mixedRows[0]);

    const status = host.querySelector(".pagination-info");
    expect(status.textContent.trim()).toBe("Showing 2 of 5");

    const loadMoreBtn = host.querySelector(".table-pagination button");
    expect(loadMoreBtn.textContent.trim()).toBe("Show more sites");
    loadMoreBtn.click();
    expect(onLoadMore).toHaveBeenCalledTimes(1);

    const audit = await toBeAccessible({ element: host });
    expect(audit.pass, audit.message()).toBe(true);
  });

  it("prevents duplicate landmark names when nested inside an aria-labelledby section with hideHeader=true and supports explicit ariaLabel", async () => {
    const host = mountHost(
      () => () =>
        h("div", {}, [
          h(
            "section",
            {
              id: "study-locks-section",
              "aria-labelledby": "study-locks-title",
            },
            [
              h("h3", { id: "study-locks-title" }, "Study Locks"),
              h(ClinicalDataTable, {
                title: "Study Locks",
                labelledBy: "study-locks-title",
                hideHeader: true,
                columns: [
                  { key: "scope", label: "Scope" },
                  { key: "target", label: "Target" },
                ],
                data: [{ id: "L-1", scope: "Subject", target: "SUBJ-001" }],
              }),
            ]
          ),
          h(ClinicalDataTable, {
            title: "Explicit Label Cohort",
            ariaLabel: "Explicit cohort table",
            hideHeader: true,
            columns: [{ key: "id", label: "Subject ID" }],
            data: [{ id: "SUBJ-002" }],
          }),
        ])
    );

    await nextTick();

    const tables = host.querySelectorAll("table.clinical-table");
    expect(tables).toHaveLength(2);
    expect(tables[0].getAttribute("aria-labelledby")).toBe("study-locks-title");
    expect(tables[1].getAttribute("aria-label")).toBe("Explicit cohort table");
    expect(tables[1].hasAttribute("aria-labelledby")).toBe(false);

    const regions = host.querySelectorAll(".table-wrapper[role='region']");
    expect(regions[0].getAttribute("aria-labelledby")).not.toBe(
      "study-locks-title"
    );
    expect(regions[1].getAttribute("aria-label")).toBe("Explicit cohort table");

    const audit = await toBeAccessible({ element: host });
    expect(audit.pass, audit.message()).toBe(true);
  });

  it("preserves computed -3px outline-offset and inset 5px halo on .table-wrapper, .sort-button, and .data-row when tokens.css is loaded", async () => {
    const tokensCss = fs.readFileSync(
      path.resolve(__dirname, "../tokens.css"),
      "utf-8"
    );
    const sfcSource = fs.readFileSync(
      path.resolve(
        __dirname,
        "../src/components/clinical/ClinicalDataTable.vue"
      ),
      "utf-8"
    );
    const scopedMatch = sfcSource.match(/<style[^>]*>([\s\S]*?)<\/style>/i);
    expect(scopedMatch).not.toBeNull();

    const styleEl = document.createElement("style");
    styleEl.textContent = `${tokensCss}\n${scopedMatch[1]}`;
    document.head.appendChild(styleEl);

    try {
      const host = mountHost(
        () => () =>
          h(ClinicalDataTable, {
            title: "Inset Focus Cohort",
            columns,
            data: sampleRows,
            pageSize: 10,
          })
      );
      await nextTick();

      const wrapper = host.querySelector(".table-wrapper");
      const sortBtn = host.querySelector(".sort-button");
      const firstRow = host.querySelector("tbody tr.data-row");

      for (const el of [wrapper, sortBtn, firstRow]) {
        expect(el).not.toBeNull();
        expect(el.classList.contains("focus-ring-inset")).toBe(true);
        el.focus();
        const cs = window.getComputedStyle(el);
        expect(cs.outlineStyle).toBe("solid");
        expect(cs.outlineWidth).toBe("3px");
        expect(cs.outlineOffset).toBe("-3px");
        expect(cs.boxShadow).toMatch(
          /inset.*0px 0px 0px 5px|0px 0px 0px 5px.*inset/
        );
      }
    } finally {
      styleEl.remove();
    }
  });

  it("projects rows into ClinicalRecordCard items at <= 480px viewports reusing #cell-*, #masked-row, #after-row, and #empty slots with a mobile sort bar", async () => {
    const originalInnerWidth = window.innerWidth;
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 390,
    });
    window.dispatchEvent(new Event("resize"));

    const rowsRef = ref([
      {
        id: "VISIT-02",
        site: "Site 102",
        status: "OPEN",
        due: "2026-09-10",
        masked: false,
      },
      {
        id: "VISIT-01",
        site: "Site 101",
        status: "VERIFIED",
        due: "2026-09-02",
        masked: false,
      },
      {
        id: "VISIT-99",
        masked: true,
        maskReason: "Unassigned site",
      },
    ]);
    const onSortChange = vi.fn();
    const onInspect = vi.fn();

    try {
      const host = mountHost(
        () => () =>
          h(
            ClinicalDataTable,
            {
              title: "Monitoring Visits",
              density: "touch",
              mobileMode: "auto",
              columns: [
                {
                  key: "id",
                  label: "Visit ID",
                  sortable: true,
                  sticky: "left",
                  cardRole: "identifier",
                  mono: true,
                },
                {
                  key: "site",
                  label: "Site",
                  sortable: true,
                  cardRole: "title",
                },
                {
                  key: "status",
                  label: "Status",
                  sortable: true,
                  cardRole: "status",
                },
                { key: "due", label: "Target Date", mono: true },
                {
                  key: "actions",
                  label: "Actions",
                  sticky: "right",
                  cardRole: "actions",
                },
              ],
              data: rowsRef.value,
              isRowMasked: (r) => Boolean(r.masked),
              "onSort-change": onSortChange,
            },
            {
              "cell-status": ({ row }) =>
                h("span", { class: "custom-status-pill" }, row.status),
              "cell-actions": ({ row }) =>
                h(
                  "button",
                  {
                    type: "button",
                    class: "card-action-btn",
                    onClick: () => onInspect(row.id),
                  },
                  [h("span", "Review"), h("span", { class: "sr-only" }, row.id)]
                ),
              "masked-row": ({ row }) =>
                h("span", { class: "masked-card-msg" }, row.maskReason),
              "after-row": ({ row, isCardView }) =>
                isCardView && row.id === "VISIT-01"
                  ? h("div", { class: "projected-after-card" }, "Expanded note")
                  : null,
              empty: () => h("div", { class: "custom-empty-slot" }, "Zero due"),
            }
          )
      );

      await nextTick();
      await new Promise((resolve) => setTimeout(resolve, 20));
      await nextTick();

      // Table is replaced by projected ClinicalRecordCard list
      expect(host.querySelector("table.clinical-table")).toBeNull();
      const cards = Array.from(host.querySelectorAll(".clinical-record-card"));
      expect(cards).toHaveLength(3);
      expect(cards[0].classList.contains("clinical-record-card--touch")).toBe(
        true
      );
      expect(host.querySelector(".custom-status-pill")?.textContent).toBe(
        "OPEN"
      );
      expect(host.querySelector(".masked-card-msg")?.textContent).toBe(
        "Unassigned site"
      );
      expect(host.querySelector(".projected-after-card")?.textContent).toBe(
        "Expanded note"
      );

      // Mobile sort selector bar is rendered and sorts cards
      const sortBar = host.querySelector(
        '[data-testid="clinical-mobile-sort-bar"]'
      );
      expect(sortBar).not.toBeNull();
      const sortSelect = sortBar.querySelector("select");
      sortSelect.value = "id";
      sortSelect.dispatchEvent(new Event("change", { bubbles: true }));
      await nextTick();

      const sortedIds = Array.from(
        host.querySelectorAll(".clinical-record-card__identifier")
      ).map((el) => el.textContent.trim());
      expect(sortedIds[0]).toBe("VISIT-01");
      expect(onSortChange).toHaveBeenCalledWith({
        key: "id",
        order: "asc",
      });

      const orderBtn = sortBar.querySelector(".mobile-sort-order-btn");
      orderBtn.click();
      await nextTick();
      expect(onSortChange).toHaveBeenLastCalledWith({
        key: "id",
        order: "desc",
      });

      // Slotted action button works in card footer
      host.querySelector(".card-action-btn").click();
      expect(onInspect).toHaveBeenCalledTimes(1);

      // Empty state reuses #empty slot in card mode
      rowsRef.value = [];
      await nextTick();
      expect(host.querySelector(".custom-empty-slot")?.textContent).toBe(
        "Zero due"
      );

      const audit = await toBeAccessible({ element: host });
      expect(audit.pass, audit.message()).toBe(true);
    } finally {
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        writable: true,
        value: originalInnerWidth,
      });
      window.dispatchEvent(new Event("resize"));
    }
  });

  it("renders sticky left/right column classes, stickyHeader, Lucide SVG sort icons without Unicode arrows, and density modifiers on desktop viewports", async () => {
    const originalInnerWidth = window.innerWidth;
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 1280,
    });
    window.dispatchEvent(new Event("resize"));

    try {
      const host = mountHost(
        () => () =>
          h(ClinicalDataTable, {
            title: "Desktop Sticky Matrix",
            stickyHeader: true,
            density: "compact",
            captionVisible: true,
            columns: [
              { key: "id", label: "ID", sticky: "left", sortable: true },
              { key: "glucose", label: "Glucose", sortable: true },
              { key: "actions", label: "Actions", sticky: "right" },
            ],
            data: sampleRows,
          })
      );

      await nextTick();

      const container = host.querySelector(".clinical-data-table-container");
      expect(
        container.classList.contains("clinical-data-table-container--compact")
      ).toBe(true);
      const table = host.querySelector("table.clinical-table");
      expect(table.classList.contains("clinical-table--sticky-header")).toBe(
        true
      );
      expect(
        table.querySelector("caption.table-caption-visible")
      ).not.toBeNull();

      const headers = table.querySelectorAll("thead th");
      expect(headers[0].classList.contains("col-sticky-left")).toBe(true);
      expect(headers[2].classList.contains("col-sticky-right")).toBe(true);

      const firstRowCells = table.querySelectorAll("tbody tr:first-child > *");
      expect(firstRowCells[0].classList.contains("col-sticky-left")).toBe(true);
      expect(firstRowCells[2].classList.contains("col-sticky-right")).toBe(
        true
      );

      const sortSvg = headers[0].querySelector(".sort-icon svg");
      expect(sortSvg).not.toBeNull();
      expect(sortSvg.getAttribute("stroke-width")).toBe("1.75");
      expect(headers[0].textContent).not.toMatch(/[\u21C5\u25B2\u25BC]/);
    } finally {
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        writable: true,
        value: originalInnerWidth,
      });
      window.dispatchEvent(new Event("resize"));
    }
  });
});
