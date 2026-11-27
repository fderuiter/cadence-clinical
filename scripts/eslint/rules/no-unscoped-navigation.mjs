import path from "node:path";

const SCOPED_NAVIGATION_CODEC_FILES = [
  "apps/web/src/canvas/palette.ts",
  "apps/web/src/canvas/ecrfRedirect.ts",
  "apps/web/src/composables/useScopedNavigation.ts",
  "apps/web/src/router/index.js",
  "apps/web/src/router/availability.js",
  "apps/web/src/views/ObservationView.vue",
  "apps/web/src/composables/useObservationScopeNavigation.ts",
];

const PRE_TRIAL_ENTRY_FILES = [
  "apps/web/src/views/PlansView.vue",
  "apps/web/src/views/TrialSetupView.vue",
  "apps/web/src/views/LoginView.vue",
  "apps/web/src/views/ForbiddenView.vue",
  "apps/web/src/views/EcrfLiveWorkspace.vue",
];

const WORKFLOW_ROUTE_PREFIXES = [
  "/studies",
  "/ecrf",
  "/ctms",
  "/ssu",
  "/training",
  "/etmf",
  "/tickets",
  "/knowledge",
  "/overview",
  "/observations",
  "/mdr",
  "/digitization",
  "/icf-builder",
  "/econsent-authoring",
  "/amendment-diff",
  "/imaging",
  "/devices",
  "/device-investigation",
  "/adjudication",
  "/cec",
  "/audit",
  "/auditor",
  "/rules",
  "/coding",
  "/data-lock",
  "/lakehouse",
  "/sensors",
  "/labs",
  "/dct",
  "/safety",
  "/exports",
  "/quality",
  "/notifications",
  "/documents",
  "/participant-wallet",
  "/lay-summaries",
];

