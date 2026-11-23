export interface DemoMswAdapter {
  readonly http: {
    get(
      path: string,
      resolver: (info: {
        request: Request;
        params: Record<string, string>;
      }) => unknown
    ): unknown;
    post(
      path: string,
      resolver: (info: {
        request: Request;
        params: Record<string, string>;
      }) => unknown
    ): unknown;
  };
  readonly HttpResponse: {
    json(
      body: unknown,
      init?: { status?: number; headers?: HeadersInit }
    ): Response;
  };
}

export declare function decodeReceiptBase64ToText(
  imageBase64?: string | null
): string;

export declare function parseDemoReceiptContent(
  body?: Record<string, unknown>,
  options?: {
    fallbackOnPlainText?: boolean;
    emptyWarning?: string;
  }
): Record<string, unknown>;

export declare function evaluateDemoExpenseSubmission(
  body?: Record<string, unknown>,
  options?: {
    wallet?: Record<string, unknown> | null;
    nextTimestampMs?: () => number;
    nextTimestampIso?: () => string;
    idPrefix?: string;
    defaultSubjectId?: string;
    defaultMerchantName?: string;
    defaultExpenseDate?: string;
  }
): Record<string, unknown>;

export declare function createDemoHealthRoute(
  msw: DemoMswAdapter,
  payload?: Record<string, unknown> | (() => Record<string, unknown>)
): unknown;

export declare function createDemoSignatureVerificationRoute(
  msw: DemoMswAdapter,
  options?: {
    validatePayload?: ((body: Record<string, unknown>) => boolean) | null;
    createToken?: (body: Record<string, unknown>) => string;
    defaultAction?: string;
  }
): unknown;

export declare function createDemoAmendmentReconsentRoute(
  msw: DemoMswAdapter,
  options?: {
    buildResponse?: (
      body: Record<string, unknown>,
      request: Request
    ) => Record<string, unknown> | Promise<Record<string, unknown>>;
  }
): unknown;

export declare function createDemoParticipantExpenseOcrRoute(
  msw: DemoMswAdapter,
  options?: {
    path?: string;
    fallbackOnPlainText?: boolean;
    emptyWarning?: string;
  }
): unknown;

export declare function createDemoParticipantExpenseSubmitRoute(
  msw: DemoMswAdapter,
  options?: {
    path?: string;
    status?: number;
    resolveWallet?:
      | ((
          body: Record<string, unknown>,
          request: Request
        ) => Record<string, unknown> | null)
      | null;
    getIdempotencyHit?:
      | ((ctx: {
          body: Record<string, unknown>;
          request: Request;
          wallet: Record<string, unknown> | null;
        }) => Record<string, unknown> | null)
      | null;
    onEvaluated?:
      | ((
          evaluated: Record<string, unknown>,
          ctx: {
            body: Record<string, unknown>;
            request: Request;
            wallet: Record<string, unknown> | null;
          }
        ) => void)
      | null;
    nextTimestampMs?: () => number;
    nextTimestampIso?: () => string;
    idPrefix?: string;
    defaultSubjectId?: string;
    defaultMerchantName?: string;
    defaultExpenseDate?: string;
  }
): unknown;
