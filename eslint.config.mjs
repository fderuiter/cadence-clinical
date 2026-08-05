// ESLint configuration for Vue single-file components
import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";
import vuejsAccessibility from "eslint-plugin-vuejs-accessibility";

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
    ],
  },
  js.configs.recommended,
  ...vuePlugin.configs["flat/recommended"],
  ...vuejsAccessibility.configs["flat/recommended"],
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
];
