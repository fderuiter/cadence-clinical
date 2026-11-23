import { registerDemoStoreReset } from "../utils/demoReset.js";
import { createCtmsSsuSliceStore } from "./slices/ctms-ssu.js";
import { createEtmfDocumentsSliceStore } from "./slices/etmf-documents.js";
import { createExecutionEcrfSliceStore } from "./slices/execution-ecrf.js";
import { createKernelSliceStore } from "./slices/kernel.js";
import { createKnowledgeTicketsSliceStore } from "./slices/knowledge-tickets.js";
import { createPaymentsWalletSliceStore } from "./slices/payments-wallet.js";
import { createProtocolSoaSliceStore } from "./slices/protocol-soa.js";
import { createSafetyLabsDctSliceStore } from "./slices/safety-labs-dct.js";
import { createTrainingLocksSliceStore } from "./slices/training-locks.js";

export const WEB_DEMO_SLICE_NAMES = Object.freeze([
  "kernel",
  "protocol-soa",
  "execution-ecrf",
  "ctms-ssu",
  "etmf-documents",
  "payments-wallet",
  "knowledge-tickets",
  "safety-labs-dct",
  "training-locks",
]);

export function createWebDemoStore() {
  let sequenceCounter = 10;
  const resetListeners = new Set();

  const nextCounter = () => {
    sequenceCounter += 1;
    return sequenceCounter;
  };

  const state = {};
  const ctx = {
    state,
    nextCounter,
    store: null,
  };

  const slices = {
    kernel: createKernelSliceStore(ctx),
    "protocol-soa": createProtocolSoaSliceStore(ctx),
    "execution-ecrf": createExecutionEcrfSliceStore(ctx),
    "ctms-ssu": createCtmsSsuSliceStore(ctx),
    "etmf-documents": createEtmfDocumentsSliceStore(ctx),
    "payments-wallet": createPaymentsWalletSliceStore(ctx),
    "knowledge-tickets": createKnowledgeTicketsSliceStore(ctx),
    "safety-labs-dct": createSafetyLabsDctSliceStore(ctx),
    "training-locks": createTrainingLocksSliceStore(ctx),
  };

  function reset() {
    sequenceCounter = 10;
    for (const name of WEB_DEMO_SLICE_NAMES) {
      slices[name].reset();
    }
    for (const listener of resetListeners) {
      try {
        listener();
      } catch {
        // Ignore non-fatal reset listener errors.
      }
    }
  }

  const store = {
    ...slices.kernel.methods,
    ...slices["protocol-soa"].methods,
    ...slices["execution-ecrf"].methods,
    ...slices["ctms-ssu"].methods,
    ...slices["etmf-documents"].methods,
    ...slices["payments-wallet"].methods,
    ...slices["knowledge-tickets"].methods,
    ...slices["safety-labs-dct"].methods,
    ...slices["training-locks"].methods,
    state,
    slices,
    reset,
    onReset(listener) {
      if (typeof listener !== "function") return () => {};
      resetListeners.add(listener);
      return () => {
        resetListeners.delete(listener);
      };
    },
    get locks() {
      return state.demoLocks;
    },
    set locks(value) {
      state.demoLocks = Array.isArray(value) ? [...value] : [];
    },
  };

  ctx.store = store;
  reset();

  return store;
}

export const webDemoStore = createWebDemoStore();
registerDemoStoreReset(() => webDemoStore.reset());
