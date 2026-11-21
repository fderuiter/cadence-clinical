import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SINGLE_CORE_WRAPPER_PATH = path.resolve(
  __dirname,
  "playwright-chromium-single-core.sh"
);

/**
 * Builds Playwright Chromium `launchOptions` that pin the Chromium browser
 * process tree to a single allowed CPU core on Linux hosts (`taskset`).
 *
 * On virtualized multi-vCPU Linux runners (GitHub Actions Azure VMs / KVM),
 * microsecond cross-vCPU `CLOCK_MONOTONIC` skew triggers a fatal `CHECK`
 * (`int3`/`ud2`) in Chromium 156's `Chrome_ChildIOT` (`NetworkService`),
 * aborting in-flight JS/CSS subresource requests with `net::ERR_ABORTED`.
 *
 * @param {{ executablePath: () => string }} chromiumBrowserType
 * @param {{ args?: string[], executablePath?: string, platform?: string, env?: NodeJS.ProcessEnv, wrapperPath?: string }} [baseOptions]
 * @returns {{ args?: string[], executablePath?: string }}
 */
export function buildChromiumLaunchOptions(
  chromiumBrowserType,
  baseOptions = {}
) {
  const {
    args,
    executablePath: explicitExecutablePath,
    platform = process.platform,
    env = process.env,
    wrapperPath = SINGLE_CORE_WRAPPER_PATH,
  } = baseOptions;

  const launchOptions = {};
  if (args && args.length > 0) {
    launchOptions.args = [...args];
  }

  const resolvedBinary =
    explicitExecutablePath ||
    env.PLAYWRIGHT_CHROMIUM_PATH ||
    resolveDefaultChromiumExecutable(chromiumBrowserType);

  if (
    platform === "linux" &&
    resolvedBinary &&
    fs.existsSync(resolvedBinary) &&
    fs.existsSync(wrapperPath)
  ) {
    env.CADENCE_REAL_CHROMIUM_BIN = resolvedBinary;
    launchOptions.executablePath = wrapperPath;
    return launchOptions;
  }

  if (explicitExecutablePath) {
    launchOptions.executablePath = explicitExecutablePath;
  } else if (env.PLAYWRIGHT_CHROMIUM_PATH) {
    launchOptions.executablePath = env.PLAYWRIGHT_CHROMIUM_PATH;
  }

  return launchOptions;
}

function resolveDefaultChromiumExecutable(chromiumBrowserType) {
  try {
    return chromiumBrowserType.executablePath();
  } catch {
    return undefined;
  }
}
