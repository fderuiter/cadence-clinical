import { afterEach, beforeEach } from "vitest";
import { setupServer } from "msw/node";
import { createWebDemoStore } from "../../../src/mocks/demoStore.js";
import { buildWebGatewayHandlers } from "../../../src/mocks/handlers.js";

export const DEMO_HEADERS = {
  Authorization: "Bearer demo-token-001",
  "Content-Type": "application/json",
};

export function useDemoSliceServer() {
  const ctx = {
    store: null,
    server: null,
    origin: window.location.origin,
    fetchPath(path, init = {}) {
      const headers = {
        ...DEMO_HEADERS,
        ...(init.headers || {}),
      };
      return fetch(`${ctx.origin}${path}`, {
        ...init,
        headers,
      });
    },
    async requestJson(path, init = {}) {
      const res = await ctx.fetchPath(path, init);
      const data = await res.json();
      return { res, status: res.status, data };
    },
  };

  beforeEach(() => {
    ctx.store = createWebDemoStore();
    ctx.server = setupServer(
      ...buildWebGatewayHandlers({ forceDemo: true, store: ctx.store })
    );
    ctx.server.listen({ onUnhandledRequest: "error" });
  });

  afterEach(() => {
    ctx.server?.close();
  });

  return ctx;
}
