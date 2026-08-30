# Clinical Domain Glossary

### Hybrid CQRS & Query Services Pattern

An architectural separation pattern where strict Domain Repository Ports govern domain entity state mutations and transactional invariants (Commands), while specialized, high-throughput Read-Model Query Services perform direct, read-only SQL projections and batch extractions (e.g. CDISC SDTM/ADaM datasets, cross-domain eCRF anomaly scans, and GxP audit ledger verification) without loading full domain entities into memory.

### Hexagonal Unit of Work (UoW) Protocol

An asynchronous transaction boundary abstraction defined in `packages.hexagonal` that encapsulates repository collection access, optimistic concurrency version checks, and atomic commit/rollback lifecycles across multiple domain aggregate roots, guaranteeing atomic multi-repository operations without leaking underlying ORM sessions into domain application services.

### Dual-Defense GxP Audit Trail Architecture

A regulatory data integrity pattern combining application-level Domain Event Dispatching (which records structured business change reasons, electronic signatures, and user attestation payloads) with database-level PostgreSQL triggers (`cadence.current_user_id` / `cadence.current_change_reason` session variables and SHA-256 cryptographic block-sealing), guaranteeing that direct SQL interventions and service-level mutations are both immutably audited under 21 CFR Part 11 and ICH GCP E6(R2).

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

### Protocol Semantic Diff Engine

A deep, pure in-process domain module that ingests polymorphic protocol version representations, performs 4-layer comparison across USDM Graph AST, Schedule of Activities (SoA) Matrix, Eligibility Logic, and eCRF Schemas, and evaluates operational amendment impacts including subject re-consent gating, downstream migration directives, operational ticket blueprints, and site burden scoring with zero I/O side effects.
_Avoid_: Scattered dictionary flattener scripts, fragmented ripple helpers, router-level diff assembly

### Dynamic Consent Engine

A deep, local-substitutable domain engine in `apps/econsent` that evaluates participant consent state, protocol amendment version alignments, granular optional research choices, and age-of-majority transitions in ephemeral memory without I/O dependencies.
_Avoid_: Monolithic database queries inside evaluation loops, fragmented router-level consent checks

### Re-Consent Gating Matrix

A deterministic clinical policy engine defining operational data capture permissions across study encounters, efficacy assessments, investigational product dispensing, and safety-exempt carve-outs (adverse events, emergency unblinding) when participants require protocol re-consent.
_Avoid_: Global participant freezes, unchecked safety form locks

### Granular Consent Capability

A strongly-typed, auditable domain permission token derived from participant optional research selections (e.g. pharmacogenomic biobanking, future biomedical research, longitudinal re-contact) that explicitly authorizes or restricts exploratory clinical data capture and sample export.
_Avoid_: Unstructured metadata JSON string queries

### Minor-to-Adult Transition Window

A regulatory lifecycle window governing pediatric participant transitions upon reaching jurisdiction-specific legal age thresholds, coordinating pre-notification warnings, state advancement, and visit-aligned adult self-consent enforcement.
_Avoid_: Immediate mid-visit execution locks without pre-notification

### Clinical Issue Hub

A deep, local-substitutable domain engine in `apps/tickets` that performs multi-tier module-based routing, dual-target SLA tracking (initial acknowledgment vs. final resolution), state-dependent clock pauses, and controlled reopening window evaluations in ephemeral memory without I/O dependencies.
_Avoid_: Fragmented router-level routing matrices, monolithic database queries inside SLA loops, untracked single-clock deadlines

### Cadence Workspace Plugin

An encapsulated Antigravity customization bundle (`.agents/plugins/cadence-core/`) that unifies clinical skill runbooks, 21 CFR Part 11 lifecycle hooks, and workspace rules into a single discoverable and version-controlled unit.
_Avoid_: Loose unmanaged script directories, fragmented per-tool config files

### 6-Facet Site Greenlight Engine

A multi-factor site activation evaluator in CTMS that assesses 6 statutory readiness facets (IRB/Ethics approval, executed Clinical Trial Agreement, completed essential regulatory documents, PI-certified DOA log, site staff protocol training, and IP release authorization) against country jurisdiction presets, issuing cryptographic HMAC-SHA256 tokens upon unanimous approval.
_Avoid_: Unchecked manual activation toggles without multi-facet verification

