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
    },
    files: ["apps/**/*.js", "packages/**/*.js", "apps/**/*.vue"],
  },
  {
    files: ["packages/**/*.js", "packages/**/*.vue"],
    rules: {
      "no-restricted-imports": ["error", {
        patterns: [
          {
            group: ["**/apps/**", "../apps/**", "../../apps/**", "web", "subject-portal"],
            message: "Shared packages cannot import code from applications."
          }
        ]
      }]
    }
  },
  {
    files: ["apps/web/**/*.js", "apps/web/**/*.vue"],
    rules: {
      "no-restricted-imports": ["error", {
        patterns: [
          {
            group: ["**/apps/subject-portal/**", "../subject-portal/**", "../../subject-portal/**", "subject-portal"],
            message: "Applications cannot directly import from other applications."
          }
        ]
      }]
    }
  },
  {
    files: ["apps/subject-portal/**/*.js", "apps/subject-portal/**/*.vue"],
    rules: {
      "no-restricted-imports": ["error", {
        patterns: [
          {
            group: ["**/apps/web/**", "../web/**", "../../web/**", "web"],
            message: "Applications cannot directly import from other applications."
          }
        ]
      }]
    }
  }
];
