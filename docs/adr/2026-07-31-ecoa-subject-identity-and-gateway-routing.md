# ADR-051: eCOA Subject Identity, Authorization, and Gateway Routing

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement

To establish a secure, compliant, first-class Subject (patient) identity boundary within the eCOA (electronic Clinical Outcome Assessment) diary submission portal, the Cadence Clinical platform requires rigorous authorization controls. Patients must be restricted from accessing staff-only endpoints, and they must only be permitted to view or submit records associated with their own authenticated subject identifier.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints

* **Driver 1 (Diary-Only Permissions):** Compliance policies and security specifications demand a diary-only patient boundary. Subjects must not have access to any clinical metadata management, eTMF, CTMS, or staff-scoped interop routes.
* **Driver 2 (Subject Identity Protection):** Cross-subject data leakage must be prevented at both the gateway and microservice layers.
* **Driver 3 (Compliance and 21 CFR Part 11):** Audit trails must cleanly record the Subject role and actions without bypassing security or change justification protocols.

## 3. Options Considered

### Option 1: Gateway Routing Restrictions combined with Downstream Identity Binding (Selected)
Define a dedicated `Subject` realm role in Keycloak. Enforce routing restrictions in `apps/gateway/main.py` allowing clients with the `Subject` role to only access designated ePRO submission routes. Implement reusable authorization helpers in `apps/interop/auth.py` to bind downstream payload `subject_id` to the authenticated identity.
* **Pros:**
  * ✅ Defense-in-depth: unauthorized requests are blocked both at the gateway and the microservice.
  * ✅ Reusable helpers can be imported by any future subject-facing interop modules.
  * ✅ Extremely clean separation of duties without code duplication.
* **Cons:**
  * ❌ Requires maintaining gateway routing path whitelist for Subjects.

### Option 2: Relying solely on Downstream Microservice Role Checking
Only validate roles and bind identities inside the interop microservice itself.
* **Pros:**
  * ✅ Gateway remains completely agnostic to roles.
* **Cons:**
  * ❌ Increased risk of misconfiguration leading to unauthorized staff endpoints being exposed to Subjects.
  * ❌ Gateway would forward requests that are destined to be rejected downstream, wasting bandwidth and compute resources.

## 4. Decision Outcome

* **Chosen Option:** Option 1
* **Justification:** Option 1 provides robust, end-to-end security aligning with industry best practices. Any request from a Subject is filtered at the API Gateway, and verified downstream in `apps/interop` to guarantee a patient cannot submit/sync diary logs for any other patient identifier.

## 5. Consequences & Trade-offs

* **Positive Impact:** Secure, first-class Subject principal boundary. Cross-subject leakage is fully eliminated. Staff roles remain intact.
* **Negative Impact / Technical Debt:** Added role check on the API Gateway requires updating the whitelist whenever new subject-facing endpoints are developed.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `docker/cadence-realm.json`, `apps/gateway/main.py`, `apps/interop/auth.py`, `apps/interop/main.py`
* **Verification Plan:** Unit and integration tests in `tests/test_interop.py` verify Subject role authentication, identity matching, cross-subject 403 rejections, and gateway routing blocks.
