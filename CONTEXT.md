# Clinical Domain Glossary

### Protocol Amendment

A formal, versioned modification to an approved clinical study protocol. Protocol amendments can introduce changes to study arms, epochs, visit schedules, procedures, dosage cohorts, or safety requirements, and may be designated as requiring subject re-consent.

### Immutable Graph Branching

A metadata management pattern where approved and published protocol versions remain permanently immutable in the graph database. New draft amendments fork the metadata hierarchy into an isolated version node linked by predecessor relationships, ensuring ongoing clinical execution is never disrupted.

### Dynamic Subject Schema Projection

A runtime execution pattern where eCRF forms, visit schedules, observation fields, and validation rules are resolved dynamically based on each individual subject's active protocol version tag, rather than a single static database schema.

### Non-Destructive Historical Retention

The clinical compliance guarantee that observations, form submissions, and visit records completed under earlier protocol versions remain permanently intact and bound to their historical schema definitions, preventing retroactive data corruption or loss.

### Re-Consent Gating

A regulatory enforcement mechanism that automatically blocks site investigators and coordinators from recording clinical data for upcoming visits when a protocol amendment requiring re-consent is published, until a signed and verified Informed Consent Form (ICF) matching the amendment version is registered for the subject.

### Unified Study Data Model (USDM)

The CDISC TransCelerate standard defining a machine-readable protocol representation including study design elements, arms, epochs, encounters, activities, and biomedical concepts.

### Schedule of Activities (SoA) Matrix

A high-density clinical matrix representing the intersection of study encounters/visits with protocol activities/procedures across study epochs and arms.

### Biomedical Concept (BC)

A standardized clinical entity definition (e.g. Systolic Blood Pressure, 12-Lead ECG) encapsulating value-level metadata, data types, unit catalogs, and direct mapping to CDASH and SDTM variables.

### Global Metadata Repository (MDR)

A centralized enterprise catalog of reusable, version-controlled clinical design assets (Forms, Data Elements, Arms, Visits, Rules) with formal lifecycle governance (Draft, In Review, Approved, Published, Archived).

### Instantiated-From Provenance

A graph relationship pattern linking study-specific design nodes back to their originating Global Library templates, permitting local study customization while preserving provenance and notifying authors of upstream updates.

### Digital Data Flow (DDF)

The automated, event-driven compilation and handoff pipeline transforming study design specifications and biomedical concepts into executable CDASH eCRF definitions, edit check rules, and CDISC ODM packages.

### ICH M11 Narrative Synchronization

A bidirectional authoring architecture where the USDM graph serves as the source of truth, and embedded `<usdm:ref>` tokens within ICH M11 protocol narrative sections maintain live parity between human-readable text and machine-readable data.

### Multi-Layer Semantic Protocol Diff

A structured comparison engine that evaluates differences between two protocol versions across USDM Graph Structure, SoA Matrix, Eligibility Logic, and eCRF Schemas to generate regulatory amendment summaries and downstream migration directives.

### Granular Entity Lease

A fine-grained concurrency control mechanism that acquires scoped editing locks on specific protocol sections or entities (encounters, forms, narrative blocks) paired with optimistic version indices to prevent collaboration collisions without blocking the entire study.

### Protocol Quality Sentinel

A continuous validation engine evaluating study designs against CDISC USDM rules, circular dependency checks, patient/site burden scoring, readability indices, and synthetic cohort eligibility feasibility simulations.

### eISF Regulatory Binder Taxonomy

A template-driven, hierarchical document classification standard derived from ICH GCP E6(R2) Section 8 and the DIA eISF Reference Model. Establishes a standardized structural baseline (e.g. Protocol, Regulatory/IRB, Staff Qualifications, DOA/Training, IP Accountability, Safety, Monitoring) with protocol-level inheritance and site-level extension codes while maintaining deterministic cross-system mapping to Sponsor eTMF artifacts.

### Storage Port & Metadata Envelope Pattern

An architectural decoupled storage pattern where binary blobs (documents, medical images, audio/video, attachments) reside in tenant-isolated S3/MinIO object buckets, while regulatory metadata, version indices, virus scan certifications, SHA-256 cryptographic checksums, and 21 CFR Part 11 signature manifests reside in a relational database envelope. Pre-signed upload and download URLs are issued only against pre-registered, audit-controlled relational document entities.

