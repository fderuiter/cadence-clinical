# ADR-2523: Explicit Transactions Run Inside the Execution Strategy

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (backlog pass of epic #5310, gate 1)
- **Requirements:** PRD-ESR-002, PRD-ESR-003, PRD-SUB-001, 21 CFR 11.10(a), issue #5383

---

## 1. Context & Problem Statement

`AddCadencePostgresDbContext` registers every module context with Npgsql's retrying execution strategy
(`EnableRetryOnFailure`). Under a retrying strategy EF Core refuses a transaction the application opens itself unless
the whole unit of work runs inside `IExecutionStrategy.ExecuteAsync`, because a retry has to replay the whole
transaction, not the one statement that failed.

The code did not follow that rule:

- Interop's eSource batch accept and batch reject opened a transaction directly. On PostgreSQL both routes failed
  with 500 every time. The tests that qualified PRD-ESR-002/003 used the in-memory provider, where the transaction
  branch never ran.
- Execution registered its context without a retrying strategy and instead wrapped `SaveChangesAsync` in a Polly
  pipeline. That pipeline could not retry a save inside an open transaction, so it skipped those saves, and
  enrollment left a transaction open on the repository between two calls to hold its advisory lock.
- Safety and the IP kit reconciliation used the strategy correctly, each written by hand.

## 2. Decision Drivers & Constraints

- A transient failure must be retried as a whole unit of work, or not at all.
- One way to do it, so a new call site cannot get it wrong.
- A check that fails the build when someone opens a transaction outside that way.

## 3. Options Considered

### Option 1: One helper, and a ban on everything else (Selected)

- **Overview:** `CadenceTransactions.ExecuteInTransactionAsync` in `Cadence.Database` runs a delegate in one
  transaction inside the context's execution strategy and commits when it returns. On a retry it clears the change
  tracker and the pending audit actions, and runs the delegate again from the start. It joins a transaction that is already open, and runs the
  delegate directly on a provider without transactions. `BannedSymbols.txt` bans every `BeginTransaction` and
  `BeginTransactionAsync` overload, so the analyzer fails the build at any other call site.
- **Pros:**
  - ✅ Correct by construction at every call site.
  - ✅ The ban is the check #5383 asks for, and it runs in every build.
- **Cons:**
  - ❌ The delegate must load what it changes inside itself, because a retry discards what it tracked.

### Option 2: Fix each call site by hand

- **Pros:**
  - ✅ No new API.
- **Cons:**
  - ❌ The next call site repeats the mistake; nothing catches it.

### Option 3: Drop `EnableRetryOnFailure`

- **Pros:**
  - ✅ Explicit transactions work anywhere.
- **Cons:**
  - ❌ No context retries a dropped connection or a serialization failure.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** It is the only option that keeps retries and makes the wrong form fail the build.

Where a single `SaveChangesAsync` is the whole write, no explicit transaction is needed: EF Core already saves it
atomically and the strategy retries it. The eSource batch routes and the clinical query batch operations are of that
kind, so their transactions are removed rather than wrapped.

## 5. Consequences & Trade-offs

- **Positive Impact:** eSource batch reconciliation works on PostgreSQL. Execution retries through the same
  strategy as every other module, and the Polly pipeline is gone. Enrollment takes its advisory lock inside the
  caller's unit of work, so the lock, the index read and the insert are retried together.
- **Negative Impact / Technical Debt:** The kernel's command pipeline and outbox relay still open transactions
  directly, under a suppression that names its reason. That is correct only while the kernel context has no retrying
  strategy; `KernelExecutionStrategyTests` fails if one is added.
- **Mitigation Strategy:** Moving the kernel onto a retrying strategy belongs to the composition host work (#5315).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/database` (`CadenceTransactions`), `BannedSymbols.txt`,
  `apps/interop`, `apps/execution`, `apps/safety`, the kernel persistence suppressions.
- **Verification Plan:**
  - `EsourceReconciliationPostgresTests` runs both batch routes through the real interop host on PostgreSQL as the
    application role. Before the fix they returned 500.
  - `ExecutionRepositoryBaseTests` shows a transient failure re-runs a whole unit of work once and writes one row
    and one audit entry, not one per attempt.
  - `EnrollmentIndexConcurrencyTests` enrolls 20 subjects concurrently on PostgreSQL through the new unit of work.
  - `ExecutionAuditInterceptorRegistrationTests` asserts the Execution host's context retries on failure.
