import { test, expect } from "@playwright/test";

test.describe("Clinical Workflows and ePRO Portal Tests", () => {
  test.describe("CRA Monitoring Query Management Workflow", () => {
    test.use({ storageState: "playwright/.auth/user.json" });

    test("should successfully create, respond to, and close a CRA discrepancy query", async ({
      page,
    }) => {
      // 1. Navigate relative to baseURL
      await page.goto("ecrf");

      // Verify page is loaded
      await expect(
        page.locator(".crc-form-renderer .card-title").first()
      ).toContainText("Subject eCRF Data Entry Form");

      // Locate the query flag button next to pulse input
      const queryFlag = page.locator("#query-flag-pulse");
      await expect(queryFlag).toBeVisible();
      await queryFlag.evaluate((el) =>
        el.scrollIntoView({ block: "center", inline: "nearest" })
      );
      await queryFlag.click();

      // Open the query panel
      const queryPanel = page.locator("#query-panel-pulse");
      await expect(queryPanel).toBeVisible();

      // Raise a query
      await page.fill(
        "#query-message-pulse",
        "Pulse rate of 72 bpm requires clinical verification of resting state."
      );
      await page.click("button:has-text('Submit Query')");

      // Verify the query flag status class is updated to open
      await expect(queryFlag).toHaveClass(/query-status-open/);

      // Now, respond to the query
      // Open panel if it closed, otherwise reuse open panel
      const responseArea = page.locator("#query-response-pulse");
      if (!(await responseArea.isVisible())) {
        await queryFlag.click();
      }
      await responseArea.fill(
        "Confirmed: subject was in resting state for 15 minutes prior to measurement."
      );
      await page.click("button:has-text('Submit Response')");

      // Verify query flag status is updated to answered
      await expect(queryFlag).toHaveClass(/query-status-answered/);

      // Verify we can close/resolve the query
      if (!(await page.locator("button:has-text('Close Query')").isVisible())) {
        await queryFlag.click();
      }
      await page.click("button:has-text('Close Query')");

      // Verify query flag status is updated to closed
      await expect(queryFlag).toHaveClass(/query-status-closed/);
    });
  });

  test.describe("ePRO Participant Companion Portal Workflow", () => {
    test.use({ serviceWorkers: "block" });

    test("should complete a daily health diary questionnaire with Part 11 compliant digital signature", async ({
      page,
    }) => {
      await page.route("**/reconsent/pending/**", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify([]),
        });
      });

      await page.route("**/assignments/subject/*", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify([
            {
              id: "assign_01",
              subject_id: "subject_001",
              instrument_id: "inst_daily_diary",
              instrument_name: "Daily Health & Vital Diary",
              due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
              end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
              status: "PENDING",
            },
          ]),
        });
      });

      await page.route("**/instruments", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify([
            {
              id: "inst_daily_diary",
              name: "Daily Health & Vital Diary",
              description:
                "Please record your systolic/diastolic blood pressure, pulse, and current symptoms.",
              items: {
                vssbp: {
                  label: "Systolic Blood Pressure (mmHg)",
                  type: "numeric",
                  required: true,
                  min: 50,
                  max: 250,
                },
                vsdpb: {
                  label: "Diastolic Blood Pressure (mmHg)",
                  type: "numeric",
                  required: true,
                  min: 30,
                  max: 150,
                },
                vshr: {
                  label: "Pulse Rate (bpm)",
                  type: "numeric",
                  required: true,
                  min: 30,
                  max: 200,
                },
                has_symptoms: {
                  label:
                    "Are you experiencing any new physical symptoms today?",
                  type: "choice_single",
                  options: ["Yes", "No"],
                },
              },
            },
          ]),
        });
      });

      await page.route("**/compliance", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            subject_id: "subject_001",
            compliance_rate: 100,
            completed_count: 1,
            pending_count: 0,
            overdue_count: 0,
            assignments: [],
          }),
        });
      });

      await page.route("**/notifications", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify([]),
        });
      });

      await page.route(
        "**/api/v1/payments/participant/me/wallet",
        async (route) => {
          await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify({
              wallet: {
                id: "wal_001",
                study_id: "STUDY-101",
                current_balance: 75.0,
                lifetime_stipends: 75.0,
                lifetime_reimbursements: 0.0,
                taxable_ytd: 75.0,
                w9_status: "NOT_REQUIRED",
                card_status: "ACTIVE",
                virtual_card_mask: "4111 •••• •••• 8842",
              },
              recent_transactions: [],
              expense_claims: [],
            }),
          });
        }
      );

      // Intercept Part 11 step-up signature verification endpoint
      await page.route(
        "**/api/v1/auth/signature-verification",
        async (route) => {
          const payload = route.request().postDataJSON();
          expect(payload?.username).toBe("subject_001");
          expect(payload?.semantic_action).toBe("collection.sign");
          await route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify({ sig_token: "sig_tok_epro_mock_001" }),
          });
        }
      );

      // Intercept ePRO sync endpoint
      await page.route("**/epro/sync", async (route) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            results: [
              {
                status: "CREATED",
                answers: {
                  vssbp: "120",
                  vsdpb: "80",
                  vshr: "72",
                  has_symptoms: "No",
                },
              },
            ],
          }),
        });
      });

      // Inject mock session credentials before page load to authenticate without live Keycloak
      await page.addInitScript(() => {
        sessionStorage.setItem("mock_user_id", "subject_001");
        sessionStorage.setItem("mock_token", "mock_jwt_token_xyz_123");
      });

      // Navigate to ePRO portal on port 5174
      await page.goto("http://127.0.0.1:5174/subject-portal/");
      const skipLink = page.locator("#app .skip-link");
      if (
        !(await skipLink
          .waitFor({ state: "attached", timeout: 5000 })
          .then(() => true)
          .catch(() => false))
      ) {
        await page.reload();
      }

      // Complete initial AES-GCM PIN wrapper setup when prompted
      const pinSetupModal = page.locator("#portal-pin-setup-modal");
      if (
        await pinSetupModal
          .waitFor({ state: "visible", timeout: 4000 })
          .then(() => true)
          .catch(() => false)
      ) {
        await page.fill("#setup-pin", "246810");
        await page.fill("#confirm-setup-pin", "246810");
        await page.click("#btn-pin-setup-confirm");
        await expect(pinSetupModal).toBeHidden();
      }

      // Verify portal header is visible
      await expect(page.locator(".portal-header h1")).toContainText(
        "My Cadence"
      );

      // Click "Start Survey" on the Daily Health & Vital Diary task
      const startSurveyBtn = page.locator(
        "#task-card-assign_01 .btn-start-task"
      );
      await expect(startSurveyBtn).toBeVisible();
      await startSurveyBtn.click();

      // Verify questionnaire form view is displayed
      await expect(page.locator("#questionnaire-title")).toContainText(
        "Daily Health & Vital Diary"
      );

      // Populate CDASH clinical inputs
      await page.fill("#vssbp", "120");
      await page.fill("#vsdpb", "80");
      await page.fill("#vshr", "72");
      await page.check("input[name='has_symptoms'][value='No']");

      // Click "Sign and Submit"
      await page.click("#btn-submit-questionnaire");

      // Verify digital signature credential modal is presented
      const signatureModal = page.locator("#portal-sign-modal");
      await expect(signatureModal).toBeVisible();

      // Select standard reason and provide credentials
      await page.selectOption(
        "#sign-reason",
        "Initial Questionnaire Completion"
      );
      await page.fill("#sign-username", "subject_001");
      await page.fill("#sign-password", "subject_pin_123");

      // Click Sign and Confirm
      await page.click("#btn-modal-sign");

      // Verify we returned to the tasks dashboard and the first assignment is marked as completed
      await expect(page.locator("#view-tasks")).toBeVisible();
      await expect(page.locator("#task-card-assign_01")).not.toBeVisible();

      // Verify that offline sync queue list shows the synchronized entry
      const syncList = page.locator("#sync-queue-list");
      await expect(syncList).toContainText("Sent");
    });
  });
});
