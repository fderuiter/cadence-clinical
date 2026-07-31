import js from "@eslint/js";
import vuePlugin from "eslint-plugin-vue";
import vuejsAccessibility from "eslint-plugin-vuejs-accessibility";

const crossWorkspaceBoundaryRule = {
  meta: {
    type: "problem",
    docs: {
      description: "Disallow direct imports across workspace boundaries",
    },
    schema: [],
  },
  create(context) {
    return {
      ImportDeclaration(node) {
        const importPath = node.source.value;
        const filename = context.filename || context.getFilename();
        const isCrossBoundary = (
          (filename.includes("/apps/") && (importPath.includes("../packages") || importPath.includes("../../packages"))) ||
          (filename.includes("/apps/") && (importPath.includes("../apps/") || importPath.includes("../../apps/"))) ||
          (filename.includes("/packages/") && importPath.includes("../apps/"))
        );
        if (isCrossBoundary) {
          context.report({
            node,
            message: "Direct cross-workspace imports are prohibited. Use standard workspace names (e.g. 'ui') instead of relative paths across boundaries.",
          });
        }
      }
    };
  }
};

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
    plugins: {
      "local-rules": {
        rules: {
          "no-cross-workspace-imports": crossWorkspaceBoundaryRule,
        }
      }
    },
    rules: {
      "no-unused-vars": "error",
      "no-undef": "off",
      "vue/multi-word-component-names": "off",
      "vuejs-accessibility/label-has-for": "warn",
      "vuejs-accessibility/click-events-have-key-events": "warn",
      "vuejs-accessibility/no-static-element-interactions": "warn",
      "vuejs-accessibility/form-control-has-label": "warn",
      "local-rules/no-cross-workspace-imports": "error",
    },
    files: ["apps/**/*.js", "packages/**/*.js", "apps/**/*.vue", "packages/**/*.vue"],
  }
];