### Legal Retention Hold

A regulatory compliance lock applied to clinical trial documents, regulatory binders, or audit logs that strictly supersedes and suspends automated document lifecycle expiration or pruning policies, preventing deletion, redaction, or physical purging while litigation, inspection, or regulatory inquiries remain active.

### Persona Context Switching

A multi-tenant role simulation and test harness pattern that allows authorized users or automated verification suites to switch execution contexts dynamically across distinct clinical personas (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`) without session corruption, validating role-based authorization gates, PII de-identification masks, and golden path workflows.

### Bi-Directional DDF Compilation

The continuous, automated synthesis pipeline that translates high-level CDISC USDM study definitions into runtime EDC eCRF definitions, encounter visit matrices, and declarative edit-check rules, while feeding execution metrics and discrepancy telemetry back into upstream study design sentinels.

### Module-Based Ticket Routing

A deterministic operational routing pattern mapping clinical inquiries, data discrepancies, and system support tickets to specialized functional queues based on clinical domain ownership (`PROTOCOL_DESIGNER`, `DATA_CAPTURE_ECRF`, `SUBJECT_MANAGEMENT_RTSM`, `REPORTING_ANALYTICS`, `PLATFORM_ADMIN_ACCESS`, `REGULATORY_COMPLIANCE`).

### Dual-Target SLA Model

A service-level agreement tracking model establishing decoupled response-time commitments for initial acknowledgment/triage versus final clinical resolution, with automated priority escalation on breach and clock suspension during external waiting states.

### Controlled Ticket Reopening Window

A regulatory compliance lifecycle rule permitting historical support or discrepancy records to transition from resolved or closed status back to an active state only within a bounded calendar timeframe and accompanied by an explicit 21 CFR Part 11 reason for change, after which records are permanently immutable.

### Mandatory GxP Notification Policy

A regulatory compliance rule ensuring that high-severity clinical trial alerts, service level agreement (SLA) breaches, urgent protocol amendments, and direct patient-safety or task assignments strictly bypass user opt-out preferences and are delivered across all designated communication channels (in-app, email, webhooks).

### Multi-Channel Notification Dispatcher

An asynchronous, decoupled delivery engine that routes event notifications across multiple transport channels (In-App inbox, SMTP Email with domain-specific Jinja2 templates, and HMAC-signed Outbound Webhooks) backed by bounded exponential backoff retries and 21 CFR Part 11 audit trails.

### Deterministic Notification Idempotency Key

A structured composite identifier (`{entity_type}:{entity_id}:{event_type}:{version_index}`) that uniquely identifies a notification dispatch trigger, preventing duplicate delivery tasks and phantom alerts across rapid user edits, retry sweeps, and background worker polling cycles.

### Two-Tier Controlled Document Model

A regulatory metadata and content storage pattern where an entity head record (`KnowledgeArticle`) maintains current lifecycle state, active published version pointers, taxonomy tags, and persona access constraints, while version snapshot records (`KnowledgeArticleVersion`) permanently retain immutable historical revisions of approved clinical and regulatory guidance.

### Contextual Help Route Mapping

A deterministic routing rule mapping frontend UI route patterns and active user personas to approved operational SOPs and guidance articles with specificity- and priority-based tie-breaking.

### Hierarchical Specificity Scoring

A deterministic multi-tier resolution algorithm that evaluates candidate contextual help mappings by route pattern specificity (exact > parameterized > longest prefix > global wildcard), persona specificity (exact persona match > universal wildcard), administrator priority weight, and recency tie-breaking to select primary and secondary guidance articles.

### Contextual Help Drawer

A slide-in interface panel anchored to the application shell that dynamically presents context-sensitive procedural SOPs and clinical guidance without compressing or reflowing full-width clinical workspaces.

### Fallback Escalation

An interactive error-recovery pattern providing instant cross-knowledge-base search and pre-populated support ticket creation when no explicit guidance article maps to a user's current route and persona context.

### Native Stdio MCP Server

A Model Context Protocol endpoint exposed directly by the Cadence CLI binary to provide typed tool execution, schema introspection, and zoom mechanics for AI coding agents without shell escaping or unstructured text parsing.

### Two-Tier Staged Pre-Commit Hook

A fast git pre-commit validation pipeline that executes file-level static checks and formatters on staged hunks in sub-500ms followed by repository-level import boundary and fast unit test assertions in sub-1.5s.

### Authored Terminal Document

A structured CLI rendering pattern that formats command execution output as a cohesive, readable terminal document with narrative headings, status indicator cards, column alignment, clickable file links, and contextual next-action CTAs.

### Deterministic Tooling Self-Healing

An automated developer tooling remediation pattern that resolves non-destructive environment drift (SQLite initializations, git hooks, code format, import sorting, ADR indexing, OpenAPI schema exports) while providing guided diagnostic CTAs for external container and network states.

### Transitive Test Dependency Graph

A static AST analysis and dependency resolution model that evaluates Python and TypeScript source file import trees to calculate the precise, minimal set of downstream unit and integration tests affected by a changeset, enabling sub-second targeted test cycles.

### Ephemeral In-Memory Database Harness

An isolated test execution pattern where fast unit tests and TDD slices run against zero-IO in-memory SQLite instances and mock graph repositories, bypassing PostgreSQL and Neo4j network services to guarantee sub-500ms test iteration loops.

### Progressive Disclosure Zoom Token

A stateful or deterministic reference token emitted in MCP tool summary envelopes that allows AI coding agents to drill into granular error traces, telemetry logs, or full schema snapshots on demand without polluting model context windows.

### Incremental GxP Verification Cache

A compliance artifact caching mechanism that records test run outcomes and JUnit XML fragments keyed by git tree hashes, allowing localized GxP Traceability Matrix generation in sub-2 seconds while reserving full suite execution for merge-gate verification.

### Unified Sentinel Plugin Engine

A modular architecture unifying repository sentinels (drift detection, duplication analysis, ADR validation, schema verification) under a typed, concurrent check interface that emits structured JSON telemetry and formatted terminal reports.

### AI Gateway Microservice

A dedicated internal routing and inference microservice (`apps/ai_gateway`) that encapsulates model provider orchestration, token rate-limiting, cost accounting, tier-based model selection, and prompt template execution behind a uniform HMAC-authenticated REST API.

### Three-Tier Model Routing Strategy

A cost-control and performance routing policy categorizing AI workloads into Tier 1 (local SLMs and embedded vector encoders), Tier 2 (fast, high-throughput cost-effective cloud LLMs), and Tier 3 (frontier multi-modal reasoning models reserved for complex clinical synthesis).

### In-Flight De-Identification Air-Gap

A privacy protection pattern where outbound prompts sent to external cloud model providers are automatically scrubbed of Protected Health Information (PHI) and Personally Identifiable Information (PII) using surrogate tokens, with reverse unmasking executed in ephemeral memory or short-lived encrypted cache upon response reception.

### Dual-Attribution AI Compliance Ledger

A 21 CFR Part 11 regulatory record pattern where entities generated or modified by AI are flagged in a draft state (`DRAFT_AI`) paired with generation metadata (model version, prompt hash, confidence score) and require human-in-the-loop review and cryptographic electronic signature verification before entering active clinical status.

### Hybrid Semantic Medical Coding

A clinical coding pattern combining exact and lexical hierarchy navigation with dense vector embeddings to match verbatim medical terms against authoritative MedDRA and WHODrug dictionaries, yielding deterministic candidate rankings with mathematical cosine similarity scores.

### Grounded Protocol Citation Extraction

A retrieval-augmented generation validation pattern that bounds AI responses to explicit source document coordinates (protocol version, section, page number) and suppresses generation when citation faithfulness or retrieval confidence falls below established regulatory safety thresholds.

### eConsent Readability Harmonization

An interactive Informed Consent Form (ICF) authoring workflow that calculates standardized readability metrics (Flesch-Kincaid, Dale-Chall) and generates patient-friendly vocabulary substitutions without mutating underlying legal provenance or IRB version indexing.

### Multimodal DIA TMF Classifier

A document processing pipeline combining optical character recognition (OCR), visual layout analysis, and DIA TMF Reference Model taxonomy matching to extract regulatory metadata, verify signature completeness, and stage classified artifacts for Clinical Research Associate (CRA) quality control.

### Cross-Domain Clinical Discrepancy Worker

An asynchronous background evaluation worker that detects contextual inconsistencies across correlated eCRF domains (e.g. Adverse Events, Concomitant Medications, Laboratory Diagnostics) and generates staged query drafts for human Data Manager adjudication.

### Protocol Digitization Stage DAG

An asynchronous multi-stage directed acyclic graph workflow in `apps/designer` that decomposes protocol PDF ingestion into checkpointed transformations (layout parsing, SoA extraction, biomedical concept mapping, and eCRF synthesis) with strict schema validation gates at each stage boundary.

### Protocol Amendment Impact Assessment

An automated analysis engine evaluating graph and narrative deltas between protocol revisions to quantify operational scope changes, calculate subject re-consent requirements, and generate domain-routed operational tickets.

### Generative Pharmacovigilance Narrative

A regulatory narrative drafting pipeline in `apps/safety` that compiles chronological de-identified clinical event streams from eCRF data into FDA MedWatch and CIOMS-I compliant narrative summaries gated behind physician electronic signatures.

### Hybrid Interoperability Semantic Mapping

An integration pattern in `apps/interop` combining pre-compiled deterministic FHIR-to-CDISC ConceptMaps with confidence-gated embedding and LLM fallbacks for unstructured electronic health record (EHR) data.

### Embedded AI Action Primitive

A standardized frontend component pattern (`<AiActionButton>`, `<AiSuggestionDrawer>`) providing contextual AI triggers, visual confidence indicators, diff inspections, and role-governed human review actions within high-density clinical workspaces.

### Interactive Credential Bootstrapping

A structured, step-by-step human-in-the-loop CLI workflow that automates 256-bit cryptographic secret generation, guides third-party dashboard navigation for external API keys, performs idempotent `.env` upserts, and syncs secrets to CI/CD repositories.

### Multi-Tier Environment Provisioning

An environment configuration strategy distinguishing between developer local fast-paths (zero-touch local storage, auto-generated HMAC keys) and production compliance paths (external S3 storage, real OIDC/SMTP secrets, and strict Part 11 entropy validation).

### Automated Trigger-to-Payable Engine

A financial lifecycle pattern where completed eCRF visits, forms, and CRA monitoring verification events automatically compute scheduled site reimbursements, split payees, and withholding reserves based on versioned investigator grant contracts.

### Zero-Footprint Blinded DICOM Adjudication

A clinical imaging workflow combining in-flight pixel and metadata PHI scrubbing, web-based image measurement tools (RECIST 1.1, Lugano), and dual-independent blinded reader evaluation with automated tie-breaker escalation to an Adjudication Committee Chair.

### Deterministic Global Regulatory Reporting Engine

A pharmacovigilance decision tree that evaluates Individual Case Safety Reports (ICSRs) against multi-country reporting criteria (FDA 7/15-day expedited, EMA EudraVigilance, PMDA, MHRA) with countdown deadline management.

### AS2 Regulatory B2B Gateway

A secure EDI communications adapter providing direct, encrypted Applicability Statement 2 transmission of ICH E2B(R3) safety XML packages to regulatory agencies with cryptographic Message Disposition Notification (MDN) verification.

### Multi-Tier Regulatory Acknowledgement Lifecycle

A monotonic regulatory verification state machine that tracks the end-to-end receipt of ICH E2B(R3) safety transmissions across perimeter transport (ACK A / MDN with SHA-256 MIC integrity check), schema syntax validation (ACK B), and health authority database ingestion (ACK C) with automated 21 CFR Part 11 audit ledger sealing.

### Multi-Echelon Clinical Supply Optimization

An RTSM inventory pattern tracking investigational product shipments across manufacturing, central depots, regional depots, and sites with predictive automated replenishment based on real-time enrollment velocity.

### Mean Kinetic Temperature (MKT) Excursion Engine

An automated cold chain evaluation algorithm that calculates MKT across temperature logger data streams, compares cumulative exposure against stability budgets, and quarantines impacted drug kits.

### Vein-to-Vein Chain of Custody (COC/COI)

A traceability standard for cell and gene therapies enforcing deterministic subject-to-lot identity binding across apheresis collection, cryogenic transport, cell engineering, and infusion.

### Centralized Statistical Monitoring (CSM)

An ICH E6(R3) data integrity surveillance model evaluating cross-site statistical distributions, digit preference, variance anomalies, and multivariate correlation clusters to detect data fabrication and systemic site errors.

### TransCelerate RACT Matrix

A formal Risk Assessment and Categorization Tool linking identified protocol operational risks directly to dynamic Key Risk Indicators (KRIs) and targeted monitoring thresholds.

### Clinical Data Repository (CDR) Lakehouse

A centralized, versioned clinical data pool aggregating multi-source streams (EDC, ePRO, central/local labs, connected sensors, EHR FHIR) with automated transformation into CDISC SDTM and ADaM models.

### Validated Statistical Computing Environment (SCE)

A 21 CFR Part 11 compliant analytical workspace providing containerized R and Python statistical execution environments with version-pinned dataset snapshots, package integrity locks, and immutable analysis audit trails.

### Connected Study Training Compliance

A continuous qualification engine that maps protocol amendments and operational SOP revisions into required training curricula for site investigators and CRAs, enforcing automated gating on EDC write permissions until training completion is digitally verified.

### Decentralized Clinical Trial (DCT) Connected Sensor Stream

An IoT ingestion pipeline parsing continuous biometric data (CGM, actigraphy, spirometry, blood pressure) via standard Open mHealth / FHIR formats with automated time-series outlier flags and compliance tracking.

### Greenlight Site Activation Engine

An automated multi-factor governance sentinel that programmatically prevents RTSM subject randomization, IP kit shipment, and eCRF data entry until all prerequisite conditions (IRB approval certificate, executed CTA, verified eISF binder, executed DOA log, PI and site staff training certifications, and depot release) are digitally verified.

### SMART on FHIR eSource Ingestion

An interoperability pipeline extracting structured clinical data (demographics, vital signs, laboratory results, concomitant medications) directly from hospital Electronic Health Record (EHR) systems via HL7 FHIR USCDI standards into draft eCRFs with explicit source provenance and CRC click-to-commit review.

### Automated Laboratory Toxicity Grading

A clinical safety evaluation engine that harmonizes central and local laboratory test results across disparate reference ranges and units (SI and conventional), evaluates values against standardized toxicity scales (NCI-CTCAE v5.0, DAIDS), and automatically generates staged Adverse Event drafts upon detection of Grade 3 or 4 toxicities.

### Clinical Endpoint Adjudication Committee (CEC)

A chartered, independent panel of clinical specialists responsible for evaluating blinded, de-identified patient event dossiers (medical narratives, diagnostic records, laboratory panels, imaging reports) against standardized diagnostic criteria to produce verified endpoint classifications (e.g. MACE, stroke, mortality) for statistical analysis and CDISC SDTM CE domain export.

### Aggregate Safety Compilation Engine

An automated pharmacovigilance reporting system that calculates cumulative and interval patient-years of exposure, tabulates SAE and SUSAR line listings by MedDRA System Organ Class and Preferred Term against Reference Safety Information (RSI), and compiles structured annual Development Safety Update Reports (DSUR), Periodic Safety Update Reports (PSUR), and investigator safety notifications.

### Medical Device Investigation Lifecycle

A regulatory and operational framework supporting ISO 14155:2020, EU MDR (2017/745), and IVDR (2017/746) standards, encompassing Unique Device Identifier (UDI) barcode tracking, device batch/serial accountability, device deficiency and malfunction handling, and specialized Adverse Device Effect (ADE / SADE / USADE) reporting.

### Patient Financial Wallet & Lay Summary Distribution

A patient-facing portal capability facilitating automated expense claim submission, receipt optical character recognition (OCR), approval workflows, and digital disbursement of travel stipends and participant compensation via virtual debit cards or direct ACH, alongside a secure channel for disseminating IRB-approved Plain Language Summaries of clinical trial results per EU CTR 536/2014.

### Declarative Procedure-Fee Rule Engine

A deterministic financial calculation model evaluating procedure fee matrices, institutional overhead markups, and payee splits as pure mathematical expressions upon EDC and SDV triggers.

### Client-Side WebGL Multi-Planar Reconstruction (MPR)

A zero-footprint browser imaging pipeline rendering axial, sagittal, and coronal views from streamed DICOM byte ranges with client-side window/level adjustment and caliper tools.

### Native S/MIME AS2 Client

An asynchronous EDI transmission protocol implementing S/MIME encryption, SHA-256 digital signing, and Message Disposition Notification (MDN) verification for B2B regulatory submission.

### Vectorized Disproportionality Signal Engine

A high-throughput pharmacovigilance surveillance algorithm computing PRR, ROR, and Empirical Bayes Geometric Mean (EBGM) across 2x2 contingency matrices using vectorized array operations.

### Stochastic Monte Carlo Supply Resupply

An RTSM inventory forecasting model running probabilistic simulations across screening velocity, enrollment distributions, and visit schedules to compute automated depot replenishment orders.

### Perceptual Image Hash (pHash) Sentinel

A computer vision fraud detection algorithm computing frequency-domain visual hashes on medical image attachments to detect duplicated or manipulated scans across clinical sites.

### Containerized Reproducible SCE Sandbox

A 21 CFR Part 11 compliant execution environment running isolated R and Python statistical scripts against version-pinned data snapshots with immutable package lockfile validation (`renv` and `uv.lock`).

### Gateway Training Interceptor

A security middleware pattern that intercepts mutating eCRF API requests and evaluates active protocol training claims, rejecting unqualified write attempts with HTTP 403.

### Normalized Open mHealth Telemetry Pipeline

A streaming IoT time-series adapter converting continuous wearable and medical sensor streams (CGM, actigraphy, spirometry) into standardized Open mHealth and HL7 FHIR Observation schemas.

### Cumulative MKT Stability Ledger

A cold chain product-lot budget tracking cumulative thermal exposure minutes and calculated Mean Kinetic Temperature (MKT) across all transit legs and depot hops against stability budgets (USP <1079> / PDA TR 39), instantly quarantining compromised kits upon threshold breach.

### Bayesian BOIN Dose Escalation State Machine

An adaptive oncology trial allocation model that evaluates safety in batched cohorts of 3 subjects, calculating real-time Dose-Limiting Toxicity (DLT) rates against target toxicity boundaries ($\lambda_e = 0.236, \lambda_d = 0.358$) to output deterministic dose modification recommendations (Escalate, Stay, De-escalate).

### Sample-Gated Statistical Surveillance

An ICH E6(R2)/(R3) data integrity pattern that activates statistical fraud tests (Benford's Law, digit preference, variance shrinkage, multivariate distance) only after a site achieves minimum statistical power thresholds ($N \ge 15$ subjects or $\ge 50$ longitudinal data points) and automatically dispatches confirmed anomalies into CRA Monitoring Visit Report (MVR) action items.

### Role-Gated Unblinded Data Vault

A cryptographic data isolation boundary in the Clinical Data Repository that encrypts and restricts access to unblinded treatment arm variables (`ARMCD`, `TRT01P`) from blinded study personnel and standard statisticians, granting access only to authorized Unblinded Statisticians and IDMC members until formal database lock.

### Automated 20% SOD Discordance Trigger

A blinded medical imaging adjudication rule evaluating Sum of Diameters (SOD) across independent dual readers under RECIST 1.1; deltas $> 20\%$ or discordant overall response classifications automatically escalate the event dossier to the Adjudication Committee Chair.

### Automated Golden Path Video Recording

A deterministic browser automation pipeline using Playwright to execute end-to-end clinical persona workflows in high-definition (1080p), capturing continuous, high-fidelity video artifacts and execution traces across all application views.

### Triple Sentinel Verification Gate

A composite automated quality assurance protocol enforcing: (1) DOM state transition and interaction assertions, (2) zero unhandled JavaScript console errors and zero unexpected HTTP 4xx/5xx network failures, and (3) verified backend 21 CFR Part 11 audit trail event emissions for all mutating clinical operations.

### Google Drive Run Manifest Envelope

A versioned, cloud-synchronized execution payload containing modular video recordings, an interactive HTML verification report, structured test telemetry, and SHA-256 cryptographic checksums uploaded to a designated Google Drive destination via headless service account authentication.

### Demonstration-Paced E2E Recording

An automated browser test execution mode that introduces human-perceptible pacing, smooth cursor simulation, and interaction highlighting during Playwright golden path runs to generate high-clarity video walkthroughs suitable for stakeholder demonstration, validation, and training.

### Persona-Partitioned Golden Path Suite

An end-to-end test and video capture architecture that organizes clinical application verification around authentic user personas (`sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `safety_physician`, `auditor`), executing role-specific workflows across all application routes and views with verified RBAC context transitions.

