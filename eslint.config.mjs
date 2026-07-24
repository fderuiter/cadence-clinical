import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";

export default [
  {
    ignores: ["**/dist/**", "**/node_modules/**"],
  },
  js.configs.recommended,
  ...vuePlugin.configs["flat/recommended"],
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-unused-vars": "error",
      "no-undef": "off",
      "vue/multi-word-component-names": "off",
    },
    files: ["apps/**/*.js", "packages/**/*.js", "apps/**/*.vue"],
  }
];
