function createStubProgram(code) {
  const lines = code.split(/\r?\n/);
  const endLine = Math.max(1, lines.length);
  const endCol = lines[endLine - 1]?.length ?? 0;
  return {
    ast: {
      type: "Program",
      body: [],
      sourceType: "module",
      tokens: [],
      comments: [],
      range: [0, code.length],
      loc: {
        start: { line: 1, column: 0 },
        end: { line: endLine, column: endCol },
      },
    },
  };
}

export const cssStubParser = {
  meta: { name: "css-stub-parser", version: "1.0.0" },
  parseForESLint(code) {
    return createStubProgram(code);
  },
};

export const plainTextEslintParser = {
  meta: { name: "cadence-plain-text-parser", version: "1.0.0" },
  parseForESLint(text) {
    return createStubProgram(text);
  },
};
