export interface ChromiumExecutableProvider {
  executablePath(): string;
}

export interface BuildChromiumLaunchOptionsInput {
  args?: string[];
  executablePath?: string;
  platform?: NodeJS.Platform;
  env?: NodeJS.ProcessEnv;
  wrapperPath?: string;
}

export interface ChromiumLaunchOptionsResult {
  args?: string[];
  executablePath?: string;
}

export function buildChromiumLaunchOptions(
  chromiumBrowserType: ChromiumExecutableProvider,
  baseOptions?: BuildChromiumLaunchOptionsInput
): ChromiumLaunchOptionsResult;
