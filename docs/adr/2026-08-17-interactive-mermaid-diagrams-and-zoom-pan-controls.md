# ADR-097: Interactive Mermaid Diagrams and Responsive Layout Controls

* Status: Accepted
* Date: 2026-08-17
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
The documentation portal for Cadence Clinical relies heavily on visual aids, including architecture flowcharts and sequence diagrams. Historically, these system diagrams were displayed either as raw static ASCII art (which overflows viewports and is difficult to read on smaller screens) or as raw text-based Mermaid code blocks without compilation. To ensure a seamless onboarding experience and robust readability for clinical systems engineers and technical reviewers across all device sizes, we need an automated, interactive rendering engine built directly into the static documentation site.

## 2. Decision Drivers & Constraints
* **Accessibility & Responsiveness:** Diagrams must scale responsively down to mobile viewports without causing page-level horizontal overflow or rendering unreadable text.
* **Performance & SEO:** Diagrams should render static assets or compile at build-time to avoid client-side rendering lag and maintain search indexing speed, rather than loading large third-party runtime bundles on the fly.
* **Usability & Interaction:** Large-scale workflows and sequence flows must support panning, zooming, and resetting controls to allow detailed examination of granular clinical execution steps.
* **Theme Integration:** Rendered diagram colors must automatically synchronize with the user's selected global theme (light/dark reading modes) to preserve readability and design consistency.

## 3. Options Considered
### Option 1: Static Image Exports (PNG/SVG) from External Design Tools
* **Overview:** Maintain diagrams in external editors and export them as static SVGs/PNGs to include in Markdown files.
* **Pros:**
  * ✅ High visual design precision and zero build-time impact.
* **Cons:**
  * ❌ No integration with dynamic theme switching (light/dark mode).
  * ❌ Heavy maintenance burden to keep diagrams in sync with active codebase changes.
  * ❌ Lacks interactive zooming/panning/resetting out of the box.

### Option 2: Build-Time Compilation with vitepress-plugin-mermaid & Custom Interactive Vue Wrapper
* **Overview:** Utilize `vitepress-plugin-mermaid` to natively compile standard Markdown Mermaid code blocks into SVGs during the build phase. Supplement the output with custom Vue interactivity wrappers (`docs/.vitepress/theme/index.js` and CSS styles) to provide custom controls (+, -, ↺) along with drag-to-pan/touch interactions.
* **Pros:**
  * ✅ Build-time compilation ensures zero runtime performance penalty and keeps SEO fast.
  * ✅ Full CSS theme integration natively synchronizes light/dark modes.
  * ✅ Interactive controls prevent horizontal scrollbars and allow clear viewing on mobile viewports.
* **Cons:**
  * ❌ Introduces a build-time dependency on `mermaid` and `vitepress-plugin-mermaid`.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 provides a fully responsive, interactive, and maintainable diagram solution that aligns perfectly with our developer workflows and documentation quality standards. All diagrams are authored directly in Markdown, compiled at build-time, and enhanced with interactive zoom/pan controls to optimize the reading experience on any screen.

## 5. Consequences & Trade-offs
* **Positive Impact:** Documentation maintains a clean, modern aesthetic with highly readable diagrams that auto-scale. Developers can edit architecture specifications using text blocks directly in Git.
* **Negative Impact / Technical Debt:** Added `mermaid` and `vitepress-plugin-mermaid` to root `package.json`, which slightly increases build and installation times.
* **Mitigation Strategy:** Pin dependency versions and configure VitePress caching to minimize build overhead during continuous integration.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Documentation workspace files (`package.json`, `pnpm-lock.yaml`, `docs/.vitepress/config.mjs`, `docs/.vitepress/theme/index.js`, `docs/.vitepress/theme/custom.css`).
* **Verification Plan:** Verify success of documentation compilation by running `pnpm run build:docs` and ensuring the ADR is correctly indexed by running `python3 scripts/validate_adrs.py`.
