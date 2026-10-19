// The local 21 CFR Part 11 audit ledger shown in the footer.
import { defineStore } from "pinia";

export const useAuditStore = defineStore("audit", {
  state: () => ({
    ledgerBlocks: [],
  }),
});
