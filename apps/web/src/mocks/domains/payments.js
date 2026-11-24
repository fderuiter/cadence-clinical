export function createPaymentsDomain(ctx) {
  const { state } = ctx;

  state.participantWallets = [];
  state.stipendRules = [];
  state.expenseClaims = [];
  state.walletTransactions = [];
  state.paymentOcrTokens = new Map();
  state.consumedPaymentOcrTokens = new Set();
  state.recordedPaymentReceiptDigests = new Set();

  function seed() {
    state.participantWallets = [
      {
        id: "wallet-1001",
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        site_id: "SITE-101",
        current_balance: 150,
        lifetime_stipends: 150,
        lifetime_reimbursements: 0,
        lifetime_disbursements: 0,
        taxable_ytd: 150,
        is_1099_threshold_exceeded: false,
        w9_status: "VERIFIED",
        currency: "USD",
        status: "ACTIVE",
        card_type: "RELOADABLE_DEBIT",
        card_status: "ACTIVE",
        virtual_card_mask: "**** **** **** 4821",
        virtual_card_expiry: "12/29",
        ach_routing_mask: "*****0021",
        ach_account_mask: "ACH-***4321",
        ach_account_holder: "Participant SUBJ-1001",
        anti_coercion_cap: 200,
      },
      {
        id: "wallet-1002",
        subject_id: "SUBJ-1002",
        study_id: "DEMO-01",
        site_id: "SITE-102",
        current_balance: 175,
        lifetime_stipends: 625,
        lifetime_reimbursements: 0,
        lifetime_disbursements: 450,
        taxable_ytd: 625,
        is_1099_threshold_exceeded: true,
        w9_status: "REQUIRED",
        currency: "USD",
        status: "ACTIVE",
        card_type: "RELOADABLE_DEBIT",
        card_status: "ACTIVE",
        virtual_card_mask: "**** **** **** 9012",
        virtual_card_expiry: "06/28",
        ach_routing_mask: "*****0021",
        ach_account_mask: "ACH-***9012",
        ach_account_holder: "Participant SUBJ-1002",
        anti_coercion_cap: 200,
      },
    ];

    state.stipendRules = [
      {
        id: "srule-01",
        visit_code: "V1_SCREENING",
        visit_name: "Screening",
        amount: 75,
        currency: "USD",
        taxable: true,
        anti_coercion_cap: 150,
        is_milestone: false,
      },
      {
        id: "srule-02",
        visit_code: "VISIT_BASELINE",
        visit_name: "Baseline (Day 1)",
        amount: 75,
        currency: "USD",
        taxable: true,
        anti_coercion_cap: 200,
        is_milestone: false,
      },
    ];

    state.expenseClaims = [];

    state.walletTransactions = [
      {
        id: "wtx-1001",
        created_at: "2026-08-01T10:00:00Z",
        subject_id: "SUBJ-1001",
        study_id: "DEMO-01",
        transaction_type: "VISIT_STIPEND",
        amount: 150,
        balance_after: 150,
        description: "Baseline visit stipend credit",
      },
    ];

    state.paymentOcrTokens = new Map();
    state.consumedPaymentOcrTokens = new Set();
    state.recordedPaymentReceiptDigests = new Set();
  }

  return { seed, methods: {} };
}
