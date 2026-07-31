# ADR-053: Protocol Document Rendering Architecture and Content Contract

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Clinical trial protocol documents (comprising narrative content, synopsis, and the Schedule of Activities (SoA) matrix) must be compiled and rendered into standard, human-readable formats such as PDF and DOCX directly from digital clinical study definitions (conforming to CDISC USDM/DDF standards).
Directly traversing raw, normalized graph schemas or complex USDM models inside Jinja2 HTML templates or HTTP handlers introduces excessive architectural coupling, degrades query performance, and complicates version-control auditing.

To solve this, we must establish a clear architectural boundary defining:
1. Which service owns the templates and rendering orchestration.
2. The server-side rendering library stack for PDF and DOCX.
3. The threading boundaries for CPU-bound rendering tasks in asynchronous frameworks.
4. A standard, layout-agnostic, render-oriented data contract (presentation-centric view models) in `core-models` that cleanly bridges raw USDM data structures and presentation templates.

This decision implements requirements under Trace-7.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance and 21 CFR Part 11 Auditability):** Exported protocol documents are regulatory records. Every export metadata record must enforce GxP-compliant fields: creator identity, UTC timestamp, change reason, and a sequential version index.
* **Driver 2 (Separation of Concerns):** Templates and document assembly logic must not be coupled directly to raw Neo4j graph schemas or the standard CDISC USDM schema. They must receive flat, pre-ordered presentation-centric data models.
* **Driver 3 (Event-Loop Protection):** PDF generation (rendering HTML/CSS via a layout engine) and DOCX compilation are CPU-intensive, blocking operations. They must never block FastAPI's asynchronous event loop.
* **Driver 4 (Layout Quality & Advanced Printing Standards):** The Schedule of Activities (SoA) matrix requires wide, landscape page formatting. Running headers, footers, exact page numbers, and table-of-contents page references are mandatory.

## 3. Options Considered

### Option 1: Client-Side Document Rendering (Browser jsPDF / HTML Print)
Compile the presentation views on the client-side SPA and utilize browser printing features or JavaScript libraries (e.g., `jsPDF`, `docx.js`) to generate exports.
* **Pros:**
  * ✅ Offloads computing overhead to client devices.
  * ✅ Highly responsive interface with zero server-side rendering latency.
* **Cons:**
  * ❌ Lacks strict version control and deterministic layouts (output varies drastically depending on the browser version, installed system fonts, and OS).
  * ❌ Violates 21 CFR Part 11 integrity: the server cannot cryptographically sign or record audit trail records for client-generated files before they are outputted.
  * ❌ Poor CSS Paged Media support, making running headers/footers and dynamic page numbering extremely fragile.

### Option 2: Server-Side Low-Level PDF Library (ReportLab)
Use low-level canvas painting libraries like ReportLab to construct PDFs programmatically line-by-line, table-by-table.
* **Pros:**
  * ✅ High execution speed and native Python library with no external system/C dependencies.
* **Cons:**
  * ❌ Extremely high developer overhead. Programmatic canvas drawings make formatting tweaks (padding, page breaks, font styling) highly complex.
  * ❌ Violates separation of concerns by coupling document styling directly to Python code instead of standard CSS/HTML templates.

### Option 3: Server-Side HTML-to-PDF/DOCX via Jinja2, WeasyPrint, and python-docx (Selected)
Generate structured HTML from presentation models using Jinja2, compile PDFs using WeasyPrint (a Python-based visual layout engine with excellent support for CSS Paged Media), and compile DOCX files programmatically using `python-docx` or HTML conversion.
* **Pros:**
  * ✅ Separates document layout/style (HTML/CSS) from data compilation (Python).
  * ✅ Outstanding support for advanced CSS Paged Media standards (running page numbers, footers, page-break margins, landscape sections for wide tables).
  * ✅ Server-controlled generation ensures deterministic, pixel-perfect, reproducible documents that can be safely logged, versioned, and sealed.
  * ✅ Reuses python-docx for native-grade Microsoft Word files.
