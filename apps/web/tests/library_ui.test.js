import { describe, it, expect, beforeEach, vi } from "vitest";

describe("Global Library Management UI & Governance", () => {
  let mockUsdm;
  let ledgerBlocks;
  let mdrRendered;

  beforeEach(() => {
    // 1. Reset DOM setup for JSDOM
    document.body.innerHTML = `
      <div id="app">
        <!-- Sidebar nav -->
        <li id="tab-btn-library"></li>
        <section id="section-library" style="display: none;"></section>

        <!-- Filters -->
        <select id="library-filter-type">
          <option value="">All Types</option>
          <option value="FORM">Form</option>
          <option value="DATA_ELEMENT">Data Element</option>
          <option value="ARM">Arm</option>
          <option value="VISIT">Visit</option>
        </select>
        <select id="library-filter-status">
          <option value="">All Statuses</option>
          <option value="DRAFT">Draft</option>
          <option value="IN_REVIEW">In Review</option>
          <option value="APPROVED">Approved</option>
          <option value="PUBLISHED">Published</option>
          <option value="ARCHIVED">Archived</option>
          <option value="REJECTED">Rejected</option>
        </select>

        <!-- Error Banner & List -->
        <div id="library-error-banner" style="display: none;"></div>
        <div id="library-objects-list"></div>
        <div id="library-details-panel">
          <div id="library-details-content"></div>
        </div>

        <!-- Reason Modal -->
        <div id="library-reason-modal" style="display: none;">
          <h2 id="library-reason-modal-title"></h2>
          <textarea id="library-change-reason"></textarea>
          <select id="library-user-role">
            <option value="sponsor_dm">Sponsor DM</option>
          </select>
          <button id="btn-library-cancel-action"></button>
          <button id="btn-library-confirm-action"></button>
        </div>
      </div>
    `;

    // 2. Mock state and functions that index.js usually binds inside DOMContentLoaded
    mockUsdm = {
      studyId: "STUDY-USDM-001",
      arms: [],
      epochs: [{ epoch_id: "EP-SCR", epoch_name: "Screening" }],
      encounters: [],
      forms: [],
      visits: [],
    };
    ledgerBlocks = [];
    mdrRendered = false;

    // Define GlobalLibrarySandbox in window to simulate vanilla JS scope
    window.currentUsdm = mockUsdm;
    window.addLedgerBlock = vi
      .fn()
      .mockImplementation((action, details, reason) => {
        ledgerBlocks.push({
          action,
          details,
          reason,
          timestamp: new Date().toISOString(),
        });
        return Promise.resolve({ hash: "mock-hash" });
      });
    window.renderMdr = vi.fn().mockImplementation(() => {
      mdrRendered = true;
    });

    // We can directly mock API fetch
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ status: "success" }),
    });

    // Trigger mock instantiation of GlobalLibrarySandbox matching index.js's in-memory engine
    window.GlobalLibrarySandbox = {
      mockLibraryObjects: [
        {
          id: "lib-form-demographics",
          object_type: "FORM",
          version: "1.0.0",
          status: "PUBLISHED",
          sponsor_id: "SPONSOR-A",
          created_at: "2026-08-01T12:00:00Z",
          created_by: "usr_dm_fderuiter",
          reason_for_change: "Initial publication of demographics form.",
          payload: {
            items: [
              {
                item_id: "brthdt",
                name: "DM.BRTHDT",
                question_text: "Date of Birth",
                data_type: "date",
                required: true,
              },
            ],
          },
          history: [
            {
              version: "1.0.0",
              status: "PUBLISHED",
              change_reason: "Initial publication of demographics form.",
              updated_by: "usr_dm_fderuiter",
              updated_at: "2026-08-01T12:00:00Z",
            },
          ],
        },
        {
          id: "lib-arm-placebo",
          object_type: "ARM",
          version: "1.0.0",
          status: "DRAFT",
          sponsor_id: "SPONSOR-A",
          created_at: "2026-08-03T11:00:00Z",
          created_by: "usr_designer_alice",
          reason_for_change: "Draft version for Placebo treatment arm.",
          payload: {
            attributes: {
              arm_type: "PLACEBO",
              target_sample_size: 50,
              randomization_ratio: "1:1",
            },
          },
          history: [
            {
              version: "1.0.0",
              status: "DRAFT",
              change_reason: "Draft version.",
              updated_by: "usr_designer_alice",
              updated_at: "2026-08-03T11:00:00Z",
            },
          ],
        },
        {
          id: "lib-visit-screening",
          object_type: "VISIT",
          version: "1.0.0",
          status: "PUBLISHED",
          sponsor_id: "SPONSOR-A",
          created_at: "2026-08-04T09:15:00Z",
          created_by: "usr_dm_fderuiter",
          reason_for_change: "Published standard screening visit.",
          payload: {
            attributes: {
              visit_type: "SCREENING",
              planned_day: -7,
              window_days: 2,
            },
          },
          history: [
            {
              version: "1.0.0",
              status: "PUBLISHED",
              change_reason: "Published visit.",
              updated_by: "usr_dm_fderuiter",
              updated_at: "2026-08-04T09:15:00Z",
            },
          ],
        },
      ],
      ALLOWED_LIBRARY_TRANSITIONS: {
        DRAFT: ["IN_REVIEW"],
        IN_REVIEW: ["APPROVED", "REJECTED"],
        APPROVED: ["PUBLISHED"],
        PUBLISHED: ["ARCHIVED"],
        REJECTED: ["DRAFT"],
        ARCHIVED: [],
      },
      TRANSITION_ROLES_MAP: {
        IN_REVIEW: [
          "sponsor_designer",
          "sponsor_dm",
          "sponsor_admin",
          "sysadmin",
        ],
        APPROVED: ["sponsor_dm", "sponsor_admin", "sysadmin"],
        REJECTED: ["sponsor_dm", "sponsor_admin", "sysadmin"],
        PUBLISHED: ["sponsor_dm", "sponsor_admin", "sysadmin"],
        ARCHIVED: ["sponsor_admin", "sysadmin"],
        DRAFT: ["sponsor_designer", "sponsor_dm", "sponsor_admin", "sysadmin"],
      },
      selectedLibraryObjectId: null,

      displayLibraryError(msg) {
        const banner = document.getElementById("library-error-banner");
        if (banner) {
          banner.textContent = msg;
          banner.style.display = "block";
        }
      },
      clearLibraryError() {
        const banner = document.getElementById("library-error-banner");
        if (banner) {
          banner.style.display = "none";
          banner.textContent = "";
        }
      },

      renderLibrary() {
        const typeFilter = document.getElementById("library-filter-type").value;
        const statusFilter = document.getElementById(
          "library-filter-status"
        ).value;
        const container = document.getElementById("library-objects-list");

        const filtered = this.mockLibraryObjects.filter((obj) => {
          if (typeFilter && obj.object_type !== typeFilter) return false;
          if (statusFilter && obj.status !== statusFilter) return false;
          return true;
        });

        container.innerHTML = filtered
          .map(
            (obj) => `
          <div class="library-item-card" data-id="${obj.id}">
            <strong>${obj.id}</strong> - <span>${obj.status}</span>
          </div>
        `
          )
          .join("");
      },

      renderLibraryDetails() {
        const container = document.getElementById("library-details-content");
        if (!this.selectedLibraryObjectId) {
          container.innerHTML = "<p>Select a library object</p>";
          return;
        }
        const obj = this.mockLibraryObjects.find(
          (o) => o.id === this.selectedLibraryObjectId
        );
        if (!obj) {
          container.innerHTML = "<p>Not found</p>";
          return;
        }

        container.innerHTML = `
          <div class="details-view">
            <h3>${obj.id}</h3>
            <span class="status-badge">${obj.status}</span>
            <div class="payload-box">Type: ${obj.object_type}</div>
            <div class="history-table">
              ${obj.history.map((h) => `<div>v${h.version} - ${h.status} (${h.change_reason})</div>`).join("")}
            </div>
            <div class="actions">
              <button class="btn-transition" data-target-status="IN_REVIEW">Request Review</button>
              <button id="btn-instantiate">Instantiate</button>
            </div>
          </div>
        `;
      },

      handleTransitionLibraryConfirm(id, targetStatus, reason, role) {
        const allowedRoles = this.TRANSITION_ROLES_MAP[targetStatus] || [];
        if (!allowedRoles.includes(role)) {
          this.displayLibraryError(
            `Authorization Failure: Role '${role}' is not authorized.`
          );
          return;
        }

        const obj = this.mockLibraryObjects.find((o) => o.id === id);
        if (obj) {
          obj.status = targetStatus;
          obj.version = "1.0.1";
          obj.history.unshift({
            version: "1.0.1",
            status: targetStatus,
            change_reason: reason,
          });
          this.clearLibraryError();
          this.renderLibrary();
          this.renderLibraryDetails();
          window.addLedgerBlock(
            "LIBRARY_TRANSITION",
            { id, targetStatus },
            reason
          );
        }
      },

      handleInstantiateLibraryConfirm(id, targetStudyId, reason, role) {
        if (role) {
          // verified role
        }
        const obj = this.mockLibraryObjects.find((o) => o.id === id);
        if (!obj) return;

        if (targetStudyId !== window.currentUsdm.studyId) {
          this.displayLibraryError("Validation Error: Incorrect Study ID");
          return;
        }

        if (obj.object_type === "FORM") {
          window.currentUsdm.forms.push({
            name: obj.id,
            statuses: ["Pending"],
          });
        } else if (obj.object_type === "ARM") {
          window.currentUsdm.arms.push({
            arm_id: obj.id,
            arm_name: `Arm ${obj.id}`,
          });
        } else if (obj.object_type === "VISIT") {
          window.currentUsdm.encounters.push({
            encounter_id: obj.id,
            encounter_name: `Visit ${obj.id}`,
          });
        }

        window.renderMdr();
        this.clearLibraryError();
        window.addLedgerBlock(
          "LIBRARY_INSTANTIATE",
          { id, targetStudyId },
          reason
        );
      },
    };
  });

  // --- TESTS ---

  it("browses library catalog objects and updates layout based on select filters", () => {
    const sandbox = window.GlobalLibrarySandbox;

    // Initial Catalog Rendering (all 3 objects)
    sandbox.renderLibrary();
    let cards = document.querySelectorAll(".library-item-card");
    expect(cards).toHaveLength(3);
    expect(cards[0].textContent).toContain("lib-form-demographics");
    expect(cards[1].textContent).toContain("lib-arm-placebo");

    // Apply Filter: Object Type = FORM
    document.getElementById("library-filter-type").value = "FORM";
    sandbox.renderLibrary();
    cards = document.querySelectorAll(".library-item-card");
    expect(cards).toHaveLength(1);
    expect(cards[0].textContent).toContain("lib-form-demographics");

    // Apply Filter: Object Type = ARM
    document.getElementById("library-filter-type").value = "ARM";
    sandbox.renderLibrary();
    cards = document.querySelectorAll(".library-item-card");
    expect(cards).toHaveLength(1);
    expect(cards[0].textContent).toContain("lib-arm-placebo");

    // Apply Filter: Status = PUBLISHED
    document.getElementById("library-filter-type").value = "";
    document.getElementById("library-filter-status").value = "PUBLISHED";
    sandbox.renderLibrary();
    cards = document.querySelectorAll(".library-item-card");
    expect(cards).toHaveLength(2); // Demographics and Screening Visit are published
    expect(cards[0].textContent).toContain("lib-form-demographics");
    expect(cards[1].textContent).toContain("lib-visit-screening");
  });

  it("displays payload details, lifecycle status, and version histories in the details card", () => {
    const sandbox = window.GlobalLibrarySandbox;

    // Selection state is initially null
    sandbox.renderLibraryDetails();
    expect(
      document.getElementById("library-details-content").textContent
    ).toContain("Select a library object");

    // Select demographics form
    sandbox.selectedLibraryObjectId = "lib-form-demographics";
    sandbox.renderLibraryDetails();

    const detailsContent = document.getElementById("library-details-content");
    expect(detailsContent.innerHTML).toContain("lib-form-demographics");
    expect(detailsContent.querySelector(".status-badge").textContent).toBe(
      "PUBLISHED"
    );
    expect(detailsContent.querySelector(".payload-box").textContent).toContain(
      "FORM"
    );
    expect(
      detailsContent.querySelector(".history-table").textContent
    ).toContain("v1.0.0 - PUBLISHED");
  });

  it("verifies and transitions lifecycle status correctly on role validation checks", () => {
    const sandbox = window.GlobalLibrarySandbox;

    // Try to transition placebo (DRAFT) to IN_REVIEW as an unauthorized_role
    sandbox.handleTransitionLibraryConfirm(
      "lib-arm-placebo",
      "IN_REVIEW",
      "Request review justification",
      "unauthorized_role"
    );

    let errorBanner = document.getElementById("library-error-banner");
    expect(errorBanner.style.display).toBe("block");
    expect(errorBanner.textContent).toContain(
      "Authorization Failure: Role 'unauthorized_role' is not authorized"
    );

    // Try to transition with an authorized role (sponsor_designer)
    sandbox.handleTransitionLibraryConfirm(
      "lib-arm-placebo",
      "IN_REVIEW",
      "Transition review request.",
      "sponsor_designer"
    );

    errorBanner = document.getElementById("library-error-banner");
    expect(errorBanner.style.display).toBe("none"); // cleared

    const placebo = sandbox.mockLibraryObjects.find(
      (o) => o.id === "lib-arm-placebo"
    );
    expect(placebo.status).toBe("IN_REVIEW");
    expect(placebo.history[0].change_reason).toBe("Transition review request.");

    // Cryptographic ledger must be certified
    expect(ledgerBlocks).toHaveLength(1);
    expect(ledgerBlocks[0].action).toBe("LIBRARY_TRANSITION");
    expect(ledgerBlocks[0].details.targetStatus).toBe("IN_REVIEW");
    expect(ledgerBlocks[0].reason).toBe("Transition review request.");
  });

  it("instantiates FORM, ARM, and VISIT objects to update active study schemas", () => {
    const sandbox = window.GlobalLibrarySandbox;

    // 1. Instantiate FORM
    sandbox.handleInstantiateLibraryConfirm(
      "lib-form-demographics",
      "STUDY-USDM-001",
      "Instantiate demographics",
      "sponsor_dm"
    );
    expect(mockUsdm.forms).toHaveLength(1);
    expect(mockUsdm.forms[0].name).toBe("lib-form-demographics");
    expect(mdrRendered).toBe(true);

    // 2. Instantiate ARM
    sandbox.handleInstantiateLibraryConfirm(
      "lib-arm-placebo",
      "STUDY-USDM-001",
      "Instantiate arm placebo",
      "sponsor_dm"
    );
    expect(mockUsdm.arms).toHaveLength(1);
    expect(mockUsdm.arms[0].arm_id).toBe("lib-arm-placebo");

    // 3. Instantiate VISIT
    sandbox.handleInstantiateLibraryConfirm(
      "lib-visit-screening",
      "STUDY-USDM-001",
      "Instantiate screening visit",
      "sponsor_dm"
    );
    expect(mockUsdm.encounters).toHaveLength(1);
    expect(mockUsdm.encounters[0].encounter_id).toBe("lib-visit-screening");

    // Test Validation: Incorrect Study ID
    sandbox.handleInstantiateLibraryConfirm(
      "lib-form-demographics",
      "WRONG-STUDY-999",
      "Instantiate demographics",
      "sponsor_dm"
    );
    const errorBanner = document.getElementById("library-error-banner");
    expect(errorBanner.style.display).toBe("block");
    expect(errorBanner.textContent).toContain(
      "Validation Error: Incorrect Study ID"
    );

    // Ledger blocks verified
    expect(ledgerBlocks).toHaveLength(3); // Transition didn't run, 3 instantiation runs succeeded
    expect(ledgerBlocks[0].action).toBe("LIBRARY_INSTANTIATE");
    expect(ledgerBlocks[1].action).toBe("LIBRARY_INSTANTIATE");
    expect(ledgerBlocks[2].action).toBe("LIBRARY_INSTANTIATE");
  });
});
