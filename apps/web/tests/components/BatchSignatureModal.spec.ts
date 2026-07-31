/**
 * Unit & Component tests for BatchSignatureModal.vue
 *
 * Requirements Traceability: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
 */

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import BatchSignatureModal from "../../src/components/signatures/BatchSignatureModal.vue";
import { useSignatureStore } from "../../src/stores/signatures";

// Mock the apiClient to prevent actual network calls during unit tests
vi.mock("../../src/api/apiClient", () => {
  return {
    apiClient: {
      post: vi.fn(),
    },
  };
});

import { apiClient } from "../../src/api/apiClient";

describe("BatchSignatureModal.vue", () => {
  let pinia: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("does not render modal structure when isOpen is false", () => {
    /**
     * @req: PRD-SYS-001
     * Verify that when isOpen is false, the modal is not visible in the DOM.
     */
    const wrapper = mount(BatchSignatureModal, {
      props: {
        isOpen: false,
        subjectId: "SUBJ-101",
        selectedForms: ["Form-A", "Form-B"],
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find("#batch-signature-modal").exists()).toBe(false);
  });

  it("renders selected form IDs and layout correctly when isOpen is true", async () => {
    /**
     * @req: PRD-SYS-001
     * Verify that when isOpen is true, the modal renders list of selected form IDs,
     * summary text, and standard input fields.
     */
    const wrapper = mount(BatchSignatureModal, {
      props: {
        isOpen: true,
        subjectId: "SUBJ-101",
        selectedForms: ["Form-A", "Form-B"],
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find("#batch-signature-modal").exists()).toBe(true);
    expect(wrapper.find(".summary-text").text()).toContain("2");
    expect(wrapper.find(".summary-text").text()).toContain("SUBJ-101");

    // Check table rows
    const rows = wrapper.findAll("tbody tr");
    expect(rows.length).toBe(2);
    expect(rows[0].text()).toContain("Form-A");
    expect(rows[1].text()).toContain("Form-B");

    // Check password and meaning inputs exist
    expect(wrapper.find(".password-input").exists()).toBe(true);
    expect(wrapper.find(".signature-meaning-picker").exists()).toBe(true);
    expect(wrapper.find(".totp-input").exists()).toBe(true);
  });

  it("disables execute button until password and signature meaning are provided", async () => {
    /**
     * @req: PRD-SYS-001
     * Verify that 'Confirm & Execute Signature' button is disabled until password
     * and signature meaning are populated, and enabled afterwards.
     */
    const wrapper = mount(BatchSignatureModal, {
      props: {
        isOpen: true,
        subjectId: "SUBJ-101",
        selectedForms: ["Form-A"],
      },
      global: {
        plugins: [pinia],
      },
    });

    const submitBtn = wrapper.find("button[type='submit']");
    expect(submitBtn.element.hasAttribute("disabled")).toBe(true);

    // Set signature meaning and password
    await wrapper.find(".signature-meaning-picker").setValue("APPROVED");
    await wrapper.find(".password-input").setValue("valid_password"); // pragma: allowlist secret

    expect(submitBtn.element.hasAttribute("disabled")).toBe(false);
  });

  it("automatically clears password field and displays error message on 401 re-authentication failure", async () => {
    /**
     * @req: PRD-SYS-001
     * Verify that on 401 Unauthorized, re-authentication failure displays an error banner
     * and automatically clears the password field.
     */
    const mockPost = vi.mocked(apiClient.post);
    mockPost.mockRejectedValue({
      status: 401,
      message: "Unauthorized",
    });

    const wrapper = mount(BatchSignatureModal, {
      props: {
        isOpen: true,
        subjectId: "SUBJ-101",
        selectedForms: ["Form-A"],
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find(".password-input").setValue("wrong_password"); // pragma: allowlist secret
    await wrapper.find(".signature-meaning-picker").setValue("APPROVED");

    // Submit form
    await wrapper.find("form").trigger("submit.prevent");

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));
    await wrapper.vm.$nextTick();

    // Verify error banner is shown
    expect(wrapper.find(".error-banner").exists()).toBe(true);
    expect(wrapper.find(".error-banner").text()).toContain(
      "Authentication failed"
    );

    // Password must be wiped
    const passwordInput = wrapper.find(".password-input")
      .element as HTMLInputElement;
    expect(passwordInput.value).toBe("");
  });

  it("completes successful batch signing and displays confirmation banner", async () => {
    /**
     * @req: PRD-SYS-001
     * Verify that on successful electronic signature execution, the confirmation banner
     * is displayed with signature certificate serial number, digest, and timestamp.
     */
    const mockResponse = {
      signature_id: "sig_abc123",
      study_id: "STUDY-USDM-001",
      subject_id: "SUBJ-101",
      signed_forms_count: 1,
      content_digest:
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", // pragma: allowlist secret
      timestamp_utc: "2026-08-27T10:00:00Z",
      audit_tx: "tx_xyz789",
    };

    const mockPost = vi.mocked(apiClient.post);
    mockPost.mockResolvedValue(mockResponse);

    const wrapper = mount(BatchSignatureModal, {
      props: {
        isOpen: true,
        subjectId: "SUBJ-101",
        selectedForms: ["Form-A"],
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find(".password-input").setValue("correct_password"); // pragma: allowlist secret
    await wrapper.find(".signature-meaning-picker").setValue("APPROVED");

    // Submit form
    await wrapper.find("form").trigger("submit.prevent");

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));
    await wrapper.vm.$nextTick();

    // Emitted 'signed' with result
    expect(wrapper.emitted("signed")).toBeTruthy();
    expect(wrapper.emitted("signed")?.[0][0]).toEqual(mockResponse);

    // Error banner should not exist
    expect(wrapper.find(".error-banner").exists()).toBe(false);

    // Success confirmation banner is shown
    expect(wrapper.find(".confirmation-banner").exists()).toBe(true);
    const confirmationText = wrapper.find(".confirmation-banner").text();
    expect(confirmationText).toContain("sig_abc123");
    expect(confirmationText).toContain("2026-08-27T10:00:00Z");
    expect(confirmationText).toContain("tx_xyz789");
  });
});
