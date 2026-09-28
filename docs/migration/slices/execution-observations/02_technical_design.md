# Technical Design: EXECUTION-OBSERVATIONS (.NET 10 / C# 14)

**Slice:** `execution-observations` · **Bounded Context:** `Cadence.Apps.Execution` · **Target Assembly:** `apps/execution/src/Cadence.Apps.Execution.csproj`

---

## 1. Architectural Overview & Component Boundaries

### 1.1 Single-Assembly Clean Architecture (ADR-2233 / ADR-2234)

In alignment with ADR-2234, the observation, form submission, and clinical query components are organized within the single `Cadence.Apps.Execution` assembly using file-scoped namespaces and strict layer boundaries:

```
apps/execution/src/
  Domain/
    Observations/
      ObservationId.cs, DomainCode.cs, TestCode.cs                 (Strongly-typed IDs & value objects)
      ClinicalObservation.cs                                        (Aggregate root, IGxPAuditable)
      ObservationAdditionalProperties.cs                           (Structured dynamic projection model)
      DomainEvents/ ObservationCreatedDomainEvent.cs, ...
      Errors/ ObservationErrors.cs                                 (DomainError factory)
    Forms/
      FormSubmissionId.cs, FormId.cs                               (Strongly-typed IDs)
      FormSubmission.cs                                            (Aggregate root, IGxPAuditable)
      FormSubmissionStatus.cs                                      (Enum: Draft, Completed, Approved)
      SignatureManifest.cs                                         (Value object for 21 CFR Part 11 envelope)
      DomainEvents/ FormApprovedDomainEvent.cs, ...
      Errors/ FormErrors.cs
    Queries/
      QueryId.cs                                                   (Strongly-typed ID)
      ClinicalQuery.cs                                             (Aggregate root, IGxPAuditable)
      QueryStatus.cs                                               (Enum: Candidate, Open, Answered, Closed, etc.)
      DomainEvents/ QueryRaisedDomainEvent.cs, ...
      Errors/ QueryErrors.cs
    Ports/
      IObservationRepository.cs, IFormSubmissionRepository.cs, IClinicalQueryRepository.cs
  Application/
    Observations/
      CreateObservationCommand.cs + Handler
      VerifyObservationSdvCommand.cs + Handler
      GetObservationDetailQuery.cs + Handler
      ListObservationsBySubjectQuery.cs + Handler
    Forms/
      CreateFormSubmissionCommand.cs + Handler
      UpdateFormPayloadCommand.cs + Handler
      CompleteFormSubmissionCommand.cs + Handler
      ApproveFormSubmissionCommand.cs + Handler
      GetFormSubmissionDetailQuery.cs + Handler
    Queries/
      CreateQueryCommand.cs + Handler
      AnswerQueryCommand.cs + Handler
      CloseQueryCommand.cs + Handler
      ListQueriesBySubjectQuery.cs + Handler
    EditChecks/
      BatchEvaluationContext.cs                                    (3-query batch evaluation container)
      IBatchEvaluationContextLoader.cs                             (Port for efficient loading)
      IEditCheckEngine.cs                                          (AST evaluation engine)
    Abstractions/
      IUcumNormalizationService.cs, ILabRangeService.cs, IOutlierDetectionService.cs
    Serialization/
      ExecutionJsonContext.cs                                      (System.Text.Json Native AOT source generator)
  Infrastructure/
    Persistence/
      Configurations/
        ClinicalObservationConfiguration.cs                        (JSONB column mapping, GIN indexing)
        FormSubmissionConfiguration.cs                             (JSONB payload mapping)
        ClinicalQueryConfiguration.cs
      Repositories/
        ObservationRepository.cs, FormSubmissionRepository.cs, ClinicalQueryRepository.cs
    EditChecks/
      BatchEvaluationContextLoader.cs                              (Executes visits/forms/obs batch queries)
      AstEditCheckEngine.cs                                        (High-performance expression tree evaluator)
    Normalization/
      UcumNormalizationService.cs                                  (Canonical unit conversion engine)
  Endpoints/
    ObservationEndpoints.cs                                        (MapObservationEndpoints)
    FormEndpoints.cs                                               (MapFormEndpoints)
    QueryEndpoints.cs                                              (MapQueryEndpoints)
```

