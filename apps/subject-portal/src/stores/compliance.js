// The participant's compliance overview (rate, counts and history).
import { defineStore } from "pinia";

export const useComplianceStore = defineStore("compliance", {
  state: () => ({
    compliance: null, // Holds SubjectComplianceResponse
    complianceLoading: false,
    complianceError: null,
  }),
});
