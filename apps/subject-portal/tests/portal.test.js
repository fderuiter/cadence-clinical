import { describe, it, expect, beforeEach, vi } from "vitest";

// Set up JSDOM mock elements before importing index.js
beforeEach(() => {
  window.__MOCK_TEST_ENV__ = true;
  document.body.innerHTML = `
    <div id="app">
      <header class="portal-header">
        <div class="header-branding">
          <h1>My <span>Cadence</span></h1>
        </div>
      </header>

      <div class="portal-container">
        <nav class="portal-navigation">
          <ul class="nav-tabs">
            <li id="tab-btn-tasks" class="nav-item">
              <button type="button">My Tasks</button>
            </li>
            <li id="tab-btn-compliance" class="nav-item">
              <button type="button">My Compliance</button>
            </li>
            <li id="tab-btn-inbox" class="nav-item">
              <button type="button">My Inbox <span id="unread-count">0</span></button>
            </li>
          </ul>
          <span id="session-subject-id">Loading...</span>
        </nav>

        <main class="portal-main">
          <!-- Tasks View -->
          <section id="view-tasks" class="portal-view">
            <div id="tasks-list-container"></div>
          </section>

          <!-- Questionnaire View -->
          <section id="view-questionnaire" class="portal-view">
            <button type="button" id="btn-back-to-tasks">Back</button>
            <h2 id="questionnaire-title"></h2>
            <p id="questionnaire-desc"></p>
            <div id="questionnaire-form-container"></div>
            <button type="button" id="btn-cancel-questionnaire">Cancel</button>
            <button type="button" id="btn-submit-questionnaire">Submit</button>
          </section>

          <!-- Compliance View -->
          <section id="view-compliance" class="portal-view">
            <div id="compliance-rate-pct">0%</div>
            <div id="compliance-completed-count">0</div>
            <div id="compliance-pending-count">0</div>
            <div id="compliance-overdue-count">0</div>
            <table>
              <tbody id="compliance-history-tbody"></tbody>
            </table>
          </section>

          <!-- Inbox View -->
          <section id="view-inbox" class="portal-view">
            <div id="inbox-container"></div>
          </section>
        </main>
      </div>

      <!-- Electronic Signature Modal -->
      <div id="portal-sign-modal" style="display: none;">
        <input type="text" id="sign-username" />
        <input type="password" id="sign-password" />
        <select id="sign-reason">
          <option value="Initial Questionnaire Completion">Initial Questionnaire Completion</option>
          <option value="Other">Other</option>
        </select>
        <textarea id="sign-reason-custom"></textarea>
        <button type="button" id="btn-modal-cancel">Cancel</button>
        <button type="button" id="btn-modal-sign">Sign</button>
      </div>

      <!-- Footer / Ledger -->
      <div id="portal-ledger-timeline"></div>
    </div>
  `;

  // Mock global fetch to prevent actual calls in unit tests
  globalThis.fetch = vi.fn().mockImplementation(() =>
    Promise.resolve({
      ok: true,
      json: () => Promise.resolve({ status: "success" }),
    })
  );
});

