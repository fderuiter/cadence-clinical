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
      "**/.pytest_cache/**"
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
      "no-unused-vars": "error",
      "no-undef": "off",
      "vue/multi-word-component-names": "off",
      "vuejs-accessibility/label-has-for": "warn",
      "vuejs-accessibility/click-events-have-key-events": "warn",
      "vuejs-accessibility/no-static-element-interactions": "warn",
      "vuejs-accessibility/form-control-has-label": "warn",
    },
    files: ["apps/**/*.js", "packages/**/*.js", "apps/**/*.vue"],
  }
];