function isBareWorkflowRoutePath(rawValue) {
  if (typeof rawValue !== "string") return false;
  const trimmed = rawValue.trim();
  if (!trimmed.startsWith("/") || trimmed.includes("?")) return false;
  const pathname = trimmed.split("#")[0];
  return WORKFLOW_ROUTE_PREFIXES.some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`)
  );
}

function getStaticPropertyKey(prop) {
  if (!prop || prop.type !== "Property" || prop.computed) return null;
  if (prop.key?.type === "Identifier") return prop.key.name;
  if (prop.key?.type === "Literal" && typeof prop.key.value === "string") {
    return prop.key.value;
  }
  return null;
}

function getStaticStringValue(expr) {
  if (!expr) return null;
  if (expr.type === "Literal" && typeof expr.value === "string") {
    return expr.value;
  }
  if (
    expr.type === "TemplateLiteral" &&
    expr.expressions.length === 0 &&
    expr.quasis.length === 1
  ) {
    return expr.quasis[0].value.cooked ?? expr.quasis[0].value.raw ?? null;
  }
  return null;
}

function expressionPreservesStudyScope(expr) {
  if (!expr) return false;
  if (expr.type === "ObjectExpression") {
    return expr.properties.some((prop) => {
      if (prop.type === "SpreadElement") return true;
      const keyName = getStaticPropertyKey(prop);
      return keyName === "site" || keyName === "participant";
    });
  }
  if (expr.type === "Identifier") {
    return expr.name === "nextQuery" || expr.name === "routeQuery";
  }
  if (expr.type === "CallExpression") {
    const calleeName =
      expr.callee?.type === "Identifier"
        ? expr.callee.name
        : expr.callee?.type === "MemberExpression" &&
            !expr.callee.computed &&
            expr.callee.property?.type === "Identifier"
          ? expr.callee.property.name
          : "";
    return calleeName.startsWith("scoped");
  }
  if (expr.type === "ConditionalExpression") {
    return (
      expressionPreservesStudyScope(expr.consequent) ||
      expressionPreservesStudyScope(expr.alternate)
    );
  }
  return false;
}

function isInsideNamedFunction(node, fnName) {
  let current = node?.parent;
  while (current) {
    if (
      current.type === "FunctionDeclaration" &&
      current.id?.type === "Identifier" &&
      current.id.name === fnName
    ) {
      return true;
    }
    current = current.parent;
  }
  return false;
}

function isInsideZoomStudiesElement(node) {
  let current = node?.parent;
  while (current) {
    if (current.type === "VElement") {
      const idAttr = (current.startTag?.attributes || []).find(
        (attr) =>
          !attr.directive &&
          attr.key?.type === "VIdentifier" &&
          attr.key.name === "id"
      );
      if (idAttr?.value?.value === "zoom-studies") {
        return true;
      }
    }
    current = current.parent;
  }
  return false;
}

export const noUnscopedNavigationRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Enforce canonical study, site, and participant route scope across apps/web/src by blocking unscoped { name: 'study' } route objects, buildStudyScopedPath menu calls, and bare workflow path strings.",
      category: "Best Practices",
    },
    messages: {
      unscopedStudyObject:
        'Unscoped { name: "study" } route object drops active site/participant context. Use useScopedNavigation().scopedStudyLocation(...) or buildScopedStudyLocation(...) instead.',
      noBuildStudyScopedPath:
        "Do not call buildStudyScopedPath(...) for workspace or preview navigation; use buildScopedWorkflowPath(...) or useScopedNavigation().scopedWorkflowPath(...) so site and participant scope are preserved.",
      bareWorkflowPath:
        'Bare workflow route target "{{target}}" drops active study/site/participant context. Use useScopedNavigation() or buildScopedWorkflowPath(...) / buildStudiesWorkspacePath(...) instead.',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    const isWebSrc =
      filename.includes("/apps/web/src/") ||
      filename.startsWith("apps/web/src/");
    if (!isWebSrc) return {};

    const isCodecFile = SCOPED_NAVIGATION_CODEC_FILES.some(
      (suffix) => filename === suffix || filename.endsWith(`/${suffix}`)
    );
    const isPreTrialEntryFile = PRE_TRIAL_ENTRY_FILES.some(
      (suffix) => filename === suffix || filename.endsWith(`/${suffix}`)
    );
    const isStudyCanvasView =
      filename === "apps/web/src/views/StudyCanvasView.vue" ||
      filename.endsWith("/apps/web/src/views/StudyCanvasView.vue");
    const isAvailabilityCatalog =
      filename === "apps/web/src/router/availability.js" ||
      filename.endsWith("/apps/web/src/router/availability.js");

    function checkCallExpression(node) {
      const callee = node.callee;
      const calleeName =
        callee?.type === "Identifier"
          ? callee.name
          : callee?.type === "MemberExpression" &&
              !callee.computed &&
              callee.property?.type === "Identifier"
            ? callee.property.name
            : "";

      if (calleeName === "buildStudyScopedPath") {
        context.report({
          node,
          messageId: "noBuildStudyScopedPath",
        });
        return;
      }

      if (
        !isCodecFile &&
        !isPreTrialEntryFile &&
        callee?.type === "MemberExpression" &&
        !callee.computed &&
        callee.property?.type === "Identifier" &&
        (callee.property.name === "push" || callee.property.name === "replace")
      ) {
        const firstArg = node.arguments?.[0];
        const literalPath = getStaticStringValue(firstArg);
        if (literalPath && isBareWorkflowRoutePath(literalPath)) {
          context.report({
            node: firstArg,
            messageId: "bareWorkflowPath",
            data: { target: literalPath },
          });
        }
      }
    }

    function checkObjectExpression(node) {
      let isStudyRouteObject = false;
      let isRouteRecordDefinition = false;
      let queryProp = null;
      let pathProp = null;

      for (const prop of node.properties || []) {
        const keyName = getStaticPropertyKey(prop);
        if (!keyName) continue;
        if (
          keyName === "name" &&
          getStaticStringValue(prop.value) === "study"
        ) {
          isStudyRouteObject = true;
        } else if (
          keyName === "component" ||
          keyName === "components" ||
          keyName === "redirect" ||
          keyName === "meta"
        ) {
          isRouteRecordDefinition = true;
        } else if (keyName === "query") {
          queryProp = prop;
        } else if (keyName === "path") {
          pathProp = prop;
        }
      }

      if (isRouteRecordDefinition) return;

      if (isStudyRouteObject && !pathProp) {
        if (isCodecFile) return;
        if (
          isStudyCanvasView &&
          (isInsideZoomStudiesElement(node) ||
            isInsideNamedFunction(node, "studyLocation"))
        ) {
          return;
        }
        if (!queryProp || !expressionPreservesStudyScope(queryProp.value)) {
          context.report({
            node,
            messageId: "unscopedStudyObject",
          });
        }
        return;
      }

      if (!isCodecFile && !isPreTrialEntryFile && pathProp && !queryProp) {
        const literalPath = getStaticStringValue(pathProp.value);
        if (literalPath && isBareWorkflowRoutePath(literalPath)) {
          context.report({
            node: pathProp.value,
            messageId: "bareWorkflowPath",
            data: { target: literalPath },
          });
        }
      }
    }

    function checkReturnOrConditionalDefaultPath(expr) {
      if (isAvailabilityCatalog || !expr) return;
      if (
        expr.type === "MemberExpression" &&
        !expr.computed &&
        expr.property?.type === "Identifier" &&
        expr.property.name === "defaultPath"
      ) {
        const parent = expr.parent;
        if (
          parent?.type === "ReturnStatement" ||
          (parent?.type === "ConditionalExpression" &&
            (parent.consequent === expr || parent.alternate === expr))
        ) {
          context.report({
            node: expr,
            messageId: "bareWorkflowPath",
            data: {
              target: context.sourceCode.getText(expr),
            },
          });
        }
      }
    }

    const scriptVisitors = {
      CallExpression: checkCallExpression,
      ObjectExpression: checkObjectExpression,
      MemberExpression: checkReturnOrConditionalDefaultPath,
    };

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return scriptVisitors;
    }

    return parserServices.defineTemplateBodyVisitor(
      {
        CallExpression: checkCallExpression,
        ObjectExpression: checkObjectExpression,
        MemberExpression: checkReturnOrConditionalDefaultPath,
        VAttribute(node) {
          if (isCodecFile || isPreTrialEntryFile) return;
          const parentElement = node.parent?.parent;
          const rawTag = parentElement?.rawName || parentElement?.name || "";
          const normalizedTag = rawTag.toLowerCase();
          const isRouterLink =
            normalizedTag === "router-link" || normalizedTag === "routerlink";
          if (!isRouterLink) return;

          if (
            !node.directive &&
            node.key?.type === "VIdentifier" &&
            node.key.name === "to" &&
            node.value?.value &&
            isBareWorkflowRoutePath(node.value.value)
          ) {
            context.report({
              node: node.value,
              messageId: "bareWorkflowPath",
              data: { target: node.value.value },
            });
            return;
          }

          if (
            node.directive &&
            node.key?.type === "VDirectiveKey" &&
            node.key.name?.name === "bind" &&
            node.key.argument?.type === "VIdentifier" &&
            node.key.argument.name === "to" &&
            node.value?.expression
          ) {
            const literalPath = getStaticStringValue(node.value.expression);
            if (literalPath && isBareWorkflowRoutePath(literalPath)) {
              context.report({
                node: node.value.expression,
                messageId: "bareWorkflowPath",
                data: { target: literalPath },
              });
            }
          }
        },
      },
      scriptVisitors
    );
  },
};
