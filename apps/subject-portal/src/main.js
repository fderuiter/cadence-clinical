// Browser startup entry loaded by index.html. Public test and E2E helpers
// remain exported from `./index.js` so cold boot only loads the portal shell.
import { initializeApp } from "./bootstrap/initialize.js";

export function bootSubjectPortal() {
  if (typeof document === "undefined") {
    return;
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeApp, {
      once: true,
    });
  } else {
    void initializeApp();
  }
}

if (!import.meta.env?.VITEST && import.meta.env?.MODE !== "test") {
  bootSubjectPortal();
}
