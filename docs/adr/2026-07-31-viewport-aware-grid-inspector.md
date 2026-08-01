# ADR 2026-07-31: Viewport-Aware Grid Inspector for eCRF designer

## Status
Accepted

## Context
During Electronic Case Report Form (eCRF) design, study designers frequently lay out multi-column grids (e.g., 12-column layouts) without realizing how they will adapt to smaller screens (tablet and mobile). Historically, this lack of responsive preview has led to unexpected layout flattening, awkward tab-order sequences, and clipped labels during clinical trials on mobile devices. Fixing these layout issues post-deployment required running costly and time-consuming database revision cycles.

To shift these quality checks left, we required a real-time responsive simulation tool integrated into the clinical form builder.

## Decision
We implemented a client-side **Viewport-Aware Grid Inspector**:
1. **Client-Side-Only Layout Engine:** All layout simulation and warning rules are computed in the frontend via Pinia (`designer.js`). This avoids expensive network roundtrips to the backend during rapid window scaling and ensures a fluid 60fps UX.
2. **Warn, Don't Auto-Correct:** To preserve GxP compliance and schema predictability, the inspector only *flags* layout violations (when simulated column width is under 150px). It never alters the user's layout settings or clinical schema attributes automatically.
3. **Compiler Gating with Explicit Override:** To enforce responsive validation without blocking edge-case designs, the eCRF compiler blocks transition if active warnings exist unless the designer actively acknowledges and dismisses the warning via an override checkbox (`#dismiss-warnings-checkbox`).

## Alternatives Considered
- **Backend-based responsive checks:** We evaluated compiling drafts on the backend to test layout constraints, but it was too slow for rapid client-side viewport toggling and real-time canvas resizing.
- **Auto-resizing columns:** Automatically adjusting columns to prevent overlaps was rejected because automated schema updates interfere with clinical compliance audits and GxP requirements.

## Trade-offs
- **Positive:** Reduces post-deployment eCRF schema revisions. Empowers designers to diagnose and resolve column-wrap issues in under a minute directly within the authoring canvas.
- **Negative:** Relies on client-side estimation, which approximates but does not guarantee absolute layout layout precision across all downstream mobile devices.

This decision supports requirements under Trace-8 and Trace-11.