### DDF Intermediate Representation (IR)

A strongly-typed, version-agnostic domain structure representing normalized clinical study forms, items, rules, and multi-language narrative definitions that decouples protocol ingestion from downstream layout rendering.
_Avoid_: Template dict, raw USDM payload

### XPath Rule Compiler

A deterministic recursive AST compiler that translates structured rule condition trees into OpenRosa-compliant XPath expressions for skip-logic relevants and validation constraints.
_Avoid_: String concatenation, inline regex builder

### DDF Job Runner

An asynchronous execution adapter that wraps the in-memory DDF compiler inside database transaction lifecycles, managing translation job state transitions and 21 CFR Part 11 accessibility audit logs.
_Avoid_: Monolithic translation worker

### Quality Surveillance Runner

A unified, autonomous quality monitoring engine that orchestrates multi-domain data harvesting from execution services, enforces sample-gated statistical power thresholds, executes statistical forensics and perceptual hash sentinels, and idempotently dispatches 21 CFR Part 11 deviations and KRI alerts.
_Avoid_: Shallow forensics wrapper, manual anomaly scanner

### Harvested Surveillance Bundle

A strongly-typed aggregate container encapsulating normalized multi-site demographic profiles, longitudinal observation vectors, audit trail timestamps, and medical image perceptual hash manifests harvested across microservices for quality surveillance.
_Avoid_: Raw observation arrays, loose JSON payloads

