# Cadence Clinical

> **The Metadata-Driven Clinical Execution Platform.** > *Unifying Clinical Study Design (MDR/SDR) and Electronic Data Capture (EDC) into a single, automated digital data flow.*

---

## 🚀 Overview

**Cadence Clinical** is a next-generation, open eClinical platform built to eliminate manual study builds, handoffs, and data silos in clinical research. By integrating the concepts of upstream Clinical Metadata Repositories (MDR) with downstream Electronic Data Capture (EDC) engines, Cadence Clinical turns static protocols into executable, machine-readable digital data workflows.

Cadence Clinical synthesizes domain paradigms extracted from two open-source reference implementations:
1. **`openstudybuilder-ref`**: Upstream study design, CDISC Unified Study Definitions Model (USDM), and graph-based metadata modeling.
2. **`openclinica-ref`**: Downstream EDC execution, subject enrollment state machines, eCRF form rendering (OpenRosa/Enketo XForms), and audit trails.

---

## 🏛️ System Architecture

Cadence Clinical operates as a modular, API-first monorepo designed around clean bounded contexts:

```text
               ┌─────────────────────────────────────────┐
               │          Unified Web Frontend           │
               └────────────────────┬────────────────────┘
                                    │
                                    ▼
               ┌─────────────────────────────────────────┐
               │        API Gateway & Auth (OIDC)        │
               └─────────┬─────────────────────┬─────────┘
                         │                     │
          ┌──────────────┴─────────┐         ┌─┴──────────────────────┐
          │                        │         │                        │
          ▼                        ▼         ▼                        ▼
┌──────────────────┐    ┌──────────────────────┐    ┌──────────────────┐
│  Designer App    │    │  Core Models Package │    │  Execution App   │
│  (MDR / USDM)    │───►│  (USDM ↔ ODM Models) │◄───│  (EDC & eCRFs)   │
└─────────┬────────┘    └──────────────────────┘    └─────────┬────────┘
          │                                                   │
          ▼                                                   ▼
┌──────────────────┐                                ┌──────────────────┐
│ Neo4j Graph DB   │                                │ PostgreSQL DB    │
│ (Study Metadata) │                                │ (Trial Data)     │
└──────────────────┘                                └──────────────────┘
