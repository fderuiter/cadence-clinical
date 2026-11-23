import { http, HttpResponse } from "msw";
import {
  createEconsentSliceHandlers,
  createEconsentSliceStore,
} from "./slices/econsent.js";
import {
  createInteropEproSliceHandlers,
  createInteropEproSliceStore,
} from "./slices/interop-epro.js";
import { createWalletPaymentsSliceStore } from "./slices/wallet-payments.js";

function bindSliceProperty(target, sliceState, key) {
  Object.defineProperty(target, key, {
    enumerable: true,
    get: () => sliceState[key],
    set: (value) => {
      sliceState[key] = value;
    },
  });
}

export const PORTAL_DEMO_SLICE_NAMES = Object.freeze([
  "interop-epro",
  "econsent",
  "wallet-payments",
]);

export function createPortalDemoStore() {
  const interopEpro = createInteropEproSliceStore();
  const econsent = createEconsentSliceStore();
  const walletPayments = createWalletPaymentsSliceStore();
  const slices = {
    "interop-epro": interopEpro,
    econsent,
    "wallet-payments": walletPayments,
  };
  Object.defineProperties(slices, {
    interopEpro: { value: interopEpro },
    walletPayments: { value: walletPayments },
  });

  const seqBox = { actionSeq: 0 };
  const state = {};
  bindSliceProperty(state, slices.interopEpro.state, "assignments");
  bindSliceProperty(state, slices.interopEpro.state, "instruments");
  bindSliceProperty(state, slices.interopEpro.state, "notifications");
  bindSliceProperty(state, slices.interopEpro.state, "submissions");
  bindSliceProperty(state, slices.interopEpro.state, "complianceScore");
  bindSliceProperty(state, slices.econsent.state, "pendingReconsents");
  bindSliceProperty(state, slices.econsent.state, "consentCaptured");
  bindSliceProperty(state, slices.walletPayments.state, "walletBalance");
  bindSliceProperty(state, slices.walletPayments.state, "walletLedger");
  bindSliceProperty(state, seqBox, "actionSeq");

  function nextSeq() {
    seqBox.actionSeq += 1;
    return seqBox.actionSeq;
  }

  function reset() {
    slices.interopEpro.reset();
    slices.econsent.reset();
    slices.walletPayments.reset();
    seqBox.actionSeq = 0;
  }

  reset();

  return {
    slices,
    state,
    nextSeq,
    reset,
  };
}

export function createPortalDemoHandlers(store = createPortalDemoStore()) {
  const nextSeq = () => {
    if (typeof store.nextSeq === "function") {
      return store.nextSeq();
    }
    store.state.actionSeq += 1;
    return store.state.actionSeq;
  };

  const econsentSlice = store.slices?.econsent ?? { state: store.state };
  const interopEproSlice = store.slices?.interopEpro ?? { state: store.state };

  return [
    ...createEconsentSliceHandlers({
      http,
      HttpResponse,
      slice: econsentSlice,
      nextSeq,
    }),
    ...createInteropEproSliceHandlers({
      http,
      HttpResponse,
      slice: interopEproSlice,
      nextSeq,
    }),
  ];
}
