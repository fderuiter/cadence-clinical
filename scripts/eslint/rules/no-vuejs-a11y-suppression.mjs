export const noVuejsA11ySuppressionRule = {
  meta: {
    type: "problem",
    docs: {
      description:
        "Disallow eslint-disable directives targeting vuejs-accessibility rules across shared UI primitives and live operational hub files.",
      category: "Accessibility",
    },
    messages: {
      noVuejsA11ySuppression:
        "Suppressing vuejs-accessibility rules via eslint-disable is prohibited in shared UI primitives and live clinical hubs. Remediate the underlying semantic HTML, keyboard, or ARIA label defect instead.",
    },
  },
  create(context) {
    function inspectComments(programNode) {
      const seen = new Set();
      const comments = [
        ...(context.sourceCode?.getAllComments?.() || []),
        ...(programNode?.templateBody?.comments || []),
      ];
      for (const comment of comments) {
        const key = comment.range
          ? `${comment.range[0]}:${comment.range[1]}`
          : `${comment.loc?.start.line}:${comment.loc?.start.column}`;
        if (seen.has(key)) continue;
        seen.add(key);

        const text = comment.value || "";
        if (
          /\beslint-disable(?:-next-line|-line)?\b/.test(text) &&
          /\bvuejs-accessibility(?:\/|\b)/.test(text)
        ) {
          context.report({
            loc: comment.loc,
            messageId: "noVuejsA11ySuppression",
          });
        }
      }
    }

    const parserServices =
      context.sourceCode?.parserServices || context.parserServices;
    if (parserServices?.defineTemplateBodyVisitor) {
      return parserServices.defineTemplateBodyVisitor(
        {},
        {
          "Program:exit"(programNode) {
            inspectComments(programNode);
          },
        }
      );
    }

    return {
      "Program:exit"(programNode) {
        inspectComments(programNode);
      },
    };
  },
};
