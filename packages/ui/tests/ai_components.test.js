import { describe, it, expect } from "vitest";
import fs from "fs";
import path from "path";
import {
  AiActionButton,
  ConfidenceBadge,
  AiSuggestionDrawer,
} from "../src/index.js";
import {
  AiActionButton as RootAiActionButton,
  ConfidenceBadge as RootConfidenceBadge,
  AiSuggestionDrawer as RootAiSuggestionDrawer,
} from "../index.js";

describe("AI UI Primitives & Components (#4367)", () => {
  const actionBtnPath = path.resolve(
    __dirname,
    "../src/components/clinical/AiActionButton.vue"
  );
  const badgePath = path.resolve(
    __dirname,
    "../src/components/clinical/ConfidenceBadge.vue"
  );
  const drawerPath = path.resolve(
    __dirname,
    "../src/components/clinical/AiSuggestionDrawer.vue"
  );

  it("exports AiActionButton, ConfidenceBadge, and AiSuggestionDrawer from ui package entries", () => {
    expect(AiActionButton).toBeDefined();
    expect(ConfidenceBadge).toBeDefined();
    expect(AiSuggestionDrawer).toBeDefined();

    expect(RootAiActionButton).toBeDefined();
    expect(RootConfidenceBadge).toBeDefined();
    expect(RootAiSuggestionDrawer).toBeDefined();
  });

  describe("AiActionButton Component", () => {
    it("file exists and contains required props and role-based gating logic", () => {
      expect(fs.existsSync(actionBtnPath)).toBe(true);
      const code = fs.readFileSync(actionBtnPath, "utf-8");

      // Props and defaults
      expect(code).toContain("AI Assist");
      expect(code).toContain("✨");
      expect(code).toContain("loading");
      expect(code).toContain("loadingText");
      expect(code).toContain("requiredRoles");
      expect(code).toContain("userRoles");
      expect(code).toContain("unauthorizedTooltip");

      // Role check computation
      expect(code).toContain("isAuthorized");
      expect(code).toContain("normalizedRequiredRoles");
      expect(code).toContain("normalizedUserRoles");

      // ARIA and accessibility
      expect(code).toContain('aria-busy="loading ? \'true\' : \'false\'"');
      expect(code).toContain('aria-disabled="isDisabled ? \'true\' : \'false\'"');
      expect(code).toContain("computedAriaLabel");

      // Design tokens & styling
      expect(code).toContain("var(--color-primary");
      expect(code).toContain("var(--color-surface");
      expect(code).toContain("var(--radius-md");
      expect(code).toContain("ai-spinner");
    });
  });

  describe("ConfidenceBadge Component", () => {
    it("file exists and implements standard confidence tiers and colors", () => {
      expect(fs.existsSync(badgePath)).toBe(true);
      const code = fs.readFileSync(badgePath, "utf-8");

      // Score normalization & thresholds
      expect(code).toContain("normalizedScore");
      expect(code).toContain("high: 90");
      expect(code).toContain("medium: 75");
      expect(code).toContain("confidenceTier");

      // Tier classes & labels
      expect(code).toContain("confidence-high");
      expect(code).toContain("confidence-medium");
      expect(code).toContain("confidence-low");
      expect(code).toContain("High Confidence");
      expect(code).toContain("Medium Confidence");
      expect(code).toContain("Low Confidence");

      // ARIA status
      expect(code).toContain('role="status"');
      expect(code).toContain("ariaLabel");

      // Token-based CSS styling (Green, Amber, Red)
      expect(code).toContain("var(--color-success");
      expect(code).toContain("var(--color-warning");
      expect(code).toContain("var(--color-error");
    });
  });

  describe("AiSuggestionDrawer Component", () => {
    it("file exists and satisfies side-by-side diff, Part 11 justification, and drawer lifecycle", () => {
      expect(fs.existsSync(drawerPath)).toBe(true);
      const code = fs.readFileSync(drawerPath, "utf-8");

      // Drawer props & emits
      expect(code).toContain("originalValue");
      expect(code).toContain("suggestedValue");
      expect(code).toContain("confidenceScore");
      expect(code).toContain("modelIdentifier");
      expect(code).toContain("promptSummary");
      expect(code).toContain("requireReason");
      expect(code).toContain("approve");
      expect(code).toContain("accept");
      expect(code).toContain("dismiss");
      expect(code).toContain("reject");

      // Diff display
      expect(code).toContain("ai-diff-original");
      expect(code).toContain("ai-diff-suggested");
      expect(code).toContain("formatDisplayValue");

      // Inline editable capability
      expect(code).toContain("isEditing");
      expect(code).toContain("toggleEdit");
      expect(code).toContain("editedValueText");

      // 21 CFR Part 11 Audit compliance
      expect(code).toContain("Reason for Change (21 CFR Part 11 Audit Justification)");
      expect(code).toContain("reasonForChange");
      expect(code).toContain("validateReason");

      // Modal & ARIA attributes
      expect(code).toContain('role="dialog"');
      expect(code).toContain('aria-modal="true"');
      expect(code).toContain("handleKeyDown");
      expect(code).toContain("Escape");

      // Action buttons
      expect(code).toContain("Dismiss / Reject");
      expect(code).toContain("Accept & Apply");
    });
  });
});
