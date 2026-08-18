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
