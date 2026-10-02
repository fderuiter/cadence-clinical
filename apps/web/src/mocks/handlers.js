import { http, HttpResponse, passthrough } from "msw";
import { isGatewayApiCall, validateGatewayRequest } from "ui";

export const gatewayHandlers = [
  // Intercept all HTTP methods matching API gateway host or API paths
  http.all("*", async ({ request }) => {
    const url = new URL(request.url);

    // Only intercept API / service calls targeting gateway or non-asset paths
    const isApiCall = isGatewayApiCall(url);

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
