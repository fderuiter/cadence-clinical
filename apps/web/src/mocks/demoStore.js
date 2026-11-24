import { registerDemoStoreReset } from "../utils/demoReset.js";
import { createCtmsAndFinancialsDomain } from "./domains/ctmsAndFinancials.js";
import { createEcrfAndQueriesDomain } from "./domains/ecrfAndQueries.js";
import { createEtmfDomain } from "./domains/etmf.js";
import { createKernelDirectoryDomain } from "./domains/kernelDirectory.js";
import { createKernelGraphDomain } from "./domains/kernelGraph.js";
import { createKnowledgeAndTicketsDomain } from "./domains/knowledgeAndTickets.js";
import { createLocksAndEntitlementsDomain } from "./domains/locksAndEntitlements.js";
import { createObservationsDomain } from "./domains/observations.js";
import { createPaymentsDomain } from "./domains/payments.js";
import { createProtocolDesignDomain } from "./domains/protocolDesign.js";
import { createSubjectsAndSitesDomain } from "./domains/subjectsAndSites.js";

export function createWebDemoStore() {
  let sequenceCounter = 10;

  const nextCounter = () => {
    sequenceCounter += 1;
    return sequenceCounter;
  };

  const state = {};
  const ctx = {
    state,
    nextCounter,
  };

  const domainSlices = [
    createObservationsDomain(ctx),
    createKernelDirectoryDomain(ctx),
    createKernelGraphDomain(ctx),
    createProtocolDesignDomain(ctx),
    createSubjectsAndSitesDomain(ctx),
    createEcrfAndQueriesDomain(ctx),
    createCtmsAndFinancialsDomain(ctx),
    createEtmfDomain(ctx),
    createKnowledgeAndTicketsDomain(ctx),
    createLocksAndEntitlementsDomain(ctx),
    createPaymentsDomain(ctx),
  ];

  function seedInitialState() {
    sequenceCounter = 10;
    for (const slice of domainSlices) {
      slice.seed();
    }
  }

  seedInitialState();

  const resetListeners = new Set();

  function resetStoreState() {
    seedInitialState();
    for (const listener of resetListeners) {
      try {
        listener();
      } catch {
        // Ignore individual listener errors
      }
    }
  }

  const composedMethods = Object.assign(
    {},
    ...domainSlices.map((slice) => slice.methods)
  );

  return {
    reset: resetStoreState,
    onReset(listener) {
      if (typeof listener !== "function") {
        return () => {};
      }
      resetListeners.add(listener);
      return () => {
        resetListeners.delete(listener);
      };
    },
    state,
    get locks() {
      return state.demoLocks;
    },
    set locks(value) {
      state.demoLocks = Array.isArray(value) ? value : [];
    },
    ...composedMethods,
  };
}

export const webDemoStore = createWebDemoStore();
registerDemoStoreReset(() => webDemoStore.reset());
