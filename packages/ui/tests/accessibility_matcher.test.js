// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import { toBeAccessible } from "../accessibility-matcher.js";
import * as uiBarrel from "../index.js";

describe("ui/vitest accessibility matcher isolation", () => {
  it("excludes toBeAccessible from the root ui runtime barrel", () => {
    expect("toBeAccessible" in uiBarrel).toBe(false);
  });

  it("passes accessible HTML strings and wrapper objects", async () => {
    const htmlResult = await toBeAccessible(
      '<button type="button">Submit Visit</button>'
    );
    expect(htmlResult.pass).toBe(true);
    expect(htmlResult.message()).toContain(
      "no accessibility violations were found"
    );

    const button = document.createElement("button");
    button.type = "button";
    button.textContent = "Confirm Signature";
    const wrapperResult = await toBeAccessible({ element: button });
    expect(wrapperResult.pass).toBe(true);
  });

  it("rejects null or undefined targets with a clear diagnostic", async () => {
    const result = await toBeAccessible(null);
    expect(result.pass).toBe(false);
    expect(result.message()).toContain("received null or undefined");
  });

  it("enforces strictLayoutMode landmarks when requested", async () => {
    const missingLandmarks = await toBeAccessible("<div>Fragment</div>", {
      strictLayoutMode: true,
    });
    expect(missingLandmarks.pass).toBe(false);
    expect(missingLandmarks.message()).toContain("Missing main landmark");
    expect(missingLandmarks.message()).toContain(
      "Missing skip-to-content link"
    );

    const completeShell = await toBeAccessible(
      '<div><a class="skip-link" href="#main-content">Skip to main</a><main id="main-content"><h1>Workspace</h1></main></div>',
      { strictLayoutMode: true }
    );
    expect(completeShell.pass).toBe(true);
  });

  it("reports axe-core WCAG violations on inaccessible markup", async () => {
    const violationResult = await toBeAccessible('<img src="/chart.png" />');
    expect(violationResult.pass).toBe(false);
    expect(violationResult.message()).toContain("image-alt");
  });
});
