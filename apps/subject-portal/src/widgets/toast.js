// Transient status toast appended to the document body.

export function showToast(message) {
  if (typeof document === "undefined") return;
  let toast = document.getElementById("toast-container");
  if (!toast) {
    toast = document.createElement("div");
    toast.id = "toast-container";
    toast.className = "toast-notification";
    document.body.appendChild(toast);
  }
  toast.textContent = message;
  toast.classList.add("toast-show");
  setTimeout(() => {
    toast.classList.remove("toast-show");
  }, 3000);
}
