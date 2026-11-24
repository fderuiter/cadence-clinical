// Participant wallet ledger balances, masked payout accounts, receipt OCR, and claim state.
import { DEFAULT_DEMO_SITE, DEMO_STUDY } from "demo-fixtures";
import { defineStore } from "pinia";

function isInitialDemoBuild(env = import.meta.env) {
  return (
    env?.MODE === "demo" ||
    env?.VITE_DEMO_MODE === "true" ||
    (typeof process !== "undefined" && process.env?.VITE_DEMO_MODE === "true")
  );
}

export const useWalletStore = defineStore("wallet", {
  state: () => {
    const demo = isInitialDemoBuild();
    const defaultStudyId = demo ? DEMO_STUDY?.code || null : null;
    const defaultSiteId = demo ? DEFAULT_DEMO_SITE?.code || null : null;
    return {
      walletId: null,
      walletStudyId: defaultStudyId,
      walletSiteId: defaultSiteId,
      walletLoading: false,
      walletLoaded: false,
      walletBalance: null,
      lifetimeStipends: 0.0,
      lifetimeReimbursements: 0.0,
      lifetimeDisbursements: 0.0,
      taxableYtd: 0.0,
      is1099ThresholdExceeded: false,
      w9Status: "NOT_REQUIRED",
      currency: "USD",
      virtualCardMask: null,
      virtualCardExpiry: null,
      achRoutingMask: null,
      achAccountMask: null,
      achAccountHolder: null,
      recentTransactions: [],
      expenseClaims: [],
      disbursingBalance: false,
      disburseModalOpen: false,
      disburseForm: {
        disbursementType: "DIRECT_ACH",
        amount: "",
        error: null,
        idempotencyKey: null,
      },
      disbursementConfirmation: null,
      expenseSubmitting: false,
      expenseManualEntry: false,
      expenseError: null,
      expenseOcr: {
        loading: false,
        error: null,
        warning: null,
        data: null,
        previewUrl: null,
        ocrToken: null,
        imageBase64: null,
      },
      expenseForm: {
        subjectId: null,
        studyId: defaultStudyId,
        siteId: defaultSiteId,
        category: "TRANSIT",
        merchantName: "",
        grossAmount: 0.0,
        isMealAllowance: false,
        perDiemCap: 75.0,
        expenseDate: "",
        ocrConfidence: 0.0,
        receiptImageName: "",
        idempotencyKey: null,
      },
      expenseResult: null,
    };
  },
});