### Sovereign Site Execution Gatekeeper

An autonomous middleware security interceptor in `apps/execution` that verifies site greenlight approval and HMAC token validity before permitting subject randomization, eCRF visit creation, or IP kit dispensation, returning RFC 7807 `HTTP 423 Resource Locked` for non-greenlight sites.
_Avoid_: Allowing unapproved site data capture or soft unblocking warnings

### Principal Investigator Electronic DOA Log

A 21 CFR Part 11 compliant site task delegation log where the Principal Investigator electronically signs delegated site duties and staff role authorizations using signature step-up tokens (`execution.doa.signoff`), generating immutable audit ledger seals and certified watermarked PDF exports.
_Avoid_: Unsigned database role flags, unsealed delegation tables

### DIA Reference Model v3.2.0

An authoritative clinical document taxonomy (10 canonical zones) standardized across eTMF and eISF binders, supporting deterministic bidirectional synchronization, correlation key idempotency, CRA QC review staging, and zero-downtime dual-read object storage adapters.
_Avoid_: Hardcoded proprietary folder trees, unmapped binder synchronizations

### Real-Time Event-to-Payable Ledger

An automated financial calculation engine in `apps/payments` that processes transactional outbox events triggered by eCRF visit completion, procedure submissions, and CRA Source Data Verification (SDV) certifications to generate immutable payable line items mapped against active investigator fee matrices.
_Avoid_: Unverified instant payouts on uncertified visits, delayed manual spreadsheet invoicing

### Multi-Party Payee Split

A contractual grant allocation mechanism that dynamically divides earned clinical procedure payables among multiple designated entity accounts (e.g. Institution 70% / Principal Investigator 30%) with exact arithmetic precision.
_Avoid_: Unsplit manual ledger reconciliations

### Closeout Holdback Retention Escrow

A statutory financial risk-mitigation reserve that automatically sequesters a 10% deduction from all earned procedure payables into escrow, strictly releasable at site closeout only upon dual 21 CFR Part 11 electronic signatures from Sponsor Finance and Lead CRA.
_Avoid_: Single-user holdback release, un-escrowed closeout disbursements

### Patient Financial Wallet

A participant-facing compensation and expense portal supporting automated SoA visit stipends, mobile receipt OCR uploads with 1-click CRC approval, and an automated $600 USD IRS 1099-MISC threshold accumulator that triggers mandatory W-9 validation.
_Avoid_: Unmonitored cash gift cards, untracked annual compensation thresholds

### Plain Language Lay Summary Portal

An EU CTR 536/2014 Article 37 compliant delivery portal providing trial participants with access to approved clinical trial lay summaries and generating cryptographically signed SHA-256 confirmed read-receipts stored in the GxP audit ledger.
_Avoid_: Unverified unauthenticated public links without audit confirmation

### Automated NCI-CTCAE v5.0 Toxicity Grading

A pharmacovigilance evaluation engine in `apps/safety` that analyzes numerical clinical laboratory results and UCUM units against NCI-CTCAE v5.0 criteria, automatically staging Grade 3 (Severe) and Grade 4 (Life-threatening) events as adverse event drafts for Safety Physician causality review.
_Avoid_: Manual lab report grading, un-staged automatic regulatory transmissions without medical review

### ICH E2B(R3) AS2 S/MIME EDI Gateway

A B2B electronic regulatory transmission pipeline in `apps/safety` that compiles Individual Case Safety Reports (ICSR) into ISO 27953-2 compliant ICH E2B(R3) XML payloads and transmits them over RFC 4130 AS2 with SHA-256 S/MIME encryption, digital signatures, and synchronous MDN verification.
_Avoid_: Unencrypted email attachments, manual un-tracked agency portal uploads

### Centralized Statistical Monitoring (CSM) Forensics

A risk-based quality management engine in `apps/quality` that computes Benford's Law Chi-Square and Mean Absolute Deviation (MAD), terminal digit bias, and Mahalanobis distance multivariate outlier detection across site data streams to uncover fabrication and clinical anomaly clusters.
_Avoid_: Single-metric variance checks, un-audited manual data spot checks

