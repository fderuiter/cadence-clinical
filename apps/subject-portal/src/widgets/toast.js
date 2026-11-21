// Transient accessible toast appended to the document body.

let activeToastTimer = null;

export function showToast(message, options = {}) {
  if (typeof document === "undefined") return;
  const variant =
    typeof options === "string" ? options : options?.variant || "success";
  const duration =
    typeof options === "object" && typeof options?.duration === "number"
      ? options.duration
      : 3500;

  let toast = document.getElementById("toast-container");
  if (!toast) {
    toast = document.createElement("div");
    toast.id = "toast-container";
    toast.className = "toast-notification";
    toast.setAttribute("data-focus-trap-exempt", "true");
    document.body.appendChild(toast);
  }

  toast.classList.remove("toast-error", "toast-warning");
  if (variant === "error") {
    toast.classList.add("toast-error");
    toast.setAttribute("role", "alert");
    toast.setAttribute("aria-live", "assertive");
  } else if (variant === "warning") {
    toast.classList.add("toast-warning");
    toast.setAttribute("role", "status");
    toast.setAttribute("aria-live", "polite");
  } else {
    toast.setAttribute("role", "status");
    toast.setAttribute("aria-live", "polite");
  }

  toast.textContent = message;
  toast.classList.add("toast-show");

  if (activeToastTimer) {
    clearTimeout(activeToastTimer);
  }
  activeToastTimer = setTimeout(() => {
    toast.classList.remove("toast-show");
    activeToastTimer = null;
  }, duration);
}
