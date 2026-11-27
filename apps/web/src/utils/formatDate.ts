/**
 * Formats an ISO timestamp into a human-readable US locale date-time string,
 * returning `"N/A"` when empty or the original string if parsing throws.
 */
export function formatDate(dateStr?: string | null): string {
  if (!dateStr) return "N/A";
  try {
    const d = new Date(dateStr);
    return d.toLocaleString("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return dateStr;
  }
}
