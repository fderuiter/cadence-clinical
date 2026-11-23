import { http, HttpResponse, passthrough } from "msw";
import { evaluateDemoBoundaryRequest } from "ui/testing";
import { isDemoBuild } from "../utils/demoMode.js";
import {
  createPortalDemoHandlers,
  createPortalDemoStore,
} from "./demoHandlers.js";
import {
  createWalletPaymentsSliceHandlers,
  createWalletPaymentsSliceStore,
} from "./slices/wallet-payments.js";

export function createPortalGatewayInstance(options = {}) {
  const store = options.store ?? createPortalDemoStore();
  const walletSlice =
    store.slices?.walletPayments ?? createWalletPaymentsSliceStore();

  const demoActive =
    options.forceDemo !== undefined
      ? Boolean(options.forceDemo)
      : isDemoBuild();

  const walletActionHandlers = createWalletPaymentsSliceHandlers({
    http,
    HttpResponse,
    slice: walletSlice,
  });

  const demoHandlers = demoActive ? createPortalDemoHandlers(store) : [];

  const boundaryHandler = http.all("*", async ({ request }) => {
    const result = await evaluateDemoBoundaryRequest(request, {
      demoActive,
      appLabel: "portal demo",
    });

    if (result.passthrough) {
      return passthrough();
    }

    return HttpResponse.json(result.body, {
      status: result.status,
      headers: result.headers,
    });
  });

  return {
    store,
    handlers: [...walletActionHandlers, ...demoHandlers, boundaryHandler],
    reset: () => store.reset(),
  };
}

export function buildPortalGatewayHandlers(options = {}) {
  return createPortalGatewayInstance(options).handlers;
}

const defaultGatewayInstance = createPortalGatewayInstance();

export function resetMockWalletLedger() {
  defaultGatewayInstance.store.reset();
}

export const gatewayHandlers = defaultGatewayInstance.handlers;
