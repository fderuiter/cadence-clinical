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
