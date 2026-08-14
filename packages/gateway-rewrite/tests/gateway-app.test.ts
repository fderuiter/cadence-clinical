import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { NestFactory } from "@nestjs/core";
import { FastifyAdapter, NestFastifyApplication } from "@nestjs/platform-fastify";
import { AppModule } from "../src/app.module.js";

describe("NestJS-Fastify Gateway Application Integration Tests", () => {
  let app: NestFastifyApplication;
  let serverUrl: string;

  beforeAll(async () => {
    // Disable eager prefetching in tests to prevent hanging/network calls
    process.env.SKIP_JWKS_FETCH = "true";
    process.env.PORT = "0"; // Bind to dynamic random port

    app = await NestFactory.create<NestFastifyApplication>(
      AppModule,
      new FastifyAdapter()
    );

    await app.listen(0, "127.0.0.1");
    serverUrl = await app.getUrl();
  });

  afterAll(async () => {
    if (app) {
      await app.close();
    }
  });

  it("should return ok status on health check endpoint", async () => {
    const res = await fetch(`${serverUrl}/health`);
    expect(res.status).toBe(200);
    const json = await res.json() as any;
    expect(json).toEqual({ status: "ok", service: "gateway" });
  });

  it("should successfully generate a demo session token with default values", async () => {
    const res = await fetch(`${serverUrl}/api/v1/auth/demo`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({}),
    });
    expect(res.status).toBe(200);
    const json = await res.json() as any;
    expect(json.access_token).toBeDefined();
    expect(json.token_type).toBe("Bearer");
    expect(json.tenant_id).toBe("sandbox-tenant-default");
    expect(json.username).toBe("demo-user");
    expect(json.roles).toContain("admin");
  });

  it("should enforce sandbox prefix on demo session tenant_id", async () => {
    const res = await fetch(`${serverUrl}/api/v1/auth/demo`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tenant_id: "custom-tenant" }),
    });
    expect(res.status).toBe(200);
    const json = await res.json() as any;
    expect(json.tenant_id).toBe("sandbox-custom-tenant");
  });

  it("should reject proxy requests with missing Authorization header", async () => {
    const res = await fetch(`${serverUrl}/designer/api/v1/studies`);
    expect(res.status).toBe(401);
    const json = await res.json() as any;
    expect(json.detail).toContain("Authorization");
  });
});