* **Cons:**
  * ❌ WeasyPrint depends on native C shared libraries (such as Pango, Cairo, and GObject), which must be installed on the host OS/Docker container.
  * ❌ CPU-bound execution can block FastAPI's event loop if not handled correctly.

## 4. Decision Outcome

* **Chosen Option:** Option 3
* **Justification:** Option 3 is the only approach that meets GxP standards for deterministic rendering, provides advanced CSS Paged Media features for printing clinical SoAs, and maintains clean code structure via Jinja2 HTML templates.

---

### Architectural Specifications & Boundaries

#### A. Service Ownership and Boundary
1. **Owner:** The **Metadata Designer microservice** (`apps/designer/`) owns study schemas, protocols, and the rule authoring engine. Therefore, it is the sole logic owner of template rendering configurations, Jinja2 template files, and export orchestrations.
2. **Gateway Role:** The API Gateway (`apps/gateway/`) exposes `/api/v1/designer/studies/{study_id}/export` endpoints, forwarding requests to the Designer microservice.

#### B. Synchronous Generation Boundaries
1. **The Problem:** WeasyPrint's PDF layout rendering is a synchronous, CPU-intensive C-extension wrapper. Running it directly inside an `async def` handler will block the entire process, including other active requests.
2. **The Solution:** The Designer service must execute all PDF rendering and DOCX generation operations inside standard thread pools (e.g. using FastAPI's `run_in_threadpool` helper or starlette background tasks), safely delegating the heavy computations to worker threads:
   ```python
   from fastapi.concurrency import run_in_threadpool

   pdf_bytes = await run_in_threadpool(render_pdf, presentation_model)
   ```
3. For very large protocols (>200 pages) requiring asynchronous generation feedback, the platform will utilize an out-of-band celery/redis worker queue, returning an HTTP 202 accepted status and a status-polling URL.

#### C. Native-Library Requirements
To ensure the container can execute PDF rendering, the Docker build files and local developer instructions must include the installation of WeasyPrint's native dependencies:
- **Debian/Ubuntu:** `apt-get install -y libpango1.0-dev libcairo2-dev libgirepository1.0-dev libglib2.0-0 shared-mime-info`
- **Alpine:** `apk add pango-dev cairo-dev gdk-pixbuf-dev glib-dev`

#### D. Render-Oriented Content Contract
Instead of supplying raw database structures or complex CDISC USDM models directly to templates, we establish a standardized presentation-centric view model library under `packages/core-models/protocol_render/models.py`.
The content contract mandates:
1. **Ordered Narrative Section Views:** Flattened sections and items pre-sorted chronologically or by standard hierarchy index, abstracting recursive relationships.
2. **Synopsis View:** High-level key attributes (phase, objectives, population, study design) compiled into standard, easily displayable flat string lists and values.
3. **SoA Matrix View:** An epoch-by-encounter-by-activity cell matrix, representing active procedures and timing applicability maps ready for tabular iteration in HTML/DOCX tables.
4. **Export Metadata:** Explicit 21 CFR Part 11 audit details (creator, timestamp, change reason, version index) nested alongside every persisted or exported protocol document version.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * ✅ High-fidelity, deterministic, printer-ready PDF output with professional layout control.
  * ✅ Completely decoupled database schema and rendering templates.
  * ✅ Full 21 CFR Part 11 compliant audit and signature metadata are cleanly captured.
* **Negative Impact / Technical Debt:**
  * ❌ Larger container base-image size due to native C rendering dependencies.
  * ❌ Memory usage spikes during extremely large table rendering; mitigated by enforcing pagination and chunking strategies if memory thresholds are exceeded.

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  - `packages/core-models/protocol_render/` (new package)
  - `pyproject.toml` (new dependencies)
  - `docs/adr/index.md` (registration)
* **Verification Plan:**
  1. Verify schema validation, default-factories, and Part 11 rules (such as `reason_for_change` non-empty constraint when `version_index > 1`) using automated tests.
  2. Verify that `usdm_model.Study` or related objects integrate perfectly within the presentation model schemas.
  3. Verify that standard build and dependency installations succeed with `uv lock`.
