#!/usr/bin/env node
// Line-oriented browser driver for the local Cadence stack (web on :3000, Keycloak on :8080).
// Reads one command per line from stdin, so it works piped (heredoc) or interactively under tmux.
// Run from the repository root: node .claude/skills/run-cadence/driver.mjs
import { createRequire } from "node:module";
import { mkdirSync } from "node:fs";
import path from "node:path";
import readline from "node:readline";

const require = createRequire(path.resolve("apps/web/package.json"));
const { chromium } = require("@playwright/test");

const WEB =
  process.env.CADENCE_WEB_URL ?? "http://localhost:3000/cadence-clinical/";
const SHOTS = process.env.CADENCE_SHOTS ?? "/tmp/cadence-shots";
const EXE = process.env.CADENCE_CHROMIUM ?? "/opt/pw-browsers/chromium";
mkdirSync(SHOTS, { recursive: true });

const browser = await chromium.launch({
  executablePath: EXE,
  args: ["--no-sandbox"],
});
const page = await (
  await browser.newContext({ viewport: { width: 1440, height: 900 } })
).newPage();
const consoleErrors = [];
const apiCalls = [];
page.on("console", (m) => m.type() === "error" && consoleErrors.push(m.text()));
page.on("pageerror", (e) => consoleErrors.push(`pageerror: ${e.message}`));
page.on("response", (r) => {
  if (/\/api\/v\d/.test(r.url()))
    apiCalls.push(`${r.status()} ${r.request().method()} ${r.url()}`);
});

// The gateway sends no CORS headers, and the AppHost builds the web app with
// VITE_API_BASE_URL=http://localhost:8000, so every API call from :3000 is blocked by the browser.
// Fetch gateway calls from Node instead and answer them with CORS headers (CADENCE_CORS_SHIM=0 to see the bug).
const GATEWAY = process.env.CADENCE_GATEWAY_URL ?? "http://localhost:8000";
if (process.env.CADENCE_CORS_SHIM !== "0") {
  await page.route(`${GATEWAY}/**`, async (route) => {
    const req = route.request();
    const origin = req.headers().origin ?? new URL(WEB).origin;
    const cors = {
      "access-control-allow-origin": origin,
      "access-control-allow-credentials": "true",
      "access-control-allow-headers":
        req.headers()["access-control-request-headers"] ?? "*",
      "access-control-allow-methods": "GET,POST,PUT,PATCH,DELETE,OPTIONS",
      "access-control-expose-headers": "*",
    };
    if (req.method() === "OPTIONS")
      return route.fulfill({ status: 204, headers: cors });
    try {
      const res = await route.fetch();
      return route.fulfill({
        response: res,
        headers: { ...res.headers(), ...cors },
      });
    } catch (e) {
      return route.fulfill({
        status: 502,
        headers: cors,
        body: String(e.message),
      });
    }
  });
}

let shot = 0;
const url = (p) =>
  /^https?:/.test(p) ? p : new URL(p.replace(/^\//, ""), WEB).href;

const commands = {
  // nav <path-or-url>: path is relative to the web base (studies/DEMO-001)
  async nav(arg) {
    await page.goto(url(arg || ""), { waitUntil: "domcontentloaded" });
    return page.url();
  },
  // login <user> [password]: Keycloak sign-in; users come from docker/cadence-realm.json (password "password")
  async login(arg) {
    const [user, pass = "password"] = arg.split(/\s+/);
    if (!page.url().startsWith("http")) await page.goto(WEB);
    if (!page.url().includes("/realms/")) {
      await page.waitForLoadState("networkidle");
      if (!page.url().includes("/realms/")) {
        await page
          .getByRole("button", { name: /sign in/i })
          .first()
          .click();
      }
    }
    await page.waitForURL(/\/realms\/cadence\//, { timeout: 20000 });
    await page.fill("#username", user);
    await page.fill("#password", pass);
    await page.click("#kc-login");
    await page.waitForURL((u) => u.href.startsWith(new URL(WEB).origin), {
      timeout: 20000,
    });
    await page.waitForLoadState("networkidle");
    return page.url();
  },
  async click(sel) {
    await page.locator(sel).first().click();
    return "ok";
  },
  // fill <selector> <text...>
  async fill(arg) {
    const [sel, ...rest] = arg.split(" ");
    await page.locator(sel).first().fill(rest.join(" "));
    return "ok";
  },
  async press(key) {
    await page.keyboard.press(key);
    return "ok";
  },
  // wait <selector-or-text=...> waits up to 20s for it to be visible
  async wait(sel) {
    await page
      .locator(sel)
      .first()
      .waitFor({ state: "visible", timeout: 20000 });
    return "visible";
  },
  async idle() {
    await page.waitForLoadState("networkidle");
    return "idle";
  },
  // ss [name]: full-page screenshot to $CADENCE_SHOTS
  async ss(name) {
    const file = path.join(
      SHOTS,
      `${String(++shot).padStart(2, "0")}-${name || "shot"}.png`
    );
    await page.screenshot({ path: file, fullPage: true });
    return file;
  },
  // text [selector]: visible text (default main), trimmed to 2000 chars
  async text(sel) {
    return (
      await page
        .locator(sel || "main")
        .first()
        .innerText()
    ).slice(0, 2000);
  },
  async eval(js) {
    return JSON.stringify(await page.evaluate(js));
  },
  // fetch <METHOD> <path> [json]: call the gateway from the page as the signed-in user (bearer token added by
  // the app's authorized fetch); mutations send X-Change-Reason and Idempotency-Key
  async fetch(arg) {
    const [method, p, ...body] = arg.split(" ");
    return page.evaluate(
      async ({ method, target, body }) => {
        const headers = { "Content-Type": "application/json" };
        if (method !== "GET") {
          headers["X-Change-Reason"] = "run-cadence driver";
          headers["Idempotency-Key"] = crypto.randomUUID();
        }
        const r = await fetch(target, {
          method,
          headers,
          body: body || undefined,
        });
        return `${r.status} ${(await r.text()).slice(0, 2000)}`;
      },
      {
        method: method.toUpperCase(),
        target: new URL(p, GATEWAY).href,
        body: body.join(" "),
      }
    );
  },
  async url() {
    return page.url();
  },
  // api: /api/ responses seen since the last call (status method url)
  async api() {
    return apiCalls.splice(0).join("\n") || "(none)";
  },
  async console() {
    return consoleErrors.splice(0).join("\n") || "(no errors)";
  },
  async quit() {
    await browser.close();
    process.exit(0);
  },
};

const rl = readline.createInterface({ input: process.stdin, terminal: false });
console.log(`driver ready (web ${WEB}, shots ${SHOTS})`);
for await (const raw of rl) {
  const line = raw.trim();
  if (!line || line.startsWith("#")) continue;
  const [cmd, ...rest] = line.split(" ");
  const fn = commands[cmd];
  if (!fn) {
    console.log(
      `? unknown command ${cmd}; known: ${Object.keys(commands).join(" ")}`
    );
    continue;
  }
  try {
    console.log(`> ${line}\n${await fn(rest.join(" "))}`);
  } catch (e) {
    console.log(`> ${line}\n! ${e.message.split("\n")[0]}`);
  }
}
await browser.close();
