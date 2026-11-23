import { http, HttpResponse } from "msw";
import { webDemoStore } from "./demoStore.js";
import { createCtmsSsuSliceHandlers } from "./slices/ctms-ssu.js";
import { createEtmfDocumentsSliceHandlers } from "./slices/etmf-documents.js";
import { createExecutionEcrfSliceHandlers } from "./slices/execution-ecrf.js";
import { createKernelSliceHandlers } from "./slices/kernel.js";
import { createKnowledgeTicketsSliceHandlers } from "./slices/knowledge-tickets.js";
import { createPaymentsWalletSliceHandlers } from "./slices/payments-wallet.js";
import { createProtocolSoaSliceHandlers } from "./slices/protocol-soa.js";
import { createSafetyLabsDctSliceHandlers } from "./slices/safety-labs-dct.js";
import { createTrainingLocksSliceHandlers } from "./slices/training-locks.js";

const mswDeps = { http, HttpResponse };

export function createWebDemoHandlers(store = webDemoStore) {
  return [
    ...createKernelSliceHandlers(store, mswDeps),
    ...createTrainingLocksSliceHandlers(store, mswDeps),
    ...createExecutionEcrfSliceHandlers(store, mswDeps),
    ...createCtmsSsuSliceHandlers(store, mswDeps),
    ...createKnowledgeTicketsSliceHandlers(store, mswDeps),
    ...createEtmfDocumentsSliceHandlers(store, mswDeps),
    ...createPaymentsWalletSliceHandlers(store, mswDeps),
    ...createSafetyLabsDctSliceHandlers(store, mswDeps),
    ...createProtocolSoaSliceHandlers(store, mswDeps),
  ];
}
