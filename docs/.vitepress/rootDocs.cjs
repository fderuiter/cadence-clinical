"use strict";

/**
 * Canonical mapping from repository-root documentation filenames to their
 * temporary staged filenames inside `docs/` during VitePress dev and build.
 */
const STAGED_ROOT_DOCS = Object.freeze({
  "README.md": "README.md",
  "ARCHITECTURE.md": "ARCHITECTURE.md",
  "AGENTS.md": "AGENTS.md",
  "CONTEXT.md": "CONTEXT.md",
  "CONTRIBUTING.md": "CONTRIBUTING.md",
  "SECURITY.md": "SECURITY.md",
  "SUPPORT.md": "SUPPORT.md",
  LICENSE: "LICENSE.md",
});

module.exports = {
  STAGED_ROOT_DOCS,
};
