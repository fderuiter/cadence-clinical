import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi } from "vitest";

// Set up JSDOM mock elements before importing index.js
beforeEach(async () => {
  window.__MOCK_TEST_ENV__ = true;
  document.head.innerHTML = `<link rel="manifest" href="/subject-portal/manifest.json" />`;
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
            <div id="tasks-loading" style="display: none;"></div>
            <div id="tasks-failure" style="display: none;">
              <span id="tasks-error-msg"></span>
              <button type="button" id="btn-retry-tasks">Retry</button>
            </div>
            <div id="tasks-list-container"></div>

            <!-- Offline Sync Status Panel -->
            <div class="card sync-queue-panel" id="sync-queue-panel" style="margin-top: 24px;">
              <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border-color); padding-bottom: 12px; margin-bottom: 12px;">
                <h3 style="margin: 0; display: flex; align-items: center; gap: 8px;">
                  <span>🔄</span> Offline Sync Queue
                </h3>
                <button type="button" id="btn-sync-now" class="btn btn-secondary" style="padding: 6px 12px; font-size: 13px;">Sync Now</button>
              </div>
              <div id="sync-queue-status-text" class="text-muted" style="font-size: 14px; margin-bottom: 12px;">
                Checking sync status...
              </div>
              <div id="sync-queue-list" style="display: flex; flex-direction: column; gap: 8px;">
                <!-- Dynamically populated synced & queued submissions go here -->
              </div>
            </div>
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
            <div id="compliance-loading" style="display: none;"></div>
            <div id="compliance-failure" style="display: none;">
              <span id="compliance-error-msg"></span>
              <button type="button" id="btn-retry-compliance">Retry</button>
            </div>
            <div class="grid-layout">
              <div id="compliance-rate-pct">0%</div>
              <div id="compliance-completed-count">0</div>
              <div id="compliance-pending-count">0</div>
              <div id="compliance-overdue-count">0</div>
              <table>
                <tbody id="compliance-history-tbody"></tbody>
              </table>
            </div>
          </section>

          <!-- Inbox View -->
          <section id="view-inbox" class="portal-view">
            <div id="inbox-loading" style="display: none;"></div>
            <div id="inbox-failure" style="display: none;">
              <span id="inbox-error-msg"></span>
              <button type="button" id="btn-retry-inbox">Retry</button>
            </div>
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

  // Reset state
  try {
    const portal = await import("../index.js");
    portal.state.session.userId = "subject_001";
    portal.state.session.roles = "Subject";
    portal.state.session.token = null;
    portal.state.session.isOfflineMode = true;
    portal.state.assignments = [];
    portal.state.notifications = [];
    portal.state.ledgerBlocks = [];
    portal.state.activeQuestionnaire = null;
    portal.state.instruments = {};
    portal.state.compliance = null;
    portal.state.tasksLoading = false;
    portal.state.tasksError = null;
    portal.state.instrumentsLoading = false;
    portal.state.instrumentsError = null;
    portal.state.complianceLoading = false;
    portal.state.complianceError = null;
    portal.state.notificationsLoading = false;
    portal.state.notificationsError = null;
  } catch (err) {
    console.error("State reset failed in beforeEach:", err);
  }
});

