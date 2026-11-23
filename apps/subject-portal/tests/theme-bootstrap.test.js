import { describe, it, expect, beforeEach } from "vitest";
import { resetThemeControllerForTests } from "ui";

describe("apps/subject-portal pre-paint theme-bootstrap module", () => {
  beforeEach(() => {
    resetThemeControllerForTests();
    window.localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.removeAttribute("data-resolved-theme");
    document.documentElement.classList.remove("dark");
    document.documentElement.style.colorScheme = "";
  });

  it("applies stored VitePress dark appearance onto documentElement before portal mount", async () => {
    window.localStorage.setItem("vitepress-theme-appearance", "dark");
    await import("../src/theme-bootstrap.js");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.getAttribute("data-resolved-theme")).toBe(
      "dark"
    );
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");
  });
});
