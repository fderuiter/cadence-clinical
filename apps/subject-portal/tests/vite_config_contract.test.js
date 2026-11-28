// @vitest-environment node
import path from "node:path";
import { fileURLToPath } from "node:url";
import { resolveConfig } from "vite";
import { afterEach, describe, expect, it } from "vitest";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(__dirname, "..");
const configFile = path.resolve(appRoot, "vite.config.js");

const ENV_KEYS = [
  "CADENCE_GATEWAY_URL",
  "VITE_API_BASE_URL",
  "VITE_OIDC_AUTHORITY",
];

describe("apps/subject-portal Vite URL and proxy contract", () => {
  const savedEnv = Object.fromEntries(
    ENV_KEYS.map((key) => [key, process.env[key]])
  );

  afterEach(() => {
    for (const key of ENV_KEYS) {
      if (savedEnv[key] === undefined) {
        delete process.env[key];
      } else {
        process.env[key] = savedEnv[key];
      }
    }
  });

  it("omits localhost defaults and proxy in production builds when gateway env vars are unset", async () => {
    delete process.env.CADENCE_GATEWAY_URL;
    delete process.env.VITE_API_BASE_URL;
    delete process.env.VITE_OIDC_AUTHORITY;

    const resolved = await resolveConfig(
      { configFile, root: appRoot },
      "build",
      "production",
      "production"
    );

    expect(resolved.server.proxy).toBeUndefined();
    expect(
      JSON.stringify({
        proxy: resolved.server.proxy ?? null,
        define: resolved.define ?? null,
      })
    ).not.toContain("localhost");
  });

  it("restricts localhost:8000 proxy and secure:false to development serve mode, and enforces secure:true outside development", async () => {
    delete process.env.CADENCE_GATEWAY_URL;
    delete process.env.VITE_API_BASE_URL;

    const devServe = await resolveConfig(
      { configFile, root: appRoot },
      "serve",
      "development",
      "development"
    );
    expect(devServe.server.proxy?.["/api"]?.target).toBe(
      "http://localhost:8000"
    );
    expect(devServe.server.proxy?.["/api"]?.secure).toBe(false);

    const stagingServeUnset = await resolveConfig(
      { configFile, root: appRoot },
      "serve",
      "staging",
      "production"
    );
    expect(stagingServeUnset.server.proxy).toBeUndefined();

    process.env.CADENCE_GATEWAY_URL = "https://gateway.staging.cadence.example";
    const stagingServeConfigured = await resolveConfig(
      { configFile, root: appRoot },
      "serve",
      "staging",
      "production"
    );
    expect(stagingServeConfigured.server.proxy?.["/api"]?.target).toBe(
      "https://gateway.staging.cadence.example"
    );
    expect(stagingServeConfigured.server.proxy?.["/api"]?.secure).toBe(true);
  });
});
