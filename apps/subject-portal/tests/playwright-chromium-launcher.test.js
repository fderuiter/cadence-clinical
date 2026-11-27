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
  it("wraps existing Chromium executable on Linux and sets CADENCE_REAL_CHROMIUM_BIN", () => {
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

    expect(options.executablePath).toBe(wrapperScript);
    expect(options.args).toEqual(["--no-sandbox", "--disable-dev-shm-usage"]);
    expect(env.CADENCE_REAL_CHROMIUM_BIN).toBe(process.execPath);
  });

  it("honors PLAYWRIGHT_CHROMIUM_PATH override before default browserType executablePath", () => {
    const env = { PLAYWRIGHT_CHROMIUM_PATH: process.execPath };
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

  it("falls back gracefully on non-Linux platforms or missing binaries", () => {
    const darwinOptions = buildChromiumLaunchOptions(
      { executablePath: () => process.execPath },
      {
        args: ["--no-sandbox"],
        platform: "darwin",
        env: {},
        wrapperPath: wrapperScript,
      }
    );
    expect(darwinOptions.executablePath).toBeUndefined();
    expect(darwinOptions.args).toEqual(["--no-sandbox"]);

    const missingOptions = buildChromiumLaunchOptions(
      { executablePath: () => "/nonexistent/chromium-binary" },
      {
        platform: "linux",
        env: {},
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

  it("distributes parallel Playwright workers across distinct allowed CPU cores while keeping each pinned to one core", () => {
    if (process.platform !== "linux") {
      return;
    }

    const probeScript =
      "import('node:child_process').then(({ execFileSync }) => { try { process.stdout.write(execFileSync('taskset', ['-cp', String(process.pid)], { encoding: 'utf8' })); } catch { process.stdout.write('no-taskset'); } })";

    const hostAffinityRaw = (() => {
      try {
        return execFileSync("taskset", ["-cp", String(process.pid)], {
          encoding: "utf8",
        }).trim();
      } catch {
        return "no-taskset";
      }
    })();

    if (hostAffinityRaw === "no-taskset") {
      return;
    }

    const worker0Out = execFileSync(wrapperScript, ["-e", probeScript], {
      encoding: "utf8",
      env: {
        ...process.env,
        CADENCE_REAL_CHROMIUM_BIN: process.execPath,
        TEST_PARALLEL_INDEX: "0",
      },
    }).trim();
    const worker1Out = execFileSync(wrapperScript, ["-e", probeScript], {
      encoding: "utf8",
      env: {
        ...process.env,
        CADENCE_REAL_CHROMIUM_BIN: process.execPath,
        TEST_PARALLEL_INDEX: "1",
      },
    }).trim();

    const core0 = worker0Out.split(":").pop()?.trim() ?? "";
    const core1 = worker1Out.split(":").pop()?.trim() ?? "";
    expect(core0).toMatch(/^\d+$/);
    expect(core1).toMatch(/^\d+$/);

    const hostRange = hostAffinityRaw.split(":").pop()?.trim() ?? "";
    if (hostRange.includes("-") || hostRange.includes(",")) {
      expect(core1).not.toBe(core0);
    }
  });
});