### Blinded Dual-Reader Adjudication Workflow

A deep clinical workflow module encapsulating independent dual-reader oncology evaluation (RECIST 1.1 / Lugano), automated delta threshold discordance detection, 21 CFR Part 11 cryptographic sealing, and persistence mutations behind a minimal two-method command interface.
_Avoid_: Inline router orchestration, fragmented review handlers

### Step-Up Adjudication Sign-Off

A 21 CFR Part 11 compliant digital sealing operation where an Adjudication Committee Chair provides mandatory clinical justification rationale and generates a cryptographic SHA-256 signature manifest to bind discordant endpoints and synchronize EDC tumor assessments.
_Avoid_: Unverified tie-breaking, manual database override

### Site Payment Ledger Engine

A deep domain module encapsulating investigator grant budget contracts, multi-tier overhead and withholding calculations, split payee configurations, real-time eCRF/SDV/milestone trigger processing, and dual-signoff holdback retention reserve governance behind a compact, database-backed interface.
_Avoid_: Monolithic PaymentsService, 40-method repository CRUD wrappers

### Self-Billing & ERP Gateway

A deep domain module responsible for automated monthly and quarterly billing cycle compilation into self-billing invoices, cryptographic SHA-256 PDF remittance advice generation and 21 CFR Part 11 digital signing, enterprise ERP synchronization (SAP S/4HANA, NetSuite SuiteTalk), inbound webhook handling, and automated payment reconciliation.
_Avoid_: Manual invoicing scripts, unsealed financial statements

### Participant Compensation Service

A deep domain module managing participant financial wallets, SoA-linked visit stipend rules, deterministic EDC visit verification triggers, mobile travel/meal expense reimbursement claims with OCR metadata extraction, coordinator 1-click approvals, digital payouts (Virtual Card/ACH), and EU CTR 536/2014 Article 37 Plain Language Lay Summary distribution with cryptographic receipt confirmation seals.
_Avoid_: Fragmented participant wallet endpoints, unvalidated receipt workflows



