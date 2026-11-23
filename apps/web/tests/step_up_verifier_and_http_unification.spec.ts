import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createPinia, setActivePinia } from "pinia";
import { sha256 } from "ui";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ApiError as LegacyClientApiError,
  apiClient,
} from "../src/api/apiClient";
import {
  fetchWithUnauthorizedRetry,
  installAuthorizedFetch,
} from "../src/api/authorization.js";
import {
  ApiError as OpenApiClientError,
  createOpenApiClient,
  getBaseUrl,
  unwrapResponse,
} from "../src/api/client";
import { econsentService } from "../src/api/econsent";
import { etmfService } from "../src/api/etmf";
import { signatureApi } from "../src/api/signatureApi";
import { soaClient } from "../src/api/soaClient";
import { setKeycloakClient } from "../src/auth/keycloak";
import { useAuthStore } from "../src/stores/auth";
import {
  ApiError as CanonicalApiError,
  ensureOk,
} from "../src/utils/apiErrors.js";
import { resolveGatewayBaseUrl } from "../src/utils/demoMode.js";
import { delegationBatchBindingId } from "../src/utils/signatureBinding";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SRC_ROOT = path.resolve(__dirname, "../src");

describe("Shared step-up verifier, modal decoupling, and unified web HTTP transport (ADR-2651)", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    setKeycloakClient(null);
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
    setKeycloakClient(null);
  });

  it("shares a single ApiError class across apiErrors.js, client.ts, and apiClient.js", async () => {
    expect(OpenApiClientError).toBe(CanonicalApiError);
    expect(LegacyClientApiError).toBe(CanonicalApiError);

    await expect(
      ensureOk(
        new Response(JSON.stringify({ detail: "Bad input" }), {
          status: 400,
          statusText: "Bad Request",
          headers: { "Content-Type": "application/json" },
        })
      )
    ).rejects.toBeInstanceOf(CanonicalApiError);

    vi.spyOn(globalThis, "fetch").mockImplementation(
      async () =>
        new Response(JSON.stringify({ detail: "Conflict" }), {
          status: 409,
          statusText: "Conflict",
          headers: { "Content-Type": "application/json" },
        })
    );

    const typedClient = createOpenApiClient<{
      "/api/v1/kernel/studies": {
        get: {
          responses: { 200: { content: { "application/json": unknown } } };
        };
      };
    }>("http://localhost:8000");

    let openApiErr: unknown;
    try {
      await unwrapResponse(typedClient.GET("/api/v1/kernel/studies"));
    } catch (err) {
      openApiErr = err;
    }
    expect(openApiErr).toBeInstanceOf(OpenApiClientError);
    expect(openApiErr).toBeInstanceOf(LegacyClientApiError);

    let legacyErr: unknown;
    try {
      await apiClient.get("/api/v1/notifications");
    } catch (err) {
      legacyErr = err;
    }
    expect(legacyErr).toBeInstanceOf(OpenApiClientError);
    expect(legacyErr).toBeInstanceOf(LegacyClientApiError);
  });

  it("delegates client.ts getBaseUrl to resolveGatewayBaseUrl and shares fetchWithUnauthorizedRetry with installAuthorizedFetch", async () => {
    vi.stubEnv("VITE_API_BASE_URL", "https://gateway.cadence.example");
    expect(getBaseUrl()).toBe(resolveGatewayBaseUrl());

    const keycloak = {
      authenticated: true,
      token: "stale-access-token",
      refreshToken: "refresh-token-1",
      tokenParsed: {
        sub: "user-1",
        preferred_username: "pi.site101",
        tenant_id: "cadence-local",
        realm_access: { roles: ["Site Investigator"] },
      },
      updateToken: vi.fn(async (minValidity: number) => {
        if (minValidity >= 0) return false;
        keycloak.token = "fresh-access-token";
        return true;
      }),
    };
    setKeycloakClient(keycloak);
    useAuthStore().setAuth(keycloak);

    const seen: Array<{ auth: string | null; body: string }> = [];
    const target = {
      fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const reqAuth =
          init?.headers instanceof Headers
            ? init.headers.get("Authorization")
            : input instanceof Request
              ? input.headers.get("Authorization")
              : null;
        const bodyText =
          input instanceof Request
            ? await input.clone().text()
            : String(init?.body ?? "");
        seen.push({ auth: reqAuth, body: bodyText });
        if (reqAuth === "Bearer stale-access-token") {
          return new Response(JSON.stringify({ detail: "Expired" }), {
            status: 401,
            headers: { "Content-Type": "application/json" },
          });
        }
        return new Response(JSON.stringify({ saved: true }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }),
    };

    installAuthorizedFetch(target);
    const postRequest = new Request(
      "https://gateway.cadence.example/api/v1/execution/forms",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ study_id: "CAD-101" }),
      }
    );
    const res = await target.fetch(postRequest);
    expect(await res.json()).toEqual({ saved: true });
    expect(seen).toEqual([
      { auth: "Bearer stale-access-token", body: '{"study_id":"CAD-101"}' },
      { auth: "Bearer fresh-access-token", body: '{"study_id":"CAD-101"}' },
    ]);

    // Step-up 401 failures must never trigger session refresh/logout
    keycloak.updateToken.mockClear();
    const stepUpRes = await fetchWithUnauthorizedRetry(
      "https://gateway.cadence.example/api/v1/auth/signature-verification",
      { method: "POST" },
      {
        fetchImpl: async () =>
          new Response(JSON.stringify({ detail: "Invalid credentials" }), {
            status: 401,
            headers: { "Content-Type": "application/json" },
          }),
      }
    );
    expect(stepUpRes.status).toBe(401);
    expect(keycloak.updateToken).not.toHaveBeenCalled();
  });

  it("routes signatureApi, etmfService, soaClient, and econsentService through verifyStepUpSignature and validates sig_token", async () => {
    const bodies: unknown[] = [];
    vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
      if (input instanceof Request) {
        bodies.push(await input.clone().json());
      }
      return new Response(
        JSON.stringify({ sig_token: "issued-token-xyz", expires_in: 60 }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }
      );
    });

    const batchRes = await signatureApi.verifySignature({
      username: "pi.site101",
      password: "Password!123", // pragma: allowlist secret
      totp: "654321",
      action: "/api/v1/training/delegations/bulk-signoff",
      semanticAction: "knowledge.delegation.sign_off",
      batchId: "batch-binding-99",
    });
    expect(batchRes.sig_token).toBe("issued-token-xyz");

    await etmfService.verifySignature({
      username: "pi.site101",
      password: "Password!123", // pragma: allowlist secret
      action: "/api/v1/etmf/documents/doc-7/sign-off",
    });
    await soaClient.verifySignature({
      username: "designer.lead",
      password: "Password!123", // pragma: allowlist secret
      action: "/api/v1/designer/soa/study-1/approve",
      batchId: "soa-batch-1",
    });
    await econsentService.verifySignature({
      username: "subject.site101",
      password: "Password!123", // pragma: allowlist secret
      action: "/api/v1/execution/amendments/reconsent",
    });

    expect(bodies).toEqual([
      {
        username: "pi.site101",
        password: "Password!123", // pragma: allowlist secret
        totp: "654321",
        action: "/api/v1/training/delegations/bulk-signoff",
        semantic_action: "knowledge.delegation.sign_off",
        batch_id: "batch-binding-99",
      },
      {
        username: "pi.site101",
        password: "Password!123", // pragma: allowlist secret
        action: "/api/v1/etmf/documents/doc-7/sign-off",
        semantic_action: "etmf.document.sign_off",
      },
      {
        username: "designer.lead",
        password: "Password!123", // pragma: allowlist secret
        action: "/api/v1/designer/soa/study-1/approve",
        semantic_action: "designer.soa.approve",
        batch_id: "soa-batch-1",
      },
      {
        username: "subject.site101",
        password: "Password!123", // pragma: allowlist secret
        action: "/api/v1/execution/amendments/reconsent",
        semantic_action: "econsent.reconsent.sign",
      },
    ]);

    vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify({ expires_in: 60 }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );
    await expect(
      signatureApi.verifySignature({
        username: "pi.site101",
        password: "Password!123", // pragma: allowlist secret
        action: "/api/v1/execution/forms/f-1/sign",
      })
    ).rejects.toThrow(
      "Signature verification did not return a signature token."
    );
  });

  it("keeps SignatureCaptureModal free of static etmf imports, ProtocolTrainingConsole free of soaClient imports, and reuses sha256 from ui", async () => {
    const outerModalSrc = fs.readFileSync(
      path.join(SRC_ROOT, "components/SignatureCaptureModal.vue"),
      "utf8"
    );
    const innerModalSrc = fs.readFileSync(
      path.join(
        SRC_ROOT,
        "features/signatures/components/SignatureCaptureModal.vue"
      ),
      "utf8"
    );
    const trainingConsoleSrc = fs.readFileSync(
      path.join(SRC_ROOT, "components/clinical/ProtocolTrainingConsole.vue"),
      "utf8"
    );
    const bindingSrc = fs.readFileSync(
      path.join(SRC_ROOT, "utils/signatureBinding.ts"),
      "utf8"
    );

    expect(outerModalSrc).not.toMatch(/from\s+["'][^"']*etmf["']/);
    expect(innerModalSrc).not.toMatch(/import\s+.*from\s+["'][^"']*etmf["']/);
    expect(trainingConsoleSrc).not.toMatch(/soaClient/);
    expect(bindingSrc).toMatch(/import\s*\{\s*sha256\s*\}\s*from\s*["']ui["']/);

    expect(await delegationBatchBindingId(["del-b", "del-a"])).toBe(
      await sha256("del-a,del-b")
    );
  });
});
