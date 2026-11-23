// eConsent review, comprehension check, signature and protocol re-consent.
import { defineStore } from "pinia";

export const useConsentStore = defineStore("consent", {
  state: () => ({
    consentLanguage: "en",
    consentPassed: false,
    consentSigned: false,
    consentContent: null,
    consentCheck: null,
    pendingReconsent: null,
    reconsentModalOpen: false,
    reconsentModalError: "",
    reconsentForm: {
      username: "",
      password: "",
      reason: "Protocol Amendment Re-Consent Acknowledgment",
      customReason: "",
    },
    reconsentComprehensionAnswers: {},
    consentLivenessToken: null,
    consentLivenessExpiresAt: null,
    livenessVerified: false,
  }),
});
