/**
 * @req: PRD-SYS-182
 */
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises, type VueWrapper } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ParticipantWalletView from "../../src/views/ParticipantWalletView.vue";
import { useToastStore } from "../../src/stores/toasts.js";

describe("ParticipantWalletView Mutation Error Handling (@req: PRD-SYS-182)", () => {
  let pinia: ReturnType<typeof createPinia>;
  let toastStore: ReturnType<typeof useToastStore>;
  const originalFetch = global.fetch;

  const mockWallet = {
    id: "wallet-test-1",
    subject_id: "SUBJ-1001",
    study_id: "STUDY-DCT-001",
    site_id: "SITE-101",
    current_balance: 150.0,
    lifetime_stipends: 300.0,
    lifetime_reimbursements: 150.0,
    taxable_ytd: 450.0,
    virtual_card_token: "VTOK-12345",
    debit_card_status: "PENDING",
    w9_status: "SUBMITTED",
    tax_form_submitted: true,
    tax_form_verified: false,
    tax_id_last_four: "4321",
  };

  function createMockFetch(
    customHandler?: (
      url: string,
      options?: RequestInit
    ) => Promise<Response> | null
  ) {
    return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === "string" ? input : input.toString();

      if (customHandler) {
        const customRes = customHandler(url, init);
        if (customRes !== null) {
          return customRes;
        }
      }

      // Default mock responses for loadAllData & selectWallet
      if (url === "/api/v1/payments/participant/wallets") {
        return new Response(JSON.stringify([mockWallet]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url === "/api/v1/payments/participant/stipend-rules") {
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url === "/api/v1/payments/participant/expenses") {
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url.includes("/tax-1099-status")) {
        return new Response(
          JSON.stringify({
            taxable_ytd: 450.0,
            threshold: 600.0,
            requires_w9: true,
            status: "AWAITING_VERIFICATION",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        );
      }
      if (url.includes("/balance")) {
        return new Response(
          JSON.stringify({
            current_balance: 150.0,
            available_balance: 150.0,
            pending_claims_total: 0.0,
            lifetime_stipends: 300.0,
            lifetime_reimbursements: 150.0,
            taxable_ytd: 450.0,
            currency: "USD",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } }
        );
      }

      return new Response(JSON.stringify({}), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    });
  }

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    toastStore = useToastStore();
    global.fetch = createMockFetch();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  async function mountComponent(): Promise<VueWrapper<any>> {
    const wrapper = mount(ParticipantWalletView, {
      global: {
        plugins: [pinia],
      },
    });
    await flushPromises();
    return wrapper;
  }

  function getIssueCardButton(wrapper: any) {
    return wrapper
      .findAll("button")
      .find((b: any) => b.text().includes("Issue Card"));
  }

  function getVerifyW9Button(wrapper: any) {
    return wrapper
      .findAll("button")
      .find((b: any) => b.text().includes("Verify W-9"));
  }

  describe("issueReloadableCard error handling", () => {
    it("handles HTTP 4xx error with ensureOk and reports apiError toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/issue-card")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                detail: "Participant mailing address incomplete",
              }),
              { status: 400, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      const issueBtn = getIssueCardButton(wrapper);
      expect(issueBtn).toBeDefined();

      // Trigger issueReloadableCard
      await issueBtn!.trigger("click");
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.tone).toBe("error");
      expect(toast.title).toBe("Issuing reloadable card failed");
      expect(toast.message).toContain(
        "Participant mailing address incomplete (HTTP 400)"
      );

      // Verify isSubmitting was reset to false and button is re-enabled
      expect(wrapper.vm.isSubmitting).toBe(false);
      expect(issueBtn!.attributes("disabled")).toBeUndefined();
    });

    it("handles HTTP 5xx error with ensureOk and reports apiError toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/issue-card")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                title: "Gateway Timeout",
                detail: "Card vendor provider unresponsive",
              }),
              { status: 504, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      const issueBtn = getIssueCardButton(wrapper);
      expect(issueBtn).toBeDefined();

      await issueBtn!.trigger("click");
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.title).toBe("Issuing reloadable card failed");
      expect(toast.message).toContain(
        "Card vendor provider unresponsive (HTTP 504)"
      );
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("handles network failure and reports apiError toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/issue-card")) {
          return Promise.reject(new TypeError("Failed to fetch"));
        }
        return null;
      });

      const wrapper = await mountComponent();
      const issueBtn = getIssueCardButton(wrapper);
      expect(issueBtn).toBeDefined();

      await issueBtn!.trigger("click");
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.title).toBe("Issuing reloadable card failed");
      expect(toast.message).toBe("Failed to fetch");
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("successfully issues card and reloads data", async () => {
      let issued = false;
      global.fetch = createMockFetch((url) => {
        if (url.includes("/issue-card")) {
          issued = true;
          return Promise.resolve(
            new Response(
              JSON.stringify({
                card_token: "CARD-12345",
                status: "ISSUED",
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      const issueBtn = getIssueCardButton(wrapper);
      expect(issueBtn).toBeDefined();

      await issueBtn!.trigger("click");
      await flushPromises();

      expect(issued).toBe(true);
      expect(toastStore.toasts).toHaveLength(0);
      expect(wrapper.vm.isSubmitting).toBe(false);
    });
  });

  describe("submitW9Form error handling", () => {
    it("handles HTTP 4xx error: preserves w9Form data, keeps modal open, and shows toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/submit-w9")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                detail: "TIN format does not conform to IRS requirements",
              }),
              { status: 422, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();

      // Select wallet and open W9 modal
      await wrapper.vm.selectWallet(mockWallet);
      wrapper.vm.showW9Modal = true;
      wrapper.vm.w9Form = {
        full_legal_name: "Jane Doe",
        tax_id_last_four: "4321",
        address: "123 Research Blvd, Boston, MA",
        certify_accurate: true,
      };
      await flushPromises();

      // Submit W9 form
      await wrapper.vm.submitW9Form();
      await flushPromises();

      // Assert error toast is shown
      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.tone).toBe("error");
      expect(toast.title).toBe("Submitting W-9 form failed");
      expect(toast.message).toContain(
        "TIN format does not conform to IRS requirements (HTTP 422)"
      );

      // Assert modal remains open and data preserved
      expect(wrapper.vm.showW9Modal).toBe(true);
      expect(wrapper.vm.w9Form.full_legal_name).toBe("Jane Doe");
      expect(wrapper.vm.w9Form.tax_id_last_four).toBe("4321");
      expect(wrapper.vm.w9Form.address).toBe("123 Research Blvd, Boston, MA");
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("handles HTTP 5xx error: keeps modal open and reports toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/submit-w9")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                detail: "IRS verification gateway down",
              }),
              { status: 503, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      wrapper.vm.showW9Modal = true;
      wrapper.vm.w9Form = {
        full_legal_name: "John Smith",
        tax_id_last_four: "9876",
        address: "456 Clinical Way, San Diego, CA",
        certify_accurate: true,
      };
      await flushPromises();

      await wrapper.vm.submitW9Form();
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      expect(toastStore.toasts[0].title).toBe("Submitting W-9 form failed");
      expect(toastStore.toasts[0].message).toContain(
        "IRS verification gateway down (HTTP 503)"
      );
      expect(wrapper.vm.showW9Modal).toBe(true);
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("handles network error: keeps modal open and reports toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/submit-w9")) {
          return Promise.reject(
            new TypeError("Network error: connection lost")
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      wrapper.vm.showW9Modal = true;
      wrapper.vm.w9Form = {
        full_legal_name: "Alice Johnson",
        tax_id_last_four: "1111",
        address: "789 Health Ave, Chicago, IL",
        certify_accurate: true,
      };
      await flushPromises();

      await wrapper.vm.submitW9Form();
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      expect(toastStore.toasts[0].title).toBe("Submitting W-9 form failed");
      expect(toastStore.toasts[0].message).toBe(
        "Network error: connection lost"
      );
      expect(wrapper.vm.showW9Modal).toBe(true);
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("closes modal on successful submit", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/submit-w9")) {
          return Promise.resolve(
            new Response(JSON.stringify({ status: "SUBMITTED" }), {
              status: 200,
              headers: { "Content-Type": "application/json" },
            })
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      wrapper.vm.showW9Modal = true;
      wrapper.vm.w9Form = {
        full_legal_name: "Alice Johnson",
        tax_id_last_four: "1111",
        address: "789 Health Ave, Chicago, IL",
        certify_accurate: true,
      };
      await flushPromises();

      await wrapper.vm.submitW9Form();
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(0);
      expect(wrapper.vm.showW9Modal).toBe(false);
      expect(wrapper.vm.isSubmitting).toBe(false);
    });
  });

  describe("verifyW9Form error handling", () => {
    it("handles HTTP 4xx error and reports apiError toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/verify-w9")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                detail: "Participant record not eligible for verification",
              }),
              { status: 400, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      await flushPromises();

      const verifyBtn = getVerifyW9Button(wrapper);
      expect(verifyBtn).toBeDefined();

      await verifyBtn!.trigger("click");
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.tone).toBe("error");
      expect(toast.title).toBe("Verifying W-9 form failed");
      expect(toast.message).toContain(
        "Participant record not eligible for verification (HTTP 400)"
      );
      expect(wrapper.vm.isSubmitting).toBe(false);
      expect(verifyBtn!.attributes("disabled")).toBeUndefined();
    });

    it("handles HTTP 5xx error and reports apiError toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/verify-w9")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                detail: "TIN verification ledger unavailable",
              }),
              { status: 502, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      await flushPromises();

      await wrapper.vm.verifyW9Form(mockWallet.id);
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.title).toBe("Verifying W-9 form failed");
      expect(toast.message).toContain(
        "TIN verification ledger unavailable (HTTP 502)"
      );
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("handles network failure and reports apiError toast", async () => {
      global.fetch = createMockFetch((url) => {
        if (url.includes("/verify-w9")) {
          return Promise.reject(new TypeError("Network error"));
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      await flushPromises();

      await wrapper.vm.verifyW9Form(mockWallet.id);
      await flushPromises();

      expect(toastStore.toasts).toHaveLength(1);
      const toast = toastStore.toasts[0];
      expect(toast.title).toBe("Verifying W-9 form failed");
      expect(toast.message).toBe("Network error");
      expect(wrapper.vm.isSubmitting).toBe(false);
    });

    it("successfully verifies W9 form and refreshes state", async () => {
      let verified = false;
      global.fetch = createMockFetch((url) => {
        if (url.includes("/verify-w9")) {
          verified = true;
          return Promise.resolve(
            new Response(JSON.stringify({ status: "VERIFIED" }), {
              status: 200,
              headers: { "Content-Type": "application/json" },
            })
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      await flushPromises();

      await wrapper.vm.verifyW9Form(mockWallet.id);
      await flushPromises();

      expect(verified).toBe(true);
      expect(toastStore.toasts).toHaveLength(0);
      expect(wrapper.vm.isSubmitting).toBe(false);
    });
  });

  describe("UI disabled state during isSubmitting", () => {
    it("disables mutation buttons when isSubmitting is true", async () => {
      let resolveCardRequest: (value: Response) => void;
      const cardPromise = new Promise<Response>((resolve) => {
        resolveCardRequest = resolve;
      });

      global.fetch = createMockFetch((url) => {
        if (url.includes("/issue-card")) {
          return cardPromise;
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(mockWallet);
      wrapper.vm.showW9Modal = true;
      wrapper.vm.w9Form = {
        full_legal_name: "Jane Doe",
        tax_id_last_four: "4321",
        address: "123 Research Blvd",
        certify_accurate: true,
      };
      await flushPromises();

      const issueBtn = getIssueCardButton(wrapper);
      const verifyBtn = getVerifyW9Button(wrapper);
      const submitW9Btn = wrapper.find(".modal-footer button.btn-primary");
      const cancelW9Btn = wrapper.find(".modal-footer button.btn-secondary");

      expect(issueBtn).toBeDefined();
      expect(verifyBtn).toBeDefined();
      expect(issueBtn!.attributes("disabled")).toBeUndefined();
      expect(verifyBtn!.attributes("disabled")).toBeUndefined();
      expect(submitW9Btn.attributes("disabled")).toBeUndefined();
      expect(cancelW9Btn.attributes("disabled")).toBeUndefined();

      // Trigger issueReloadableCard without awaiting completion
      const issueCall = wrapper.vm.issueReloadableCard(mockWallet.id);
      await flushPromises();

      expect(wrapper.vm.isSubmitting).toBe(true);
      expect(issueBtn!.attributes("disabled")).toBeDefined();
      expect(verifyBtn!.attributes("disabled")).toBeDefined();
      expect(submitW9Btn.attributes("disabled")).toBeDefined();
      expect(cancelW9Btn.attributes("disabled")).toBeDefined();

      // Resolve the pending request
      resolveCardRequest!(
        new Response(JSON.stringify({ status: "ISSUED" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );
      await issueCall;
      await flushPromises();

      expect(wrapper.vm.isSubmitting).toBe(false);
      expect(issueBtn!.attributes("disabled")).toBeUndefined();
      expect(verifyBtn!.attributes("disabled")).toBeUndefined();
      expect(submitW9Btn.attributes("disabled")).toBeUndefined();
      expect(cancelW9Btn.attributes("disabled")).toBeUndefined();
    });
  });

  describe("masked payout credentials and server-backed receipt OCR", () => {
    it("masks routing identifiers and never renders raw routing numbers", async () => {
      global.fetch = createMockFetch((url) => {
        if (url === "/api/v1/payments/participant/wallets") {
          return Promise.resolve(
            new Response(
              JSON.stringify([
                {
                  ...mockWallet,
                  achRoutingNumber: "021000021",
                  achAccountMask: "ACH-***4321",
                },
              ]),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      await wrapper.vm.selectWallet(wrapper.vm.wallets[0]);
      await flushPromises();

      expect(wrapper.text()).not.toContain("021000021");
      expect(wrapper.text()).toContain("*****0021");
      expect(wrapper.text()).toContain("ACH-***4321");
    });

    it("uploads base64 receipt bytes to /api/v1/payments/participant/expenses/ocr-extract and populates extracted fields", async () => {
      let capturedOcrBody: any = null;
      global.fetch = createMockFetch((url, init) => {
        if (url === "/api/v1/payments/participant/expenses/ocr-extract") {
          capturedOcrBody = JSON.parse(String(init?.body || "{}"));
          return Promise.resolve(
            new Response(
              JSON.stringify({
                ocrReadable: true,
                merchantName: "SHELL SERVICE STATION",
                expenseDate: "2026-09-14",
                grossAmount: 42.75,
                taxDeduction: 0,
                netAmount: 42.75,
                category: "FUEL",
                confidenceScore: 0.96,
                rawText: "SHELL SERVICE STATION\nDATE 2026-09-14\nTOTAL $42.75",
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      wrapper.vm.activeTab = "ocr";
      await flushPromises();

      const receiptFile = new File(
        ["<svg><text>SHELL SERVICE STATION $42.75</text></svg>"],
        "fuel_receipt.svg",
        { type: "image/svg+xml" }
      );
      await wrapper.vm.extractReceiptFromFile(receiptFile);
      await flushPromises();

      expect(capturedOcrBody).not.toBeNull();
      expect(capturedOcrBody.filename).toBe("fuel_receipt.svg");
      expect(capturedOcrBody.imageBase64).toBeTruthy();
      expect(wrapper.vm.expenseForm.merchant_name).toBe(
        "SHELL SERVICE STATION"
      );
      expect(wrapper.vm.expenseForm.gross_amount).toBe(42.75);
      expect(wrapper.vm.expenseForm.category).toBe("FUEL");
      expect(wrapper.find("#staff-ocr-warning-banner").exists()).toBe(false);
    });

    it("never fabricates merchant or amount when OCR returns unreadable image", async () => {
      global.fetch = createMockFetch((url) => {
        if (url === "/api/v1/payments/participant/expenses/ocr-extract") {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                ocrReadable: false,
                merchantName: "",
                expenseDate: "",
                grossAmount: 0,
                taxDeduction: 0,
                netAmount: 0,
                category: "OTHER",
                confidenceScore: 0,
                rawText: "",
                warning: "Receipt image is blank or unreadable.",
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      wrapper.vm.activeTab = "ocr";
      await flushPromises();

      const blankFile = new File([new Uint8Array([0, 1, 2])], "blank.png", {
        type: "image/png",
      });
      await wrapper.vm.extractReceiptFromFile(blankFile);
      await flushPromises();

      expect(wrapper.vm.expenseForm.merchant_name).toBe("");
      expect(wrapper.vm.expenseForm.gross_amount).toBe(0);
      expect(wrapper.vm.expenseForm.ocr_confidence).toBe(0);
      expect(wrapper.find("#staff-ocr-warning-banner").exists()).toBe(true);
      expect(wrapper.find("#staff-ocr-warning-banner").text()).toContain(
        "Receipt image is blank or unreadable."
      );
    });

    it("renders camelCase .NET wallet columns, balance inquiry, visit verification, claim review, payout, and ledger transactions without runtime errors", async () => {
      let visitVerifiedCalled = false;
      let claimReviewedDecision = "";
      let disburseCalled = false;

      global.fetch = createMockFetch((url, init) => {
        if (url === "/api/v1/payments/participant/wallets") {
          return Promise.resolve(
            new Response(
              JSON.stringify([
                {
                  id: "w-camel-1",
                  subjectId: "SUBJ-2001",
                  studyId: "STUDY-DCT-001",
                  siteId: "SITE-101",
                  currentBalance: 225.0,
                  lifetimeStipends: 150.0,
                  lifetimeReimbursements: 75.0,
                  lifetimeDisbursements: 50.0,
                  taxableYtd: 510.0,
                  is1099ThresholdExceeded: false,
                  w9Status: "SUBMITTED",
                  currency: "USD",
                  status: "ACTIVE",
                  cardType: "RELOADABLE_DEBIT",
                  cardStatus: "ACTIVE",
                  virtualCardMask: "**** **** **** 7788",
                  virtualCardExpiry: "11/29",
                  achRoutingMask: "*****0021",
                  achAccountMask: "ACH-***8899",
                  achAccountHolder: "Participant SUBJ-2001",
                  antiCoercionCap: 300.0,
                  taxIdLastFour: "8899",
                },
              ]),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url === "/api/v1/payments/participant/stipend-rules") {
          return Promise.resolve(
            new Response(
              JSON.stringify([
                {
                  id: "rule-1",
                  visitCode: "V1_SCREENING",
                  visitName: "Screening & Consent",
                  amount: 75.0,
                  currency: "USD",
                  taxable: false,
                  antiCoercionCap: 100.0,
                  isMilestone: false,
                },
              ]),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url === "/api/v1/payments/participant/expenses") {
          return Promise.resolve(
            new Response(
              JSON.stringify([
                {
                  id: "claim-camel-12345678",
                  subjectId: "SUBJ-2001",
                  expenseDate: "2026-10-12",
                  category: "LODGING",
                  merchantName: "CLINICAL INN",
                  grossAmount: 140.0,
                  taxDeduction: 0,
                  netAmount: 140.0,
                  status: "PENDING_REVIEW",
                  receiptImageName: "hotel_receipt.png",
                },
              ]),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url === "/api/v1/payments/participant/transactions") {
          return Promise.resolve(
            new Response(
              JSON.stringify([
                {
                  id: "tx-camel-1",
                  createdAt: "2026-10-12T10:00:00Z",
                  subjectId: "SUBJ-2001",
                  studyId: "STUDY-DCT-001",
                  transactionType: "STIPEND_CREDIT",
                  amount: 75.0,
                  balanceAfter: 225.0,
                  description: "Screening visit stipend credited",
                },
              ]),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url.includes("/tax-1099-status")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                walletId: "w-camel-1",
                subjectId: "SUBJ-2001",
                taxableYtd: 510.0,
                thresholdAmount: 600.0,
                thresholdPercentage: 85.0,
                is1099ThresholdExceeded: false,
                w9Status: "SUBMITTED",
                warningLevel: "APPROACHING",
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url.includes("/balance")) {
          return Promise.resolve(
            new Response(
              JSON.stringify({
                walletId: "w-camel-1",
                subjectId: "SUBJ-2001",
                availableBalance: 225.0,
                pendingClaimsTotal: 140.0,
                lifetimeStipends: 150.0,
                lifetimeReimbursements: 75.0,
                lifetimeDisbursements: 50.0,
                taxableYtd: 510.0,
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url === "/api/v1/payments/participant/events/visit-verified") {
          visitVerifiedCalled = true;
          return Promise.resolve(
            new Response(
              JSON.stringify({
                amount: 75.0,
                currentBalance: 300.0,
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        if (url.includes("/expenses/claim-camel-12345678/review")) {
          const parsed = JSON.parse(String(init?.body || "{}"));
          claimReviewedDecision = parsed.status;
          return Promise.resolve(
            new Response(JSON.stringify({ status: parsed.status }), {
              status: 200,
              headers: { "Content-Type": "application/json" },
            })
          );
        }
        if (url.includes("/wallets/w-camel-1/disburse")) {
          disburseCalled = true;
          return Promise.resolve(
            new Response(
              JSON.stringify({
                id: "disb-1",
                status: "DISPATCHED",
                referenceNumber: "DISB-ACH-998877",
              }),
              { status: 200, headers: { "Content-Type": "application/json" } }
            )
          );
        }
        return null;
      });

      const wrapper = await mountComponent();
      expect(wrapper.text()).toContain("SUBJ-2001");
      expect(wrapper.text()).toContain("$225.00 USD");
      expect(wrapper.text()).toContain("$300.00 max");
      expect(wrapper.text()).toContain("**** **** **** 7788");

      // Inspect wallet -> renders camelCase balance inquiry & tax status cleanly
      await wrapper.vm.selectWallet(wrapper.vm.wallets[0]);
      await flushPromises();
      expect(wrapper.text()).toContain("APPROACHING (85.0%)");
      expect(wrapper.text()).toContain("$140.00");

      // Complete visit verification
      wrapper.vm.activeTab = "stipends";
      await flushPromises();
      await wrapper.vm.triggerVisitVerified();
      await flushPromises();
      expect(visitVerifiedCalled).toBe(true);
      expect(wrapper.vm.visitTriggerSuccess).toContain("Credited $75.00");

      // Complete 1-click coordinator claim review
      wrapper.vm.activeTab = "approvals";
      await flushPromises();
      expect(wrapper.text()).toContain("CLINICAL INN");
      await wrapper.vm.reviewClaim("claim-camel-12345678", "APPROVED");
      await flushPromises();
      expect(claimReviewedDecision).toBe("APPROVED");

      // Complete payout action & render transaction ledger tab
      wrapper.vm.activeTab = "wallets";
      await wrapper.vm.selectWallet(wrapper.vm.wallets[0]);
      await flushPromises();
      await wrapper.vm.triggerDisbursement();
      await flushPromises();
      expect(disburseCalled).toBe(true);
      expect(wrapper.vm.activeTab).toBe("disbursements");
      expect(wrapper.text()).toContain("STIPEND_CREDIT");
      expect(wrapper.text()).toContain("+$75.00");
      expect(wrapper.text()).toContain("$225.00");
      expect(toastStore.toasts).toHaveLength(0);
    });
  });
});
