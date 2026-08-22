/**
 * Unit & Component tests for ReAuthModal.vue
 *
 * Requirements Traceability: PRD-SYS-001, PRD-DOC-001 | GxP 21 CFR Part 11 Electronic Signature Re-Authentication
 */

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import ReAuthModal from "../../src/components/shared/ReAuthModal.vue";

describe("ReAuthModal.vue", () => {
  it("does not render in DOM when isOpen is false", () => {
    /**
     * @req: PRD-SYS-001
     * @req: PRD-DOC-001
     */
    const wrapper = mount(ReAuthModal, {
      props: {
        isOpen: false,
      },
    });

    expect(wrapper.find("#re-auth-modal").exists()).toBe(false);
  });

  it("renders credential input, signing reasons, and filename context when isOpen is true", () => {
    /**
     * @req: PRD-SYS-001
     * @req: PRD-DOC-001
     */
    const wrapper = mount(ReAuthModal, {
      props: {
        isOpen: true,
        filename: "Clinical_Protocol_v1.0.pdf",
        username: "crc.signer",
      },
    });

    expect(wrapper.find("#re-auth-modal").exists()).toBe(true);
    expect(wrapper.text()).toContain("Clinical_Protocol_v1.0.pdf");
    expect(wrapper.find("#reauth-credential").exists()).toBe(true);
    expect(wrapper.find("#reauth-signing-reason").exists()).toBe(true);
  });

  it("emits confirm with entered credential and signing reason and clears buffer", async () => {
    /**
     * @req: PRD-SYS-001
     * @req: PRD-DOC-001
     */
    const wrapper = mount(ReAuthModal, {
      props: {
        isOpen: true,
        filename: "Study_Protocol.pdf",
        username: "crc.signer",
      },
    });

    const credentialInput = wrapper.find("#reauth-credential");
    await credentialInput.setValue("SecretPIN1234!");

    const reasonSelect = wrapper.find("#reauth-signing-reason");
    await reasonSelect.setValue("SPONSOR_APPROVAL");

    const confirmBtn = wrapper.findAll("button").find((b) => b.text().includes("Confirm & Sign"));
    expect(confirmBtn).toBeDefined();
    await confirmBtn?.trigger("click");

    expect(wrapper.emitted("confirm")).toBeTruthy();
    expect(wrapper.emitted("confirm")?.[0]).toEqual([
      {
        credential: "SecretPIN1234!",
        signing_reason: "SPONSOR_APPROVAL",
      },
    ]);

    // Credential must be cleared immediately from local state
    expect((wrapper.vm as any).credentialVal).toBe("");
  });

  it("emits cancel and close when Cancel button is clicked", async () => {
    /**
     * @req: PRD-SYS-001
     * @req: PRD-DOC-001
     */
    const wrapper = mount(ReAuthModal, {
      props: {
        isOpen: true,
      },
    });

    const cancelBtn = wrapper.findAll("button").find((b) => b.text().includes("Cancel"));
    await cancelBtn?.trigger("click");

    expect(wrapper.emitted("cancel")).toBeTruthy();
    expect(wrapper.emitted("close")).toBeTruthy();
  });
});
