// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { createApp, nextTick } from "vue";
import {
  ClinicalExplanation,
  ClinicalGatedAction,
  EntityLink,
  openEntityPeek,
  parseNodeRef,
  registerEntityPeekHandler,
  stripRawPermissionTokens,
} from "../index.js";
import { toBeAccessible } from "../accessibility-matcher.js";

expect.extend({ toBeAccessible });

const mountedApps = [];

function mountComponent(component, props = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const app = createApp(component, props);
  app.mount(host);
  mountedApps.push({ app, host });
  return host;
}

afterEach(() => {
  while (mountedApps.length > 0) {
    const { app, host } = mountedApps.pop();
    app.unmount();
    host.remove();
  }
});

describe("ClinicalExplanation, ClinicalGatedAction, and EntityLink primitives", () => {
  it("parses standard, unversioned, empty, raw, and K-EXP-2 masked NodeRefs without leaking hidden IDs", () => {
    expect(parseNodeRef("").kindLabel).toBe("Record");
    expect(parseNodeRef("plain-code").displayLabel).toBe("plain-code");

    const visible = parseNodeRef(
      "qualification:4f891a20-1234-4abc-9def-000000000001@2"
    );
    expect(visible.masked).toBe(false);
    expect(visible.kind).toBe("qualification");
    expect(visible.version).toBe(2);

    const masked = parseNodeRef("masked:study_site:NotPermitted");
    expect(masked.masked).toBe(true);
    expect(masked.maskReason).toBe("NotPermitted");
    expect(masked.id).toBeNull();
    expect(openEntityPeek("")).toBe(false);
    expect(openEntityPeek("masked:study_site:NotPermitted")).toBe(false);
    expect(openEntityPeek("study_site:SITE-101")).toBe(false);
  });

  it("strips raw permission tokens from legacy denial strings", () => {
    expect(stripRawPermissionTokens("")).toBe("");
    expect(
      stripRawPermissionTokens(
        "Site activation requires Sponsor authority (site_compliance:activate)."
      )
    ).toBe("Site activation requires Sponsor authority.");
  });

  it("triggers registered peek handler when clicking an unmasked EntityLink and renders masked badge non-interactively", async () => {
    const spy = vi.fn();
    const unregister = registerEntityPeekHandler(spy);

    const activeHost = mountComponent(EntityLink, {
      nodeRef: "assertion:11111111-2222-3333-4444-555555555555@1",
      label: "Site Lock",
    });
    activeHost.querySelector("button").click();
    await nextTick();
    expect(spy).toHaveBeenCalledTimes(1);
    await expect(activeHost).toBeAccessible();

    const maskedHost = mountComponent(EntityLink, {
      nodeRef: "masked:enrollment:Blinded",
    });
    expect(maskedHost.querySelector("button")).toBeNull();
    expect(maskedHost.textContent).toContain("Blinded treatment record");
    unregister();
  });

  it("renders ClinicalExplanation steps, met/unmet conditions, and ClinicalGatedAction states", async () => {
    const spy = vi.fn();
    const unregister = registerEntityPeekHandler(spy);

    const permittedHost = mountComponent(ClinicalGatedAction, {
      permitted: true,
      label: "Sign Form",
      onAction: spy,
    });
    permittedHost.querySelector(".clinical-gated-action__fallback-btn").click();
    expect(spy).toHaveBeenCalledTimes(1);

    const host = mountComponent(ClinicalGatedAction, {
      permitted: false,
      label: "Activate Site",
      reason: "Missing IRB approval (site_compliance:activate)",
      explanation: {
        subject: "study_site:SITE-101",
        claim: "Clinical Research Coordinators cannot activate clinical sites.",
        producer: "security.rbac",
        steps: [
          {
            statement: "Checked active site startup grant.",
            evidence: ["grant:22222222-3333-4444-5555-666666666666"],
          },
        ],
        conditions: [
          {
            code: "grant.active",
            met: true,
            description: "Study coordinator membership is active.",
            evidence: ["grant:22222222-3333-4444-5555-666666666666"],
          },
          {
            code: "rbac.role_permission",
            met: false,
            description:
              "Assign a Sponsor Data Manager role to activate clinical sites.",
            evidence: [],
          },
        ],
        next_actions: [
          {
            description:
              "Request role assignment from your study administrator.",
            capability: "site_compliance:activate",
            target: "study_site:SITE-101",
          },
        ],
      },
    });

    const fallbackBtn = host.querySelector(
      ".clinical-gated-action__fallback-btn"
    );
    expect(fallbackBtn.hasAttribute("disabled")).toBe(true);
    expect(host.textContent).not.toContain("site_compliance:activate");

    host.querySelector(".clinical-gated-action__why-btn").click();
    await nextTick();
    expect(host.querySelector(".clinical-explanation")).not.toBeNull();
    expect(host.textContent).toContain(
      "Clinical Research Coordinators cannot activate clinical sites."
    );
    await expect(host).toBeAccessible();

    const fullExpHost = mountComponent(ClinicalExplanation, {
      code: "K.Authorization.Denied",
      explanation: {
        subject: "study_site:SITE-101",
        claim: "Blocked by missing training.",
        steps: [
          {
            statement: "Evaluated protocol v2 qualification requirement.",
            evidence: [
              "qualification_requirement:33333333-4444-5555-6666-777777777777",
            ],
          },
        ],
        conditions: [],
        nextActions: [
          {
            description: "Complete protocol v2 training.",
            target:
              "qualification_requirement:33333333-4444-5555-6666-777777777777",
          },
        ],
      },
    });
    expect(fullExpHost.textContent).toContain("Rule evaluation steps (1)");
    await expect(fullExpHost).toBeAccessible();
    unregister();
  });
});
