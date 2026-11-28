using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Form signatures through the real kernel host on PostgreSQL (#5463): an investigator signs the exact revisions of a
/// form's values with a gateway step-up token, a correction leaves the signature as it was and shows it superseded beside
/// the corrected value, and a second signature then signs the new revisions. Every unusable token is refused before
/// anything is written. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormSignatureHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@execution-signatures.test";
    private const string Investigator = "pi@execution-signatures.test";
    private const string SignReason = "I approve the data on this form";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// The investigator signs revision 1 of the systolic value; the coordinator corrects it to revision 2, which leaves the
    /// signature bound to revision 1 and shows it superseded; signing again with the hash read before the correction is
    /// refused with 409; signing the current hash records a second signature. The kernel's audit chain holds both.
    /// </summary>
    [Fact(DisplayName = "HostSignature_BindsTheRevisions_IsSupersededByACorrection_AndIsSignedAgain")]
    [GxPRequirement("SRS-PRD-EDC-003")]
    public async Task HostSignatureBindsTheRevisionsIsSupersededByACorrectionAndIsSignedAgain()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, form) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var investigator = Client(code, Investigator);

        var unsigned = await ReadAsync(investigator, form, ct);
        unsigned.Signatures.Should().BeEmpty();
        var first = await SignAsync(investigator, form, Token(Investigator, form), "approved", unsigned.ContentHash!, HttpStatusCode.Created, ct);
        first.Should().Match<FormSignatureResponse>(s => s.Meaning == "Approved" && s.SignedBy == Investigator && !s.Superseded && s.ContentHash == unsigned.ContentHash);
        first.SignedValues.Should().ContainSingle().Which.Should().Match<FormSignedValueResponse>(v => v.ItemId == Systolic && v.Revision == 1);

        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, null, new
        {
            values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 1 } },
        }, HttpStatusCode.OK, ct);

        var corrected = await ReadAsync(investigator, form, ct);
        corrected.ContentHash.Should().NotBe(unsigned.ContentHash, "a correction is a new revision");
        corrected.Items.Single(i => i.ItemId == Systolic).Revision.Should().Be(2);
        var superseded = corrected.Signatures.Should().ContainSingle().Subject;
        superseded.Should().Match<FormSignatureResponse>(s => s.AttestationId == first.AttestationId && s.Superseded && s.ContentHash == unsigned.ContentHash);
        superseded.SignedValues.Should().ContainSingle().Which.Revision.Should().Be(1, "the signature still names the revision it signed");

        using (var stale = await investigator.SendAsync(SignRequest(form, Token(Investigator, form), "approved", unsigned.ContentHash!), ct))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(stale, ct)).Should().Be(TrustErrors.TargetContentChanged.Code);
        }

        var second = await SignAsync(investigator, form, Token(Investigator, form), "approved", corrected.ContentHash!, HttpStatusCode.Created, ct);
        second.SignedValues.Should().ContainSingle().Which.Revision.Should().Be(2);
        var resigned = await ReadAsync(investigator, form, ct);
        resigned.Signatures.Select(s => (s.AttestationId, s.Superseded)).Should().Equal((first.AttestationId, true), (second.AttestationId, false));

        await using var db = fixture.CreateAdminContext();
        var signedIds = new[] { first.AttestationId.ToString(), second.AttestationId.ToString() };
        (await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.AggregateType == "Attestation" && signedIds.Contains(a.AggregateId!))
                .Select(a => a.ActorId)
                .ToListAsync(ct))
            .Should().Equal([Investigator, Investigator], "both signatures are on the kernel's audit chain, by their signer");
    }

    /// <summary>
    /// The route refuses, with 401 and nothing recorded, a missing token, an expired one, one bound to another form or to
    /// another action, one issued to someone else and one already spent; a coordinator with a valid token but no
    /// <c>collection.sign</c> grant gets 403, and a meaning other than Approved or Responsibility gets 422.
    /// </summary>
    [Fact(DisplayName = "HostSignature_RefusesEveryUnusableStepUpToken_AndCallersWithoutTheGrant")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task HostSignatureRefusesEveryUnusableStepUpTokenAndCallersWithoutTheGrant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (_, code, form) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var investigator = Client(code, Investigator);
        var hash = (await ReadAsync(investigator, form, ct)).ContentHash!;

        var unusable = new (string Case, string? Token)[]
        {
            ("missing", null),
            ("expired", TestSecurityContext.CreateSigToken(
                Investigator, FormSignatureEndpoints.SignAction(form), FormSignatureEndpoints.SignSemanticAction, issuedAt: SystemClock.Instance.UtcNow.AddMinutes(-3))),
            ("another form", Token(Investigator, Guid.NewGuid())),
            ("another action", TestSecurityContext.CreateSigToken(Investigator, FormSignatureEndpoints.SignAction(form), "execution.form.approve")),
            ("someone else's", Token(Crc, form)),
        };
        foreach (var (name, token) in unusable)
        {
            using var refused = await investigator.SendAsync(SignRequest(form, token, "approved", hash), ct);
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, name);
        }

        var spent = Token(Investigator, form);
        await SignAsync(investigator, form, spent, "responsibility", hash, HttpStatusCode.Created, ct);
        using (var replay = await investigator.SendAsync(SignRequest(form, spent, "responsibility", hash), ct))
        {
            replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a step-up token signs once");
        }

        using (var ungranted = await crc.SendAsync(SignRequest(form, Token(Crc, form), "approved", hash), ct))
        {
            ungranted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(ungranted, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        using (var reviewed = await investigator.SendAsync(SignRequest(form, Token(Investigator, form), "reviewed", hash), ct))
        {
            reviewed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(reviewed, ct)).Should().Be(CollectionErrors.SignatureMeaningInvalid.Code);
        }

        (await ReadAsync(investigator, form, ct)).Signatures.Should().ContainSingle().Which.Meaning.Should().Be("Responsibility");
    }

    /// <summary>
    /// When an investigator without <c>enrollment.unblind</c> signs a form that holds both a routine clinical observation
    /// and a blinded randomization treatment observation, the immediate <c>201 Created</c> <see cref="FormSignatureResponse"/>
    /// masks <c>Reason</c>, <c>ContentHash</c>, and the blinded item's <see cref="FormSignedValueResponse"/>
    /// (<c>ObservationId == Guid.Empty</c>, <c>NullFlavor.Masked</c>) while keeping non-blinded signed values intact, and
    /// an unblinded pharmacist reads the full unmasked signature.
    /// </summary>
    [Fact(DisplayName = "HostSignature_ImmediateResponse_MasksBlindedValuesHashesAndReasons_WhilePreservingNonBlindedValues")]
    [KernelRule("K-AUTH-4")]
    [GxPRequirement("SRS-PRD-EDC-003")]
    public async Task HostSignatureImmediateResponseMasksBlindedValuesHashesAndReasonsWhilePreservingNonBlindedValues()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string pharmacistUser = "pharmacist@execution-signatures.test";
        const string treatmentItem = "item.randomization_treatment_assignment";
        const string blindedArm = "ARM-BLINDED-DOSE-200MG";
        const string signReasonWithArm = "Investigator approved vitals alongside ARM-BLINDED-DOSE-200MG kit";

        var code = $"T-SBL-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Blinded signature host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);

        var sbpConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic blood pressure", "SBP.", KernelProtocolScenario.Key(), "Defined SBP"),
            ct);
        var treatmentConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Randomized treatment arm (randomization_treatment_assignment)",
                "Treatment allocation.",
                KernelProtocolScenario.Key(),
                "Defined blinded concept"),
            ct);

        var design = Cadence.TestSupport.Kernel.Protocol.ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Cadence.Kernel.Protocol.Design.Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new Cadence.Kernel.Protocol.Design.PlannedActivity(
                    VisitId,
                    "Week 12 visit",
                    "ep.treatment",
                    [Cadence.Kernel.Protocol.Design.ActivityLocation.Site],
                    [SpecId],
                    new Cadence.Kernel.Protocol.Design.ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new Cadence.Kernel.Protocol.Design.CollectionSpec(
                    SpecId,
                    "Vital signs and kit check",
                    [
                        new Cadence.Kernel.Protocol.Design.CollectionItem(Systolic, sbpConcept, "mm[Hg]", Required: true),
                        new Cadence.Kernel.Protocol.Design.CollectionItem(treatmentItem, treatmentConcept, ValueKind: Cadence.Kernel.Evidence.ObservationValueKind.Text, Required: true),
                    ]),
            ],
        };

        var version = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, version, ct, Cadence.Kernel.Protocol.Design.ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Ready for review"), ct);
        var protocolSig = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, Cadence.Kernel.Trust.AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, protocolSig, KernelProtocolScenario.Key(), "Approved protocol"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, version, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = System.Text.Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsVersion = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethicsVersion,
                Cadence.Kernel.Evidence.ArtifactKind.EthicsApproval,
                study,
                "Ethics approval",
                new Cadence.Kernel.Evidence.ArtifactContent(Cadence.Kernel.Trust.ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://evidence/ethics"),
                KernelProtocolScenario.Key(),
                "Filed approval"),
            ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, _scenario.Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Made effective"),
            ct);

        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var (person, capability) in new[]
        {
            (Crc, "collection.record"),
            (Investigator, "collection.sign"),
            (pharmacistUser, "observation.verify"),
            (pharmacistUser, "enrollment.unblind"),
        })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(person), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Appointed to role"),
                ct);
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var saved = await _scenario.Protocol.OkAsync(
            Save(
                new Form(subject, version, opened.CollectionInstanceId),
                Value(Systolic, Mmhg(122)),
                Value(treatmentItem, new Cadence.Kernel.Evidence.TextValue(blindedArm))),
            ct);
        var formId = opened.CollectionInstanceId.Value;
        var sbpObsId = saved.Values.Single(v => v.ItemId == Systolic).ObservationId.Value;
        var treatmentObsId = saved.Values.Single(v => v.ItemId == treatmentItem).ObservationId.Value;

        using var investigator = Client(code, Investigator);
        using var pharmacist = Client(code, pharmacistUser);

        var unblindedForm = await ReadAsync(pharmacist, formId, ct);
        var unblindedHash = unblindedForm.ContentHash!;
        unblindedHash.Should().NotBeNullOrWhiteSpace();

        var blindedReadBefore = await ReadAsync(investigator, formId, ct);
        blindedReadBefore.ContentHash.Should().BeNull("blinded form read omits raw content_hash");

        using var signHttpReq = Request(
            HttpMethod.Post,
            $"/{formId:D}/signatures",
            signReasonWithArm,
            Token(Investigator, formId),
            new { meaning = "approved", content_hash = unblindedHash });
        using var signHttpRes = await investigator.SendAsync(signHttpReq, ct);
        var immediateText = await signHttpRes.Content.ReadAsStringAsync(ct);
        signHttpRes.StatusCode.Should().Be(HttpStatusCode.Created, immediateText);

        immediateText.Should().NotContain(blindedArm, "immediate sign response must never leak blinded treatment values or reasons");
        immediateText.Should().NotContain(unblindedHash, "immediate sign response must mask content_hash when blinded items are present");
        immediateText.Should().NotContain(treatmentObsId.ToString("D"), "immediate sign response must mask blinded observation identifiers");

        var immediateSig = JsonSerializer.Deserialize<FormSignatureResponse>(immediateText, Json)!;
        immediateSig.Meaning.Should().Be("Approved");
        immediateSig.SignedBy.Should().Be(Investigator);
        immediateSig.Reason.Should().Be("masked");
        immediateSig.ContentHash.Should().Be("masked");
        immediateSig.SignedValues.Should().NotBeNull().And.HaveCount(2);

        var signedSbp = immediateSig.SignedValues!.Single(v => v.ItemId == Systolic);
        signedSbp.ObservationId.Should().Be(sbpObsId);
        signedSbp.Value.Should().Be(Mmhg(122));

        var signedTreatment = immediateSig.SignedValues!.Single(v => v.ItemId == treatmentItem);
        signedTreatment.ObservationId.Should().Be(Guid.Empty);
        signedTreatment.Value.Should().Be(new Cadence.Kernel.Evidence.MissingValue(Cadence.Kernel.Evidence.NullFlavor.Masked));

        var pharmacistAfter = await ReadAsync(pharmacist, formId, ct);
        var unmaskedSig = pharmacistAfter.Signatures.Should().ContainSingle().Subject;
        unmaskedSig.AttestationId.Should().Be(immediateSig.AttestationId);
        unmaskedSig.Reason.Should().Be(signReasonWithArm);
        unmaskedSig.ContentHash.Should().Be(unblindedHash);
        unmaskedSig.SignedValues!.Single(v => v.ItemId == treatmentItem).ObservationId.Should().Be(treatmentObsId);
    }

    /// <summary>
    /// Investigator sign-off succeeds under a covering <see cref="LockKind.Frozen"/> lock so closeout signatures can be
    /// collected during soft freeze, whereas a covering <see cref="LockKind.Locked"/> lock rejects new signatures with
    /// <c>423 K.Assertion.Locked</c> without writing orphan <c>Attestation</c> rows.
    /// </summary>
    [Fact(DisplayName = "HostSignature_AllowsSigningUnderFrozenLock_AndRefusesUnderCoveringLockedLock")]
    [GxPRequirement("SRS-PRD-EDC-003")]
    public async Task HostSignatureAllowsSigningUnderFrozenLockAndRefusesUnderCoveringLockedLock()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, form) = await SeedAsync(ct);
        using var investigator = Client(code, Investigator);

        var readBeforeFreeze = await ReadAsync(investigator, form, ct);
        var observation = ObservationId.FromGuid(readBeforeFreeze.Items.Single(i => i.ItemId == Systolic).ObservationId!.Value);

        StudyId study;
        EnrollmentId enrollment;
        await using (var db = fixture.CreateContext(tenant))
        {
            var instance = await db.CollectionInstances.AsNoTracking().SingleAsync(c => c.Id == CollectionInstanceId.FromGuid(form), ct);
            study = instance.StudyId;
            enrollment = instance.EnrollmentId;
        }

        await _scenario.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment, observation, 1, KernelProtocolScenario.Key(), "SDV completed before freeze"),
            ct);

        var enrollmentScope = NodeRef.For(enrollment);
        var frozen = await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(tenant, study, enrollmentScope, ct, LockKind.Frozen),
            ct);

        var readWhileFrozen = await ReadAsync(investigator, form, ct);
        var signedUnderFreeze = await SignAsync(
            investigator,
            form,
            Token(Investigator, form),
            "approved",
            readWhileFrozen.ContentHash!,
            HttpStatusCode.Created,
            ct);
        signedUnderFreeze.Superseded.Should().BeFalse();

        await _scenario.Protocol.OkAsync(
            await _scenario.Observations.ReleaseAsync(tenant, study, frozen.LockId, ct),
            ct);

        await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(tenant, study, enrollmentScope, ct, LockKind.Locked),
            ct);

        int attestationsBeforeRefusal;
        await using (var db = fixture.CreateContext(tenant))
        {
            attestationsBeforeRefusal = await db.Attestations.CountAsync(ct);
        }

        using (var refused = await investigator.SendAsync(SignRequest(form, Token(Investigator, form), "responsibility", readWhileFrozen.ContentHash!), ct))
        {
            refused.StatusCode.Should().Be((HttpStatusCode)423);
            (await ProblemCodeAsync(refused, ct)).Should().Be(AssertionErrors.Locked.Code);
        }

        var directHandlerResult = await fixture.Sender.SendAsync(
            new SignCollectionInstance(
                tenant,
                AttestationId.New(),
                CollectionInstanceId.FromGuid(form),
                enrollment,
                AttestationMeaning.Responsibility,
                readWhileFrozen.ContentHash!,
                _scenario.Tick(),
                FormSignatureEndpoints.StepUpMethod,
                KernelProtocolScenario.Key(),
                "Attempted post-lock handler signature"),
            cancellationToken: ct);
        directHandlerResult.Error.Should().NotBeNull();
        directHandlerResult.Error!.Code.Should().Be(AssertionErrors.Locked.Code);

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(
                attestationsBeforeRefusal,
                "post-lock signature attempts must write zero new Attestation rows");
        }
    }

    /// <summary>
    /// A tenant whose study has the vital signs form in force, one screened participant with a form whose systolic value is
    /// entered at revision 1, the coordinator granted to enter values and the investigator granted to sign at the
    /// participant's study site.
    /// </summary>
    private async Task<(TenantId Tenant, string Code, Guid Form)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-SIG-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution signature host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct, requireDiastolic: false);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var (person, capability) in new[] { (Crc, "collection.record"), (Investigator, "collection.sign") })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(person), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Appointed to role"),
                ct);
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        await _scenario.Protocol.OkAsync(Save(new Form(subject, version, opened.CollectionInstanceId), Value(Systolic, Mmhg(120))), ct);
        return (tenant, code, opened.CollectionInstanceId.Value);
    }

    private static string Token(string signer, Guid form) =>
        TestSecurityContext.CreateSigToken(signer, FormSignatureEndpoints.SignAction(form), FormSignatureEndpoints.SignSemanticAction);

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error the signature route returns must be one its command declares (ADR-2600 §4.5).
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static Task<FormResponse> ReadAsync(HttpClient client, Guid form, CancellationToken ct) =>
        SendAsync<FormResponse>(client, HttpMethod.Get, $"/{form:D}", null, null, null, HttpStatusCode.OK, ct);

    private static async Task<FormSignatureResponse> SignAsync(
        HttpClient client, Guid form, string token, string meaning, string hash, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(SignRequest(form, token, meaning, hash), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<FormSignatureResponse>(text, Json)!;
    }

    private static HttpRequestMessage SignRequest(Guid form, string? token, string meaning, string hash) =>
        Request(HttpMethod.Post, $"/{form:D}/signatures", SignReason, token, new { meaning, content_hash = hash });

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, string? token, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, token, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, string? token, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (token is not null)
        {
            request.Headers.Add(GatewayHeaders.SigToken, token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
