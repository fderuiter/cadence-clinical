import { test, expect } from "@playwright/test";

test.describe("Staged /cadence-clinical/demo/ Subpath Bundle & MSW Verification", () => {
  test("boots with MSW service worker and Rollup chunking active while downloading 0 bytes of axe-core, keycloak-js, and hls.js on #/documents", async ({
    page,
  }) => {
    const requestedUrls: string[] = [];
    const failedAssetUrls: string[] = [];

    page.on("request", (req) => {
      requestedUrls.push(req.url());
    });
    page.on("response", (res) => {
      const url = res.url();
      if (url.includes("/cadence-clinical/demo/") && res.status() >= 400) {
        failedAssetUrls.push(`${res.status()} ${url}`);
      }
    });

    await page.goto("/cadence-clinical/demo/");
    await expect(page.locator("#main-content")).toBeVisible();

    const activeWorkerUrl = await page.evaluate(async () => {
      if (!("serviceWorker" in navigator)) return null;
      const reg = await navigator.serviceWorker.ready;
      return reg.active?.scriptURL || null;
    });
    expect(activeWorkerUrl).toContain(
      "/cadence-clinical/demo/mockServiceWorker.js"
    );

    const jsChunkUrls = requestedUrls.filter((u) =>
      /\/cadence-clinical\/demo\/assets\/[^?]+\.js(?:$|\?)/.test(u)
    );
    expect(jsChunkUrls.length).toBeGreaterThan(3);

    // Navigate to #/documents and perform a deep-link reload under /cadence-clinical/demo/
    await page.goto("/cadence-clinical/demo/#/documents");
    await expect(
      page.getByRole("heading", { name: "Documents Hub" })
    ).toBeVisible();

    await page.reload();
    await expect(
      page.getByRole("heading", { name: "Documents Hub" })
    ).toBeVisible();
    await expect(page.locator(".hub-content-area")).toBeVisible();

    expect(failedAssetUrls).toEqual([]);

    const contaminatedRequests = requestedUrls.filter((u) =>
      /\/assets\/(?:axe|axe-core|keycloak|vendor-auth|hls|vendor-media)(?:[-.][^/?]+)?\.m?js\b/i.test(
        u
      )
    );
    expect(contaminatedRequests).toEqual([]);
  });
});
