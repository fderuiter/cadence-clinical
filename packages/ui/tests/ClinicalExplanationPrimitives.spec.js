import { describe, expect, it, vi } from "vitest";
import { mount } from "@vue/test-utils";
import EntityLink from "../src/components/clinical/EntityLink.vue";
import ClinicalExplanation from "../src/components/clinical/ClinicalExplanation.vue";
import ClinicalGatedAction from "../src/components/clinical/ClinicalGatedAction.vue";
import {
  openEntityPeek,
  parseNodeRef,
  registerEntityPeekHandler,
  stripRawPermissionTokens,
} from "../src/utils/entityPeekRegistry.js";

describe("ClinicalExplanation, ClinicalGatedAction, and EntityLink primitives", () => {
  it("parses standard and K-EXP-2 masked NodeRefs without leaking hidden IDs", () => {
    const visible = parseNodeRef("qualification:4f891a20-1234-4abc-9def-000000000001@2");
    expect(visible.masked).toBe(false);
    expect(visible.kind).toBe("qualification");
    expect(visible.version).toBe(2);

    const masked = parseNodeRef("masked:study_site:NotPermitted");
    expect(masked.masked).toBe(true);
    expect(masked.maskReason).toBe("NotPermitted");
    expect(masked.id).toBeNull();
    expect(openEntityPeek("masked:study_site:NotPermitted")).toBe(false);
  });

  it("strips raw permission tokens from legacy denial strings", () => {
    expect(
      stripRawPermissionTokens("Site activation requires Sponsor authority (site_compliance:activate).")
    ).toBe("Site activation requires Sponsor authority.");
  });

  it("triggers registered peek handler when clicking an unmasked EntityLink and renders masked badge non-interactively", async () => {
    const spy = vi.fn();
    const unregister = registerEntityPeekHandler(spy);

    const activeWrapper = mount(EntityLink, {
      props: { nodeRef: "assertion:11111111-2222-3333-4444-555555555555@1", label: "Site Lock" },
    });
    await activeWrapper.find("button").trigger("click");
    expect(spy).toHaveBeenCalledTimes(1);

    const maskedWrapper = mount(EntityLink, {
      props: { nodeRef: "masked:enrollment:Blinded" },
    });
    expect(maskedWrapper.find("button").exists()).toBe(false);
    expect(maskedWrapper.text()).toContain("Blinded treatment record");
    unregister();
  });

  it("renders ClinicalExplanation and ClinicalGatedAction with met/unmet conditions and NextAction targets", async () => {
    const wrapper = mount(ClinicalGatedAction, {
      props: {
        permitted: false,
        label: "Activate Site",
        reason: "Missing IRB approval (site_compliance:activate)",
        explanation: {
          claim: "Clinical Research Coordinators cannot activate clinical sites.",
          producer: "security.rbac",
          conditions: [
            {
              code: "rbac.role_permission",
              met: false,
              description: "Assign a Sponsor Data Manager role to activate clinical sites.",
              evidence: [],
            },
          ],
          next_actions: [
            {
              description: "Request role assignment from your study administrator.",
              capability: "site_compliance:activate",
              target: "study_site:SITE-101",
            },
          ],
        },
      },
    });

    expect(wrapper.find(".clinical-gated-action__fallback-btn").attributes("disabled")).toBeDefined();
    expect(wrapper.text()).not.toContain("site_compliance:activate");
    await wrapper.find(".clinical-gated-action__why-btn").trigger("click");
    expect(wrapper.findComponent(ClinicalExplanation).exists()).toBe(true);
    expect(wrapper.text()).toContain("Clinical Research Coordinators cannot activate clinical sites.");
  });
});
