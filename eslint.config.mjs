import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";

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
  {
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
    rules: {
      "no-unused-vars": "error",
      "no-undef": "off",
      "vue/multi-word-component-names": "off",
      "no-restricted-syntax": [
        "error",
        {
          "selector": "FunctionDeclaration[id.name='debounce'], VariableDeclarator[init.type='FunctionExpression'][id.name='debounce'], VariableDeclarator[init.type='ArrowFunctionExpression'][id.name='debounce']",
          "message": "Do not declare local debounce helpers. Import debounce from the shared 'ui' package instead."
        }
      ]
    },
    files: ["apps/**/*.js", "packages/**/*.js", "apps/**/*.vue"],
  }
];
