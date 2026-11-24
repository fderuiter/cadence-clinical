import {
  buildDemoHealthPayload,
  buildSignatureVerificationOutcome,
} from "demo-fixtures/mocks";
import { http, HttpResponse } from "msw";
import { isValidSemanticAction, isValidStepUpAction } from "ui";
import { evaluateDemoRequestPolicy } from "../demoPolicy.js";
import { readJsonBody } from "./shared.js";

export function createHealthAndAuthHandlers(store) {
  return [
    http.get("*/health", () =>
      HttpResponse.json(
        buildDemoHealthPayload({ surface: "mode", value: "demo" })
      )
    ),
    http.get("*/api/v1/kernel/health", () =>
      HttpResponse.json({ status: "healthy", module: "kernel-demo" })
    ),
    http.get("*/api/v1/execution/health", () =>
      HttpResponse.json({
        status: "healthy",
        service: "execution-demo",
        version: "1.0.0",
      })
    ),
    http.all("*/api/*", async ({ request }) => {
      const denied = await evaluateDemoRequestPolicy(request, store);
      if (denied) {
        return HttpResponse.json(denied.problem, {
          status: denied.status,
          headers: { "Content-Type": "application/problem+json" },
        });
      }
      return undefined;
    }),
    http.post("*/api/v1/auth/signature-verification", async ({ request }) => {
      const body = await readJsonBody(request);
      const outcome = buildSignatureVerificationOutcome(body, {
        validateAction: isValidStepUpAction,
        validateSemanticAction: isValidSemanticAction,
        requireSemanticAction: true,
        tokenPrefix: "demo_sig_tok_",
        timestampMs: Date.now(),
      });
      return HttpResponse.json(outcome.body, { status: outcome.status });
    }),
  ];
}
