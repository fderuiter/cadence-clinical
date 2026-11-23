---
layout: home

hero:
  name: "Cadence Clinical"
  text: "Metadata-Driven Clinical Research Platform"
  tagline: "Explore architecture, GxP engineering guides, and self-contained interactive demos for study staff and clinical trial participants—no local backend required."
  actions:
    - theme: brand
      text: "Launch Staff Demo"
      link: "/demo/"
      target: "_self"
    - theme: alt
      text: "Launch Participant Demo"
      link: "/subject-portal/"
      target: "_self"
    - theme: alt
      text: "Explore Documentation"
      link: "/DOCUMENTATION_INDEX"
    - theme: alt
      text: "Repository Overview"
      link: "/README"

features:
  - title: "Staff Workbench Demo"
    details: "Walk through USDM study design, protocol digitization, eCRF data capture, query management, CTMS, safety, eTMF, and audit trails in synthetic study DEMO-01."
    link: "/demo/"
    linkText: "Open Staff Demo"
    target: "_self"
  - title: "Participant Portal Demo"
    details: "Experience participant ePRO questionnaires, 21 CFR Part 11 eConsent and amendment re-consent, clinical inbox notifications, offline sync queue, and stipend wallet."
    link: "/subject-portal/"
    linkText: "Open Participant Demo"
    target: "_self"
  - title: "Maintainer & Repository Overview"
    details: "Review the repository layout, .NET 10 Aspire and Docker Compose full-stack setup, Keycloak OIDC configuration, and developer CLI workflows."
    link: "/README"
    linkText: "Read Repository README"
  - title: "Clinical Kernel Specification"
    details: "Inspect the immutable observation model, temporal validity rules, provenance lineage, and domain invariants shared across clinical modules."
    link: "/kernel/CLINICAL_KERNEL_SPECIFICATION"
    linkText: "Read Kernel Spec"
  - title: "System Architecture & ADRs"
    details: "Browse service topology, YARP API gateway routing, field-level cryptographic protection, and numbered Architectural Decision Records."
    link: "/ARCHITECTURE"
    linkText: "Explore Architecture"
  - title: "API Reference & GxP Validation"
    details: "Examine OpenAPI service contracts, requirements traceability, 21 CFR Part 11 and ICH E6(R3) compliance artifacts, and automated quality sentinels."
    link: "/api/"
    linkText: "Browse API Docs"
---

## Choosing an Evaluation Path

Cadence Clinical separates **public browser evaluation** from **authenticated full-stack execution** so evaluators can explore workflows immediately while production deployments maintain strict OIDC and audit boundaries.

| Mode                                 | Where It Runs                                                                                     | Identity & API Layer                                                                                           | When to Use                                                                                          |
| ------------------------------------ | ------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| **Hosted Staff & Participant Demos** | Co-hosted on GitHub Pages under `/cadence-clinical/demo/` and `/cadence-clinical/subject-portal/` | Offline synthetic evaluator persona + stateful in-browser Mock Service Worker (zero external API or IdP calls) | Instant product walkthroughs, UX evaluation, and feature previews with synthetic `DEMO-01` data      |
| **Local Full-Stack Development**     | Local workstation via `.NET Aspire` AppHost or `docker compose`                                   | Real Keycloak OIDC + PKCE, YARP API Gateway (`:8000`), `.NET 10` services, and PostgreSQL 18                   | Backend/frontend engineering, database migrations, OIDC authorization testing, and GxP qualification |

- Setting up a local stack? Follow the [Local Development Environment guide](LOCAL_DEV_ENVIRONMENT.md).
- Looking for a specific design decision, architectural narrative, service contract, or compliance artifact? Start at the [Documentation Index](DOCUMENTATION_INDEX.md), the [Engineering Blog](blog/index.md), or the [ADR Log](adr/index.md).
