export interface DemoHealthOptions {
  surface?: string;
  value?: string;
}

export interface DemoSignatureVerificationOptions {
  validateAction?: ((action: unknown) => boolean) | null;
  validateSemanticAction?: ((semanticAction: unknown) => boolean) | null;
  requireSemanticAction?: boolean;
  tokenPrefix?: string;
  defaultAction?: string;
  timestampMs?: number;
}

export interface DemoSignatureVerificationOutcome {
  status: number;
  body: Record<string, unknown>;
}

export interface DemoAmendmentReconsentOptions {
  status?: string;
  recordedAt?: string;
}

export interface DemoReceiptOcrFallback {
  merchantName: string;
  expenseDate: string;
  grossAmount: number;
  category: string;
  confidenceScore?: number;
}

export interface DemoReceiptOcrOptions {
  issueToken?: boolean;
  allowTextOnlyToken?: boolean;
  fallbackExtracted?: DemoReceiptOcrFallback | null;
  ocrTokens?: Map<string, Record<string, unknown>> | null;
  tokenPrefix?: string;
  nextTimestampMs?: () => number;
}

export interface DemoReceiptOcrPayload {
  ocrReadable: boolean;
  ocr_readable: boolean;
  merchantName: string;
  merchant_name: string;
  expenseDate: string;
  expense_date: string;
  grossAmount: number;
  gross_amount: number;
  taxDeduction: number;
  tax_deduction: number;
  netAmount: number;
  net_amount: number;
  category: string;
  confidenceScore: number;
  confidence_score: number;
  rawText: string;
  raw_text: string;
  ocrToken: string | null;
  ocr_token: string | null;
  receiptSha256: string | null;
  receipt_sha256: string | null;
  warning: string | null;
}

export interface DemoExpenseEvaluationOptions {
  wallet?: Record<string, unknown> | null;
  ocrTokens?: Map<string, Record<string, unknown>> | null;
  consumedOcrTokens?: Set<string> | null;
  recordedReceiptDigests?: Set<string> | null;
  claimPrefix?: string;
  defaultSubjectId?: string;
  defaultMerchantName?: string;
  defaultExpenseDate?: string;
  nextTimestampMs?: () => number;
  nextTimestampIso?: () => string;
}

export declare function buildDemoHealthPayload(
  options?: DemoHealthOptions
): Record<string, string>;

export declare function buildSignatureVerificationOutcome(
  body?: Record<string, unknown>,
  options?: DemoSignatureVerificationOptions
): DemoSignatureVerificationOutcome;

export declare function buildAmendmentReconsentPayload(
  options?: DemoAmendmentReconsentOptions
): Record<string, string>;

export declare function computeMockReceiptDigest(
  rawBytesText?: string | null
): string | null;

export declare function decodeReceiptImageText(
  imageBase64?: string | null
): string;

export declare function extractDemoReceiptOcr(
  body?: Record<string, unknown>,
  options?: DemoReceiptOcrOptions
): DemoReceiptOcrPayload;

export declare function evaluateDemoExpenseClaim(
  body?: Record<string, unknown>,
  options?: DemoExpenseEvaluationOptions
): Record<string, unknown>;
