import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { buildChromiumLaunchOptions } from "../../../scripts/playwright-chromium-launcher.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const wrapperScript = path.resolve(
  __dirname,
  "../../../scripts/playwright-chromium-single-core.sh"
);

describe("Playwright single-core Chromium launcher", () => {
  it("leaves local Linux launches unpinned when neither CI nor CADENCE_PIN_CHROMIUM_SINGLE_CORE=1 is set", () => {
    const env = {};
    const options = buildChromiumLaunchOptions(
      { executablePath: () => process.execPath },
      {
        args: ["--no-sandbox", "--disable-dev-shm-usage"],
        platform: "linux",
        env,
        wrapperPath: wrapperScript,
      }
    );

    expect(options.executablePath).toBeUndefined();
    expect(options.args).toEqual(["--no-sandbox", "--disable-dev-shm-usage"]);
    expect(env.CADENCE_REAL_CHROMIUM_BIN).toBeUndefined();

    const falseCiEnv = {
      CI: "false",
      PLAYWRIGHT_CHROMIUM_PATH: process.execPath,
    };
    const overrideOptions = buildChromiumLaunchOptions(
      { executablePath: () => "/nonexistent/default" },
      {
        platform: "linux",
        env: falseCiEnv,
        wrapperPath: wrapperScript,
      }
    );
    expect(overrideOptions.executablePath).toBe(process.execPath);
    expect(falseCiEnv.CADENCE_REAL_CHROMIUM_BIN).toBeUndefined();
  });

  it("wraps existing Chromium executable on Linux when CI=true and sets CADENCE_REAL_CHROMIUM_BIN", () => {
    const env = { CI: "true" };
    const options = buildChromiumLaunchOptions(
      { executablePath: () => process.execPath },
      {
        args: ["--no-sandbox", "--disable-dev-shm-usage"],
        platform: "linux",
        env,
        wrapperPath: wrapperScript,
      }
    );

    expect(options.executablePath).toBe(wrapperScript);
    expect(options.args).toEqual(["--no-sandbox", "--disable-dev-shm-usage"]);
    expect(env.CADENCE_REAL_CHROMIUM_BIN).toBe(process.execPath);
  });

  it("wraps Chromium on Linux when CADENCE_PIN_CHROMIUM_SINGLE_CORE=1 and honors PLAYWRIGHT_CHROMIUM_PATH", () => {
    const env = {
      CADENCE_PIN_CHROMIUM_SINGLE_CORE: "1",
      PLAYWRIGHT_CHROMIUM_PATH: process.execPath,
    };
    const options = buildChromiumLaunchOptions(
      {
        executablePath: () => {
          throw new Error("should not query browserType when env override set");
        },
      },
      {
        platform: "linux",
        env,
        wrapperPath: wrapperScript,
      }
    );

    expect(options.executablePath).toBe(wrapperScript);
    expect(env.CADENCE_REAL_CHROMIUM_BIN).toBe(process.execPath);
  });

  it("falls back gracefully on non-Linux platforms or missing binaries even under CI=true", () => {
    const darwinOptions = buildChromiumLaunchOptions(
      { executablePath: () => process.execPath },
      {
        args: ["--no-sandbox"],
        platform: "darwin",
        env: { CI: "true" },
        wrapperPath: wrapperScript,
      }
    );
    expect(darwinOptions.executablePath).toBeUndefined();
    expect(darwinOptions.args).toEqual(["--no-sandbox"]);

    const missingOptions = buildChromiumLaunchOptions(
      { executablePath: () => "/nonexistent/chromium-binary" },
      {
        platform: "linux",
        env: { CI: "true" },
        wrapperPath: wrapperScript,
      }
    );
    expect(missingOptions.executablePath).toBeUndefined();
  });

  it("executes target binary pinned to a single CPU core when taskset is available on Linux", () => {
    if (process.platform !== "linux") {
      return;
    }

    const output = execFileSync(
      wrapperScript,
      [
        "-e",
        "import('node:child_process').then(({ execFileSync }) => { try { process.stdout.write(execFileSync('taskset', ['-cp', String(process.pid)], { encoding: 'utf8' })); } catch { process.stdout.write('no-taskset'); } })",
      ],
      {
        encoding: "utf8",
        env: {
          ...process.env,
          CADENCE_REAL_CHROMIUM_BIN: process.execPath,
        },
      }
    ).trim();

    if (output !== "no-taskset") {
      const affinityList = output.split(":").pop()?.trim() ?? "";
      expect(affinityList).toMatch(/^\d+$/);
    }
  });
});
