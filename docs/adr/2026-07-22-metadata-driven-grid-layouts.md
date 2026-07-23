# ADR 2026-07-22: Metadata-Driven Grid Layouts via Jinja2 Templating

## Status
Accepted

## Context
Previously, the background translation engine compiled clinical data into flat, unstyled XML documents via imperative Python `ElementTree` generation. This lacked visual metadata and standard Enketo layout parameters, which forced downstream systems to use fragile custom CSS workarounds to achieve multi-column forms for clinical execution.

To support robust responsive forms that accurately render clinical designs on multiple device formats natively, we needed a way to interpret multi-column properties from the USDM specifications and compile them into OpenRosa XForms constraints and visual classes without impacting strict CDISC ODM structures.

## Decision
We will shift to **Metadata-Driven Grid Layouts** natively mapped from USDM definitions.
1. **Declarative Jinja2 Templating:** We transition away from imperative XML construction and instead use Jinja2 templates (`odm_template.xml.j2` and `openrosa_template.xml.j2`).
2. **Metadata Extraction:** USDM parsing now intercepts formatting attributes such as `cols`, `column_span`, and `span`, converting layout configurations into compliant Enketo appearance classes (`w1`-`w4`).
3. **Decoupled Architecture:** Schema structure generation (CDISC ODM) remains strictly decoupled from visual representation (OpenRosa / Enketo layouts).

## Alternatives Considered
- **Enhancing ElementTree Logic:** Maintaining Python-based imperative XML generation was considered, but embedding UI-layer layout mapping mixed with ODM schema rules inside a background worker rapidly decreased maintainability.
- **Client-Side Interpretation:** We evaluated having the frontend parse raw USDM directly, but it violates our architectural principle that standard EDC clients should only deal with fully-compiled XForms.

## Trade-offs
- **Positive:** UI generation is fully declarative, easily readable, and highly maintainable. Layout constraints scale responsively using established OpenRosa standard width semantics. Schema conformance remains protected.
- **Negative:** We introduce a minor dependency on Jinja2 for text manipulation, requiring parsing safety to strip out empty XML whitespace blocks.
