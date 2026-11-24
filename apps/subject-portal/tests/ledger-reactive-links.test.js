import { beforeEach, describe, expect, it } from "vitest";
import { createApp, h, nextTick } from "vue";
import { createMemoryHistory, createRouter } from "vue-router";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import LedgerFooter from "../src/components/LedgerFooter.vue";
import { state } from "../src/stores/state.js";
import { logAuditRecord } from "../src/features/audit.js";

const Stub = { render: () => h("div") };
const testRoutes = [
  { path: "/", component: Stub },
  { path: "/consent", component: Stub },
  { path: "/compliance", component: Stub },
  { path: "/wallet", component: Stub },
];

describe("Subject portal reactive LedgerFooter links & zero innerHTML", () => {
  beforeEach(() => {
    document.body.innerHTML = "";
    state.ledgerBlocks.splice(0, state.ledgerBlocks.length);
  });

  it("routes consent events to /consent, survey events to /compliance, and wallet/stipend/receipt events to /wallet without internal codes", async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: testRoutes,
    });
    await router.push("/");
    await router.isReady();

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(LedgerFooter);
    app.use(router);
    app.mount(host);
    await nextTick();

    await logAuditRecord("SIGN_CONSENT", { version: "1.0" }, "Consent signed");
    await logAuditRecord(
      "EPRO_SUBMIT",
      { diary_id: "promis_fatigue" },
      "Survey submitted"
    );
    await logAuditRecord(
      "AUTOMATIC_STIPEND_DISBURSED",
      { stipend_amount: 50 },
      "Visit stipend"
    );

    const links = Array.from(
      host.querySelectorAll("#portal-ledger-timeline [data-ledger-route]")
    ).map((el) => el.getAttribute("data-ledger-route"));

    expect(links).toEqual(["/wallet", "/compliance", "/consent"]);
    const timelineText =
      host.querySelector("#portal-ledger-timeline")?.textContent || "";
    expect(timelineText).not.toContain("SIGN_CONSENT");
    expect(timelineText).not.toContain("EPRO_SUBMIT");
    expect(timelineText).not.toContain("AUTOMATIC_STIPEND_DISBURSED");

    const footerSrc = readFileSync(
      resolve(__dirname, "../src/components/LedgerFooter.vue"),
      "utf8"
    );
    const auditSrc = readFileSync(
      resolve(__dirname, "../src/features/audit.js"),
      "utf8"
    );
    expect(footerSrc).not.toContain("innerHTML");
    expect(auditSrc).not.toContain("innerHTML");

    app.unmount();
    host.remove();
  });
});
