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

    const confirmBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Confirm & Sign"));
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

    const cancelBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Cancel"));
    await cancelBtn?.trigger("click");

    expect(wrapper.emitted("cancel")).toBeTruthy();
    expect(wrapper.emitted("close")).toBeTruthy();
  });

  it("supports Enter submission, targeted ARIA error cues, Dismiss clear-error, and post-failure focus recovery", async () => {
    /**
     * @req: PRD-SYS-001
     * @req: PRD-DOC-001
     */
    const wrapper = mount(ReAuthModal, {
      attachTo: document.body,
      props: {
        isOpen: true,
        username: "crc.signer",
        errorMessage: "Server rejected stale credential",
      },
    });

    expect(wrapper.find("form.reauth-modal-form").exists()).toBe(true);
    expect(wrapper.find(".close-btn").attributes("type")).toBe("button");

    const credentialInput = wrapper.find("#reauth-credential");
    const reasonSelect = wrapper.find("#reauth-signing-reason");

    expect(credentialInput.attributes("aria-invalid")).toBe("true");
    expect(credentialInput.attributes("aria-describedby")).toBe(
      "reauth-error-alert"
    );
    expect(reasonSelect.attributes("aria-invalid")).toBeUndefined();

    // Pressing Enter while credential is empty surfaces localError over stale server error and focuses credential
    await credentialInput.trigger("keydown.enter");
    await wrapper.vm.$nextTick();
    expect(wrapper.find("#reauth-error-alert").text()).toContain(
      "PIN or password credential is required."
    );
    expect(document.activeElement).toBe(credentialInput.element);

    // Dismissing clears both localError and parent errorMessage via clear-error and keeps focus on credential
    await wrapper
      .find('[data-testid="modal-error-dismiss-btn"]')
      .trigger("click");
    await wrapper.vm.$nextTick();
    expect(wrapper.emitted("clear-error")).toBeTruthy();
    expect(wrapper.find("#reauth-error-alert").exists()).toBe(false);
    expect(document.activeElement).toBe(credentialInput.element);

    // Missing signing reason marks #reauth-signing-reason invalid instead of #reauth-credential
    await credentialInput.setValue("ValidPIN987!");
    (wrapper.vm as any).signingReasonVal = "";
    await reasonSelect.trigger("keydown.enter");
    await wrapper.vm.$nextTick();
    expect(wrapper.find("#reauth-error-alert").text()).toContain(
      "Please select a controlled signing reason."
    );
    expect(reasonSelect.attributes("aria-invalid")).toBe("true");
    expect(reasonSelect.attributes("aria-describedby")).toBe(
      "reauth-error-alert"
    );
    expect(credentialInput.attributes("aria-invalid")).toBeUndefined();
    expect(document.activeElement).toBe(reasonSelect.element);

    // Selecting reason and pressing Enter on #reauth-signing-reason confirms
    await reasonSelect.setValue("REVIEW");
    await reasonSelect.trigger("keydown.enter");
    expect(wrapper.emitted("confirm")?.[0]).toEqual([
      {
        credential: "ValidPIN987!",
        signing_reason: "REVIEW",
      },
    ]);

    // Simulate async parent verification failure (busy: true -> false with errorMessage) restoring focus to credential
    await wrapper.setProps({ busy: true, errorMessage: "" });
    await wrapper.setProps({
      busy: false,
      errorMessage: "Invalid PIN",
    });
    await wrapper.vm.$nextTick();
    await wrapper.vm.$nextTick();
    expect(document.activeElement).toBe(credentialInput.element);

    wrapper.unmount();
  });
});
