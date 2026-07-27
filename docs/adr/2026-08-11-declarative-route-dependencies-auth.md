# ADR-085: Route-Level Declarative Authorization Dependencies

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @fderuiter, @reviewer

---

## 1. Context & Problem Statement
Historically, authorization and Role-Based Access Control (RBAC) in the Cadence Clinical platform were enforced procedurally inside individual route functions. While this approach was functional, it led to duplicate boilerplates, increased risk of authorization bypass, and reduced overall developer velocity. The platform needed a centralized, route-level declarative system to define access permissions and role checks cleanly at the HTTP routing layer.

## 2. Decision Drivers & Constraints
* **Driver 1:** Security and Compliance (FDA 21 CFR Part 11 and GxP standards require strict, verifiable access control over clinical operations).
* **Driver 2:** Maintainability & Readability (Centralizing authorization logic into reusable decorators/dependencies reduces duplication).
* **Driver 3:** Developer Velocity (Providing clear, declarative route declarations simplifies onboarding and code reviews).

## 3. Options Considered
### Option 1: Inline Procedural Role Checking
* **Overview:** Keep checking roles directly inside the route logic using `verify_roles(user_roles)`.
* **Pros:**
  * ✅ Maximum flexibility within individual logic branches.
* **Cons:**
  * ❌ Repetitive boilerplate across dozens of endpoints.
  * ❌ Higher probability of security vulnerabilities due to human error.

### Option 2: Route-Level Declarative Dependencies - Selected
* **Overview:** Utilize FastAPI's dependency injection (`Depends`) to enforce role verification natively at the route declaration layer.
* **Pros:**
  * ✅ Enforced before the route handler is invoked.
  * ✅ Declared explicitly in the function signature, serving as self-documenting code.
  * ✅ Easy to audit and verify statically.
* **Cons:**
  * ❌ Slightly less granular than inline checks for complex mixed-role logic within a single route (though mitigable with scoped helper functions).

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Transitioning to route-level declarative dependencies standardizes routing layer security across all clinical services. It ensures consistent enforcement, meets rigid regulatory compliance parameters, and dramatically improves the readability of endpoints.

## 5. Consequences & Trade-offs
* **Positive Impact:** Cleaner route handlers, reliable authorization guards, and standardized error responses for unauthorized access.
* **Negative Impact / Technical Debt:** We must migrate legacy routes systematically to avoid authorization gaps.
* **Mitigation Strategy:** Backward compatibility is retained by supporting both legacy role claims and the structured gateway token verification patterns in tandem.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Gateway, Execution, and security libraries.
* **Verification Plan:** Verified through extensive Python testing using `pytest`, ensuring that both compliant and non-compliant access attempts are handled correctly.
