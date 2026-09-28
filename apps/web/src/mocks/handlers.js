import { http, HttpResponse, passthrough } from "msw";
import { validateGatewayRequest } from "ui";

export const gatewayHandlers = [
  // Intercept all HTTP methods matching API gateway host or API paths
  http.all("*", async ({ request }) => {
    const url = new URL(request.url);

    // Only intercept API / service calls targeting gateway or non-asset paths
    const isApiCall =
      url.port === "8000" ||
      url.host.includes("localhost:8000") ||
      url.pathname.startsWith("/api/") ||
      url.pathname.startsWith("/designer/") ||
      url.pathname.startsWith("/execution/") ||
      url.pathname.startsWith("/etmf/") ||
      url.pathname.startsWith("/interop/") ||
      url.pathname.startsWith("/ctms/") ||
      url.pathname.startsWith("/notifications/") ||
      url.pathname.startsWith("/quality/") ||
      url.pathname.startsWith("/safety/") ||
      url.pathname.startsWith("/tickets/") ||
      url.pathname.startsWith("/eisf/") ||
      url.pathname.startsWith("/org/") ||
      url.pathname.startsWith("/econsent/") ||
      url.pathname.startsWith("/terminology/") ||
      url.pathname.startsWith("/invalid-prefix/");

    if (!isApiCall) {
      return passthrough();
    }

    const validation = await validateGatewayRequest(request);

    if (!validation.valid) {
      return HttpResponse.json(validation.body, {
        status: validation.status,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Default passthrough for valid gateway requests in live dev/mock mode
    return passthrough();
  }),
];
