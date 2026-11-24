/**
 * Generated from docs/openapi/payments_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/healthz": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Site and participant payments health probe. */
    get: operations["PaymentsHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Payments operational status. */
    get: operations["PaymentsStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/disburse": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail. */
    post: operations["DisburseStipendDirect"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/disburse": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Disburse stipend to participant wallet. */
    post: operations["DisburseStipendWallet"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List all participant wallets. */
    get: operations["ListWallets"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/subject/{subjectId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve participant wallet balance and details. */
    get: operations["GetSubjectWallet"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve participant wallet details by wallet ID. */
    get: operations["GetWalletById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}/tax-1099-status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve IRS 1099 tax status and threshold monitoring tracer. */
    get: operations["GetTax1099Status"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}/balance": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Real-time balance inquiry for participant wallet. */
    get: operations["GetBalanceInquiry"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}/issue-card": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Issue or re-issue reloadable debit card for participant wallet. */
    post: operations["IssueReloadableCard"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}/submit-w9": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Submit Form W-9 taxpayer identification certification. */
    post: operations["SubmitW9Form"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}/verify-w9": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Verify participant Form W-9 submission. */
    post: operations["VerifyW9Form"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/wallets/{walletId}/disburse": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Disburse digital funds from participant wallet to debit card or bank account. */
    post: operations["DisburseParticipantFunds"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/stipend-rules": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List SoA protocol visit stipend compensation rules. */
    get: operations["ListStipendRules"];
    put?: never;
    /** Configure participant visit compensation rules linked to Schedule of Activities. */
    post: operations["CreateStipendRule"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/events/visit-verified": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules. */
    post: operations["IngestVisitVerifiedEvent"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/expenses": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List travel and meal expense reimbursement claims. */
    get: operations["ListExpenseClaims"];
    put?: never;
    /** Submit travel/meal expense claim for coordinator review. */
    post: operations["SubmitExpenseClaim"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/expenses/ocr-extract": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Extract metadata from receipt image/text using rule-based OCR engine. */
    post: operations["ExtractReceiptOcr"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/expenses/{claimId}/review": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Coordinator 1-click review and approval/rejection of expense claim. */
    post: operations["ReviewExpenseClaim"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/me/wallet": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves the authenticated participant's own study wallet balances, masks, and ledger activity. */
    get: operations["GetSelfParticipantWallet"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/me/expenses/ocr-extract": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Extracts merchant, date, category, and amount from an uploaded receipt image for the signed participant. */
    post: operations["ExtractSelfReceiptOcr"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/me/expenses": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Submits a travel or meal expense claim for the signed participant with policy auto-approval and direct ACH dispatch. */
    post: operations["SubmitSelfExpenseClaim"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/me/disburse": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Triggers an on-demand payout of the signed participant's available wallet balance. */
    post: operations["DisburseSelfWalletBalance"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/me/diary-stipend": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Credits protocol stipend for a synchronized participant diary strictly from server rules. */
    post: operations["CreditSelfDiaryStipend"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/transactions": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List participant wallet transactions ledger. */
    get: operations["ListTransactions"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/disbursements": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List digital disbursement dispatches. */
    get: operations["ListDisbursements"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/participant/disbursements/{id}/settle": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Marks a dispatched digital disbursement as settled upon confirmation. */
    post: operations["SettleDisbursement"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/audit-logs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve payment audit ledger records. */
    get: operations["GetPaymentAuditLogs"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists compiled self-billing invoices matching filter criteria. */
    get: operations["ListInvoices"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/{id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieve single self-billing invoice details with line items. */
    get: operations["GetInvoiceById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/compile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Automates single-site billing cycle compilation into a self-billing invoice. */
    post: operations["CompileBillingCycle"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/batch-compile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Executes study-wide batch milestone invoice compilation across all active trial sites. */
    post: operations["BatchCompileBillingCycles"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/{id}/approve": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Approve draft self-billing invoice for ERP export and settlement. */
    post: operations["ApproveInvoice"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/{id}/remittance-advice/sign": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Applies 21 CFR Part 11 electronic signature to cryptographic remittance advice. */
    post: operations["SignRemittanceAdvice"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/{id}/remittance-advice/pdf": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Downloads cryptographic PDF remittance statement. */
    get: operations["DownloadRemittancePdf"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/{id}/reconcile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Reconciles invoice settlement against bank wire or ERP clearing statement. */
    post: operations["ReconcileInvoice"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/payments/invoices/{id}/erp-export": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Exports self-billing invoice to ERP gateway (e.g. SAP). */
    post: operations["ExportInvoiceToErp"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
    Payments_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Payments_BatchBillingCycleCompilationRequest: {
      study_id: string;
      /** @default MONTHLY */
      billing_cycle: string;
      period_start?: null | string;
      period_end?: null | string;
      /** @default false */
      auto_approve: boolean;
      site_ids?: null | string[];
    };
    Payments_BillingCycleCompilationRequest: {
      study_id: string;
      site_id: string;
      /** @default MONTHLY */
      billing_cycle: string;
      period_start?: null | string;
      period_end?: null | string;
      /** @default false */
      auto_approve: boolean;
    };
    Payments_DisburseFundsRequestDto: {
      disbursementType: string;
      /** Format: double */
      amount: number | string;
    };
    Payments_DisburseStipendRequest: {
      subjectId: string;
      studyId: null | string;
      siteId: null | string;
      /** Format: int32 */
      sequenceNumber: null | number | string;
      assignmentId: null | string;
      diaryId: null | string;
      /** Format: double */
      amount: null | number | string;
      idempotencyKey: null | string;
      disbursementType: null | string;
    };
    Payments_IssueCardRequestDto: {
      /** @default RELOADABLE_DEBIT */
      cardType: null | string;
    };
    Payments_OcrExtractRequestDto: {
      rawText?: null | string;
      imageBase64?: null | string;
      categoryHint?: null | string;
      filename?: null | string;
    };
    Payments_ParticipantStipendRuleCreateDto: {
      studyId: string;
      visitCode: string;
      visitName: string;
      procedureCode: null | string;
      /** Format: double */
      amount: number | string;
      /** @default USD */
      currency: null | string;
      /** @default true */
      requiresVerification: boolean;
      /** @default false */
      taxable: boolean;
      /** Format: double */
      antiCoercionCap?: null | number | string;
      /** Format: double */
      autoDisbursementThreshold?: null | number | string;
      /** @default false */
      isMilestone: boolean;
      milestoneCode?: null | string;
    };
    Payments_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Payments_ReconcileInvoiceRequest: {
      payment_reference: string;
      /** @default SAP */
      erp_system: null | string;
      /** Format: double */
      amount_paid?: null | number | string;
      payment_date?: null | string;
      notes?: null | string;
    };
    Payments_ReviewExpenseClaimRequestDto: {
      status: string;
      reviewNotes?: null | string;
      /** Format: double */
      adjustedAmount?: null | number | string;
    };
    Payments_SelfServiceDiaryStipendRequestDto: {
      diaryId?: null | string;
      assignmentId?: null | string;
      /** Format: int32 */
      sequenceNumber?: null | number | string;
      idempotencyKey?: null | string;
      syncVerificationToken?: null | string;
      studyId?: null | string;
      siteId?: null | string;
      subjectId?: null | string;
      walletId?: null | string;
    };
    Payments_SelfServiceDisburseRequestDto: {
      /** @default DIRECT_ACH */
      disbursementType: null | string;
      /** Format: double */
      amount?: null | number | string;
      walletId?: null | string;
      subjectId?: null | string;
      idempotencyKey?: null | string;
    };
    Payments_SelfServiceSubmitExpenseRequestDto: {
      category: string;
      merchantName: string;
      /** Format: double */
      grossAmount: number | string;
      /** @default false */
      isMealAllowance: boolean;
      /** Format: double */
      perDiemCap?: null | number | string;
      expenseDate?: null | string;
      receiptImageName?: null | string;
      receiptOcrData?: unknown;
      /** Format: double */
      ocrConfidence?: null | number | string;
      studyId?: null | string;
      siteId?: null | string;
      subjectId?: null | string;
      walletId?: null | string;
      idempotencyKey?: null | string;
      ocrToken?: null | string;
      receiptImageBase64?: null | string;
    };
    Payments_SignRemittanceRequest: {
      signature_reason?: null | string;
    };
    Payments_SubmitExpenseClaimRequestDto: {
      subjectId: string;
      studyId: string;
      siteId: string;
      category: string;
      merchantName: string;
      /** Format: double */
      grossAmount: number | string;
      /** @default false */
      isMealAllowance: boolean;
      /** Format: double */
      perDiemCap?: null | number | string;
      expenseDate?: null | string;
      receiptImageName?: null | string;
      receiptOcrData?: unknown;
      /** Format: double */
      ocrConfidence?: null | number | string;
      ocrToken?: null | string;
      receiptImageBase64?: null | string;
    };
    Payments_SubmitW9RequestDto: {
      fullLegalName: string;
      taxIdLastFour: string;
      address?: null | string;
      /** @default true */
      certifyAccurate: boolean;
    };
    Payments_VerifyW9RequestDto: {
      notes?: null | string;
    };
    Payments_VisitVerificationEventDto: {
      studyId: string;
      siteId: string;
      subjectId: string;
      visitCode: string;
      eventId: string;
    };
  };
  responses: never;
  parameters: never;
  requestBodies: never;
  headers: never;
  pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
  PaymentsHealthCheck: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Payments_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  PaymentsStatus: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Payments_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  DisburseStipendDirect: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_DisburseStipendRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  DisburseStipendWallet: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_DisburseStipendRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ListWallets: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetSubjectWallet: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        subjectId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetWalletById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetTax1099Status: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetBalanceInquiry: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  IssueReloadableCard: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_IssueCardRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  SubmitW9Form: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_SubmitW9RequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  VerifyW9Form: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_VerifyW9RequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  DisburseParticipantFunds: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        walletId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_DisburseFundsRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ListStipendRules: {
    parameters: {
      query?: {
        studyId?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  CreateStipendRule: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_ParticipantStipendRuleCreateDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  IngestVisitVerifiedEvent: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_VisitVerificationEventDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ListExpenseClaims: {
    parameters: {
      query?: {
        status?: string;
        subjectId?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  SubmitExpenseClaim: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_SubmitExpenseClaimRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ExtractReceiptOcr: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_OcrExtractRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ReviewExpenseClaim: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        claimId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_ReviewExpenseClaimRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetSelfParticipantWallet: {
    parameters: {
      query?: {
        subjectId?: string;
        walletId?: string;
        studyId?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ExtractSelfReceiptOcr: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_OcrExtractRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  SubmitSelfExpenseClaim: {
    parameters: {
      query?: never;
      header?: {
        "Idempotency-Key"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_SelfServiceSubmitExpenseRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  DisburseSelfWalletBalance: {
    parameters: {
      query?: never;
      header?: {
        "Idempotency-Key"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_SelfServiceDisburseRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  CreditSelfDiaryStipend: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_SelfServiceDiaryStipendRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ListTransactions: {
    parameters: {
      query?: {
        subjectId?: string;
        walletId?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ListDisbursements: {
    parameters: {
      query?: {
        subjectId?: string;
        walletId?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  SettleDisbursement: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetPaymentAuditLogs: {
    parameters: {
      query?: {
        subjectId?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ListInvoices: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
        billing_cycle?: string;
        status?: string;
        erp_sync_status?: string;
        batch_execution_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  GetInvoiceById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  CompileBillingCycle: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_BillingCycleCompilationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  BatchCompileBillingCycles: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_BatchBillingCycleCompilationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ApproveInvoice: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Payments_ProblemDetails"];
        };
      };
    };
  };
  SignRemittanceAdvice: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_SignRemittanceRequest"];
      };
    };
    responses: {
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Payments_ProblemDetails"];
        };
      };
    };
  };
  DownloadRemittancePdf: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ReconcileInvoice: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Payments_ReconcileInvoiceRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  ExportInvoiceToErp: {
    parameters: {
      query?: {
        erp_system?: string;
      };
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
}
