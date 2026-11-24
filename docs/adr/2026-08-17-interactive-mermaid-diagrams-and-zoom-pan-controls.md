# ADR-2383: Interactive Mermaid Diagrams and Responsive Layout Controls

Also known as: ADR-097 (former number)

- **Status:** Accepted
- Date: 2026-08-17
- Authors: @fderuiter
- Deciders: @fderuiter

---

## 1. Context & Problem Statement

The documentation portal for Cadence Clinical relies heavily on visual aids, including architecture flowcharts and sequence diagrams. Historically, these system diagrams were displayed either as raw static ASCII art (which overflows viewports and is difficult to read on smaller screens) or as raw text-based Mermaid code blocks without compilation. To ensure a seamless onboarding experience and robust readability for clinical systems engineers and technical reviewers across all device sizes, we need an automated, interactive rendering engine built directly into the static documentation site.

This decision implements requirements under Trace-1.

## 2. Decision Drivers & Constraints

- **Accessibility & Responsiveness:** Diagrams must scale responsively down to mobile viewports without causing page-level horizontal overflow or rendering unreadable text.
- **Performance & SEO:** Diagrams should render static assets or compile at build-time to avoid client-side rendering lag and maintain search indexing speed, rather than loading large third-party runtime bundles on the fly.
- **Usability & Interaction:** Large-scale workflows and sequence flows must support panning, zooming, and resetting controls to allow detailed examination of granular clinical execution steps.
- **Theme Integration:** Rendered diagram colors must automatically synchronize with the user's selected global theme (light/dark reading modes) to preserve readability and design consistency.

## 3. Options Considered

### Option 1: Static Image Exports (PNG/SVG) from External Design Tools

- **Overview:** Maintain diagrams in external editors and export them as static SVGs/PNGs to include in Markdown files.
- _**Pros:**_
  - ✅ High visual design precision and zero build-time impact.
- _**Cons:**_
  - ❌ No integration with dynamic theme switching (light/dark mode).
  - ❌ Heavy maintenance burden to keep diagrams in sync with active codebase changes.
  - ❌ Lacks interactive zooming/panning/resetting out of the box.

### Option 2: Build-Time Syntax Verification with Deferred Client SVG Rendering & Interactive Wrapper

- **Overview:** Transform Markdown `mermaid` code blocks into inert `<div class="mermaid" v-pre data-mermaid-code="...">` placeholders during VitePress compilation (`mermaidFencePlugin` in `docs/.vitepress/repoLinks.mjs`), lazily loading `mermaid` only on pages containing diagrams (`startOnLoad: false`, `securityLevel: "strict"`) and rendering theme-aware SVGs on the client across initial mount and `html.dark` toggles. Pair deferred client rendering with deterministic build-time JSDOM `mermaid.parse()` verification (`scripts/build-docs.js`, `docs/.vitepress/navigation.test.mjs`) and Markdown fence parity checks (`MarkdownValidatorService.cs`), and supplement rendered diagrams with mutation-aware interactive wrappers (`docs/.vitepress/theme/index.js` and CSS styles) that dynamically resolve the active live `<svg>` for zoom, pan, and reset controls (+, -, ↺).
- _**Pros:**_
  - ✅ Build-time parser and fence gates block malformed diagrams or mismatched code fences before static deployment without launching headless browsers.
  - ✅ Lazy client-side `mermaid.render()` with `startOnLoad: false` avoids bundling Mermaid into non-diagram routes while live-SVG `MutationObserver` rebinding synchronizes light/dark modes without detaching interactive controls.
  - ✅ Interactive controls prevent horizontal scrollbars and allow clear viewing on mobile viewports.
- _**Cons:**_
  - ❌ Introduces a workspace dependency on `mermaid` and executes client-side SVG rendering on diagram pages.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 provides a fully responsive, interactive, and maintainable diagram architecture that aligns with our developer workflows, documentation quality standards, and GxP requirements for standard system tracking and audit capabilities (PRD-SYS-001). All diagrams are authored directly in Markdown, verified at build time against Mermaid's parser and CommonMark fence rules, lazily rendered on the client for seamless light/dark theme fidelity, and enhanced with mutation-safe zoom/pan controls.

## 5. Consequences & Trade-offs

- **Positive Impact:** Documentation maintains a clean, modern aesthetic with readable, theme-synchronized diagrams that auto-scale and preserve zoom/pan interactivity across theme switches, non-diagram routes avoid eager Mermaid bundle payloads, and pre-commit and CI gates fail closed on syntax or fence regressions.
- **Negative Impact / Technical Debt:** Requires `mermaid` and `jsdom` in the workspace and runs a JSDOM-backed `mermaid.parse()` pass during `pnpm docs:build` and Vitest runs.
- **Mitigation Strategy:** Pin dependency versions, defer Mermaid loading (`startOnLoad: false`) strictly to routes with `.mermaid` placeholders, and validate syntax in-process via JSDOM rather than spawning browser instances during standard builds.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Documentation and tooling files (`package.json`, `docs/.vitepress/config.mjs`, `docs/.vitepress/repoLinks.mjs`, `docs/.vitepress/theme/index.js`, `docs/.vitepress/theme/custom.css`, `docs/.vitepress/navigation.test.mjs`, `scripts/build-docs.js`, `packages/tooling_core/src/Services/MarkdownValidatorService.cs`, `packages/tooling_core/src/Markdown/MarkdownParser.cs`).
- **Verification Plan:** Verify documentation gates and compilation by running `pnpm docs:build`, `pnpm exec vitest run --config vitest.config.mjs`, `pnpm cadence check markdown`, and `pnpm cadence check adrs`.
