import { http, HttpResponse, passthrough } from "msw";
import { evaluateDemoBoundaryRequest } from "ui/testing";
import { isDemoBuild } from "../utils/demoMode.js";
import { createWebDemoHandlers } from "./demoHandlers.js";

export function createFailClosedBoundaryHandler(options = {}) {
  return http.all("*", async ({ request }) => {
    const demoActive =
      options.forceDemo !== undefined
        ? Boolean(options.forceDemo)
        : isDemoBuild();

    const outcome = await evaluateDemoBoundaryRequest(request, {
      demoActive,
      appLabel: "web demo",
    });

    if (outcome.passthrough) {
      return passthrough();
    }

    return HttpResponse.json(outcome.body, {
      status: outcome.status,
      headers: outcome.headers,
    });
  });
}

export function buildWebGatewayHandlers(options = {}) {
  const demoActive =
    options.forceDemo !== undefined
      ? Boolean(options.forceDemo)
      : isDemoBuild();
  const specificHandlers = demoActive
    ? createWebDemoHandlers(options.store)
    : [];
  return [...specificHandlers, createFailClosedBoundaryHandler(options)];
}

export const gatewayHandlers = buildWebGatewayHandlers();
