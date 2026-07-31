// Verified Subject Portal core workflow tests
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

describe("eCOA Companion Patient Portal - Workflow Tests", () => {
  beforeEach(async () => {
    const portal = await import("../index.js");
    portal.state.session.isDemoMode = true; // Default tests to demo-mode to preserve mock data reliance
    portal.state.session.isOfflineMode = true;
    portal.state.session.token = null;
  });

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
    expect(tasksList.innerHTML).toContain(
      "Weekly Symptoms &amp; eCOA Checklist"
    );
  });

  it("handles navigation between primary patient-facing views", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Start with tasks view active
    portal.showView("view-tasks");
    expect(
      document.getElementById("view-tasks").classList.contains("active")
    ).toBe(true);
    expect(
      document.getElementById("view-compliance").classList.contains("active")
    ).toBe(false);

    // Switch to compliance
    portal.showView("view-compliance");
    expect(
      document.getElementById("view-tasks").classList.contains("active")
    ).toBe(false);
    expect(
      document.getElementById("view-compliance").classList.contains("active")
    ).toBe(true);
  });

  it("renders eCOA questionnaire fields dynamically based on definition", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Trigger start questionnaire for "inst_daily_diary"
    portal.startQuestionnaire("assign_01");

    // Check header info
    expect(document.getElementById("questionnaire-title").textContent).toBe(
      "Daily Health & Vital Diary"
    );

    // Check input fields rendering
    const formContainer = document.getElementById(
      "questionnaire-form-container"
    );
    expect(formContainer.innerHTML).toContain("Systolic Blood Pressure (mmHg)");
    expect(formContainer.innerHTML).toContain('id="vssbp"');
    expect(formContainer.innerHTML).toContain(
      "Diastolic Blood Pressure (mmHg)"
    );
    expect(formContainer.innerHTML).toContain('id="vsdpb"');

    // Check choice single / radio grid rendering
    expect(formContainer.innerHTML).toContain(
      "Are you experiencing any new physical symptoms today?"
    );
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
    expect(document.getElementById("compliance-rate-pct").textContent).toBe(
      "33%"
    );

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
    expect(document.getElementById("compliance-rate-pct").textContent).toBe(
      "67%"
    );
    expect(
      String(document.getElementById("compliance-completed-count").textContent)
    ).toBe("2");

    // History table should contain completed status for Daily Health & Vital Diary
    const historyTbody = document.getElementById("compliance-history-tbody");
    expect(historyTbody.innerHTML).toContain("completed");
  });

  it("renders the patient notification inbox and handles write-reason acknowledgments", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    // Unread notification counts (initially 2 unread)
    expect(String(document.getElementById("unread-count").textContent)).toBe(
      "2"
    );

    const inboxContainer = document.getElementById("inbox-container");
    expect(inboxContainer.innerHTML).toContain(
      "Reminder: Daily Health &amp; Vital Diary is due shortly."
    );

    // Acknowledge notification 1
    await portal.acknowledgeNotification("notif_01");

    // Unread count should drop to 1
    expect(String(document.getElementById("unread-count").textContent)).toBe(
      "1"
    );

    // Audit trail should contain ACKNOWLEDGE_NOTIFICATION entry
    const ledgerTimeline = document.getElementById("portal-ledger-timeline");
    expect(ledgerTimeline.innerHTML).toContain("ACKNOWLEDGE_NOTIFICATION");
  });

  describe("Offline Sync and Queue Capabilities", () => {
    it("is installable as PWA with a manifest link", async () => {
      const manifestLink = document.querySelector('link[rel="manifest"]');
      expect(manifestLink).not.toBeNull();
      expect(manifestLink.getAttribute("href")).toBe(
        "/subject-portal/manifest.json"
      );
    });

    it("queues submissions in IndexedDB when offline and persists them across reload", async () => {
      const portal = await import("../index.js");
      await portal.initializeApp();

      // Clear any prior submissions to guarantee clean state
      await portal.clearAllSubmissions();

      // Go offline
      portal.state.session.isOfflineMode = true;

      // Start first questionnaire & fill out
      portal.startQuestionnaire("assign_01");
      document.getElementById("vssbp").value = "120";
      document.getElementById("vsdpb").value = "80";
      document.getElementById("vshr").value = "72";
      document.getElementById("has_symptoms_option_1").checked = true;
      expect(portal.validateActiveQuestionnaire()).toBe(true);

      // Submit first entry (it queues because we are offline)
      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      // Start second questionnaire
      portal.startQuestionnaire("assign_02");
      document.getElementById("severity_option_1").checked = true;
      document.getElementById("restricted_activity_option_1").checked = true;
      document.getElementById("missed_doses_option_1").checked = true;
      expect(portal.validateActiveQuestionnaire()).toBe(true);

      // Submit second entry
      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      // Verify that IndexedDB and UI renders both with QUEUED status and correct sequence
      const syncList = document.getElementById("sync-queue-list");
      expect(syncList.innerHTML).toContain("QUEUED");
      expect(syncList.innerHTML).toContain("Seq: #1");
      expect(syncList.innerHTML).toContain("Seq: #2");

      // Verify that data survives app reload by checking state and rendering again
      await portal.renderSyncQueueList();
      expect(syncList.innerHTML).toContain("QUEUED");
      expect(syncList.innerHTML).toContain("Seq: #1");
      expect(syncList.innerHTML).toContain("Seq: #2");
    });

    it("flushes queued items in correct sequence order upon going online", async () => {
      const portal = await import("../index.js");
      await portal.initializeApp();

      // Clean state
      await portal.clearAllSubmissions();

      // Put offline, submit one questionnaire
      portal.state.session.isOfflineMode = true;
      portal.startQuestionnaire("assign_01");
      document.getElementById("vssbp").value = "118";
      document.getElementById("vsdpb").value = "78";
      document.getElementById("vshr").value = "68";
      document.getElementById("has_symptoms_option_1").checked = true;
      expect(portal.validateActiveQuestionnaire()).toBe(true);

      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      // Submit a second questionnaire
      portal.startQuestionnaire("assign_02");
      document.getElementById("severity_option_1").checked = true;
      document.getElementById("restricted_activity_option_1").checked = true;
      document.getElementById("missed_doses_option_1").checked = true;
      expect(portal.validateActiveQuestionnaire()).toBe(true);

      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      // Clear mock fetch calls
      vi.clearAllMocks();

      // Setup success response from bulk sync endpoint
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              status: "success",
              processed_count: 2,
              created_count: 2,
              updated_count: 0,
              ignored_count: 0,
              results: [
                {
                  status: "CREATED",
                  id: "sub1",
                  diary_id: "inst_daily_diary",
                  answers: { vssbp: "118" },
                },
                {
                  status: "UPDATED_CLIENT_WINS",
                  id: "sub2",
                  diary_id: "inst_weekly_symptoms",
                  answers: { severity: "None" },
                },
              ],
            }),
        })
      );

      // Put online & trigger sync
      portal.state.session.isOfflineMode = false;
      await portal.syncOfflineQueue();

      // Ensure fetch was called exactly once for bulk sync
      expect(globalThis.fetch).toHaveBeenCalledTimes(1);

      // Verify the bulk request payload structure and sequential sorting
      const lastCallArg = globalThis.fetch.mock.calls[0];
      const requestBody = JSON.parse(lastCallArg[1].body);
      expect(requestBody.submissions).toHaveLength(2);
      expect(
        requestBody.submissions[0].offline_sync_markers.sequence_number
      ).toBe(1);
      expect(
        requestBody.submissions[1].offline_sync_markers.sequence_number
      ).toBe(2);

      // Verify status updates to SYNCED in UI
      const syncList = document.getElementById("sync-queue-list");
      expect(syncList.innerHTML).toContain("SYNCED");
      expect(syncList.innerHTML).not.toContain("QUEUED");
    });

    it("retains QUEUED status and retries when sync fails", async () => {
      const portal = await import("../index.js");
      await portal.initializeApp();

      await portal.clearAllSubmissions();

      // Offline, submit questionnaire
      portal.state.session.isOfflineMode = true;
      portal.startQuestionnaire("assign_01");
      document.getElementById("vssbp").value = "120";
      document.getElementById("vsdpb").value = "80";
      document.getElementById("vshr").value = "72";
      document.getElementById("has_symptoms_option_1").checked = true;
      expect(portal.validateActiveQuestionnaire()).toBe(true);

      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      // Mock fetch failure
      globalThis.fetch = vi
        .fn()
        .mockImplementation(() =>
          Promise.reject(new TypeError("Failed to fetch"))
        );

      // Attempt sync while online
      portal.state.session.isOfflineMode = false;
      await portal.syncOfflineQueue();

      // Verify still in QUEUED state due to failure
      const syncList = document.getElementById("sync-queue-list");
      expect(syncList.innerHTML).toContain("QUEUED");
    });

    it("asserts Bearer token is attached and gateway headers are absent when authenticated", async () => {
      const portal = await import("../index.js");
      portal.state.session.isDemoMode = false;
      portal.state.session.isOfflineMode = false;
      portal.state.session.token = "valid_test_token_abc123";

      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          json: () => Promise.resolve({ status: "success" }),
        })
      );

      await portal.dispatchApi("epro/sync", {
        method: "POST",
        body: JSON.stringify({ test: "data" }),
      });

      expect(globalThis.fetch).toHaveBeenCalledTimes(1);
      const lastCall = globalThis.fetch.mock.calls[0];
      const headers = lastCall[1].headers;

      // Bearer token present and well-formed
      expect(headers["Authorization"]).toBe("Bearer valid_test_token_abc123");

      // Gateway client-side signing headers strictly absent
      expect(headers["X-Gateway-Signature"]).toBeUndefined();
      expect(headers["X-User-Id"]).toBeUndefined();
      expect(headers["X-User-Roles"]).toBeUndefined();
      expect(headers["X-Gateway-Timestamp"]).toBeUndefined();
      expect(headers["X-Signature-Version"]).toBeUndefined();
    });

    it("asserts no Authorization header is emitted when no token is present", async () => {
      const portal = await import("../index.js");
      portal.state.session.isDemoMode = false;
      portal.state.session.isOfflineMode = false;
      portal.state.session.token = null;

      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          json: () => Promise.resolve({ status: "success" }),
        })
      );

      await portal.dispatchApi("epro/sync", {
        method: "POST",
        body: JSON.stringify({ test: "data" }),
      });

      expect(globalThis.fetch).toHaveBeenCalledTimes(1);
      const lastCall = globalThis.fetch.mock.calls[0];
      const headers = lastCall[1].headers;

      expect(headers["Authorization"]).toBeUndefined();
    });

    it("surfaces an error state on authenticated (non-demo) dispatch failures instead of falling back silently to mocks", async () => {
      const portal = await import("../index.js");
      portal.state.session.isDemoMode = false;
      portal.state.session.isOfflineMode = false;
      portal.state.session.token = "some_token";

      // Mock fetch rejection
      globalThis.fetch = vi
        .fn()
        .mockImplementation(() =>
          Promise.reject(new Error("Unreachable network"))
        );

      // Initialize application under non-demo, online authenticated context
      await portal.initializeApp();

      // Verify mocks were NOT loaded
      expect(portal.state.assignments).toEqual([]);
      expect(portal.state.notifications).toEqual([]);

      // Tasks list and inbox list contain error/failure UI state instead of mocks
      const tasksList = document.getElementById("tasks-list-container");
      expect(tasksList.innerHTML).toContain("Error loading tasks");
      expect(tasksList.innerHTML).not.toContain(
        "Daily Health &amp; Vital Diary"
      );

      const inboxList = document.getElementById("inbox-container");
      expect(inboxList.innerHTML).toContain("Error loading notifications");
      expect(inboxList.innerHTML).not.toContain(
        "Reminder: Daily Health &amp; Vital Diary"
      );
    });

    it("displays conflict resolution outcomes (MERGED, IGNORED_SERVER_WINS) cleanly without discarding", async () => {
      const portal = await import("../index.js");
      await portal.initializeApp();

      await portal.clearAllSubmissions();

      // Queue two offline items
      portal.state.session.isOfflineMode = true;
      portal.startQuestionnaire("assign_01");
      document.getElementById("vssbp").value = "120";
      document.getElementById("vsdpb").value = "80";
      document.getElementById("vshr").value = "72";
      document.getElementById("has_symptoms_option_1").checked = true;
      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      portal.startQuestionnaire("assign_02");
      document.getElementById("severity_option_1").checked = true;
      document.getElementById("restricted_activity_option_1").checked = true;
      document.getElementById("missed_doses_option_1").checked = true;
      document.getElementById("sign-username").value = "subject_001";
      document.getElementById("sign-password").value = "pin123";
      await portal.verifyAndSubmitSignature();

      // Mock conflict resolution responses from server
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              status: "success",
              processed_count: 2,
              created_count: 0,
              updated_count: 1,
              ignored_count: 1,
              results: [
                {
                  status: "MERGED",
                  id: "sub1",
                  diary_id: "inst_daily_diary",
                  answers: {
                    vssbp: "120",
                    vsdpb: "80",
                    vshr: "72",
                    has_symptoms: "Yes",
                  },
                },
                {
                  status: "IGNORED_SERVER_WINS",
                  id: "sub2",
                  diary_id: "inst_weekly_symptoms",
                  answers: { severity: "None" },
                },
              ],
            }),
        })
      );

      portal.state.session.isOfflineMode = false;
      await portal.syncOfflineQueue();

      // Verify that MERGED and CONFLICT (Ignored) statuses and explanations are visible
      const syncList = document.getElementById("sync-queue-list");
      expect(syncList.innerHTML).toContain("MERGED");
      expect(syncList.innerHTML).toContain("CONFLICT (Ignored)");
      expect(syncList.innerHTML).toContain(
        "Conflict resolved: Server data was preserved; local entry archived."
      );
      expect(syncList.innerHTML).toContain(
        "Conflict resolved: Local and server entries were combined."
      );
    });
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
        version_index: 1,
      },
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
            max: 10,
          },
        },
        response_types: {},
        scoring_metadata: {},
        created_at: "2026-01-01T00:00:00Z",
        created_by: "system",
        reason_for_change: "Initial",
        version_index: 1,
      },
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
          submitted_at: null,
        },
      ],
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
        reason_for_change: "Initial",
      },
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
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverAssignments),
          });
        }
        if (url.includes("/instruments")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverInstruments),
          });
        }
        if (url.includes("/compliance")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverCompliance),
          });
        }
        if (url.includes("/notifications")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverNotifications),
          });
        }
        return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
      });

      await portal.initializeApp();

      // Assert tasks render from server data
      portal.renderTasks();
      const tasksHtml = document.getElementById(
        "tasks-list-container"
      ).innerHTML;
      expect(tasksHtml).toContain("Server Instrument Name");

      // Assert compliance render from server compliance data
      portal.renderCompliance();
      expect(document.getElementById("compliance-rate-pct").textContent).toBe(
        "86%"
      ); // 85.5 rounded to 86
      expect(
        document.getElementById("compliance-completed-count").textContent
      ).toBe("5");

      // Assert questionnaire displays server instrument definitions
      await portal.startQuestionnaire("serv_assign_01");
      expect(document.getElementById("questionnaire-title").textContent).toBe(
        "Server Instrument Name"
      );
      expect(
        document.getElementById("questionnaire-form-container").innerHTML
      ).toContain("Server Field Label");
    });

    it("shows loading, empty, and failure states on tasks, inbox, and compliance with retry actions", async () => {
      const portal = await import("../index.js");

      portal.state.session.userId = "subject_authenticated";
      portal.state.session.token = "valid-token";
      portal.state.session.isOfflineMode = false;

      // Simulate failures for all
      globalThis.fetch = vi
        .fn()
        .mockImplementation(() => Promise.resolve({ ok: false, status: 500 }));

      await portal.initializeApp();

      // Verify tasks failure state
      portal.renderTasks();
      expect(document.getElementById("tasks-failure").style.display).toBe(
        "block"
      );
      expect(
        document.getElementById("tasks-list-container").style.display
      ).toBe("none");

      // Verify compliance failure state
      portal.renderCompliance();
      expect(document.getElementById("compliance-failure").style.display).toBe(
        "block"
      );
      expect(
        document.querySelector("#view-compliance .grid-layout").style.display
      ).toBe("none");

      // Verify inbox failure state
      portal.renderInbox();
      expect(document.getElementById("inbox-failure").style.display).toBe(
        "block"
      );
      expect(document.getElementById("inbox-container").style.display).toBe(
        "none"
      );

      // Test retry triggers refetching and rendering
      globalThis.fetch = vi.fn().mockImplementation((url) => {
        if (url.includes("assignments/subject/")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverAssignments),
          });
        }
        if (url.includes("/instruments")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverInstruments),
          });
        }
        if (url.includes("/compliance")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverCompliance),
          });
        }
        if (url.includes("/notifications")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(serverNotifications),
          });
        }
        return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
      });

      // Simulate clicking retry on tasks
      document.getElementById("btn-retry-tasks").click();
      // Wait for the async click handler to complete
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(document.getElementById("tasks-failure").style.display).toBe(
        "none"
      );
      expect(
        document.getElementById("tasks-list-container").innerHTML
      ).toContain("Server Instrument Name");

      // Simulate clicking retry on compliance
      document.getElementById("btn-retry-compliance").click();
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(document.getElementById("compliance-failure").style.display).toBe(
        "none"
      );
      expect(document.getElementById("compliance-rate-pct").textContent).toBe(
        "86%"
      );
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
          is_read: false,
        },
      ];

      portal.renderInbox();
      expect(String(document.getElementById("unread-count").textContent)).toBe(
        "1"
      );

      // Mock failure response for acknowledgement
      globalThis.fetch = vi
        .fn()
        .mockImplementation(() => Promise.resolve({ ok: false, status: 500 }));

      await portal.acknowledgeNotification("serv_notif_01");

      // Since it failed, the notification is still unread (1 unread)
      expect(String(document.getElementById("unread-count").textContent)).toBe(
        "1"
      );
      expect(portal.state.notifications[0].is_read).toBe(false);
      expect(document.getElementById("inbox-failure").style.display).toBe(
        "block"
      );

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
        reason_for_change: "Initial",
      };
      globalThis.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          json: () => Promise.resolve(ackResponse),
        })
      );

      await portal.acknowledgeNotification("serv_notif_01");

      // Successful ack updates state to read
      expect(String(document.getElementById("unread-count").textContent)).toBe(
        "0"
      );
      expect(portal.state.notifications[0].is_read).toBe(true);
    });

    it("guarantees an authenticated read failure never substitutes mock content or locally derived compliance", async () => {
      const portal = await import("../index.js");

      portal.state.session.userId = "subject_authenticated";
      portal.state.session.token = "valid-token";
      portal.state.session.isOfflineMode = false;

      // Fail all fetches
      globalThis.fetch = vi
        .fn()
        .mockImplementation(() => Promise.resolve({ ok: false, status: 500 }));

      await portal.initializeApp();

      // Check assignments are empty list, not loaded with MOCK_ASSIGNMENTS
      expect(portal.state.assignments).toHaveLength(0);

      // Check notifications are empty list, not loaded with MOCK_NOTIFICATIONS
      expect(portal.state.notifications).toHaveLength(0);

      // Check compliance is null, and renderCompliance handles error
      expect(portal.state.compliance).toBeNull();
      portal.renderCompliance();
      expect(document.getElementById("compliance-failure").style.display).toBe(
        "block"
      );
    });
  });
});
