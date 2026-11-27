import path from "node:path";
import {
  EXEMPT_2D_CLINICAL_MATRICES,
  LEGACY_WEB_RAW_TABLE_ALLOWLIST,
} from "../allowlists/index.mjs";

export const noRawClinicalTableRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow raw <table> markup in migrated clinical hubs and apps/subject-portal; require ClinicalDataTable from packages/ui unless exempted as a 2D clinical matrix (ADR-2673).",
      category: "Accessibility",
    },
    messages: {
      noRawClinicalTable:
        'Raw <table> markup is prohibited outside exempted 2D clinical matrices. Use ClinicalDataTable from "ui" to preserve keyboard sorting, focusable row activation, sr-only captions, and polite pagination status (ADR-2656 / ADR-2673).',
    },
  },
  create(context) {
    const filename = (context.filename || context.getFilename() || "")
      .split(path.sep)
      .join("/");
    if (
      !filename.endsWith(".vue") ||
      filename.includes("/packages/ui/src/components/")
    ) {
      return {};
    }

    const webSrcMarker = "/apps/web/src/";
    const webSrcIdx = filename.lastIndexOf(webSrcMarker);
    const relWebSrcPath =
      webSrcIdx >= 0 ? filename.slice(webSrcIdx + webSrcMarker.length) : null;

    if (
      relWebSrcPath !== null &&
      (EXEMPT_2D_CLINICAL_MATRICES.has(relWebSrcPath) ||
        LEGACY_WEB_RAW_TABLE_ALLOWLIST.has(relWebSrcPath))
    ) {
      return {};
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (!parserServices?.defineTemplateBodyVisitor) {
      return {};
    }

    return parserServices.defineTemplateBodyVisitor({
      VElement(node) {
        const rawTag = node.rawName || node.name || "";
        if (rawTag === "table") {
          context.report({
            node: node.startTag,
            messageId: "noRawClinicalTable",
          });
        }
      },
    });
  },
};
