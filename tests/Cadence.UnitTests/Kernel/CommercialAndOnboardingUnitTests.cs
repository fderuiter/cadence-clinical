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
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Modules;
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

        // 2. Baseline regulated capability ([BaselineKernelCommand], e.g. Attest / DefineRole / ActivateSubscription) is never blocked by subscription expiry.
        BaselineKernelCommandAttribute.IsBaseline(typeof(Attest)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(DefineRole)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(IssueGrant)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(ActivateSubscription)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(GrantProductEntitlement)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RevokeProductEntitlement)).Should().BeTrue();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RegisterStudy)).Should().BeFalse();
        BaselineKernelCommandAttribute.IsBaseline(typeof(RecordObservation)).Should().BeFalse();
    }

    [Fact(DisplayName = "HttpKeycloakOnboardingClient_WritesTenantClaimStudyWildcardAndSponsorRoles_PreservingUserFields")]
    public async Task HttpKeycloakOnboardingClientWritesTenantClaimStudyWildcardAndSponsorRolesPreservingUserFields()
    {
        var ct = TestContext.Current.CancellationToken;
        string subjectId = "4e999999-1111-4444-8888-999999999999";
        string tenantCode = "TRIAL-4E9999991111";
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

        await client.AssignTrialWorkspaceAsync(
            subjectId,
            tenantCode,
            ["Sponsor Admin", "Sponsor Designer"],
            ct);

        handler.TokenRequested.Should().BeTrue();
        handler.UpdatedProfilePolicy.Should().Be("ENABLED");
        handler.UpdatedUserJson.Should().NotBeNull();
        handler.UpdatedUserJson!["username"]!.GetValue<string>().Should().Be("founder@meridian.example");
        handler.UpdatedUserJson["email"]!.GetValue<string>().Should().Be("founder@meridian.example");
        handler.UpdatedUserJson["firstName"]!.GetValue<string>().Should().Be("Elena");
        handler.UpdatedUserJson["attributes"]!["tenant_id"]![0]!.GetValue<string>().Should().Be(tenantCode);
        handler.UpdatedUserJson["attributes"]!["study_ids"]![0]!.GetValue<string>().Should().Be("*");
        handler.AssignedRoleNames.Should().Equal("Sponsor Admin", "Sponsor Designer");
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
            CancellationToken cancellationToken) =>
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

        public string? UpdatedProfilePolicy { get; private set; }

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

            if (request.Method == HttpMethod.Get && path == "/admin/realms/cadence/users/profile")
            {
                return JsonResponse("""{"attributes":[]}""");
            }

            if (request.Method == HttpMethod.Put && path == "/admin/realms/cadence/users/profile")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                UpdatedProfilePolicy = body["unmanagedAttributePolicy"]?.GetValue<string>();
                return new HttpResponseMessage(HttpStatusCode.OK);
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
