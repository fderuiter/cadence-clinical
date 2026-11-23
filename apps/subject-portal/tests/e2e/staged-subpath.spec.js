import { test, expect } from "@playwright/test";

const STAGED_PORTAL_URL =
  "http://127.0.0.1:5176/cadence-clinical/subject-portal/";

test.describe("Staged /cadence-clinical/subject-portal/ Subpath Bundle & Service Worker Verification", () => {
  test.use({ serviceWorkers: "allow" });

  test("boots under /cadence-clinical/subject-portal/ with sw.js?msw=1 active and zero axe-core or keycloak-js chunks", async ({
    page,
  }) => {
    const requestedUrls = [];
    const failedSubpathUrls = [];

    page.on("request", (req) => {
      requestedUrls.push(req.url());
    });
    page.on("response", (res) => {
      const url = res.url();
      if (
        url.includes("/cadence-clinical/subject-portal/") &&
        res.status() >= 400
      ) {
        failedSubpathUrls.push(`${res.status()} ${url}`);
      }
    });

    await page.goto(STAGED_PORTAL_URL);
    await expect(page.locator("#app .skip-link")).toBeAttached();

    const activeWorkerUrl = await page.evaluate(async () => {
      if (!("serviceWorker" in navigator)) return null;
      const reg = await navigator.serviceWorker.ready;
      return reg.active?.scriptURL || null;
    });
    expect(activeWorkerUrl).toContain(
      "/cadence-clinical/subject-portal/sw.js?msw=1"
    );

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

    await expect(page.locator("#main-content")).toBeVisible();

    // Deep-link navigation and full reload under /cadence-clinical/subject-portal/
    await page.goto(`${STAGED_PORTAL_URL}#/wallet`);
    await page.reload();
    await expect(page.locator("#app .skip-link")).toBeAttached();

    expect(failedSubpathUrls).toEqual([]);

    const portalChunkUrls = requestedUrls.filter((u) =>
      /\/cadence-clinical\/subject-portal\/assets\/[^?]+\.js(?:$|\?)/.test(u)
    );
    expect(portalChunkUrls.length).toBeGreaterThanOrEqual(2);

    const contaminatedRequests = requestedUrls.filter((u) =>
      /\/assets\/(?:axe|axe-core|keycloak|vendor-auth)(?:[-.][^/?]+)?\.m?js\b/i.test(
        u
      )
    );
    expect(contaminatedRequests).toEqual([]);
  });
});