Dependency direction is strictly enforced: `Endpoints -> Application -> Domain`, `Infrastructure -> Domain` (implements `Domain.Ports`), `Infrastructure -> Application` (implements application abstractions). `Domain` has zero references to ASP.NET Core, MassTransit, or database drivers.

---

## 2. Core Domain Aggregates & Models

### 2.1 ClinicalObservation Aggregate Root

```csharp
namespace Cadence.Execution.Domain.Observations;

public sealed class ClinicalObservation : AggregateRootBase<ObservationId>, IGxPAuditable
{
    public SubjectCode SubjectId { get; private set; }
    public StudyCode StudyId { get; private set; }
    public SiteCode? SiteId { get; private set; }
    public string? VisitId { get; private set; }
    public DomainCode Domain { get; private set; }
    public DateTime ObservationDate { get; private set; }
    public TestCode TestCode { get; private set; }
    public string TestName { get; private set; }
    public double? Value { get; private set; }
    public string? ValueString { get; private set; }
    public string? Unit { get; private set; }
    public double? NormalizedValue { get; private set; }
    public string? NormalizedUnit { get; private set; }
    public bool IsOutlier { get; private set; }
    public bool IsSdvVerified { get; private set; }
    public string? SdvVerifiedBy { get; private set; }
    public DateTime? SdvVerifiedAt { get; private set; }
    public string? PageId { get; private set; }
    public bool IsSdvFlagged { get; private set; }
    public string? SdvFlagReason { get; private set; }
    public string? ProtocolVersionTag { get; private set; }
    public int? ProtocolVersionIndex { get; private set; }

    // Dynamic JSONB projection properties (ADR-0117)
    public ObservationAdditionalProperties AdditionalProperties { get; private set; } = new();

    // Factory method enforcing invariants
    public static Result<ClinicalObservation> Create(
        StudyCode studyId,
        SubjectCode subjectId,
        DomainCode domain,
        TestCode testCode,
        string testName,
        double? value,
        string? valueString,
        string? unit,
        DateTime? observationDate,
        SiteCode? siteId = null,
        string? visitId = null,
        ObservationAdditionalProperties? additionalProperties = null,
        IUcumNormalizationService? ucumService = null);

    public Result VerifySdv(string verifierUserId, DateTime verifiedAt);
    public Result FlagSdv(string reason);
}
```

### 2.2 FormSubmission Aggregate Root

```csharp
namespace Cadence.Execution.Domain.Forms;

public sealed class FormSubmission : AggregateRootBase<FormSubmissionId>, IGxPAuditable
{
    public StudyCode StudyId { get; private set; }
    public SiteCode SiteId { get; private set; }
    public SubjectCode SubjectId { get; private set; }
    public string? VisitId { get; private set; }
    public FormId FormId { get; private set; }
    public FormSubmissionStatus Status { get; private set; }
    public string? ProtocolVersion { get; private set; }
    public JsonDocument? Payload { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsReadonly { get; private set; }
    public string? ClonedFromId { get; private set; }
    public SignatureManifest? SignatureManifest { get; private set; }

    public static Result<FormSubmission> Initialize(
        StudyCode studyId,
        SiteCode siteId,
        SubjectCode subjectId,
        FormId formId,
        string? visitId,
        string? protocolVersion,
        JsonDocument? initialPayload);

    public Result UpdatePayload(JsonDocument newPayload, string reasonForChange);
    public Result Complete(IReadOnlyList<DomainError> blockingEditCheckErrors);
    public Result Approve(
        string signerId,
        string signerRole,
        string signingReason,
        string payloadHash,
        string hmacSignature,
        DateTime signedAt);
}
```

