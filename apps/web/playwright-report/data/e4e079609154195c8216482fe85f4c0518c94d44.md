# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: clinical_workflows.spec.ts >> Clinical Workflows and ePRO Portal Tests >> ePRO Participant Companion Portal Workflow >> should complete a daily health diary questionnaire with Part 11 compliant digital signature
- Location: tests/e2e/clinical_workflows.spec.ts:60:5

# Error details

```
Error: expect(locator).toContainText(expected) failed

Locator: locator('#sync-queue-list')
Timeout: 5000ms
- Expected substring  -  1
+ Received string     + 15

- SYNCED
+
+
+
+
+               Daily Health & Vital Diary
+               Seq: #1 | Device Time: 7/31/2026, 1:22:32 AM
+
+             QUEUED
+
+
+             Waiting for network connection...
+             Local Answers: {"vssbp":"120","vsdpb":"80","vshr":"72","has_symptoms":"No"}
+
+
+

Call log:
  - Expect "toContainText" with timeout 5000ms
  - waiting for locator('#sync-queue-list')
    14 × locator resolved to <div id="sync-queue-list">…</div>
       - unexpected value "



              Daily Health & Vital Diary
              Seq: #1 | Device Time: 7/31/2026, 1:22:32 AM

            QUEUED


            Waiting for network connection...
            Local Answers: {"vssbp":"120","vsdpb":"80","vshr":"72","has_symptoms":"No"}


      "

```

```yaml
- text: "Daily Health & Vital Diary Seq: #1 | Device Time: 7/31/2026, 1:22:32 AM QUEUED"
- paragraph: Waiting for network connection...
- strong: "Local Answers:"
- code: "{\"vssbp\":\"120\",\"vsdpb\":\"80\",\"vshr\":\"72\",\"has_symptoms\":\"No\"}"
```

# Test source

```ts
  23  |       // Raise a query
  24  |       await page.fill("#query-message-pulse", "Pulse rate of 72 bpm requires clinical verification of resting state.");
  25  |       await page.click("button:has-text('Submit Query')");
  26  |
  27  |       // Verify the query flag status class is updated to open
  28  |       await expect(queryFlag).toHaveClass(/query-status-open/);
  29  |
  30  |       // Now, respond to the query
  31  |       // Open panel if it closed, otherwise reuse open panel
  32  |       const responseArea = page.locator("#query-response-pulse");
  33  |       if (!(await responseArea.isVisible())) {
  34  |         await queryFlag.click();
  35  |       }
  36  |       await responseArea.fill("Confirmed: subject was in resting state for 15 minutes prior to measurement.");
  37  |       await page.click("button:has-text('Submit Response')");
  38  |
  39  |       // Verify query flag status is updated to answered
  40  |       await expect(queryFlag).toHaveClass(/query-status-answered/);
  41  |
  42  |       // Verify we can close/resolve the query
  43  |       if (!(await page.locator("button:has-text('Close Query')").isVisible())) {
  44  |         await queryFlag.click();
  45  |       }
  46  |       await page.click("button:has-text('Close Query')");
  47  |
  48  |       // Handle Re-authentication step-up PIN signature modal
  49  |       const reauthModal = page.locator("#reauth-modal");
  50  |       await expect(reauthModal).toBeVisible();
  51  |       await page.fill("#reauth-password", "admin_password");
  52  |       await page.click("#btn-confirm-reauth");
  53  |
  54  |       // Verify query flag status is updated to closed
  55  |       await expect(queryFlag).toHaveClass(/query-status-closed/);
  56  |     });
  57  |   });
  58  |
  59  |   test.describe("ePRO Participant Companion Portal Workflow", () => {
  60  |     test("should complete a daily health diary questionnaire with Part 11 compliant digital signature", async ({ page }) => {
  61  |       // Intercept ePRO sync endpoint
  62  |       await page.route("**/epro/sync", async (route) => {
  63  |         await route.fulfill({
  64  |           status: 200,
  65  |           contentType: "application/json",
  66  |           body: JSON.stringify({
  67  |             results: [
  68  |               {
  69  |                 status: "CREATED",
  70  |                 answers: { vssbp: "120", vsdpb: "80", vshr: "72", has_symptoms: "No" },
  71  |               },
  72  |             ],
  73  |           }),
  74  |         });
  75  |       });
  76  |
  77  |       // Inject test env mock flag before page load to bypass hanging Keycloak setup
  78  |       await page.addInitScript(() => {
  79  |         (window as any).__MOCK_TEST_ENV__ = true;
  80  |       });
  81  |
  82  |       // Navigate to ePRO portal on port 5174
  83  |       await page.goto("http://localhost:5174/subject-portal/");
  84  |
  85  |       // Verify portal header is visible
  86  |       await expect(page.locator(".portal-header h1")).toContainText("My Cadence");
  87  |
  88  |       // Click "Start Survey" on the Daily Health & Vital Diary task
  89  |       const startSurveyBtn = page.locator("#task-card-assign_01 .btn-start-task");
  90  |       await expect(startSurveyBtn).toBeVisible();
  91  |       await startSurveyBtn.click();
  92  |
  93  |       // Verify questionnaire form view is displayed
  94  |       await expect(page.locator("#questionnaire-title")).toContainText("Daily Health & Vital Diary");
  95  |
  96  |       // Populate CDASH clinical inputs
  97  |       await page.fill("#vssbp", "120");
  98  |       await page.fill("#vsdpb", "80");
  99  |       await page.fill("#vshr", "72");
  100 |       await page.check("input[name='has_symptoms'][value='No']");
  101 |
  102 |       // Click "Sign and Submit"
  103 |       await page.click("#btn-submit-questionnaire");
  104 |
  105 |       // Verify digital signature credential modal is presented
  106 |       const signatureModal = page.locator("#portal-sign-modal");
  107 |       await expect(signatureModal).toBeVisible();
  108 |
  109 |       // Select standard reason and provide credentials
  110 |       await page.selectOption("#sign-reason", "Initial Questionnaire Completion");
  111 |       await page.fill("#sign-username", "subject_001");
  112 |       await page.fill("#sign-password", "subject_pin_123");
  113 |
  114 |       // Click Sign and Confirm
  115 |       await page.click("#btn-modal-sign");
  116 |
  117 |       // Verify we returned to the tasks dashboard and the first assignment is marked as completed
  118 |       await expect(page.locator("#view-tasks")).toBeVisible();
  119 |       await expect(page.locator("#task-card-assign_01")).not.toBeVisible();
  120 |
  121 |       // Verify that offline sync queue list shows the synchronized entry
  122 |       const syncList = page.locator("#sync-queue-list");
> 123 |       await expect(syncList).toContainText("SYNCED");
      |                              ^ Error: expect(locator).toContainText(expected) failed
  124 |     });
  125 |   });
  126 | });
  127 |
```