### 64-Bit DCT Perceptual Hashing (pHash) Sentinel

An image forensics pipeline in `apps/quality` and `apps/imaging` that computes 64-bit Discrete Cosine Transform perceptual hashes for medical images and flags cross-patient or cross-site duplicates with Hamming distance $\le 5$ for fraud investigations.
_Avoid_: MD5/SHA256 exact bit comparison for lossy medical imaging duplicates

### Blinded Dual-Reader RECIST 1.1 Adjudication

An oncology imaging evaluation workflow in `apps/imaging` where two independent blinded radiologists record tumor measurements, automatically triggering Adjudication Committee review and Chair step-up electronic sign-off if Sum of Diameters (SOD) discordance exceeds 20%.
_Avoid_: Conflicting un-adjudicated radiologist reads, arbitrary single-reader overrides

### Sequential 6-Persona Golden Path

A complete end-to-end clinical trial verification scenario executing sequentially across `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `safety_physician`, and `auditor` in full-width Vanilla CSS client workspaces (`apps/web`), validating role boundaries and state handovers from protocol authoring to database lock.
_Avoid_: Fragmented isolated persona tests without lifecycle continuity

### 1080p Playwright Video Capture

An automated qualification recording pipeline using Playwright at 1920x1080 resolution (`RECORD_VIDEO=true`) with realistic action delays, cursor movements, and click ripple highlights that produces documentary video proof of system operations.
_Avoid_: Fast un-paced headless executions without video evidence for regulatory audits

### Triple Sentinel Assertion Gates

A three-layer test verification standard requiring: 1) visual and functional DOM component state verification, 2) client console and network hygiene (zero unhandled exceptions or HTTP failures), and 3) verified backend database state and 21 CFR Part 11 audit trails (`created_by`, `reason_for_change`, `version_index`, SHA-256 digests).
_Avoid_: Single HTTP status code assertions without audit trail verification

### 100% GxP RTM Synchronization

A continuous compliance assurance protocol that automates synchronization of test execution results and `@req:` docstrings via `scripts/sync_gxp.py`, guaranteeing 100% traceability across all 226 system requirements (192 PRD + 34 SRS) with zero drift in CI/CD.
_Avoid_: Manual untracked Markdown table edits, partial requirement coverage

### Model Tiering Matrix

A hierarchical compute allocation strategy in Antigravity that dynamically assigns subagents across four compute tiers (`flash_lite` for ultra-low latency parsing, `flash` for fast search and codebase sweeps, `pro` for deep architectural analysis and complex refactors, and `inherit` for mirroring the parent orchestrator), optimizing token economy and execution latency.
_Avoid_: Monolithic flagship model allocation for low-complexity search tasks

### Interactive Decision Modal (ask_question)

An interactive modal rendering protocol in Antigravity that presents structured multiple-choice questions with selectable response options, default write-in input, and multi-select support (`is_multi_select: true`), formatted strictly from the user's perspective with recommended options prefixed by `(Recommended)`.
_Avoid_: Asking trivial binary yes/no questions via modal, hardcoding duplicate "Other" options

### Workspace Isolation Modes

A filesystem sandboxing architecture for Antigravity subagents providing three operational isolation modes: `inherit` for direct in-place workspace access, `share` for isolated git worktree branching sharing underlying repo storage, and `branch` for complete ephemeral workspace isolation during throwaway prototyping.
_Avoid_: Mutating production workspace branches during exploratory throwaway prototyping

### Tenant Provisioning Engine

An automated schema provisioning and tenant isolation lifecycle pattern in `apps/execution` that provisions dedicated schemas, executes baseline migrations, and initialises 21 CFR Part 11 audit ledgers for new clinical sponsor accounts.
_Avoid_: Unisolated multi-tenant databases, manual SQL provisioning scripts

### E2B(R3) WHODrug Hierarchy Validator

An automated pharmacovigilance validation check in `apps/safety` that evaluates suspect and concomitant medicinal product codes against WHODrug active ingredient hierarchies and ATC classifications prior to AS2 regulatory transmission.
_Avoid_: Unverified free-text drug submissions in E2B(R3) XML packages

### Automated eTMF QC Lifecycle Engine

An asynchronous document lifecycle state engine in `apps/etmf` that automatically transitions ingested documents through quality control stages (`DRAFT` → `TECHNICAL_QC` → `CLINICAL_QC` → `APPROVED`) upon successful OCR, DIA classification, and signature verification.
_Avoid_: Stalled un-transitioned document states after indexing completion

### Demo Mode Client Gating

A runtime feature-toggling pattern in `apps/web` and `apps/subject-portal` that isolates client-side mock datasets and offline demo states behind an explicit `VITE_DEMO_MODE=true` environment flag, ensuring live and staging environments raise visible RFC 7807 error toasts on network or API failures.
_Avoid_: Silent catch-and-fallback to mock data masking production network errors

### Self-Signed Trust Store Gating

A 21 CFR Part 11 security pattern where trusted root and intermediate X.509 digital certificates are pre-loaded into an immutable cryptographic trust store during application startup, strictly prohibiting dynamic or in-flight self-registration of untrusted certificates during electronic signature verification.
_Avoid_: Dynamic self-registration of certificates from request payloads

### 2D Matrix Grid Navigation (WCAG 2.1)

An accessible keyboard navigation pattern for high-density clinical Schedule of Activities (SoA) matrices implementing WAI-ARIA `role="grid"` / `role="row"` / `role="gridcell"`, two-dimensional arrow key navigation (`ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`), cell status announcements (`aria-label`), and spacebar toggling.
_Avoid_: Non-standard static table cells without keyboard focus or grid semantics

### Gateway Dispatch Routing Matrix

A centralized reverse-proxy topology in `apps/gateway` that maps all microservice domain prefixes (including `/api/v1/imaging/*`, `/api/v1/payments/*`, `/api/v1/ai/*`, `/api/v1/designer/*`, `/api/v1/execution/*`, and legacy direct service paths) to dedicated downstream service ports with canonical HMAC gateway signatures, tenant isolation gating, and step-up signature verification.
_Avoid_: Fallthrough catch-all routes masking missing microservice proxy endpoints

### RFC 7807 Gateway Error Boundary

An enterprise API error handling standard where all API Gateway synthetic and transport-level failures (400 Bad Request, 401 Unauthorized, 403 Forbidden, 429 Rate Limit Exceeded, 500 Internal Server Error, 502 Bad Gateway, 504 Gateway Timeout) are emitted as standardized `application/problem+json` ProblemDetails structures containing canonical type URIs (`https://cadence.clinical/errors/*`), descriptive titles, HTTP status codes, detail strings, and request instance paths, while preserving downstream payload transparency and mapping network connection failures to 502 and timeouts to 504.
_Avoid_: Unformatted 502 HTML pages, plain-text stack trace leakages, or non-RFC 7807 ad-hoc error bodies

### Unified OpenAPI Schema Aggregation

An automated schema composition engine in `apps/gateway` that dynamically fetches and merges OpenAPI 3.1.0 specifications from all downstream microservices (`apps/*`), rewriting component schemas with isolated service prefixes (`Imaging_`, `Payments_`, `AiGateway_`, `Designer_`, `Execution_`) and qualifying path operation IDs to eliminate naming collisions and present a unified interactive Swagger UI at `/docs`.
_Avoid_: Missing service definitions in unified API specifications, colliding schema names across microservices

### Cryptographic Audit Chain Synchronization

An enterprise GxP data integrity standard enforcing monotonic SHA-256 HMAC digest chaining (`previous_digest -> sha256_digest`) and mandatory `reason_for_change` across all domain audit log entities (CTMS, EDC, eConsent, eISF, eTMF, Safety, Payments, Quality).
_Avoid_: Unchained, un-hashed plain text audit tables lacking reason for change

### Active Pre-Release Sandbox

A development and evaluation posture for the Cadence Clinical monorepo prior to v1.0 General Availability (GA), providing containerized sandbox environments with realistic clinical test datasets for stakeholder demonstration, integration testing, and developer iteration while maintaining strict 21 CFR Part 11 cryptographic architecture.
_Avoid_: Promoting pre-release development environments as production-qualified GxP systems without formal qualification

### Digital Data Flow (DDF) Pipeline

The automated, bi-directional translation pipeline bridging upstream CDISC USDM protocol graphs in Neo4j to downstream OpenRosa/Enketo XForms and relational eCRF tables in PostgreSQL via a strongly-typed DDF Intermediate Representation (IR) and XPath Rule Compiler.
_Avoid_: Manual protocol-to-EDC build translation, lossy template dictionaries

### Programmatic API Documentation Engine

An automated static analysis pipeline that inspects OpenAPI 3.1.0 schemas and FastAPI route metadata across all monorepo microservices (`apps/*`), compiling rich, searchable VitePress Markdown reference documents (`docs/api/*`) with request/response schema tables, GxP role authorizations, RFC 7807 problem detail models, and step-up signature parameters without requiring live runtime databases.
_Avoid_: Out-of-sync manual API endpoint documentation, unstructured raw JSON dumps

### Real-Time Protocol Eligibility Screening (I/E Engine)

An automated clinical evaluation mechanism in `apps/execution` and `apps/web` where subject demographic, observational, and laboratory parameters are evaluated against machine-readable CDISC USDM inclusion/exclusion criteria expressions in real time (`POST /api/v1/execution/subjects/{id}/screening`), outputting deterministic eligibility verdicts (`ELIGIBLE`, `INELIGIBLE`, `INDETERMINATE`) with itemized criteria attribution (`failed_criteria`, `indeterminate_criteria`) and append-only 21 CFR Part 11 audit blocks without exposing Protected Health Information (PHI).
_Avoid_: Client-only un-audited eligibility assertions, exposing raw PHI in screening responses

### Decoupled Persona Workspace Pattern

A frontend architectural pattern in `apps/web` where high-density clinical views (such as `EcrfView.vue`) delegate persona-specific orchestration, event handling, and data synchronization to dedicated, strongly-typed Vue composables (`useSubjectEnrollment`, `usePiSignoff`, `useCrcWorkspace`), eliminating monolithic component coupling and isolating CRC data entry from CRA monitoring and PI sign-off lifecycles.
_Avoid_: Monolithic multi-thousand-line Vue views mixing multiple persona lifecycles in local scope

### Capability-Based Clinical RBAC

A centralized role-based access control architecture in `apps/web/src/stores/auth.js` and `packages/security` that maps clinical personas and Keycloak OIDC roles to granular functional capabilities (e.g. `SUBJECT_ENROLL`, `PI_BATCH_SIGNOFF`, `SDV_VERIFY`, `ECONSENT_GATING`), eliminating brittle hardcoded string comparisons and ensuring consistent authorization across production and interactive demo modes.
_Avoid_: Scattering ad-hoc role string comparisons (`activeRole === 'site_investigator'`) across component templates

### Metadata-Driven Engine Architecture (The Engine Triad)

An enterprise architectural standard where clinical domain rules across all microservices are configured through low-code Builder interfaces, compiled into validated Domain-Specific Language (DSL) Schemas, and executed at runtime by stateless, pure Evaluators, transforming business configurations into version-pinned, auditable 21 CFR Part 11 records.
_Avoid_: Hardcoded Python if/else statements for protocol-specific rules, ad-hoc unvalidated JSON blobs

### Universal Rules & Expression Engine (UREE)

A sandboxed, AST-based expression evaluation engine for cross-form edit checks, dynamic skip logic, and protocol feasibility without executing arbitrary un-sandboxed code.
_Avoid_: Unsafe Python eval() or exec() calls in clinical data pipelines

### Vector-Based Clinical Timeline Engine

A mathematical scheduling engine that computes clinical encounter visit dates, permissible protocol windows, and automated out-of-window deviation flags dynamically from relative anchor event offsets.
_Avoid_: Hardcoded calendar date math scattered across frontend views

### Deterministic Domain-Specific Language (DSL)

A declarative, JSON/YAML-serialized specification language governing domain rules (financial fee matrices, PV decision rules, laboratory toxicity grades, ETL schema mappings, agent DAG pipelines) with immutable version indices and 21 CFR Part 11 electronic signature publication gates.
_Avoid_: Unversioned mutable configurations mutating locked historical clinical data

### Finite State Machine (FSM) Lifecycle Engine

A declarative, strongly-typed directed acyclic graph (DAG) state transition engine governing regulated entity lifecycles (subjects, documents, queries, CRF drafts) with dynamic expression guards, role authorization gates, Part 11 reason-for-change validation, and cycle/deadlock detection.
_Avoid_: Scattered procedural status updates without guard validation or audit trail manifests

### Event-Driven SLA Escalation Engine

A deterministic, zero-I/O countdown and alert engine calculating service-level agreement targets, paused clock durations, warning/critical/overdue threshold notifications, and multi-tier operational escalation policies across clinical workflows.
_Avoid_: Hardcoded timer loops and fragmented date math in microservice handlers

### Sandboxed AST Evaluator

A pure, side-effect-free in-memory expression evaluation runtime that traverses Abstract Syntax Tree (AST) representations and JSONLogic structures with strict type safety, null coalescing, and clinical arithmetic operators.
_Avoid_: Insecure eval() or exec() calls executing untrusted Python strings

### Deterministic DAG Transition Validator

A static graph analysis sentinel that validates state machine definitions for terminal state reachability, dead-end traps, cycle compliance, and non-deterministic transition conflicts prior to publication.
_Avoid_: Unvalidated state machine configurations that strand clinical entities in unreachable states

### Schedule of Activities (SoA) Vector Timeline Engine

A pure mathematical scheduling engine that computes clinical encounter target dates, permissible window envelopes ($[-w_b, +w_a]$), and automated out-of-window deviation flags dynamically from relative anchor event offsets (Screening, Randomization, First Dose, Cycle Start, Previous Visit) without I/O side effects.
_Avoid_: Static hardcoded visit day lists or scattered calendar date math across client views

### Derived Variable Math Engine

A standardized medical equation and biostatistical derivation evaluator built on pure UREE AST execution, compiling validated clinical formulas (eGFR CKD-EPI 2021 race-free, BSA Mosteller/DuBois, BMI, QTcF Fridericia, Mean Arterial Pressure, Cockcroft-Gault) with automated unit conversion and range gating.
_Avoid_: Ad-hoc procedural calculations embedded directly in API route handlers

### Declarative CTCAE & DAIDS Lab Toxicity Grading Engine

A version-pinned, declarative clinical laboratory evaluation engine that grades central and local lab observations against NCI-CTCAE v5.0 and DAIDS v2.1 criteria using ULN/LLN relative ratios, absolute threshold cuts, baseline delta checks, and automated Grade 3/4 Adverse Event staging.
_Avoid_: Monolithic procedural if/elif cascades hardcoded in Python backend services

### Hy's Law Drug-Induced Liver Injury (DILI) Sentinel

A pharmacovigilance safety sentinel evaluating concurrent hepatic biomarker elevations ($ALT/AST \ge 3\times ULN$ AND $Total Bilirubin \ge 2\times ULN$ in the absence of significant alkaline phosphatase elevation $ALP < 2\times ULN$), automatically triggering urgent clinical notifications and potential DILI safety case workflows.
_Avoid_: Disconnected single-analyte lab flags missing multi-parameter DILI triad patterns

### Asymmetric Visit Window Tolerance Envelope

A structured temporal boundary model defining permissible before ($w_b$) and after ($w_a$) calendar day offsets around a target protocol visit day (e.g. Day $28 [-3, +5]$ days), outputting deterministic deviation classifications (`WITHIN_WINDOW`, `EARLY_VISIT`, `LATE_VISIT`, `OUT_OF_WINDOW_MAJOR`, `MISSED_VISIT`).
_Avoid_: Symmetric window approximations or untracked protocol visit deviations

### Cycle-Based Template Expansion

A clinical protocol scheduling pattern in the SoA Vector Timeline Engine that generates repeating encounter sequences across oncology cycles (e.g. Q3W Cycles 1-6 with Day 1, Day 8, Day 15 visits) from a single parameterized template definition without manual visit duplicating.
_Avoid_: Manually cloning individual visit records for each multi-cycle regimen iteration

### Predecessor Visit DAG Chaining

A dynamic scheduling dependency model where downstream encounter target dates dynamically anchor to the actual completion date of predecessor visits (e.g. Visit $N$ occurs $14 \pm 2$ days after Visit $N-1$), preventing cumulative scheduling drift during subject-level delays.
_Avoid_: Static day calculations that ignore actual patient visit timing deltas

### Physiological Plausibility Boundary Gating

A biostatistical validation sentinel in the Derived Variable Math Engine that evaluates input parameters against clinical plausibility envelopes (e.g. Weight $20-300$ kg, Height $50-250$ cm, Heart Rate $30-220$ bpm) before evaluating medical equations, preventing distorted clinical scores.
_Avoid_: Silent evaluation of physically impossible input values resulting in corrupt derived variables

### Staged Laboratory Adverse Event (AE) Draft Envelope

A regulatory compliance pattern where CTCAE Grade 3 (Severe) and Grade 4 (Life-Threatening) toxicities and Hy's Law DILI alerts automatically generate structured, immutable AE draft envelopes pre-populated with MedDRA terms, requiring human Safety Physician review and 21 CFR Part 11 electronic signature sign-off before official regulatory submission.
_Avoid_: Direct un-reviewed adverse event injection into active safety ledgers

### Stochastic Randomization Engine

A zero-I/O, deterministic clinical allocation runtime implementing multi-arm treatment assignment across Stratified Permuted Block, Pocock-Simon Dynamic Covariate Adaptive Minimization, Response-Adaptive Randomization (RAR), and Phase I/II Adaptive Dose Escalation (BOIN / 3+3) designs with reproducible cryptographic random number generation.
_Avoid_: Non-reproducible unseeded pseudo-random number calls embedded in database transactions

### Pocock-Simon Covariate Adaptive Minimization Engine

A dynamic allocation engine that computes marginal imbalance scores across prognostic stratification covariates using configurable distance metrics (Range, Variance, Standard Deviation) and factor weights, assigning incoming subjects via a biased coin probability ($p \in [0.5, 1.0]$) to maintain optimal treatment group balance in small trials.
_Avoid_: Unweighted marginal counts or static post-stratification approximations

### Bayesian Optimal Interval (BOIN) Dose Escalation Engine

A model-assisted adaptive Phase I/II oncology dose-finding engine that evaluates dose-limiting toxicity (DLT) rates against target toxicity $\phi$ and mathematical escalation/de-escalation boundaries ($\lambda_e, \lambda_d$), calculating Bayesian posterior safety stopping boundaries ($\Pr(p_j > \phi | \text{data}) > 0.95$) to protect patient safety.
_Avoid_: Rigid rule-based 3+3 designs unable to incorporate continuous Bayesian toxicity evidence

### Stratified Permuted Block Randomization Engine

A deterministic block allocation strategy that partitions subjects into discrete strata based on baseline factor combinations, generating variable-length balanced permutation blocks sealed with cryptographic checksums to guarantee allocation predictability prevention while maintaining exact target ratios.
_Avoid_: Fixed-size unmasked blocks susceptible to clinical investigator selection bias

### Multi-Echelon Predictive Resupply Forecasting Engine

A supply chain optimization runtime that models inventory across the complete clinical supply hierarchy (Primary Manufacturer $\to$ Central Depot $\to$ Regional Depot $\to$ Clinical Site), calculating lead-time safety buffers, lot shelf-life expiration run-out curves, and automated resupply orders to eliminate patient treatment interruption.
_Avoid_: Reactive single-site threshold checks ignoring multi-tier replenishment lead times

### Poisson & Monte Carlo Enrollment Demand Simulator

A stochastic simulation engine that generates future investigational product demand projections across clinical sites by sampling subject accrual rates via Poisson arrival distributions and mapping anticipated visit-level dispensation schedules against current on-hand and in-transit stock.
_Avoid_: Linear demand approximations that fail to account for stochastic recruitment surges

### Direct-to-Patient (DTP) Temperature-Gated Logistics Engine

A decentralized clinical trial supply orchestrator that validates investigational product eligibility for home dispensation, enforcing continuous cold-chain temperature excursion verification, expiry thresholds, and tamper-evident packaging manifests before authorizing courier dispatch.
_Avoid_: Unverified direct shipments bypassing site investigator delegation and cold-chain compliance

### 21 CFR Part 11 Emergency Unblinding Protocol & Audit Envelope

A secure, tamper-evident emergency unblinding workflow that reveals subject treatment assignment strictly upon provision of authenticated physician identity, mandatory change justification rationale, and dual-party confirmation, immediately emitting high-priority safety notifications and immutable audit log records.
_Avoid_: Silent database lookups or unlogged treatment code disclosures during medical emergencies

### Tripartite JSON Schema Architecture

A clinical form metadata pattern decoupling form definitions into three distinct, version-controlled JSON documents: 1) Data Schema (`schema.json`) defining variables, CDISC OIDs, and validation constraints; 2) UI Schema (`uischema.json`) defining responsive CSS grid layouts, widget selections, and viewport breakpoints; and 3) Logic Schema (`rules.json`) defining declarative AST edit checks, cross-visit dependencies, and derived math formulas.
_Avoid_: Mapping form fields to physical relational database columns or generating physical DDL tables per form

### Visual Metadata IDE (eCRF Designer)

An AI-enhanced, CDISC-native visual authoring environment for Clinical Data Managers and Biostatisticians that unifies low-code form layout, real-time multiplayer CRDT collaboration (Yjs), omni-channel WYSIWYG preview (Site Desktop EDC, Patient Mobile ePRO, Tablet eSource), no-code AST rule authoring, automated synthetic UAT generation, and zero-downtime protocol amendment branching.
_Avoid_: Disjointed proprietary form programming workflows requiring 8-12 weeks of manual scripting

### Isomorphic AST Execution

A clinical logic evaluation architecture where validation rules, skip logic, and edit checks are compiled into declarative Abstract Syntax Trees (JSONLogic / Google CEL compatible) that execute identically in browser JavaScript memory (for immediate sub-millisecond red-text feedback) and backend Python services (for strict GxP enforcement).
_Avoid_: Separate un-synchronized client JavaScript and backend Python validation codebases

### Smart Clinical Component Blocks

Pre-configured, domain-validated form panels (e.g. Vital Signs, Adverse Events, ConMeds) in the Global Metadata Repository (MDR) that inherently embed standardized CDASH/SDTM metadata mappings (`VS.VSORRES`, `AE.AETERM`), standard UCUM unit catalogs (`mmHg`, `kg`, `g/dL`), and baseline reference ranges upon canvas drop.
_Avoid_: Raw unmapped primitive input fields requiring post-hoc SAS/SDTM ETL mapping

### Automated UAT & Simulation Studio

An automated qualification environment in the eCRF Designer that utilizes AI Gateway synthetic generation to construct thousands of targeted edge-case data payloads (nulls, boundary values, invalid dates, extreme laboratory spikes), evaluates them against the stateless Rules Evaluator, and compiles an immutable 21 CFR Part 11 compliant UAT Evidence Report in sub-30 seconds.
_Avoid_: Weeks of manual data entry and exploratory testing for study UAT sign-off

### In-Flight Patient Migration Matrix

A runtime routing policy governing active subjects during protocol amendments without database DDL migrations, enabling data managers to configure prospective deployment (applied only to newly enrolled subjects) versus retrospective deployment (applied to all active subjects with automated missing-data queries for past encounters).
_Avoid_: Offline database migrations, global patient freezes, or data loss during mid-study amendments

### Multiplayer Clinical Form Canvas (CRDT)

A real-time collaborative workspace powered by WebSockets and Conflict-free Replicated Data Types (Yjs) allowing multiple clinical stakeholders (Data Manager, Biostatistician, Medical Monitor) to concurrently design, edit, and review eCRF schemas with live remote cursors and contextual field-level comment threads.
_Avoid_: Rigid single-user study locks blocking multi-party protocol design collaboration

### Dynamic Repeating Log Grid

A high-density clinical data capture component for multi-instance domain records (Adverse Events, Concomitant Medications, Medical History) supporting infinite repeatable row instances, column-level validation rules, and reactive mathematical aggregations.
_Avoid_: Fixed-row static tables or unstructured text fields for longitudinal multi-event capture

### Medical Dictionary Auto-Coding Binding

A clinical input field binding that integrates directly with MedDRA and WHODrug terminology APIs, auto-suggesting standardized medical terms and recording hierarchical SOC, PT, and ATC classification codes in real time during investigator data entry.
_Avoid_: Post-study batch manual dictionary coding workflows delaying clinical database lock









