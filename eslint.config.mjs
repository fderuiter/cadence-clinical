// ESLint configuration for Vue single-file components
import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";
import vuejsAccessibility from "eslint-plugin-vuejs-accessibility";
import vueParser from "vue-eslint-parser";
import tseslint from "typescript-eslint";

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
      "vuejs-accessibility/label-has-for": "warn",
      "vuejs-accessibility/click-events-have-key-events": "warn",
      "vuejs-accessibility/no-static-element-interactions": "warn",
      "vuejs-accessibility/form-control-has-label": "warn",
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
    files: [
      "**/tests/**",
      "tests/**",
      "**/*.test.*",
      "**/*.spec.*",
    ],
    rules: {
      "vue/one-component-per-file": "off",
      "vue/require-prop-types": "off",
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": "off",
    },
  },
];
