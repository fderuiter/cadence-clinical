import { computed, onMounted, watch } from "vue";
import { handoffFocus } from "ui";
import { getSiteSupportContact } from "../../features/site-support.js";
import {
  extractReceiptOcr,
  loadParticipantWallet,
  openDisburseModal,
} from "../../features/wallet.js";
import { isScreenAvailable } from "../../router/availability.js";
import { useSessionStore } from "../../stores/session.js";
import { useUiStore } from "../../stores/ui.js";
import { useWalletStore } from "../../stores/wallet.js";
import {
  formatSignedWalletCurrency,
  formatWalletCurrency,
  normalizeCurrencyCode,
} from "../../utils/currency.js";
import { isDemoModeActive } from "../../utils/demoMode.js";

export function useWalletViewState() {
  const ui = useUiStore();
  const session = useSessionStore();
  const wallet = useWalletStore();

  const isDemo = computed(() => {
    void session.session?.isDemoMode;
    return isDemoModeActive();
  });

  const isAuthenticated = computed(() =>
    Boolean(
      session.session?.token &&
      session.session?.userId &&
      !session.session?.isOfflineMode
    )
  );

  const isWalletEnabled = computed(() => isDemo.value || isAuthenticated.value);

  const hasWalletTab = computed(() => {
    void session.session?.isDemoMode;
    void session.session?.token;
    void session.session?.userId;
    void session.session?.isOfflineMode;
    return isDemo.value || isScreenAvailable("wallet");
  });

  const normalizedCurrency = computed(() =>
    normalizeCurrencyCode(wallet.currency)
  );

  function formatCurrency(amount, currencyOverride = null) {
    return formatWalletCurrency(amount, currencyOverride || wallet.currency);
  }

  function formatSignedCurrency(amount) {
    return formatSignedWalletCurrency(amount, wallet.currency);
  }

  const formattedAvailableBalance = computed(() => {
    const amount =
      typeof wallet.walletBalance === "number" ? wallet.walletBalance : 0;
    return formatWalletCurrency(amount, wallet.currency);
  });

  const isW9HoldActive = computed(() => {
    const thresholdExceeded =
      Boolean(wallet.is1099ThresholdExceeded) ||
      Number(wallet.taxableYtd || 0) >= 600;
    const verified =
      String(wallet.w9Status || "")
        .trim()
        .toUpperCase() === "VERIFIED";
    return thresholdExceeded && !verified;
  });

  const isApproachingW9Threshold = computed(() => {
    const ytd = Number(wallet.taxableYtd || 0);
    const verified =
      String(wallet.w9Status || "")
        .trim()
        .toUpperCase() === "VERIFIED";
    return !isW9HoldActive.value && ytd >= 450 && ytd < 600 && !verified;
  });

  const disburseDescribedBy = computed(() => {
    if (isW9HoldActive.value) return "wallet-w9-hold-banner";
    if (isApproachingW9Threshold.value) return "wallet-w9-warning-banner";
    return undefined;
  });

  const siteContact = computed(
    () => ui.siteSupportContact || getSiteSupportContact()
  );

  const walletError = computed(
    () => wallet.expenseOcr?.error || wallet.expenseError || ""
  );

  function jumpToW9Support() {
    const supportEl = document.getElementById("wallet-w9-support");
    supportEl?.scrollIntoView?.({ behavior: "smooth", block: "start" });
    supportEl?.focus?.();
  }

  function maybeHydrateWallet() {
    if (
      ui.currentView === "view-wallet" &&
      isAuthenticated.value &&
      !isDemo.value &&
      !wallet.walletLoaded &&
      !wallet.walletLoading
    ) {
      loadParticipantWallet().catch(() => {});
    }
  }

  onMounted(() => {
    maybeHydrateWallet();
  });

  watch(
    () => [ui.currentView, session.session?.token, session.session?.userId],
    () => {
      maybeHydrateWallet();
    }
  );

  watch(
    () => wallet.disbursementConfirmation,
    (nextConfirm, prevConfirm) => {
      if (
        nextConfirm &&
        nextConfirm !== prevConfirm &&
        ui.currentView === "view-wallet"
      ) {
        void handoffFocus([
          "#wallet-disbursement-heading",
          "#btn-disburse-balance",
          "#wallet-view-heading",
          "#main-content",
        ]);
      }
    }
  );

  function handleClearReceiptSelection() {
    wallet.expenseForm.receiptImageName = "";
    wallet.expenseForm.receiptImageDataUrl = "";
    wallet.expenseError = "";
  }

  async function handleReceiptSelectedFile(file) {
    if (!file) return;
    try {
      await extractReceiptOcr(file);
    } catch (err) {
      wallet.expenseError =
        err?.message ||
        "Unable to extract receipt details right now. Please enter details manually below.";
    }
  }

  function handleOpenDisburseModal(event) {
    openDisburseModal(event?.currentTarget || null);
  }

  return {
    ui,
    wallet,
    isDemo,
    isWalletEnabled,
    hasWalletTab,
    normalizedCurrency,
    formatCurrency,
    formatSignedCurrency,
    formattedAvailableBalance,
    isW9HoldActive,
    isApproachingW9Threshold,
    disburseDescribedBy,
    siteContact,
    walletError,
    jumpToW9Support,
    handleClearReceiptSelection,
    handleReceiptSelectedFile,
    handleOpenDisburseModal,
  };
}
