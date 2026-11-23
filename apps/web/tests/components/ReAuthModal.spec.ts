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

  it("supports semantic form submission and Enter on #reauth-signing-reason without duplicate confirm emits", async () => {
    const wrapper = mount(ReAuthModal, {
      attachTo: document.body,
      props: {
        isOpen: true,
        filename: "Protocol_Signoff.pdf",
        username: "pi.signer",
      },
    });

    expect(wrapper.find("form").exists()).toBe(true);
    const submitBtn = wrapper.find('button[type="submit"]');
    expect(submitBtn.exists()).toBe(true);

    await wrapper.find("#reauth-credential").setValue("ValidPIN987!");
    await wrapper.find("#reauth-signing-reason").setValue("CLINICAL_QC");
    await wrapper.find("#reauth-signing-reason").trigger("keydown.enter");

    expect(wrapper.emitted("confirm")).toHaveLength(1);
    expect(wrapper.emitted("confirm")?.[0]).toEqual([
      {
        credential: "ValidPIN987!",
        signing_reason: "CLINICAL_QC",
      },
    ]);

    wrapper.unmount();
  });

  it("prioritizes localError on cleared-credential Retry, clears stale errorMessage on Dismiss, and restores focus to #reauth-credential after busy", async () => {
    const wrapper = mount(ReAuthModal, {
      attachTo: document.body,
      props: {
        isOpen: true,
        errorMessage: "Server rejected expired token",
      },
    });
    await wrapper.vm.$nextTick();

    const credInput = wrapper.find("#reauth-credential");
    expect(credInput.attributes("aria-invalid")).toBe("true");
    expect(credInput.attributes("aria-describedby")).toBe("reauth-error-alert");

    // Clicking Retry when credential was wiped surfaces localError instead of stale errorMessage and focuses #reauth-credential
    await wrapper
      .find('[data-testid="modal-error-retry-btn"]')
      .trigger("click");
    expect(wrapper.find("#reauth-error-alert").text()).toContain(
      "PIN or password credential is required."
    );
    expect(document.activeElement?.id).toBe("reauth-credential");

    // Clicking Dismiss clears both localError and stale parent errorMessage even without @clear-error listener, emit clear-error, and focuses #reauth-credential
    await wrapper
      .find('[data-testid="modal-error-dismiss-btn"]')
      .trigger("click");
    expect(wrapper.emitted("clear-error")).toBeTruthy();
    expect(wrapper.find("#reauth-error-alert").exists()).toBe(false);
    expect(credInput.attributes("aria-invalid")).toBeUndefined();
    expect(document.activeElement?.id).toBe("reauth-credential");

    // Post-busy failure recovery restores focus to #reauth-credential after disabled clears
    await wrapper.setProps({ busy: true });
    (document.activeElement as HTMLElement | null)?.blur?.();
    await wrapper.setProps({
      busy: false,
      errorMessage: "Step-up verification failed (401)",
    });
    await wrapper.vm.$nextTick();
    await wrapper.vm.$nextTick();

    expect(wrapper.find("#reauth-error-alert").text()).toContain(
      "Step-up verification failed (401)"
    );
    expect(document.activeElement?.id).toBe("reauth-credential");
    wrapper.unmount();
  });
});
