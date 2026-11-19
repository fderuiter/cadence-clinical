// Stipend wallet balance and the travel expense (receipt OCR) claim.
import { defineStore } from "pinia";

export const useWalletStore = defineStore("wallet", {
  state: () => ({
    walletBalance: null,
    expenseError: null,
    expenseOcr: {
      loading: false,
      error: null,
      data: null,
      previewUrl: null,
    },
    expenseForm: {
      subjectId: null,
      studyId: "study_001",
      siteId: "site_001",
      category: "TRANSIT",
      merchantName: "",
      grossAmount: 0.0,
      isMealAllowance: false,
      perDiemCap: 75.0,
      expenseDate: "",
      ocrConfidence: 0.98,
      receiptImageName: "",
    },
    expenseResult: null,
  }),
});
