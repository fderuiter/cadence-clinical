import { describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import { ClinicalExplanation, ClinicalGatedAction, EntityLink } from "ui";
import { useEntityPeekDrawer } from "../src/composables/useEntityPeekDrawer";
import {
  describeApiError,
  extractProblemExplanation,
} from "../src/utils/apiErrors.js";
import {
  formatProblemExplanationSummary,
  extractExplanationTargets,
} from "../src/api/kernel/shared";

describe("Inline explanation primitives, peek drawer state, and 403/422/423 ProblemDetails", () => {
  it("opens peek drawer state on EntityLink click without navigating away from current route", async () => {
    const { state } = useEntityPeekDrawer();
    const link = mount(EntityLink, {
      props: {
        nodeRef:
          "qualification_requirement:11111111-2222-3333-4444-555555555555",
        label: "GCP Protocol v2 Training",
      },
    });

    await link.find("button").trigger("click");
    expect(state.open).toBe(true);
    expect(state.current?.nodeRef).toBe(
      "qualification_requirement:11111111-2222-3333-4444-555555555555"
    );
  });

  it("extracts and formats 403, 422, and 423 ProblemDetails explanations and clickable NextAction targets", () => {
    const locked423 = {
      status: 423,
      response: {
        status: 423,
        data: {
          title: "Locked",
          status: 423,
          code: "observation.locked",
          detail: "Site is locked.",
          explanation: {
            subject: "enrollment:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@1",
            claim:
              "Screening visit cannot be modified while a hard lock is in force.",
            producer: "kernel.locks",
            conditions: [
              {
                code: "lock.unlocked",
                met: false,
                description:
                  "Release the active site lock before recording observations.",
                evidence: ["assertion:99999999-8888-7777-6666-555555555555@1"],
              },
            ],
            next_actions: [
              {
                description: "Release active site lock",
                capability: "lock.release",
                target: "assertion:99999999-8888-7777-6666-555555555555@1",
              },
            ],
          },
        },
      },
    };

    expect(describeApiError(locked423)).toContain(
      "Screening visit cannot be modified while a hard lock is in force."
    );
    expect(extractProblemExplanation(locked423)?.nextActions[0]?.target).toBe(
      "assertion:99999999-8888-7777-6666-555555555555@1"
    );
    expect(formatProblemExplanationSummary(locked423.response.data)).toContain(
      "Screening visit cannot be modified"
    );
    expect(extractExplanationTargets(locked423.response.data)).toHaveLength(1);
  });

  it("renders gated actions visibly with plain-English RBAC explanations and zero raw permission tokens", () => {
    const wrapper = mount(ClinicalGatedAction, {
      props: {
        permitted: false,
        inline: true,
        label: "Sign Case Report Form",
        reason: "Requires investigator authority (collection.sign)",
        explanation: {
          claim:
            "Clinical Research Coordinators cannot sign case report form collections.",
          producer: "security.rbac",
          conditions: [],
          next_actions: [],
        },
      },
    });
    expect(wrapper.text()).toContain("Sign Case Report Form");
    expect(wrapper.text()).toContain(
      "Clinical Research Coordinators cannot sign case report form collections."
    );
    expect(wrapper.text()).not.toContain("(collection.sign)");
    expect(wrapper.findComponent(ClinicalExplanation).exists()).toBe(true);
  });
});