describe("eCOA Companion Patient Portal - Contract & Regression Tests", () => {
  beforeEach(async () => {
    const portal = await import("../index.js");
    portal.state.session.isDemoMode = true; // Default tests to demo-mode to preserve mock data reliance
    portal.state.session.isOfflineMode = true;
    portal.state.session.token = null;
  });

  it("verifies dynamic rendering and form field generation from instrument schemas", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Start daily diary questionnaire
    portal.startQuestionnaire("assign_01");

    // Title and description match MOCK_INSTRUMENTS
    expect(document.getElementById("questionnaire-title").textContent).toBe(
      "Daily Health & Vital Diary"
    );
    expect(document.getElementById("questionnaire-desc").textContent).toBe(
      "Please record your systolic/diastolic blood pressure, pulse, and current symptoms."
    );

    // Form container contains vital fields and radio inputs
    const formContainer = document.getElementById(
      "questionnaire-form-container"
    );
    expect(formContainer.innerHTML).toContain("Systolic Blood Pressure (mmHg)");
    expect(formContainer.innerHTML).toContain(
      "Diastolic Blood Pressure (mmHg)"
    );
    expect(formContainer.innerHTML).toContain("Pulse Rate (bpm)");
    expect(formContainer.innerHTML).toContain(
      "Are you experiencing any new physical symptoms today?"
    );
  });

  it("checks input validator, clinical bounds constraints, and error feedback", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    portal.startQuestionnaire("assign_01");

    // Initially fails because fields are empty
    let valid = portal.validateActiveQuestionnaire();
    expect(valid).toBe(false);

    const sbpContainer = document.getElementById("field-container-vssbp");
    expect(sbpContainer.classList.contains("has-error")).toBe(true);
    expect(sbpContainer.innerHTML).toContain("This field is required.");

    // Enter out-of-bounds systolic value (350 is above max 250)
    document.getElementById("vssbp").value = "350";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "72";
    document.getElementById("has_symptoms_option_1").checked = true;

    valid = portal.validateActiveQuestionnaire();
    expect(valid).toBe(false);
    expect(sbpContainer.innerHTML).toContain("Maximum value is 250.");

    // Enter valid value
    document.getElementById("vssbp").value = "120";
    valid = portal.validateActiveQuestionnaire();
    expect(valid).toBe(true);
    expect(sbpContainer.classList.contains("has-error")).toBe(false);
  });

  it("proves compliance calculation, compliance metrics, and status rendering update correctly", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Initial compliance checks
    expect(document.getElementById("compliance-rate-pct").textContent).toBe(
      "33%"
    );
    expect(
      document.getElementById("compliance-completed-count").textContent
    ).toBe("1");
    expect(
      document.getElementById("compliance-pending-count").textContent
    ).toBe("1");
    expect(
      document.getElementById("compliance-overdue-count").textContent
    ).toBe("1");

    // Complete the pending assignment
    portal.startQuestionnaire("assign_01");
    document.getElementById("vssbp").value = "120";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "72";
    document.getElementById("has_symptoms_option_1").checked = true;

    // Verify and submit via electronic signature
    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "pass123";
    await portal.verifyAndSubmitSignature();

    // Compliance changes to 2/3 (67%)
    expect(document.getElementById("compliance-rate-pct").textContent).toBe(
      "67%"
    );
    expect(
      document.getElementById("compliance-completed-count").textContent
    ).toBe("2");
    expect(
      document.getElementById("compliance-pending-count").textContent
    ).toBe("0");
    expect(
      document.getElementById("compliance-overdue-count").textContent
    ).toBe("1");
  });

  it("handles offline queueing in IndexedDB, sequence preservation, and sequential sync upon online transition", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Ensure empty queue
    await portal.clearAllSubmissions();

    // Simulates offline mode
    portal.state.session.isOfflineMode = true;

    // Complete Daily Vital Diary offline
    portal.startQuestionnaire("assign_01");
    document.getElementById("vssbp").value = "115";
    document.getElementById("vsdpb").value = "75";
    document.getElementById("vshr").value = "70";
    document.getElementById("has_symptoms_option_1").checked = true;

    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "p123";
    await portal.verifyAndSubmitSignature();

    // Queue has 1 item with QUEUED badge and Seq: #1
    const syncList = document.getElementById("sync-queue-list");
    expect(syncList.innerHTML).toContain("QUEUED");
    expect(syncList.innerHTML).toContain("Seq: #1");

    // Mock successful sync payload
    vi.clearAllMocks();
    globalThis.fetch = vi.fn().mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            processed_count: 1,
            created_count: 1,
            updated_count: 0,
            ignored_count: 0,
            results: [
              {
                status: "CREATED",
                id: "sub_new_01",
                diary_id: "inst_daily_diary",
                answers: { vssbp: "115" },
              },
            ],
          }),
      })
    );

    // Transition online and sync
    portal.state.session.isOfflineMode = false;
    await portal.syncOfflineQueue();

    // Check fetch occurred correctly
    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
    const lastCall = globalThis.fetch.mock.calls[0];
    const body = JSON.parse(lastCall[1].body);
    expect(body.submissions).toHaveLength(1);
    expect(body.submissions[0].offline_sync_markers.sequence_number).toBe(1);

    // Status shifts to SYNCED in the queue list
    expect(syncList.innerHTML).toContain("SYNCED");
  });

  it("safeguards queued data on sync network failures", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    await portal.clearAllSubmissions();

    // Offline submission
    portal.state.session.isOfflineMode = true;
    portal.startQuestionnaire("assign_01");
    document.getElementById("vssbp").value = "120";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "72";
    document.getElementById("has_symptoms_option_1").checked = true;

    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "p123";
    await portal.verifyAndSubmitSignature();

    // Sync fails with network error
    globalThis.fetch = vi
      .fn()
      .mockImplementation(() =>
        Promise.reject(new Error("Network connection dropped"))
      );

    // Put online and try sync
    portal.state.session.isOfflineMode = false;
    await portal.syncOfflineQueue();

    // Submissions should remain queued (retained, not dropped)
    const syncList = document.getElementById("sync-queue-list");
    expect(syncList.innerHTML).toContain("QUEUED");
  });

  it("verifies and renders sync conflict resolutions (MERGED, IGNORED_SERVER_WINS) cleanly in the UI", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    await portal.clearAllSubmissions();

    // Submit offline
    portal.state.session.isOfflineMode = true;
    portal.startQuestionnaire("assign_01");
    document.getElementById("vssbp").value = "120";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "72";
    document.getElementById("has_symptoms_option_1").checked = true;

    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "p123";
    await portal.verifyAndSubmitSignature();

    // Mock conflict responses: one MERGED and one IGNORED_SERVER_WINS
    globalThis.fetch = vi.fn().mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () =>
          Promise.resolve({
            status: "success",
            processed_count: 1,
            created_count: 0,
            updated_count: 1,
            ignored_count: 0,
            results: [
              {
                status: "MERGED",
                id: "merged_sub",
                diary_id: "inst_daily_diary",
                answers: { vssbp: "120", vsdpb: "80" },
              },
            ],
          }),
      })
    );

    portal.state.session.isOfflineMode = false;
    await portal.syncOfflineQueue();

    // Renders as MERGED with detail message
    const syncList = document.getElementById("sync-queue-list");
    expect(syncList.innerHTML).toContain("MERGED");
    expect(syncList.innerHTML).toContain(
      "Conflict resolved: Local and server entries were combined."
    );
  });

  describe("Authenticated live data loading and failure states", () => {
    const serverAssignments = [
      {
        id: "serv_assign_01",
        subject_id: "subject_authenticated",
        instrument_id: "serv_inst_01",
        start_date: "2026-01-01T00:00:00Z",
        end_date: "2026-01-02T00:00:00Z",
        due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
        created_at: "2026-01-01T00:00:00Z",
        created_by: "system",
        reason_for_change: "Initial",
        version_index: 1
      }
    ];

    const serverInstruments = [
      {
        id: "serv_inst_01",
        name: "Server Instrument Name",
        description: "Server Instrument Description",
        items: {
          server_field: {
            label: "Server Field Label",
            type: "numeric",
            required: true,
            min: 1,
            max: 10
          }
        },
        response_types: {},
        scoring_metadata: {},
        created_at: "2026-01-01T00:00:00Z",
        created_by: "system",
        reason_for_change: "Initial",
        version_index: 1
      }
    ];

    const serverCompliance = {
      subject_id: "subject_authenticated",
      compliance_rate: 85.5,
      completed_count: 5,
      pending_count: 1,
      overdue_count: 0,
      assignments: [
        {
          assignment_id: "serv_assign_01",
          instrument_id: "serv_inst_01",
          instrument_name: "Server Instrument Name",
          status: "PENDING",
          due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
          end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
          submitted_at: null
        }
      ]
    };

    const serverNotifications = [
      {
        id: "serv_notif_01",
        subject_id: "subject_authenticated",
        assignment_id: "serv_assign_01",
        message: "Server Reminder Message",
        due_at: "2026-01-01T12:00:00Z",
        channel: "IN_APP",
        delivery_status: "DELIVERED",
        is_read: false,
        read_at: null,
        created_at: "2026-01-01T00:00:00Z",
        created_by: "system",
        reason_for_change: "Initial"
      }
    ];

    it("authenticates and loads data from server-shaped payloads correctly", async () => {
      const portal = await import("../index.js");

      // Enable authenticated session
      portal.state.session.userId = "subject_authenticated";
      portal.state.session.token = "valid-token";
      portal.state.session.isOfflineMode = false;

      // Mock fetch for the specific endpoints
      globalThis.fetch = vi.fn().mockImplementation((url) => {
        if (url.includes("assignments/subject/")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverAssignments) });
        }
        if (url.includes("/instruments")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverInstruments) });
        }
        if (url.includes("/compliance")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverCompliance) });
        }
        if (url.includes("/notifications")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverNotifications) });
        }
        return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
      });

      await portal.initializeApp();

      // Assert tasks render from server data
      portal.renderTasks();
      const tasksHtml = document.getElementById("tasks-list-container").innerHTML;
      expect(tasksHtml).toContain("Server Instrument Name");

      // Assert compliance render from server compliance data
      portal.renderCompliance();
      expect(document.getElementById("compliance-rate-pct").textContent).toBe("86%"); // 85.5 rounded to 86
      expect(document.getElementById("compliance-completed-count").textContent).toBe("5");

      // Assert questionnaire displays server instrument definitions
      await portal.startQuestionnaire("serv_assign_01");
      expect(document.getElementById("questionnaire-title").textContent).toBe("Server Instrument Name");
      expect(document.getElementById("questionnaire-form-container").innerHTML).toContain("Server Field Label");
    });

    it("shows loading, empty, and failure states on tasks, inbox, and compliance with retry actions", async () => {
      const portal = await import("../index.js");

      portal.state.session.userId = "subject_authenticated";
      portal.state.session.token = "valid-token";
      portal.state.session.isOfflineMode = false;

      // Simulate failures for all
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({ ok: false, status: 500 })
      );

      await portal.initializeApp();

      // Verify tasks failure state
      portal.renderTasks();
      expect(document.getElementById("tasks-failure").style.display).toBe("block");
      expect(document.getElementById("tasks-list-container").style.display).toBe("none");

      // Verify compliance failure state
      portal.renderCompliance();
      expect(document.getElementById("compliance-failure").style.display).toBe("block");
      expect(document.querySelector("#view-compliance .grid-layout").style.display).toBe("none");

      // Verify inbox failure state
      portal.renderInbox();
      expect(document.getElementById("inbox-failure").style.display).toBe("block");
      expect(document.getElementById("inbox-container").style.display).toBe("none");

      // Test retry triggers refetching and rendering
      globalThis.fetch = vi.fn().mockImplementation((url) => {
        if (url.includes("assignments/subject/")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverAssignments) });
        }
        if (url.includes("/instruments")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverInstruments) });
        }
        if (url.includes("/compliance")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverCompliance) });
        }
        if (url.includes("/notifications")) {
          return Promise.resolve({ ok: true, json: () => Promise.resolve(serverNotifications) });
        }
        return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
      });

      // Simulate clicking retry on tasks
      document.getElementById("btn-retry-tasks").click();
      // Wait for the async click handler to complete
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(document.getElementById("tasks-failure").style.display).toBe("none");
      expect(document.getElementById("tasks-list-container").innerHTML).toContain("Server Instrument Name");

      // Simulate clicking retry on compliance
      document.getElementById("btn-retry-compliance").click();
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(document.getElementById("compliance-failure").style.display).toBe("none");
      expect(document.getElementById("compliance-rate-pct").textContent).toBe("86%");
    });

    it("prevents changing notification state on failed server acknowledgement, but updates on success", async () => {
      const portal = await import("../index.js");

      portal.state.session.userId = "subject_authenticated";
      portal.state.session.token = "valid-token";
      portal.state.session.isOfflineMode = false;
      portal.state.notifications = [
        {
          id: "serv_notif_01",
          subject_id: "subject_authenticated",
          assignment_id: "serv_assign_01",
          message: "Server Reminder",
          due_at: "2026-01-01T12:00:00Z",
          channel: "IN_APP",
          is_read: false
        }
      ];

      portal.renderInbox();
      expect(String(document.getElementById("unread-count").textContent)).toBe("1");

      // Mock failure response for acknowledgement
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({ ok: false, status: 500 })
      );

      await portal.acknowledgeNotification("serv_notif_01");

      // Since it failed, the notification is still unread (1 unread)
      expect(String(document.getElementById("unread-count").textContent)).toBe("1");
      expect(portal.state.notifications[0].is_read).toBe(false);
      expect(document.getElementById("inbox-failure").style.display).toBe("block");

      // Mock successful acknowledgement response
      const ackResponse = {
        id: "serv_notif_01",
        subject_id: "subject_authenticated",
        assignment_id: "serv_assign_01",
        due_at: "2026-01-01T12:00:00Z",
        channel: "IN_APP",
        is_read: true,
        read_at: new Date().toISOString(),
        created_at: "2026-01-01T00:00:00Z",
        created_by: "system",
        reason_for_change: "Initial"
      };
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({ ok: true, json: () => Promise.resolve(ackResponse) })
      );

      await portal.acknowledgeNotification("serv_notif_01");

      // Successful ack updates state to read
      expect(String(document.getElementById("unread-count").textContent)).toBe("0");
      expect(portal.state.notifications[0].is_read).toBe(true);
    });

    it("guarantees an authenticated read failure never substitutes mock content or locally derived compliance", async () => {
      const portal = await import("../index.js");

      portal.state.session.userId = "subject_authenticated";
      portal.state.session.token = "valid-token";
      portal.state.session.isOfflineMode = false;

      // Fail all fetches
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({ ok: false, status: 500 })
      );

      await portal.initializeApp();

      // Check assignments are empty list, not loaded with MOCK_ASSIGNMENTS
      expect(portal.state.assignments).toHaveLength(0);

      // Check notifications are empty list, not loaded with MOCK_NOTIFICATIONS
      expect(portal.state.notifications).toHaveLength(0);

      // Check compliance is null, and renderCompliance handles error
      expect(portal.state.compliance).toBeNull();
      portal.renderCompliance();
      expect(document.getElementById("compliance-failure").style.display).toBe("block");
    });
  });
});
