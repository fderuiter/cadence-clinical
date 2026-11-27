// ESLint flat configuration — rule implementations live in ./scripts/eslint/rules/
// and domain allowlist shards live in ./scripts/eslint/allowlists/.
import js from "@eslint/js";
import globals from "globals";
import vuePlugin from "eslint-plugin-vue";
import vuejsAccessibility from "eslint-plugin-vuejs-accessibility";
import vueParser from "vue-eslint-parser";
import tseslint from "typescript-eslint";
import {
  cssStubParser,
  plainTextEslintParser,
} from "./scripts/eslint/parsers.mjs";
import { customVuePlugin } from "./scripts/eslint/rules/index.mjs";

const disabledCoreRulesForCss = Object.fromEntries(
  Object.keys(js.configs.recommended.rules).map((ruleName) => [ruleName, "off"])
);

const unusedVarsRuleConfig = [
  "error",
  { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
];

const labelHasForRuleConfig = [
  "error",
  { required: { some: ["nesting", "id"] } },
];

export default [
  {
    ignores: [
      "**/dist/**",
      "**/node_modules/**",
      "**/.venv/**",
      "**/.cadence/**",
      "**/venv/**",
      "**/.pytest_cache/**",
      "**/playwright-report/**",
      "**/test-results/**",
      "**/TestResults/**",
      "**/coverage/**",
      "**/bin/**",
      "**/obj/**",
      "**/.claude/worktrees/**",
      "docs/.vitepress/.temp/**",
      "docs/.vitepress/cache/**",
      "docs/.vitepress/dist/**",
      "**/mockServiceWorker.js",
      "apps/web/src/api/types.ts",
    ],
  },
  js.configs.recommended,
  ...vuePlugin.configs["flat/recommended"],
  ...vuejsAccessibility.configs["flat/recommended"],
  {
    files: ["**/*.vue"],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        sourceType: "module",
        ecmaVersion: 2022,
      },
    },
    plugins: { "custom-vue": customVuePlugin },
    rules: {
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
      "custom-vue/no-misused-ai-tokens": "error",
      "custom-vue/no-raw-overlays": "error",
      "custom-vue/no-suppressed-focus-outline": "error",
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/no-aria-label-mismatch": "error",
      "custom-vue/require-tabular-nums": "error",
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
      "custom-vue/no-unscoped-clinical-tabs-css": "error",
      "custom-vue/no-raw-clinical-table": "error",
      "custom-vue/no-unguarded-button-state": "error",
      "custom-vue/require-inline-focus-handoff": "error",
      "custom-vue/no-unscoped-navigation": "error",
      "vue/max-attributes-per-line": "off",
      "vue/singleline-html-element-content-newline": "off",
      "vue/multiline-html-element-content-newline": "off",
      "vue/html-closing-bracket-newline": "off",
      "vue/html-closing-bracket-spacing": "off",
      "vue/html-indent": "off",
      "vue/html-self-closing": "off",
      "vue/attributes-order": "off",
      "vue/require-default-prop": "off",
    },
  },
  {
    files: [
      "apps/web/src/components/ContextualHelpDrawer.vue",
      "apps/web/src/components/help/ContextualHelpGuidanceTab.vue",
      "apps/web/src/components/econsent/IcfSectionEditor.vue",
      "apps/web/src/views/KnowledgeView.vue",
      "apps/web/src/views/RulesView.vue",
    ],
    rules: { "vue/no-v-html": "off" },
  },
  {
    files: ["packages/**/*.css", "apps/**/*.css", "docs/.vitepress/**/*.css"],
    languageOptions: { parser: cssStubParser },
    plugins: { "custom-vue": customVuePlugin },
    rules: {
      ...disabledCoreRulesForCss,
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
      "custom-vue/no-misused-ai-tokens": "error",
      "custom-vue/no-suppressed-focus-outline": "error",
      "custom-vue/no-unguarded-button-state": "error",
      "custom-vue/require-tabular-nums": "error",
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
    },
  },
  {
    files: [
      "packages/ui/index.js",
      "packages/ui/src/**/*.js",
      "packages/ui/src/**/*.ts",
      "apps/subject-portal/src/**/*.js",
      "apps/subject-portal/src/**/*.ts",
      "apps/web/src/**/*.js",
      "apps/web/src/**/*.ts",
    ],
    plugins: { "custom-vue": customVuePlugin },
    rules: {
      "custom-vue/no-inline-hex": "error",
      "custom-vue/no-undefined-css-vars": "error",
      "custom-vue/no-misused-ai-tokens": "error",
      "custom-vue/no-unlabeled-glyph-button": "error",
      "custom-vue/require-tabular-nums": "error",
      "custom-vue/no-sub12px-font-size": "error",
      "custom-vue/no-custom-font-stack": "error",
      "custom-vue/require-inline-focus-handoff": "error",
      "custom-vue/no-unscoped-navigation": "error",
    },
  },
  {
    files: ["scripts/**/*.{js,mjs,cjs}"],
    rules: {
      "no-alert": "error",
      "no-unused-vars": unusedVarsRuleConfig,
    },
  },
  {
    files: ["docs/.vitepress/theme/**/*.js"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: { ...globals.browser },
    },
    plugins: { "custom-vue": customVuePlugin },
    rules: {
      "no-undef": "error",
      "no-unused-vars": unusedVarsRuleConfig,
      "custom-vue/no-unlabeled-glyph-button": "error",
    },
  },
  ...tseslint.configs.recommended.map((config) => ({
    ...config,
    files: config.files ?? ["**/*.ts", "**/*.tsx", "**/*.mts", "**/*.cts"],
  })),
  {
    files: ["docs/.vitepress/**/*.mjs"],
    languageOptions: { ecmaVersion: 2022, sourceType: "module" },
    rules: {
      "no-control-regex": "off",
      "no-unused-vars": unusedVarsRuleConfig,
    },
  },
  {
    files: ["docs/.vitepress/**/*.cjs"],
    languageOptions: { ecmaVersion: 2022, sourceType: "commonjs" },
    rules: { "no-unused-vars": unusedVarsRuleConfig },
  },
  {
    files: ["**/*.ts", "**/*.tsx", "**/*.mts", "**/*.cts"],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: { sourceType: "module", ecmaVersion: 2022 },
      globals: { ...globals.browser, ...globals.node },
    },
    plugins: { "@typescript-eslint": tseslint.plugin },
    rules: {
      "no-alert": "error",
      "no-undef": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-explicit-any": "off",
      "@typescript-eslint/no-empty-object-type": "off",
      "@typescript-eslint/no-unused-vars": unusedVarsRuleConfig,
    },
  },
  {
    files: ["**/*.js", "**/*.jsx", "**/*.mjs", "**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: { ...globals.browser },
    },
  },
  {
    files: ["**/*.cjs"],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "commonjs",
      globals: { ...globals.node },
    },
  },
  {
    files: [
      "*.config.{js,mjs,cjs}",
      "apps/*/*.config.{js,mjs,cjs}",
      "packages/*/*.config.{js,mjs,cjs}",
      "apps/*/scripts/**/*.{js,mjs,cjs}",
      "packages/*/scripts/**/*.{js,mjs,cjs}",
      "scripts/**/*.{js,mjs,cjs}",
      ".claude/**/*.{js,mjs,cjs}",
      "docs/**/*.mjs",
      "tests/**/*.{js,mjs,cjs}",
    ],
    languageOptions: { ecmaVersion: 2022, globals: { ...globals.node } },
  },
  {
    files: [
      "apps/web/scripts/**/*.js",
      "packages/ui/scripts/**/*.js",
      "scripts/**/*.js",
    ],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "commonjs",
      globals: { ...globals.node },
    },
  },
  {
    files: ["apps/subject-portal/public/sw.js"],
    languageOptions: {
      ecmaVersion: 2022,
      globals: { ...globals.serviceworker },
    },
  },
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: { ...globals.browser },
    },
    rules: {
      "no-alert": "error",
      "no-unused-vars": unusedVarsRuleConfig,
      "vue/multi-word-component-names": "off",
      "vuejs-accessibility/label-has-for": labelHasForRuleConfig,
      "vuejs-accessibility/click-events-have-key-events": "error",
      "vuejs-accessibility/no-static-element-interactions": "error",
      "vuejs-accessibility/form-control-has-label": "error",
    },
    files: [
      "apps/**/*.js",
      "packages/**/*.js",
      "apps/**/*.vue",
      "packages/**/*.vue",
      "docs/.vitepress/theme/**/*.vue",
    ],
  },
  {
    languageOptions: { ecmaVersion: 2022, sourceType: "module" },
    rules: {
      "vuejs-accessibility/label-has-for": labelHasForRuleConfig,
      "vuejs-accessibility/click-events-have-key-events": "error",
      "vuejs-accessibility/no-static-element-interactions": "error",
      "vuejs-accessibility/form-control-has-label": "error",
      "vue/html-self-closing": [
        "error",
        {
          html: { void: "always", normal: "never", component: "always" },
          svg: "always",
          math: "always",
        },
      ],
    },
    files: ["packages/ui/**/*.js", "packages/ui/**/*.vue"],
  },
  {
    files: ["**/*.vue"],
    plugins: { "@typescript-eslint": tseslint.plugin },
    rules: {
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": unusedVarsRuleConfig,
    },
  },
  {
    files: [
      "packages/ui/index.js",
      "packages/ui/src/**/*.{js,ts,vue}",
      "apps/subject-portal/src/**/*.{js,ts,vue}",
      "docs/.vitepress/theme/**/*.{js,vue}",
      "apps/web/src/App.vue",
      "apps/web/src/composables/**/*.{js,ts}",
      "apps/web/src/components/*.vue",
      "apps/web/src/components/help/**/*.vue",
      "apps/web/src/components/shell/**/*.vue",
      "apps/web/src/components/observation/**/*.vue",
      "apps/web/src/components/canvas/**/*.vue",
      "apps/web/src/components/common/**/*.vue",
      "apps/web/src/components/shared/**/*.vue",
      "apps/web/src/components/ctms/**/*.vue",
      "apps/web/src/components/ecrf/**/*.vue",
      "apps/web/src/components/etmf/**/*.vue",
      "apps/web/src/components/tickets/**/*.vue",
      "apps/web/src/components/clinical/*Drawer.vue",
      "apps/web/src/components/clinical/training/**/*.vue",
      "apps/web/src/components/clinical/ip-reconciliation/**/*.vue",
      "apps/web/src/components/clinical/SsuGreenlightConsole.vue",
      "apps/web/src/components/clinical/ProtocolTrainingConsole.vue",
      "apps/web/src/components/clinical/SensorTelemetryConsole.vue",
      "apps/web/src/components/clinical/LabGovernanceConsole.vue",
      "apps/web/src/views/OverviewView.vue",
      "apps/web/src/views/PlansView.vue",
      "apps/web/src/views/TrialSetupView.vue",
      "apps/web/src/views/LoginView.vue",
      "apps/web/src/views/ForbiddenView.vue",
      "apps/web/src/views/UnavailableView.vue",
      "apps/web/src/views/StudyCanvasView.vue",
      "apps/web/src/views/ObservationView.vue",
      "apps/web/src/views/EcrfView.vue",
      "apps/web/src/views/EcrfLiveWorkspace.vue",
      "apps/web/src/views/demo/EcrfDemoWorkspace.vue",
      "apps/web/src/views/CtmsView.vue",
      "apps/web/src/views/CtmsLiveWorkspace.vue",
      "apps/web/src/views/demo/CtmsDemoWorkspace.vue",
      "apps/web/src/views/DocumentManagerView.vue",
      "apps/web/src/views/StudyTrainingView.vue",
      "apps/web/src/views/TicketsView.vue",
      "apps/web/src/views/KnowledgeView.vue",
      "apps/web/src/views/DataLockView.vue",
      "apps/web/src/views/ExportWizardView.vue",
    ],
    plugins: { "custom-vue": customVuePlugin },
    rules: { "custom-vue/no-vuejs-a11y-suppression": "error" },
  },
  {
    files: [
      "**/tests/**/*.{js,jsx,ts,tsx,mjs,cjs,mts,cts,vue}",
      "tests/**/*.{js,jsx,ts,tsx,mjs,cjs,mts,cts,vue}",
      "**/*.test.{js,jsx,ts,tsx,mjs,cjs,mts,cts}",
      "**/*.spec.{js,jsx,ts,tsx,mjs,cjs,mts,cts}",
    ],
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
        ...globals.commonjs,
        ...globals.vitest,
      },
    },
    plugins: { "custom-vue": customVuePlugin },
    rules: {
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "custom-vue/no-inline-hex": "off",
      "custom-vue/no-misused-ai-tokens": "off",
      "custom-vue/no-sub12px-font-size": "off",
      "custom-vue/no-custom-font-stack": "off",
      "custom-vue/no-unscoped-navigation": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
  {
    files: [
      "packages/ui/**/*.{vue,js,ts,mjs,cjs}",
      "apps/web/**/*.{vue,js,ts,mjs,cjs}",
      "apps/subject-portal/**/*.{vue,js,ts,mjs,cjs}",
      "packages/demo-fixtures/**/*.{js,ts,mjs,cjs}",
      "docs/.vitepress/theme/**/*.{vue,js,ts,mjs,cjs}",
    ],
    plugins: { "custom-vue": customVuePlugin },
    rules: { "custom-vue/no-emoji-glyphs": "error" },
  },
  {
    files: [
      "packages/ui/**/*.css",
      "apps/web/**/*.css",
      "apps/subject-portal/**/*.css",
      "docs/.vitepress/theme/**/*.css",
      "apps/web/src/locales/*.json",
    ],
    languageOptions: { parser: plainTextEslintParser },
    plugins: { "custom-vue": customVuePlugin },
    rules: { "custom-vue/no-emoji-glyphs": "error" },
  },
];
