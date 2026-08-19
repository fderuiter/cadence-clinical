# 04: [Patient & Site CRC] eConsent, ICF Builder & 21 CFR Part 11 Signature Capture

**What to build:**
An interactive Informed Consent Form (ICF) authoring and participant consent experience (`ConsentAuthoringView.vue`, `ICFBuilderView.vue`, `ComprehensionQuizBuilder.vue`) backed by a 21 CFR Part 11 compliant dual-credential electronic signature capture modal (`SignatureCaptureModal.vue`).

**Blocked by:** 01: [Core/Platform] Multi-Engine CADENCE-101 Hero Study Seeding & Dev Cockpit

**Status:** ready-for-agent

## Context & User Story
As a Site CRC and Subject, I want to review the `CADENCE-101` Informed Consent Form with interactive comprehension check quizzes, and execute a 21 CFR Part 11 electronic signature with password re-authentication and reason for signing, so that verifiable, immutable consent records are registered prior to clinical visit data collection.

## Acceptance Criteria
- [ ] ICF Builder allows drafting and editing modular consent clauses linked to protocol versions.
- [ ] Comprehension Quiz component presents questions with instant feedback and passing threshold enforcement.
- [ ] Signature Capture modal requires: Signer Name, Role (Subject / PI), Meaning of Signing ("I agree to participate" / "Investigator Certification"), and Credential Verification.
- [ ] Submitting a signature creates an immutable cryptographic consent record in `apps/econsent` with timestamp and checksum.
- [ ] Tests in `apps/econsent/tests/` and `apps/web/tests/test_econsent.py` pass.
