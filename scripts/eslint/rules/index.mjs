import { noAriaLabelMismatchRule } from "./no-aria-label-mismatch.mjs";
import { noCustomFontStackRule } from "./no-custom-font-stack.mjs";
import { noEmojiGlyphsRule } from "./no-emoji-glyphs.mjs";
import { noInlineHexRule } from "./no-inline-hex.mjs";
import { noMisusedAiTokensRule } from "./no-misused-ai-tokens.mjs";
import { noRawClinicalTableRule } from "./no-raw-clinical-table.mjs";
import { noRawOverlaysRule } from "./no-raw-overlays.mjs";
import { noSub12pxFontSizeRule } from "./no-sub12px-font-size.mjs";
import { noSuppressedFocusOutlineRule } from "./no-suppressed-focus-outline.mjs";
import { noTransitionAllRule } from "./no-transition-all.mjs";
import { noUndefinedCssVarsRule } from "./no-undefined-css-vars.mjs";
import { noUnguardedButtonStateRule } from "./no-unguarded-button-state.mjs";
import { enhancedNoUnlabeledGlyphButtonRule } from "./no-unlabeled-glyph-button.mjs";
import { noUnscopedClinicalTabsCssRule } from "./no-unscoped-clinical-tabs-css.mjs";
import { noUnscopedNavigationRule } from "./no-unscoped-navigation.mjs";
import { noVuejsA11ySuppressionRule } from "./no-vuejs-a11y-suppression.mjs";
import { requireInlineFocusHandoffRule } from "./require-inline-focus-handoff.mjs";
import { requireTabularNumsRule } from "./require-tabular-nums.mjs";

export const customVuePlugin = {
  rules: {
    "no-inline-hex": noInlineHexRule,
    "no-undefined-css-vars": noUndefinedCssVarsRule,
    "no-misused-ai-tokens": noMisusedAiTokensRule,
    "no-raw-overlays": noRawOverlaysRule,
    "no-suppressed-focus-outline": noSuppressedFocusOutlineRule,
    "no-transition-all": noTransitionAllRule,
    "no-unlabeled-glyph-button": enhancedNoUnlabeledGlyphButtonRule,
    "no-emoji-glyphs": noEmojiGlyphsRule,
    "no-aria-label-mismatch": noAriaLabelMismatchRule,
    "require-tabular-nums": requireTabularNumsRule,
    "no-sub12px-font-size": noSub12pxFontSizeRule,
    "no-custom-font-stack": noCustomFontStackRule,
    "no-vuejs-a11y-suppression": noVuejsA11ySuppressionRule,
    "no-unscoped-clinical-tabs-css": noUnscopedClinicalTabsCssRule,
    "no-raw-clinical-table": noRawClinicalTableRule,
    "no-unguarded-button-state": noUnguardedButtonStateRule,
    "require-inline-focus-handoff": requireInlineFocusHandoffRule,
    "no-unscoped-navigation": noUnscopedNavigationRule,
  },
};