describe("eCOA Companion Patient Portal - Workflow Tests", () => {
  it("initializes app state with default tasks and mock data on load", async () => {
    // Import dynamically to trigger DOM listeners and state bootstrap
    const portal = await import("../index.js");

    // Initialize application
    await portal.initializeApp();

    // Check that ID is rendered
    const nameEl = document.getElementById("session-subject-id");
    expect(nameEl.textContent).toBe("subject_001");

    // Check default tasks rendering
    const tasksList = document.getElementById("tasks-list-container");
    expect(tasksList.innerHTML).toContain("Daily Health &amp; Vital Diary");
    expect(tasksList.innerHTML).toContain("Weekly Symptoms &amp; eCOA Checklist");
  });

  it("handles navigation between primary patient-facing views", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Start with tasks view active
    portal.showView("view-tasks");
    expect(document.getElementById("view-tasks").classList.contains("active")).toBe(true);
    expect(document.getElementById("view-compliance").classList.contains("active")).toBe(false);

    // Switch to compliance
    portal.showView("view-compliance");
    expect(document.getElementById("view-tasks").classList.contains("active")).toBe(false);
    expect(document.getElementById("view-compliance").classList.contains("active")).toBe(true);
  });

  it("renders eCOA questionnaire fields dynamically based on definition", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Trigger start questionnaire for "inst_daily_diary"
    portal.startQuestionnaire("assign_01");

    // Check header info
    expect(document.getElementById("questionnaire-title").textContent).toBe("Daily Health & Vital Diary");

    // Check input fields rendering
    const formContainer = document.getElementById("questionnaire-form-container");
    expect(formContainer.innerHTML).toContain("Systolic Blood Pressure (mmHg)");
    expect(formContainer.innerHTML).toContain('id="vssbp"');
    expect(formContainer.innerHTML).toContain("Diastolic Blood Pressure (mmHg)");
    expect(formContainer.innerHTML).toContain('id="vsdpb"');

    // Check choice single / radio grid rendering
    expect(formContainer.innerHTML).toContain("Are you experiencing any new physical symptoms today?");
    expect(formContainer.innerHTML).toContain('name="has_symptoms"');
    expect(formContainer.innerHTML).toContain('value="Yes"');
    expect(formContainer.innerHTML).toContain('value="No"');
  });

  it("enforces clinical boundaries and performs input validation", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    portal.startQuestionnaire("assign_01");

    // Try validating empty required fields
    let isValid = portal.validateActiveQuestionnaire();
    expect(isValid).toBe(false);

    const sbpContainer = document.getElementById("field-container-vssbp");
    expect(sbpContainer.classList.contains("has-error")).toBe(true);
    expect(sbpContainer.innerHTML).toContain("This field is required.");

    // Fill in values with out-of-bounds systolic bp (300 mmHg)
    document.getElementById("vssbp").value = "300";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "72";
    // Check radio
    document.getElementById("has_symptoms_option_1").checked = true;

    isValid = portal.validateActiveQuestionnaire();
    expect(isValid).toBe(false);
    expect(sbpContainer.innerHTML).toContain("Maximum value is 250.");

    // Correct the value
    document.getElementById("vssbp").value = "120";
    isValid = portal.validateActiveQuestionnaire();
    expect(isValid).toBe(true);
    expect(sbpContainer.classList.contains("has-error")).toBe(false);
  });

  it("calculates compliance scores correctly and updates the history log on submission", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Check initial compliance rate (1 completed out of 3 total = 33%)
    expect(document.getElementById("compliance-rate-pct").textContent).toBe("33%");

    // Mock completion of assignment 1
    portal.startQuestionnaire("assign_01");
    document.getElementById("vssbp").value = "120";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "70";
    document.getElementById("has_symptoms_option_1").checked = true;

    // Validate
    const isValid = portal.validateActiveQuestionnaire();
    expect(isValid).toBe(true);

    // Sign and Confirm submission
    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "pass123";
    await portal.verifyAndSubmitSignature();

    // Compliance should now be 2 completed out of 3 = 67%
    expect(document.getElementById("compliance-rate-pct").textContent).toBe("67%");
    expect(String(document.getElementById("compliance-completed-count").textContent)).toBe("2");

    // History table should contain completed status for Daily Health & Vital Diary
    const historyTbody = document.getElementById("compliance-history-tbody");
    expect(historyTbody.innerHTML).toContain("completed");
  });

  it("renders the patient notification inbox and handles write-reason acknowledgments", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Unread notification counts (initially 2 unread)
    expect(String(document.getElementById("unread-count").textContent)).toBe("2");

    const inboxContainer = document.getElementById("inbox-container");
    expect(inboxContainer.innerHTML).toContain("Reminder: Daily Health &amp; Vital Diary is due shortly.");

    // Acknowledge notification 1
    await portal.acknowledgeNotification("notif_01");

    // Unread count should drop to 1
    expect(String(document.getElementById("unread-count").textContent)).toBe("1");

    // Audit trail should contain ACKNOWLEDGE_NOTIFICATION entry
    const ledgerTimeline = document.getElementById("portal-ledger-timeline");
    expect(ledgerTimeline.innerHTML).toContain("ACKNOWLEDGE_NOTIFICATION");
  });
});
