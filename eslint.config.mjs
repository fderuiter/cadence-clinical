// ESLint configuration for Vue single-file components
import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";
import vuejsAccessibility from "eslint-plugin-vuejs-accessibility";
import vueParser from "vue-eslint-parser";
import tseslint from "typescript-eslint";

const hexColorRegex = /#(?:[0-9a-fA-F]{3,4}){1,2}\b/g;

const noInlineHexRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow hardcoded hex color codes in Vue template style attributes. Use CSS custom properties defined in packages/ui/tokens.css instead.",
      category: "Best Practices",
    },
    messages: {
      noHexInStyle:
        'Hardcoded hex color "{{hex}}" in inline style attribute is prohibited. Use design system tokens (e.g. var(--semantic-color-...)).',
    },
  },
  create(context) {
    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }
    return parserServices.defineTemplateBodyVisitor({
      VAttribute(node) {
        if (!node.key) return;
        const isStaticStyle =
          !node.directive &&
          node.key.type === "VIdentifier" &&
          node.key.name === "style";
        const isDynamicStyle =
          node.directive &&
          node.key.type === "VDirectiveKey" &&
          node.key.name?.name === "bind" &&
          node.key.argument?.name === "style";

        if ((isStaticStyle || isDynamicStyle) && node.value) {
          const text = context.sourceCode.getText(node.value);
          const matches = text.match(hexColorRegex);
          if (matches) {
            for (const hex of new Set(matches)) {
              context.report({
                node: node.value,
                messageId: "noHexInStyle",
                data: { hex },
              });
            }
          }
        }
      },
    });
  },
};

export default [
  {
    ignores: [
      "**/dist/**",
      "**/node_modules/**",
      "**/.venv/**",
      "**/venv/**",
      "**/.pytest_cache/**",
      "**/playwright-report/**",
      "**/test-results/**",
      "scripts/**",
      "docs/.vitepress/**",
      "**/mockServiceWorker.js",
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
    plugins: {
      "custom-vue": {
        rules: {
          "no-inline-hex": noInlineHexRule,
        },
      },
    },
    rules: {
      "custom-vue/no-inline-hex": "error",
    },
  },
  {
    files: ["apps/web/src/**/*.vue"],
    rules: {
      "custom-vue/no-inline-hex": "warn",
    },
  },
  {
    files: [
      "apps/web/src/components/persona/CraVerificationConsole.vue",
      "apps/web/src/components/crf/CrfAuthoringCanvas.vue",
      "apps/web/src/components/crf/CanvasFieldWidget.vue",
    ],
    rules: {
      "custom-vue/no-inline-hex": "error",
    },
  },
  {
    files: ["**/*.ts", "**/*.tsx"],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: {
        sourceType: "module",
        ecmaVersion: 2022,
      },
    },
    plugins: {
      "@typescript-eslint": tseslint.plugin,
    },
    rules: {
      "no-undef": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
      "no-undef": "off",
      "vue/multi-word-component-names": "off",
      "vuejs-accessibility/label-has-for": [
        "error",
        {
          required: {
            some: ["nesting", "id"],
          },
        },
      ],
      "vuejs-accessibility/click-events-have-key-events": "error",
      "vuejs-accessibility/no-static-element-interactions": "error",
      "vuejs-accessibility/form-control-has-label": "error",
    },
    files: [
      "apps/**/*.js",
      "packages/**/*.js",
      "apps/**/*.vue",
      "packages/**/*.vue",
    ],
  },
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "vuejs-accessibility/label-has-for": [
        "error",
        {
          required: {
            some: ["nesting", "id"],
          },
        },
      ],
      "vuejs-accessibility/click-events-have-key-events": "error",
      "vuejs-accessibility/no-static-element-interactions": "error",
      "vuejs-accessibility/form-control-has-label": "error",
      "vue/html-self-closing": [
        "error",
        {
          html: {
            void: "always",
            normal: "never",
            component: "always",
          },
          svg: "always",
          math: "always",
        },
      ],
    },
    files: ["packages/ui/**/*.js", "packages/ui/**/*.vue"],
  },
  {
    files: ["**/*.vue"],
    plugins: {
      "@typescript-eslint": tseslint.plugin,
    },
    rules: {
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["**/tests/**", "tests/**", "**/*.test.*", "**/*.spec.*"],
    rules: {
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
