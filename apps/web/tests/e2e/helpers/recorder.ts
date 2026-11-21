import { Page, expect } from "@playwright/test";
import { signInWithKeycloak } from "./keycloakLogin";

export interface SentinelRecord {
  consoleErrors: string[];
  pageErrors: string[];
  failedRequests: { url: string; status: number; method: string }[];
}

const sentinels = new WeakMap<Page, SentinelRecord>();

/**
 * Attach the Triple Sentinel listener to a Playwright page context to record
 * all console errors, unhandled exceptions, and HTTP 4xx/5xx network failures.
 */
export function attachTripleSentinel(page: Page): SentinelRecord {
  const record: SentinelRecord = {
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
  };

  sentinels.set(page, record);

  page.on("console", (msg) => {
    if (msg.type() === "error") {
      const text = msg.text();
      // Filter out known benign dev server/favicon warnings
      if (
        !text.includes("favicon.ico") &&
        !text.includes("Download the Vue DevTools") &&
        !text.includes("WebSocket connection to 'ws://localhost:3000/")
      ) {
        record.consoleErrors.push(text);
      }
    }
  });

  page.on("pageerror", (err) => {
    record.pageErrors.push(err.message || String(err));
  });

  page.on("response", (res) => {
    const status = res.status();
    const url = res.url();
    // Flag unexpected HTTP 4xx and 5xx failures
    if (status >= 400 && !url.includes("favicon.ico")) {
      record.failedRequests.push({
        url,
        status,
        method: res.request().method(),
      });
    }
  });

  return record;
}

/**
 * Assert that the Triple Sentinel gate passed with zero unhandled errors.
 */
export function assertTripleSentinelClean(page: Page): void {
  const record = sentinels.get(page);
  if (!record) return;

  if (record.pageErrors.length > 0) {
    throw new Error(
      `Triple Sentinel Page Errors detected:\n${record.pageErrors.join("\n")}`
    );
  }

  if (record.consoleErrors.length > 0) {
    throw new Error(
      `Triple Sentinel Console Errors detected:\n${record.consoleErrors.join("\n")}`
    );
  }

  if (record.failedRequests.length > 0) {
    const failures = record.failedRequests
      .map((r) => `${r.method} ${r.url} -> HTTP ${r.status}`)
      .join("\n");
    throw new Error(
      `Triple Sentinel HTTP 5xx Server Failures detected:\n${failures}`
    );
  }
}

/**
 * Introduce human-perceptible pacing for demonstration video clarity.
 */
export async function demoPause(page: Page, ms: number = 400): Promise<void> {
  const shouldPause =
    process.env.RECORD_VIDEO === "true" || process.env.DEMO_MODE === "true";
  if (shouldPause) {
    await page.waitForTimeout(ms);
  }
}

/**
 * Highlight a UI element with a visual outline before interacting.
 */
export async function highlightAndClick(
  page: Page,
  selector: string
): Promise<void> {
  const loc = page.locator(selector).first();
  await loc.waitFor({ state: "visible", timeout: 8000 });

  if (process.env.DEMO_MODE !== "false") {
    await loc.evaluate((el) => {
      const originalOutline = el.style.outline;
      const originalTransition = el.style.transition;
      el.style.transition =
        "outline 0.2s ease-in-out, box-shadow 0.2s ease-in-out";
      el.style.outline = "2px solid #00d2ff";
      el.style.boxShadow = "0 0 12px rgba(0, 210, 255, 0.6)";
      setTimeout(() => {
        el.style.outline = originalOutline;
        el.style.boxShadow = "";
        el.style.transition = originalTransition;
      }, 350);
    });
    await page.waitForTimeout(200);
  }

  await loc.click();
  await demoPause(page, 300);
}

/**
 * Smoothly types text with human cadence into an input field.
 */
export async function smoothType(
  page: Page,
  selector: string,
  text: string,
  charDelay: number = 30
): Promise<void> {
  const loc = page.locator(selector).first();
  await loc.waitFor({ state: "visible", timeout: 8000 });
  await loc.click();
  await loc.fill("");

  if (process.env.DEMO_MODE !== "false") {
    for (const char of text) {
      await page.keyboard.type(char, { delay: charDelay });
    }
  } else {
    await loc.fill(text);
  }
  await demoPause(page, 200);
}

/**
 * Sign in as a persona's Keycloak user and navigate to the target route.
 * `roles` documents the realm roles that user is seeded with in
 * docker/cadence-realm.json; the app reads roles from the token only.
 */
export async function setPersonaAndNavigate(
  page: Page,
  persona: {
    username: string;
    roles: string[];
  },
  targetPath: string
): Promise<void> {
  if (process.env.E2E_KEYCLOAK === "true") {
    await signInWithKeycloak(page, persona.username);
  }

  await page.goto(targetPath);
  await page.waitForLoadState("networkidle");
  await demoPause(page, 400);
}
