export interface DemoEnvLike {
  readonly MODE?: string;
  readonly VITE_DEMO_MODE?: string;
  readonly [key: string]: unknown;
}

export interface DemoRuntimeOptions {
  readonly isDemoActive?: () => boolean;
  readonly epochIso?: string | (() => string);
  readonly epochMs?: number | (() => number);
  readonly defaultTokenLength?: number;
  readonly seedNamespace?: string;
}

export interface DemoRuntime {
  createDemoPrng: (seedInput?: string | number) => () => number;
  resetDemoRandom: (streamKey?: string) => void;
  nextDemoStep: (counterKey?: string) => number;
  demoNowMs: (stepOffset?: number, stepSizeMs?: number) => number;
  demoNowDate: (stepOffset?: number, stepSizeMs?: number) => Date;
  demoNowIso: (stepOffset?: number, stepSizeMs?: number) => string;
  demoRandom: (streamKey?: string) => number;
  demoRandomInt: (
    min: number,
    maxInclusive: number,
    streamKey?: string
  ) => number;
  demoRandomToken: (
    length?: number,
    radix?: number,
    streamKey?: string
  ) => string;
}

export interface UnbackedActionRefusalContext {
  readonly action: string;
  readonly title: string;
  readonly detail: string;
  readonly message: string;
  readonly customDetail: string | null;
}

export interface UnbackedActionGuardOptions<TRefusalResult = void> {
  readonly isAllowed?: (env?: DemoEnvLike) => boolean;
  readonly defaultDetail?: string;
  readonly onRefuse?: (context: UnbackedActionRefusalContext) => TRefusalResult;
}

export interface UnbackedActionGuard<TRefusalResult = void> {
  allowUnbackedAction: (action: string, env?: DemoEnvLike) => boolean;
  refuseUnbackedAction: (
    action: string,
    customDetail?: string | null
  ) => TRefusalResult;
  unbackedActionsEnabled: (env?: DemoEnvLike) => boolean;
}

export declare function isDemoBuild(env?: DemoEnvLike | null): boolean;
export declare function createDemoRuntime(
  options?: DemoRuntimeOptions
): DemoRuntime;
export declare function createUnbackedActionGuard<TRefusalResult = string>(
  options?: UnbackedActionGuardOptions<TRefusalResult>
): UnbackedActionGuard<TRefusalResult>;
