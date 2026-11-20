// Shared ISO-4217 currency formatting helpers for participant wallet surfaces.

export function normalizeCurrencyCode(currency = "USD") {
  if (typeof currency === "string") {
    const trimmed = currency.trim().toUpperCase();
    if (/^[A-Z]{3}$/.test(trimmed)) {
      return trimmed;
    }
  }
  return "USD";
}

export function formatWalletCurrency(amount, currency = "USD") {
  const numeric = Number(amount);
  const safeAmount = Number.isFinite(numeric) ? numeric : 0;
  const code = normalizeCurrencyCode(currency);

  try {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency: code,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(safeAmount);
  } catch {
    return new Intl.NumberFormat("en-US", {
      style: "currency",
      currency: "USD",
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(safeAmount);
  }
}

export function formatSignedWalletCurrency(amount, currency = "USD") {
  const numeric = Number(amount);
  const safeAmount = Number.isFinite(numeric) ? numeric : 0;
  const sign = safeAmount >= 0 ? "+" : "-";
  return `${sign}${formatWalletCurrency(Math.abs(safeAmount), currency)}`;
}
