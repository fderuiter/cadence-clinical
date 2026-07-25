import { describe, it, expect, beforeEach } from "vitest";
import { createCtmsMilestoneTable, createCtmsVisitTable } from "ui";
import { renderCtms } from "../index.js";

// Minimal stub for document and elements to run without browser/jsdom
class ElementStub {
  constructor(id) {
    this.id = id;
    this._innerHTML = "";
  }
  get innerHTML() {
    return this._innerHTML;
  }
  set innerHTML(val) {
    this._innerHTML = val;
  }
}

describe("CTMS UI Helper functions", () => {
  it("createCtmsMilestoneTable renders milestone table with valid data", () => {
    const milestones = [
      {
        id: "M1",
        type: "SITE_SELECTION",
        plannedDate: "2026-08-01",
        actualDate: "2026-08-01",
        status: "ACHIEVED",
      },
      {
        id: "M2",
        type: "INITIATION_VISIT",
        plannedDate: "2026-08-10",
        actualDate: "",
        status: "PLANNED",
      },
    ];
    const html = createCtmsMilestoneTable(milestones);
    expect(html).toContain("SITE_SELECTION");
    expect(html).toContain("INITIATION_VISIT");
    expect(html).toContain("2026-08-01");
    expect(html).toContain("Pending");
    expect(html).toContain("ACHIEVED");
    expect(html).toContain("PLANNED");
  });

  it("createCtmsMilestoneTable handles invalid input gracefully", () => {
    expect(createCtmsMilestoneTable(null)).toContain("Invalid milestone data.");
    expect(createCtmsMilestoneTable("string")).toContain(
      "Invalid milestone data."
    );
  });

  it("createCtmsVisitTable renders visits table with valid data", () => {
    const visits = [
      {
        id: "V1",
        type: "SIV",
        scheduledDate: "2026-08-10",
        actualDate: "2026-08-12",
        status: "SIGNED_OFF",
        cra: "cra_fderuiter",
      },
    ];
    const html = createCtmsVisitTable(visits);
    expect(html).toContain("SIV");
    expect(html).toContain("2026-08-10");
    expect(html).toContain("2026-08-12");
    expect(html).toContain("cra_fderuiter");
    expect(html).toContain("SIGNED_OFF");
  });

  it("createCtmsVisitTable handles invalid input gracefully", () => {
    expect(createCtmsVisitTable(null)).toContain(
      "Invalid monitoring visit data."
    );
  });
});

describe("renderCtms integration with DOM", () => {
  let mockElements;

  beforeEach(() => {
    mockElements = {
      "ctms-milestones-container": new ElementStub("ctms-milestones-container"),
      "ctms-visits-container": new ElementStub("ctms-visits-container"),
      "ctms-workload-container": new ElementStub("ctms-workload-container"),
      "ctms-recruitment-container": new ElementStub(
        "ctms-recruitment-container"
      ),
    };

    // Set global document mock
    global.document = {
      getElementById: (id) => mockElements[id] || null,
    };
  });

  it("renders ctms dashboards to containers", () => {
    const mockCtmsData = {
      milestones: [
        {
          id: "M1",
          type: "SITE_SELECTION",
          plannedDate: "2026-08-01",
          actualDate: "2026-08-01",
          status: "ACHIEVED",
        },
      ],
      visits: [
        {
          id: "V1",
          type: "SIV",
          scheduledDate: "2026-08-10",
          actualDate: "2026-08-12",
          status: "SIGNED_OFF",
          cra: "cra_fderuiter",
        },
      ],
      allocations: [
        {
          cra: "cra_alice",
          activeAllocations: 1,
          sites: ["Site-03"],
          studies: ["STUDY-01"],
        },
      ],
      recruitment: [
        { siteId: "Site-01", screened: 15, enrolled: 8, target: 20 },
      ],
    };

    renderCtms(mockCtmsData);

    const milestonesHTML = mockElements["ctms-milestones-container"].innerHTML;
    const visitsHTML = mockElements["ctms-visits-container"].innerHTML;
    const workloadHTML = mockElements["ctms-workload-container"].innerHTML;
    const recruitmentHTML =
      mockElements["ctms-recruitment-container"].innerHTML;

    expect(milestonesHTML).toContain("SITE_SELECTION");
    expect(visitsHTML).toContain("SIV");
    expect(workloadHTML).toContain("cra_alice");
    expect(recruitmentHTML).toContain("Site-01");
  });
});