---

## 3. Persistence & Native AOT JSONB Projection

### 3.1 EF Core 10 Entity Mapping with JSONB

EF Core 10's native complex property JSON mapping is configured in `ClinicalObservationConfiguration`:

```csharp
public class ClinicalObservationConfiguration : IEntityTypeConfiguration<ClinicalObservation>
{
    public void Configure(EntityTypeBuilder<ClinicalObservation> builder)
    {
        builder.ToTable("clinical_observations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new ObservationId(value));

        // AdditionalProperties stored as native PostgreSQL jsonb
        builder.OwnsOne(o => o.AdditionalProperties, owned =>
        {
            owned.ToJson("additional_properties");
        });

        // Multi-column index for observation lookup coordinates
        builder.HasIndex(o => new { o.StudyId, o.SubjectId, o.VisitId, o.Domain, o.TestCode })
            .HasDatabaseName("idx_observation_coords");

        builder.HasIndex(o => o.SiteId)
            .HasDatabaseName("ix_clinical_observations_site_id");
    }
}
```

### 3.2 Native AOT JSON Serialization Context

```csharp
namespace Cadence.Execution.Application.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ObservationAdditionalProperties))]
[JsonSerializable(typeof(SignatureManifest))]
[JsonSerializable(typeof(FormSubmissionResponse))]
[JsonSerializable(typeof(ObservationResponse))]
[JsonSerializable(typeof(ClinicalQueryResponse))]
public partial class ExecutionJsonContext : JsonSerializerContext
{
}
```

---

## 4. Edit Check Execution Engine & Batching

### 4.1 BatchEvaluationContext (Preventing Connection Exhaustion)

In accordance with Phase 15 requirements, the edit check pipeline executes across three consolidated queries:

```csharp
public sealed class BatchEvaluationContext
{
    public IReadOnlyList<ClinicalVisit> Visits { get; }
    public IReadOnlyList<FormSubmission> Submissions { get; }
    public IReadOnlyList<ClinicalObservation> Observations { get; }

    public BatchEvaluationContext(
        IReadOnlyList<ClinicalVisit> visits,
        IReadOnlyList<FormSubmission> submissions,
        IReadOnlyList<ClinicalObservation> observations)
    {
        Visits = visits;
        Submissions = submissions;
        Observations = observations;
    }

    public static async Task<BatchEvaluationContext> LoadAsync(
        ExecutionDbContext context,
        SubjectCode subjectId,
        StudyCode studyId,
        CancellationToken ct)
    {
        var visitsTask = context.Visits
            .AsNoTracking()
            .Where(v => v.StudyId == studyId && v.SubjectId == subjectId)
            .ToListAsync(ct);

        var submissionsTask = context.FormSubmissions
            .AsNoTracking()
            .Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.IsActive)
            .ToListAsync(ct);

        var observationsTask = context.Observations
            .AsNoTracking()
            .Where(o => o.StudyId == studyId && o.SubjectId == subjectId)
            .ToListAsync(ct);

        await Task.WhenAll(visitsTask, submissionsTask, observationsTask);

        return new BatchEvaluationContext(visitsTask.Result, submissionsTask.Result, observationsTask.Result);
    }
}
```

---

## 5. Minimal API Routes & GxP Endpoint Filters

```csharp
public static class ObservationEndpoints
{
    public static RouteGroupBuilder MapObservationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateObservation)
            .WithName("CreateClinicalObservation")
            .WithSummary("Ingests a discrete clinical observation.")
            .AddEndpointFilter<AuditContextFilter>()
            .Produces<ObservationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", GetObservationById)
            .WithName("GetObservationById")
            .Produces<ObservationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id}/sdv", VerifyObservationSdv)
            .WithName("VerifyObservationSdv")
            .AddEndpointFilter<AuditContextFilter>()
            .Produces<ObservationResponse>(StatusCodes.Status200OK);

        return group;
    }
}
```
