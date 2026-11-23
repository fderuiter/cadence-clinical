using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Unit verification for Keycloak self-registration claim writeback, the dedicated trial onboarding operator,
/// commercial vs. clinical authority separation, and expired-trial read/export preservation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CommercialAndOnboardingUnitTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;

    [Fact(DisplayName = "TrialOnboardingOperator_IsPermittedToProvisionTenants_WithoutDevelopmentBootstrap")]
    public async Task TrialOnboardingOperatorIsPermittedToProvisionTenantsWithoutDevelopmentBootstrap()
    {
        var tenantId = TenantId.New();
        var options = new KernelAuthorityOptions(new HashSet<string>([KernelAuthorityOptions.TrialOnboardingOperator.Id]));
        var evaluator = new AuthorityEvaluator(new EmptyAuthorityStore(), options);

        var decision = await evaluator.EvaluateAsync(
            new AuthorizationRequest(
                KernelAuthorityOptions.TrialOnboardingOperator,
                tenantId,
                new Capability("tenant.provision", VerbClass.Execute),
                NodeRef.For(tenantId),
                T0,
                T0),
            CancellationToken.None);

        decision.Permitted.Should().BeTrue();

        var randomUserDecision = await evaluator.EvaluateAsync(
            new AuthorizationRequest(
                Actor.Person("01960000-0000-7000-8000-000000000010"),
                tenantId,
                new Capability("tenant.provision", VerbClass.Execute),
                NodeRef.For(tenantId),
                T0,
                T0),
            CancellationToken.None);

        randomUserDecision.Permitted.Should().BeFalse();
    }

    [Fact(DisplayName = "CompleteCommercialEntitlement_NeverGrantsClinicalVisibilityOrCommandAuthority_WithoutStudyGrant")]
    public async Task CompleteCommercialEntitlementNeverGrantsClinicalVisibilityOrCommandAuthorityWithoutStudyGrant()
    {
        var tenantId = TenantId.New();
        var sponsorId = OrganizationId.New();
        var studyId = StudyId.New();
        var studyRef = NodeRef.For(studyId);
        var obsRef = NodeRef.For(ObservationId.New());
        var callerWithoutStudyGrant = Actor.Person("01960000-0000-7000-8000-000000000020");

        // Even when the tenant holds CommercialTier.Complete and all eight product families,
        // commercial state is orthogonal to AuthorityEvaluator and GrantNodeVisibility.
        CommercialCatalog.FamiliesForTier(CommercialTier.Complete).Should().HaveCount(8);

        var store = new SingleStudyAuthorityStore(sponsorId, studyId);
        var visibility = new GrantNodeVisibility(store, new FakeClock(T0));
        var masked = await visibility.MaskAsync(
            tenantId,
            callerWithoutStudyGrant,
            [studyRef, obsRef],
            CancellationToken.None);

        masked.Should().ContainKey(studyRef).WhoseValue.Should().Be(MaskReason.NotPermitted);
        masked.Should().ContainKey(obsRef).WhoseValue.Should().Be(MaskReason.NotPermitted);

        var evaluator = new AuthorityEvaluator(
            store,
            new KernelAuthorityOptions(new HashSet<string>([KernelAuthorityOptions.TrialOnboardingOperator.Id])));

        var commandDecision = await evaluator.EvaluateAsync(
            new AuthorizationRequest(
                callerWithoutStudyGrant,
                tenantId,
                new Capability("observation.record", VerbClass.Execute),
                studyRef,
                T0,
                T0),
            CancellationToken.None);

        commandDecision.Permitted.Should().BeFalse();
        commandDecision.Conditions.Should().ContainSingle(c => !c.Met && c.Code == AuthorityEvaluator.GrantCondition);
    }

    [Fact(DisplayName = "ExpiredTrialSubscription_RefusesClinicalMutationsWith403_WhileAdmittingBaselineKernelCommands")]
    public async Task ExpiredTrialSubscriptionRefusesClinicalMutationsWith403WhileAdmittingBaselineKernelCommands()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = TenantId.New();
        var sponsorId = OrganizationId.New();
        var subscriptionId = SubscriptionId.New();
        var clock = new FakeClock(T0.AddDays(15));

        var expiredReader = new StubEntitlementReader(new TenantEntitlementSnapshot(
            tenantId,
            subscriptionId,
            CommercialTier.Individual,
            SubscriptionStatus.Expired,
            StartedAt: T0,
            TrialEndsAt: T0.AddDays(14),
            IsReadOnly: true,
            ProductFamilies: [ProductFamily.Blueprint, ProductFamily.Capture]));

        var unitOfWork = new RecordingNoOpUnitOfWorkFactory();
        await using var services = new ServiceCollection()
            .AddSingleton<IClock>(clock)
            .AddSingleton<ICurrentActor>(new FixedActor(Actor.Person("01960000-0000-7000-8000-000000000030")))
            .AddSingleton<IAuthorizer>(new PermitAllAuthorizer())
            .AddSingleton<ITenantEntitlementReader>(expiredReader)
            .AddSingleton<IKernelUnitOfWorkFactory>(unitOfWork)
            .AddKernelCommandPipeline()
            .BuildServiceProvider();

        var sender = services.GetRequiredService<IKernelCommandSender>();

        // 1. Clinical study mutation is blocked with 403 K.Subscription.Expired (audited as a refusal without committing).
        var studyResult = await sender.SendAsync(
            new RegisterStudy(
                tenantId,
                StudyId.New(),
                sponsorId,
                "TRIAL-EXP-101",
                "Blocked Post-Trial Study",
                "Phase II",
                "idem-blocked-study",
                "Attempt study creation after trial expiry"),
            cancellationToken: ct);

        studyResult.IsSuccess.Should().BeFalse();
        studyResult.Error!.Code.Should().Be(KernelErrors.SubscriptionExpired.Code);
        studyResult.Error.Code.Should().Be("K.Subscription.Expired");
        studyResult.Error.HttpStatus.Should().Be(403);
        unitOfWork.CommittedCount.Should().Be(0, "expired-trial refusal never commits state mutations");
        unitOfWork.Refusals.Should().ContainSingle(r => r.Code == "K.Subscription.Expired");

        // 2. Baseline regulated capability ([BaselineKernelCommand], e.g. Attest / ApplyLock / ReleaseLock / ActivateSubscription) is never blocked by subscription expiry,
        //    whereas SwitchTrialTier, GrantProductEntitlement, and RevokeProductEntitlement are NOT baseline so expired workspaces cannot bypass scoped activation.
        BaselineKernelCommandAttribute.IsBaseline(typeof(Attest)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(SignCollectionInstance)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(ApplyLock)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(ReleaseLock)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(DefineRole)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(IssueGrant)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(ActivateSubscription)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(GrantProductEntitlement)).Should().BeFalse();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RevokeProductEntitlement)).Should().BeFalse();
        BaselineKernelCommandAttribute.IsBaseline(typeof(SwitchTrialTier)).Should().BeFalse();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RegisterStudy)).Should().BeFalse();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RecordObservation)).Should().BeFalse();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RecordCollectedValues)).Should().BeFalse();
    }

    [Fact(DisplayName = "TrialTierSwitchPreservesTrialCountdown_AndActivateSubscriptionRequiresValidatedStudyScoping")]
    public void TrialTierSwitchPreservesTrialCountdownAndActivateSubscriptionRequiresValidatedStudyScoping()
    {
        var tenantId = TenantId.New();
        var subscriptionId = SubscriptionId.New();
        var actor = Actor.Person("01960000-0000-7000-8000-000000000040");
        var trialEnd = T0.AddDays(30);

        var subscription = new Subscription(
            subscriptionId,
            tenantId,
            CommercialTier.Individual,
            SubscriptionStatus.Trialing,
            startedAt: T0,
            trialEndsAt: trialEnd,
            createdBy: actor.Id,
            reasonForChange: "Start trial");

        subscription.Status.Should().Be(SubscriptionStatus.Trialing);
        subscription.TrialEndsAt.Should().Be(trialEnd);

        // Switching tiers during an active trial appends bitemporal SubscriptionPeriod + ProductEntitlement rows,
        // keeps Status == Trialing, and preserves original 30-day TrialEndsAt and prior CreatedBy/ReasonForChange.
        subscription.SwitchTier(CommercialTier.Select, T0.AddDays(1), "actor-select", "Switch trial tier to Select");
        subscription.Tier.Should().Be(CommercialTier.Select);
        subscription.Status.Should().Be(SubscriptionStatus.Trialing);
        subscription.TrialEndsAt.Should().Be(trialEnd);
        subscription.GetEnabledProductFamilies().Should().HaveCount(5);

        subscription.SwitchTier(CommercialTier.Complete, T0.AddDays(2), "actor-complete-1", "Switch trial tier to Complete");
        subscription.Tier.Should().Be(CommercialTier.Complete);
        subscription.Status.Should().Be(SubscriptionStatus.Trialing);
        subscription.TrialEndsAt.Should().Be(trialEnd);
        subscription.GetEnabledProductFamilies().Should().HaveCount(8);

        subscription.SwitchTier(CommercialTier.Select, T0.AddDays(3), "actor-narrow", "Narrow trial tier to Select");
        subscription.GetEnabledProductFamilies().Should().HaveCount(5);

        subscription.SwitchTier(CommercialTier.Complete, T0.AddDays(4), "actor-complete-2", "Re-expand trial tier to Complete");
        subscription.Tier.Should().Be(CommercialTier.Complete);
        subscription.TrialEndsAt.Should().Be(trialEnd);
        subscription.GetEnabledProductFamilies().Should().HaveCount(8);
        subscription.Periods.Should().Contain(p => p.CreatedBy == actor.Id && p.ReasonForChange == "Start trial", "original period row is preserved untouched in bitemporal history");

        // Validation rejects missing or invalid scoping fields with structured per-field errors.
        var missingErrors = SubscriptionScopingValidator.Validate(null);
        missingErrors.Keys.Should().BeEquivalentTo(
            "study_phase",
            "study_duration_months",
            "countries",
            "site_count",
            "participant_count",
            "active_study_count",
            "billing_confirmation");

        var invalidScoping = new SubscriptionScoping(
            StudyPhase: new string('X', 65),
            StudyDurationMonths: 0,
            Countries: ["  "],
            SiteCount: -1,
            ParticipantCount: 0,
            ActiveStudyCount: 0,
            BillingConfirmation: "PO");
        var invalidErrors = SubscriptionScopingValidator.Validate(invalidScoping);
        invalidErrors.Keys.Should().BeEquivalentTo(
            "study_phase",
            "study_duration_months",
            "countries",
            "site_count",
            "participant_count",
            "active_study_count",
            "billing_confirmation");

        // Valid scoping transitions both Expired and Trialing workspaces to Active and records scoping metadata.
        subscription.Expire(T0.AddDays(31), actor.Id, "Trial window elapsed");
        subscription.Status.Should().Be(SubscriptionStatus.Expired);
        subscription.IsExpiredAt(T0.AddDays(31)).Should().BeTrue();

        var validScoping = new SubscriptionScoping(
            StudyPhase: "Phase II",
            StudyDurationMonths: 18,
            Countries: ["US", "DE", "NL"],
            SiteCount: 14,
            ParticipantCount: 220,
            ActiveStudyCount: 2,
            BillingConfirmation: "PO-2026-8841");
        SubscriptionScopingValidator.Validate(validScoping).Should().BeEmpty();

        subscription.Activate(CommercialTier.Select, validScoping, T0.AddDays(32), actor.Id, "Scoped activation");
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.Tier.Should().Be(CommercialTier.Select);
        subscription.IsExpiredAt(T0.AddDays(32)).Should().BeFalse();
        subscription.GetScoping().Should().NotBeNull();
        subscription.GetScoping()!.StudyPhase.Should().Be("Phase II");
        subscription.GetScoping()!.Countries.Should().Equal("US", "DE", "NL");
        subscription.GetScoping()!.BillingConfirmation.Should().Be("PO-2026-8841");
    }

    [Fact(DisplayName = "SubscriptionQuoteToken_VerifiesHmacBoundQuotes_AndRejectsRawPoTamperingWrongTenantOrExpiry")]
    public void SubscriptionQuoteTokenVerifiesHmacBoundQuotesAndRejectsRawPoTamperingWrongTenantOrExpiry()
    {
        const string secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"; // pragma: allowlist secret
        var tenantId = TenantId.New();
        var scoping = new SubscriptionScoping(
            StudyPhase: "Phase II",
            StudyDurationMonths: 18,
            Countries: ["US", "DE"],
            SiteCount: 12,
            ParticipantCount: 240,
            ActiveStudyCount: 2,
            BillingConfirmation: "pending");

        string token = SubscriptionQuoteToken.Issue(
            signingSecret: secret,
            tenantId: tenantId,
            tier: CommercialTier.Select,
            scoping: scoping,
            quoteReference: "QUOTE-2026-901",
            issuedAt: T0,
            expiresAt: T0.AddHours(4));
        token.Should().StartWith("qtk1.");

        var signedScoping = scoping with { BillingConfirmation = token };
        SubscriptionQuoteToken.TryVerify(
            token,
            secret,
            tenantId,
            CommercialTier.Select,
            signedScoping,
            T0.AddHours(1),
            out var verified).Should().BeTrue();
        verified!.QuoteReference.Should().Be("QUOTE-2026-901");

        // Plain PO string is rejected
        SubscriptionQuoteToken.TryVerify("PO-2026-8841", secret, tenantId, CommercialTier.Select, signedScoping, T0, out _)
            .Should().BeFalse();

        // Wrong tenant, escalated tier, inflated scoping cap, expired token, or tampered signature all fail closed
        SubscriptionQuoteToken.TryVerify(token, secret, TenantId.New(), CommercialTier.Select, signedScoping, T0, out _)
            .Should().BeFalse();
        SubscriptionQuoteToken.TryVerify(token, secret, tenantId, CommercialTier.Complete, signedScoping, T0, out _)
            .Should().BeFalse();
        SubscriptionQuoteToken.TryVerify(token, secret, tenantId, CommercialTier.Select, signedScoping with { SiteCount = 13 }, T0, out _)
            .Should().BeFalse();
        SubscriptionQuoteToken.TryVerify(token, secret, tenantId, CommercialTier.Select, signedScoping, T0.AddHours(5), out _)
            .Should().BeFalse();
        SubscriptionQuoteToken.TryVerify(token[..^2] + "ff", secret, tenantId, CommercialTier.Select, signedScoping, T0, out _)
            .Should().BeFalse();
    }

    [Fact(DisplayName = "HttpKeycloakOnboardingClient_WritesTenantClaimExplicitSiteAndStudyScopesWithoutProfileMutationOrWildcard_AndFailsClosedWhenUnconfigured")]
    public async Task HttpKeycloakOnboardingClientWritesTenantClaimExplicitSiteAndStudyScopesWithoutProfileMutationOrWildcardAndFailsClosedWhenUnconfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        string subjectId = "4e999999-1111-4444-8888-999999999999";
        string tenantCode = "trial-4e9999991111";
        var handler = new FakeKeycloakAdminHandler(subjectId);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeycloakAdmin:BaseUrl"] = "http://keycloak.internal:8080",
                ["KeycloakAdmin:Realm"] = "cadence",
                ["KeycloakAdmin:AdminRealm"] = "master",
                ["KeycloakAdmin:ClientId"] = "admin-cli",
                ["KeycloakAdmin:AdminUsername"] = "admin",
                ["KeycloakAdmin:AdminPassword"] = "admin-secret",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeycloakOnboarding(config);
        services.AddHttpClient(KeycloakOnboardingServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IKeycloakOnboardingClient>();
        var scopeSync = provider.GetRequiredService<IKernelIdentityScopeSynchronizer>();

        await client.AssignTrialWorkspaceAsync(
            subjectId,
            tenantCode,
            ["Sponsor Admin", "Sponsor Designer", "Site Investigator"],
            ["SITE-MERIDIAN"],
            [],
            ct);

        handler.TokenRequested.Should().BeTrue();
        handler.ProfileEndpointCalled.Should().BeFalse("runtime code must never weaken Keycloak declarative user-profile policy");
        handler.UpdatedUserJson.Should().NotBeNull();
        handler.UpdatedUserJson!["username"]!.GetValue<string>().Should().Be("founder@meridian.example");
        handler.UpdatedUserJson["email"]!.GetValue<string>().Should().Be("founder@meridian.example");
        handler.UpdatedUserJson["firstName"]!.GetValue<string>().Should().Be("Elena");
        handler.UpdatedUserJson["attributes"]!["tenant_id"]![0]!.GetValue<string>().Should().Be(tenantCode);
        handler.UpdatedUserJson["attributes"]!["site_id"]![0]!.GetValue<string>().Should().Be("SITE-MERIDIAN");
        handler.UpdatedUserJson["attributes"]!["study_ids"].Should().BeNull("onboarding must never stamp wildcard study_ids = ['*'] onto OIDC tokens");
        handler.AssignedRoleNames.Should().Equal("Sponsor Admin", "Sponsor Designer", "Site Investigator");

        await scopeSync.GrantStudyScopeAsync(Actor.Person(subjectId), TenantId.New(), tenantCode, "MERIDIAN-101", ct);
        handler.ProfileEndpointCalled.Should().BeFalse("study scope sync must never call /users/profile");
        handler.UpdatedUserJson["attributes"]!["study_ids"]![0]!.GetValue<string>().Should().Be("MERIDIAN-101");
        handler.UpdatedUserJson["attributes"]!["study_ids"]!.AsArray().Select(n => n!.GetValue<string>())
            .Should().NotContain("*");

        // Blank KeycloakAdmin config fails closed instead of silently succeeding.
        var emptyConfig = new ConfigurationBuilder().Build();
        var unconfiguredServices = new ServiceCollection();
        unconfiguredServices.AddLogging();
        unconfiguredServices.AddKeycloakOnboarding(emptyConfig);
        await using var unconfiguredProvider = unconfiguredServices.BuildServiceProvider();
        var failClosedClient = unconfiguredProvider.GetRequiredService<IKeycloakOnboardingClient>();
        Func<Task> act = () => failClosedClient.AssignTrialWorkspaceAsync(
            subjectId,
            tenantCode,
            ["Sponsor Admin"],
            ["SITE-MERIDIAN"],
            [],
            ct);
        await act.Should().ThrowAsync<KeycloakOnboardingException>();
    }

    [Fact(DisplayName = "TrialOnboardingService_AndDevelopmentDemoData_GrantParticipantRegisterToExecutionForms_AndArtifactCapabilitiesToSponsorRoles")]
    public async Task TrialOnboardingServiceAndDevelopmentDemoDataGrantParticipantRegisterAndArtifactCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        const string subjectId = "77777777-2222-4444-8888-aaaaaaaaaaaa";
        var sender = new RecordingBootstrapSender();
        var directory = new EmptyTenantDirectory();
        var entitlements = new StubEntitlementReader(new TenantEntitlementSnapshot(
            TenantId.New(),
            SubscriptionId: null,
            CommercialTier.Individual,
            SubscriptionStatus.Trialing,
            StartedAt: T0,
            TrialEndsAt: T0.AddDays(30),
            IsReadOnly: false,
            ProductFamilies: []));
        var keycloak = new NoOpKeycloakClient();

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddScoped<ServiceActorScope>()
            .AddScoped<IKernelCommandSender>(_ => sender)
            .BuildServiceProvider();

        var service = new TrialOnboardingService(
            directory,
            new EmptyStudyDirectory(),
            new NoForeignBindingProbe(),
            entitlements,
            provider.GetRequiredService<IServiceScopeFactory>(),
            keycloak,
            new FakeClock(T0),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TrialOnboardingService>>());

        var result = await service.BootstrapAsync(
            Actor.Person(subjectId),
            new TrialOnboardingInput("Meridian Bio", "US", "Individual"),
            ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);

        var definedRoles = sender.Commands.OfType<DefineRole>().ToDictionary(r => r.Code, r => r.Capabilities);
        definedRoles.Should().ContainKey("TRIAL_EXECUTION_FORMS");
        definedRoles["TRIAL_EXECUTION_FORMS"].Should().Contain(["execution.form.open", "participant.register"]);

        definedRoles.Should().ContainKey("SPONSOR_ADMIN");
        definedRoles["SPONSOR_ADMIN"].Should().Contain([
            "artifact.register",
            "artifact.add_version",
            "effectivity.declare",
            "effectivity.change_status",
            "attestation.sign",
        ]);

        definedRoles.Should().ContainKey("SPONSOR_DESIGNER");
        definedRoles["SPONSOR_DESIGNER"].Should().Contain([
            "protocol.submit_for_review",
            "protocol.return_to_draft",
            "protocol.approve",
            "artifact.register",
            "artifact.add_version",
            "effectivity.declare",
            "effectivity.change_status",
            "attestation.sign",
        ]);

        var issuedGrants = sender.Commands.OfType<IssueGrant>().ToList();
        issuedGrants.Should().ContainSingle(g => g.Grantee == Actor.ServiceAccount("execution-forms"));

        var demoDesigner = Cadence.Kernel.Host.DevelopmentDemoData.Roles.Single(r => r.Code == "DEMO_DESIGNER");
        demoDesigner.Capabilities.Should().Contain([
            "protocol.submit_for_review",
            "protocol.return_to_draft",
            "protocol.approve",
            "artifact.register",
            "artifact.add_version",
            "effectivity.declare",
            "effectivity.change_status",
            "site.register",
            "study_site.link",
            "study_site.activate",
            "attestation.sign",
        ]);

        var demoDm = Cadence.Kernel.Host.DevelopmentDemoData.Roles.Single(r => r.Code == "DEMO_DATA_MANAGER");
        demoDm.Capabilities.Should().Contain([
            "artifact.register",
            "artifact.add_version",
            "effectivity.declare",
            "effectivity.change_status",
            "site.register",
            "study_site.link",
            "study_site.activate",
        ]);
    }

    private sealed class EmptyTenantDirectory : ITenantDirectory
    {
        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<TenantId?>(null);
    }

    private sealed class EmptyStudyDirectory : IStudyDirectory
    {
        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyDirectoryEntry>>([]);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudyDirectoryEntry?>(null);

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudyDirectoryEntry?>(null);

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>([]);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>([]);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteDirectoryEntry?>(null);

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(
            TenantId tenant,
            string siteCode,
            OrganizationId? institutionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteDirectoryEntry?>(null);

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>([]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudySiteDirectoryEntry?>(null);

        public Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(
            TenantId tenant,
            StudyId studyId,
            SiteId siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StudySiteDirectoryEntry?>(null);
    }

    private sealed class NoForeignBindingProbe : ISubjectTenantBindingProbe
    {
        public Task<bool> HasForeignBindingAsync(
            string subjectId,
            PersonId derivedPersonId,
            TenantId excludedTenantId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class NoOpKeycloakClient : IKeycloakOnboardingClient
    {
        public Task AssignTrialWorkspaceAsync(
            string subjectId,
            string tenantCode,
            IReadOnlyList<string> realmRoles,
            IReadOnlyList<string> siteCodes,
            IReadOnlyList<string> studyCodes,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task GrantStudyScopeAsync(
            Actor caller,
            TenantId tenantId,
            string? tenantCode,
            string studyCode,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingBootstrapSender : IKernelCommandSender
    {
        public List<ICommand> Commands { get; } = [];

        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            object outcome = command switch
            {
                ProvisionTenant pt => pt.TenantId,
                DefineRole dr => new RoleState(dr.RoleId, dr.Code, dr.Name, dr.Capabilities),
                IssueGrant ig => new GrantState(ig.GrantId, ig.Grantee, ig.Capability, ig.RoleId, ig.Scope, null, ig.WithGrantOption, ig.ValidFrom, ig.ValidTo, false),
                RegisterOrganization ro => ro.OrganizationId,
                RegisterSite rs => rs.SiteId,
                RegisterPerson rp => rp.PersonId,
                RegisterTenantMembership rtm => rtm.TenantMembershipId,
                ProvisionSubscription ps => ps.SubscriptionId,
                GrantProductEntitlement gpe => gpe.ProductEntitlementId,
                DefineWorkQueue dwq => new WorkQueueDefinitionState(dwq.WorkQueueDefinitionId, dwq.Code, 1, dwq.Title, dwq.Capability, dwq.VerbClass, dwq.Source),
                _ => throw new NotSupportedException($"Unexpected onboarding command {command.GetType().Name}."),
            };
            return Task.FromResult(CommandResult<TResult>.Success((TResult)outcome));
        }
    }

    private sealed class FixedActor(Actor actor) : ICurrentActor
    {
        public Actor? Actor { get; } = actor;
    }

    private sealed class PermitAllAuthorizer : IAuthorizer
    {
        public ValueTask<AuthorizationDecision> AuthorizeAsync(Actor actor, ICommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());
    }

    private sealed class StubEntitlementReader(TenantEntitlementSnapshot snapshot) : ITenantEntitlementReader
    {
        public Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
            TenantId tenantId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken,
            DateTimeOffset? knownAt = null) =>
            Task.FromResult(snapshot);
    }

    private sealed class RecordingNoOpUnitOfWorkFactory : IKernelUnitOfWorkFactory, IKernelCommandTransaction
    {
        public int CommittedCount { get; private set; }

        public List<KernelError> Refusals { get; } = [];

        public IKernelUnitOfWork Work => throw new InvalidOperationException("Handler should not run on expired subscription refusal.");

        public IKernelCommandTransaction Begin(CommandExecution execution) => this;

        public Task<IdempotentOutcome?> FindPriorOutcomeAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IdempotentOutcome?>(null);

        public void RecordPermit(AuthorizationDecision permit)
        {
        }

        public void RecordProposalExecution(NodeRef proposal)
        {
        }

        public Task<CommitOutcome> CommitAsync(string resultJson, CancellationToken cancellationToken)
        {
            CommittedCount++;
            return Task.FromResult(CommitOutcome.Committed);
        }

        public Task RecordDenialAsync(AuthorizationDecision denial, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RecordRefusalAsync(KernelError refusal, CancellationToken cancellationToken)
        {
            Refusals.Add(refusal);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyAuthorityStore : IAuthorityStore
    {
        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult<Grant?>(null);

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            Task.FromResult(ScopePath.TenantWide);

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);
    }

    private sealed class SingleStudyAuthorityStore(OrganizationId sponsorId, StudyId studyId) : IAuthorityStore
    {
        private readonly NodeRef _studyRef = NodeRef.For(studyId);

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult<Grant?>(null);

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            Task.FromResult(resource == _studyRef ? new ScopePath(sponsorId, studyId) : ScopePath.TenantWide);

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);
    }

    private sealed class FakeKeycloakAdminHandler(string subjectId) : HttpMessageHandler
    {
        public bool TokenRequested { get; private set; }

        public bool ProfileEndpointCalled { get; private set; }

        public JsonObject? UpdatedUserJson { get; private set; }

        public List<string> AssignedRoleNames { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path == "/realms/master/protocol/openid-connect/token")
            {
                TokenRequested = true;
                return JsonResponse("""{"access_token":"kc-admin-token"}""");
            }

            request.Headers.Authorization?.Scheme.Should().Be("Bearer");
            request.Headers.Authorization?.Parameter.Should().Be("kc-admin-token");

            if (path == "/admin/realms/cadence/users/profile")
            {
                ProfileEndpointCalled = true;
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (request.Method == HttpMethod.Get && path == $"/admin/realms/cadence/users/{subjectId}")
            {
                return JsonResponse($$"""
                    {
                      "id": "{{subjectId}}",
                      "username": "founder@meridian.example",
                      "email": "founder@meridian.example",
                      "firstName": "Elena",
                      "lastName": "Rostova",
                      "enabled": true
                    }
                    """);
            }

            if (request.Method == HttpMethod.Put && path == $"/admin/realms/cadence/users/{subjectId}")
            {
                UpdatedUserJson = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/admin/realms/cadence/roles/", StringComparison.Ordinal))
            {
                string roleName = Uri.UnescapeDataString(path["/admin/realms/cadence/roles/".Length..]);
                return JsonResponse($$"""{"id":"role-{{roleName}}","name":"{{roleName}}"}""");
            }

            if (request.Method == HttpMethod.Post && path == $"/admin/realms/cadence/users/{subjectId}/role-mappings/realm")
            {
                var arr = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsArray();
                foreach (var item in arr)
                {
                    AssignedRoleNames.Add(item!["name"]!.GetValue<string>());
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }
}
