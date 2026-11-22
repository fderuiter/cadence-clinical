using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Execution;
using Cadence.Kernel.Api;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// End-to-end PostgreSQL qualification of the Clinical Kernel study-build HTTP command surface:
/// non-demo trial onboarding grants, <c>AddCadenceStandards()</c> collection specification validation,
/// library item and standard deprecation with step-up signatures, <c>K-STD-1</c> concept mapping enforcement,
/// protocol review/approval/amendment/withdrawal, <c>K-TIME-4</c> and <c>K-TIME-5</c> site effectivity and activation,
/// eCRF opening without <c>409 K.Collection.NoGoverningVersion</c>, on-demand validation runs, and signed finding triage.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelStudyBuildApiPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private const string SampleOdmXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileType="Snapshot" FileOID="OID.HELIOS.301" CreationDateTime="2026-11-22T10:00:00Z" ODMVersion="1.3.2">
          <Study OID="S.HELIOS301">
            <GlobalVariables>
              <StudyName>HELIOS-301</StudyName>
              <StudyDescription>Helios Phase II Hypertension Trial</StudyDescription>
              <ProtocolName>HELIOS-301</ProtocolName>
            </GlobalVariables>
            <BasicDefinitions>
              <MeasurementUnit OID="MU.MMHG" Name="mm[Hg]">
                <Symbol><TranslatedText xml:lang="en">mm[Hg]</TranslatedText></Symbol>
              </MeasurementUnit>
            </BasicDefinitions>
            <MetaDataVersion OID="MDV.1" Name="v1.0">
              <Protocol>
                <StudyEventRef StudyEventOID="SE.SCREEN" OrderNumber="1" Mandatory="Yes" />
              </Protocol>
              <StudyEventDef OID="SE.SCREEN" Name="Screening" Repeating="No" Type="Scheduled">
                <FormRef FormOID="F.VS" OrderNumber="1" Mandatory="Yes" />
              </StudyEventDef>
              <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes" />
              </FormDef>
              <ItemGroupDef OID="IG.VS" Name="Vital Signs" Repeating="No" Domain="VS">
                <ItemRef ItemOID="IT.SYSBP" OrderNumber="1" Mandatory="Yes" />
              </ItemGroupDef>
              <ItemDef OID="IT.SYSBP" Name="SYSBP" DataType="integer" Length="3" SDSVarName="VSORRES">
                <Question><TranslatedText xml:lang="en">Systolic Blood Pressure</TranslatedText></Question>
                <MeasurementUnitRef MeasurementUnitOID="MU.MMHG" />
                <RangeCheck Comparator="LE" SoftHard="Soft">
                  <CheckValue>200</CheckValue>
                  <MeasurementUnitRef MeasurementUnitOID="MU.MMHG" />
                  <ErrorMessage><TranslatedText xml:lang="en">Systolic BP exceeds 200 mm[Hg].</TranslatedText></ErrorMessage>
                </RangeCheck>
              </ItemDef>
            </MetaDataVersion>
          </Study>
        </ODM>
        """;

    [Fact(DisplayName = "FounderWorkspace_CompletesFullStudyBuildSiteEffectivityFormCaptureAndSignedFindingTriage")]
    [GxPRequirement("PRD-CRF-003")]
    [GxPRequirement("PRD-CRF-004")]
    [GxPRequirement("PRD-CRF-006")]
    [GxPRequirement("PRD-CRF-010")]
    [GxPRequirement("PRD-CRF-012")]
    [GxPRequirement("PRD-SSU-003")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-192")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task FounderWorkspaceCompletesFullStudyBuildSiteEffectivityFormCaptureAndSignedFindingTriage()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(SystemClock.Instance.UtcNow);
        var keycloak = new RecordingKeycloakOnboardingClient();

        using var host = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<IKeycloakOnboardingClient>();
                services.AddSingleton<IKeycloakOnboardingClient>(keycloak);
                services.AddDeclaredKernelErrorsCheck();
            }));

        string founderSub = Guid.CreateVersion7(clock.UtcNow).ToString();

        // 1. Provision non-demo founder workspace via POST /api/v1/kernel/onboarding/trial (zero synthetic records).
        using var unprovisioned = host.CreateSignedClient(new GatewayIdentity(founderSub, [], [], TenantId: null));
        using var onboardResp = await unprovisioned.PostAsJsonAsync(
            "/api/v1/kernel/onboarding/trial",
            new { organization_name = "Helios Clinical Research", residency = "US", tier = "Individual" },
            ct);
        string onboardText = await onboardResp.Content.ReadAsStringAsync(ct);
        onboardResp.StatusCode.Should().Be(HttpStatusCode.OK, onboardText);
        using var onboardDoc = JsonDocument.Parse(onboardText);
        string tenantCode = onboardDoc.RootElement.GetProperty("tenant_code").GetString()!;
        string sponsorRef = onboardDoc.RootElement.GetProperty("organization_ref").GetString()!;

        using var founder = host.CreateSignedClient(new GatewayIdentity(
            founderSub,
            ["Sponsor Admin", "Sponsor Designer", "Data Manager"],
            ["*"],
            TenantId: tenantCode,
            StudyIds: ["*"]));

        // 2. Verify AddCadenceStandards() rejects invalid CollectionSpecification and accepts/approves/deprecates valid items & standards.
        var badCollectionContent = ParseJson("""{"name":"Invalid VS","items":[{"id":"SYSBP","concept":"NOT_A_CCODE","required":true}]}""");
        using (var badColResp = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, "library-items", "Validate collection spec", null, Key(), new
            {
                kind = "CollectionSpecification",
                key = "invalid-vs-spec",
                title = "Invalid Vital Signs",
                content = badCollectionContent,
            }),
            ct))
        {
            badColResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(badColResp, ct)).Should().Be("K.Standards.LibraryItemInvalid");
        }

        var validCollectionContent = ParseJson("""{"name":"Vital Signs","items":[{"id":"SYSBP","concept":"C25298","unit":"mm[Hg]","required":true}]}""");
        var draftedColSpec = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "library-items", "Draft vital signs block", null, Key(), new
            {
                kind = "CollectionSpecification",
                key = "vital-signs-panel",
                title = "Vital Signs Panel",
                content = validCollectionContent,
            }),
            HttpStatusCode.Created,
            ct);
        string colSpecRef = StripVersion(draftedColSpec.GetProperty("ref").GetString()!);

        string approveColToken = TestSecurityContext.CreateSigToken(
            founderSub,
            KernelValidationEndpoints.ApproveLibraryItemAction(colSpecRef),
            KernelValidationEndpoints.ApproveLibraryItemSemanticAction,
            issuedAt: clock.UtcNow);
        var approvedColSpec = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"library-items/{colSpecRef}/approve", "Approve vital signs block", approveColToken, Key(), new { meaning = "Approved" }),
            HttpStatusCode.OK,
            ct);
        approvedColSpec.GetProperty("status").GetString().Should().Be("Approved");

        // Missing X-Sig-Token on library-item deprecation must fail closed with 401.
        using (var missingDepSig = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"library-items/{colSpecRef}/deprecate", "Deprecate without sig", null, Key(), new { meaning = "Responsibility" }),
            ct))
        {
            missingDepSig.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await missingDepSig.Content.ReadAsStringAsync(ct)).Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        }

        string deprecateColToken = TestSecurityContext.CreateSigToken(
            founderSub,
            $"/api/v1/kernel/library-items/{colSpecRef}/deprecate",
            KernelValidationEndpoints.DeprecateLibraryItemSemanticAction,
            issuedAt: clock.UtcNow);
        var deprecatedColSpec = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"library-items/{colSpecRef}/deprecate", "Retire superseded block", deprecateColToken, Key(), new { meaning = "Responsibility" }),
            HttpStatusCode.OK,
            ct);
        deprecatedColSpec.GetProperty("status").GetString().Should().Be("Deprecated");

        // Register, approve, and deprecate ADaMIG 1.3 standard version (proving standard.deprecate grant + step-up check).
        var adamDraft = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "standards", "Register ADaMIG 1.3", null, Key(), new { seed_file = "adamig-1-3.json" }),
            HttpStatusCode.Created,
            ct);
        string adamRef = StripVersion(adamDraft.GetProperty("ref").GetString()!);

        string approveAdamToken = TestSecurityContext.CreateSigToken(
            founderSub,
            $"/api/v1/kernel/standards/{adamRef}/approve",
            KernelValidationEndpoints.ApproveStandardVersionSemanticAction,
            issuedAt: clock.UtcNow);
        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"standards/{adamRef}/approve", "Approve ADaMIG 1.3", approveAdamToken, Key(), new { meaning = "Approved" }),
            HttpStatusCode.OK,
            ct);

        using (var missingStdDepSig = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"standards/{adamRef}/deprecate", "Deprecate without sig", null, Key(), new { meaning = "Responsibility" }),
            ct))
        {
            missingStdDepSig.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        string deprecateAdamToken = TestSecurityContext.CreateSigToken(
            founderSub,
            $"/api/v1/kernel/standards/{adamRef}/deprecate",
            KernelValidationEndpoints.DeprecateStandardVersionSemanticAction,
            issuedAt: clock.UtcNow);
        var deprecatedAdam = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"standards/{adamRef}/deprecate", "Deprecate ADaMIG 1.3", deprecateAdamToken, Key(), new { meaning = "Responsibility" }),
            HttpStatusCode.OK,
            ct);
        deprecatedAdam.GetProperty("status").GetString().Should().Be("Deprecated");

        // 3. Register study HELIOS-301 and import ODM v1.3.2 draft (creates Draft ProtocolVersion v1 + Draft RulePack).
        var createdStudy = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "studies", "Create HELIOS-301 study", null, Key(), new
            {
                study_code = "HELIOS-301",
                title = "Helios Phase II Hypertension Trial",
                phase = "Phase 2",
                sponsor_ref = sponsorRef,
            }),
            HttpStatusCode.Created,
            ct);
        string studyRef = StripVersion(createdStudy.GetProperty("ref").GetString()!);

        var importedDraft = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"studies/{studyRef}/protocol-imports", "Import ODM v1.3.2 schema", null, Key(), new
            {
                format = "odm_xml",
                file_name = "helios-301.xml",
                content = SampleOdmXml,
            }),
            HttpStatusCode.Created,
            ct);
        string v1Ref = StripVersion(importedDraft.GetProperty("version").GetProperty("ref").GetString()!);
        var firstForm = importedDraft.GetProperty("forms")[0];
        string formSpecId = firstForm.GetProperty("id").GetString()!;
        var firstItem = firstForm.GetProperty("items")[0];
        string itemSpecId = firstItem.GetProperty("id").GetString()!;
        string itemConceptRef = firstItem.GetProperty("concept").GetString()!;
        string itemUnit = firstItem.TryGetProperty("unit", out var unitProp) && unitProp.ValueKind == JsonValueKind.String
            ? unitProp.GetString()!
            : "mm[Hg]";

        // 4. Register & approve SDTMIG 3.4, pin without mapping -> SubmitProtocolVersionForReview must enforce K-STD-1 (422 K.Standards.MappingMissing).
        var sdtmigDraft = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "standards", "Register SDTMIG 3.4", null, Key(), new { seed_file = "sdtmig-3-4.json" }),
            HttpStatusCode.Created,
            ct);
        string sdtmigRef = StripVersion(sdtmigDraft.GetProperty("ref").GetString()!);

        string approveSdtmigToken = TestSecurityContext.CreateSigToken(
            founderSub,
            $"/api/v1/kernel/standards/{sdtmigRef}/approve",
            KernelValidationEndpoints.ApproveStandardVersionSemanticAction,
            issuedAt: clock.UtcNow);
        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"standards/{sdtmigRef}/approve", "Approve SDTMIG 3.4", approveSdtmigToken, Key(), new { meaning = "Approved" }),
            HttpStatusCode.OK,
            ct);

        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Put, $"studies/{studyRef}/standards", "Declare SDTMIG 3.4 before mapping", null, Key(), new
            {
                standard_version_refs = new[] { sdtmigRef },
            }),
            HttpStatusCode.OK,
            ct);

        using (var unmappedSubmit = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/submit", "Attempt submit before mapping", null, Key(), body: null),
            ct))
        {
            unmappedSubmit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(unmappedSubmit, ct)).Should().Be("K.Standards.MappingMissing");
        }

        // 5. Draft & approve MappingSpecification + approve imported RulePack, pin both on study, submit -> return-to-draft -> submit -> sign-approve v1.
        var conceptNode = itemConceptRef.Contains(':', StringComparison.Ordinal)
            ? NodeRef.Parse(itemConceptRef)
            : NodeRef.Parse($"concept:{itemConceptRef}");
        var mappingContent = ParseJson($$"""
            {
              "title": "HELIOS-301 SDTMIG 3.4 Mapping Specification",
              "controlledTerminology": [],
              "units": [
                { "ucum": "{{itemUnit}}", "submissionValue": "mmHg" }
              ],
              "mappings": [
                {
                  "id": "map-vs-sysbp",
                  "concept": { "id": "{{conceptNode.Id}}", "version": {{conceptNode.Version ?? 1}} },
                  "dataset": "VS",
                  "assign": { "VSTESTCD": "SYSBP" },
                  "result": {
                    "original": "VSORRES",
                    "originalUnit": "VSORRESU",
                    "standardCharacter": "VSSTRESC",
                    "standardNumeric": "VSSTRESN",
                    "standardUnit": "VSSTRESU"
                  },
                  "standardUnit": "{{itemUnit}}"
                }
              ]
            }
            """);

        var mappingDraft = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "library-items", "Draft SDTM mapping spec", null, Key(), new
            {
                kind = "MappingSpecification",
                key = "helios-301-sdtm-map",
                title = "HELIOS-301 SDTMIG 3.4 Mapping Specification",
                content = mappingContent,
                standard_version_refs = new[] { sdtmigRef },
            }),
            HttpStatusCode.Created,
            ct);
        string mappingRef = StripVersion(mappingDraft.GetProperty("ref").GetString()!);

        string approveMapToken = TestSecurityContext.CreateSigToken(
            founderSub,
            KernelValidationEndpoints.ApproveLibraryItemAction(mappingRef),
            KernelValidationEndpoints.ApproveLibraryItemSemanticAction,
            issuedAt: clock.UtcNow);
        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"library-items/{mappingRef}/approve", "Approve SDTM mapping spec", approveMapToken, Key(), new { meaning = "Approved" }),
            HttpStatusCode.OK,
            ct);

        var rulePacksCatalog = await GetJsonAsync(founder, $"/api/v1/kernel/studies/{studyRef}/rule-packs", ct);
        string rulePackRef = StripVersion(rulePacksCatalog.GetProperty("rule_packs")[0].GetProperty("ref").GetString()!);
        string approveRulePackToken = TestSecurityContext.CreateSigToken(
            founderSub,
            KernelValidationEndpoints.ApproveLibraryItemAction(rulePackRef),
            KernelValidationEndpoints.ApproveLibraryItemSemanticAction,
            issuedAt: clock.UtcNow);
        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"library-items/{rulePackRef}/approve", "Approve ODM edit check rule pack", approveRulePackToken, Key(), new { meaning = "Approved" }),
            HttpStatusCode.OK,
            ct);

        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Put, $"studies/{studyRef}/standards", "Pin SDTMIG 3.4, mapping spec, and rule pack", null, Key(), new
            {
                standard_version_refs = new[] { sdtmigRef },
                mapping_spec_refs = new[] { mappingRef },
                rule_pack_refs = new[] { rulePackRef },
            }),
            HttpStatusCode.OK,
            ct);

        var inReviewOnce = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/submit", "Submit v1 for review", null, Key(), body: null),
            HttpStatusCode.OK,
            ct);
        inReviewOnce.GetProperty("status").GetString().Should().Be("InReview");

        var returnedDraft = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/return-to-draft", "Return v1 to draft for verification", null, Key(), body: null),
            HttpStatusCode.OK,
            ct);
        returnedDraft.GetProperty("status").GetString().Should().Be("Draft");

        var inReviewAgain = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/submit", "Re-submit v1 for approval", null, Key(), body: null),
            HttpStatusCode.OK,
            ct);
        inReviewAgain.GetProperty("status").GetString().Should().Be("InReview");

        // Reject missing and wrong-semantic-action step-up tokens on protocol approval.
        using (var noSigApprove = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/approve", "Approve without sig", null, Key(), new { meaning = "Approved" }),
            ct))
        {
            noSigApprove.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        string wrongSemanticToken = TestSecurityContext.CreateSigToken(
            founderSub,
            KernelProtocolEndpoints.ApproveProtocolVersionAction(v1Ref),
            KernelProtocolEndpoints.WithdrawProtocolVersionSemanticAction,
            issuedAt: clock.UtcNow);
        using (var wrongSigApprove = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/approve", "Approve with wrong semantic token", wrongSemanticToken, Key(), new { meaning = "Approved" }),
            ct))
        {
            wrongSigApprove.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        string approveV1Token = TestSecurityContext.CreateSigToken(
            founderSub,
            KernelProtocolEndpoints.ApproveProtocolVersionAction(v1Ref),
            KernelProtocolEndpoints.ApproveProtocolVersionSemanticAction,
            issuedAt: clock.UtcNow);
        var approvedV1 = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/approve", "Approve protocol v1", approveV1Token, Key(), new { meaning = "Approved" }),
            HttpStatusCode.OK,
            ct);
        approvedV1.GetProperty("status").GetString().Should().Be("Approved");

        // Replaying a consumed single-use token must fail with 401.
        using (var replayApprove = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/approve", "Replay approve token", approveV1Token, Key(), new { meaning = "Approved" }),
            ct))
        {
            replayApprove.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 6. Fork amendment v2, revise draft content, submit v2, and sign withdrawal of v2 (leaving v1 Approved).
        var forkedV2 = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v1Ref}/amendments", "Fork v2 safety amendment", null, Key(), new
            {
                intent = "Add standing blood pressure check",
                rationale = "Safety review committee recommendation",
                summary_of_changes = "Clarified Vital Signs instructions",
                impacted_elements = new[] { formSpecId },
                classification = "NonSubstantial",
                transition_policy = "Automatic",
            }),
            HttpStatusCode.Created,
            ct);
        string v2Ref = StripVersion(forkedV2.GetProperty("ref").GetString()!);

        string revisedV2Content = ProtocolDesignDocument.Write(new ProtocolDesign
        {
            Title = "Helios Phase II Hypertension Trial (v2 Safety Amendment)",
            CollectionSpecs =
            [
                new CollectionSpec(
                    formSpecId,
                    "Vital Signs (Amended)",
                    [
                        new CollectionItem(
                            itemSpecId,
                            new ConceptRef(Cadence.Kernel.ConceptId.FromGuid(conceptNode.Id), (int)(conceptNode.Version ?? 1)),
                            Unit: itemUnit,
                            Required: true,
                            Prompt: "Standing Systolic Blood Pressure"),
                    ]),
            ],
        });

        var revisedV2 = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Put, $"protocol-versions/{v2Ref}/draft", "Revise forked draft v2", null, Key(), new
            {
                content = revisedV2Content,
            }),
            HttpStatusCode.OK,
            ct);
        revisedV2.GetProperty("status").GetString().Should().Be("Draft");

        await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v2Ref}/submit", "Submit v2 for review", null, Key(), body: null),
            HttpStatusCode.OK,
            ct);

        using (var noSigWithdraw = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v2Ref}/withdraw", "Withdraw without sig", null, Key(), new { meaning = "Withdrawn" }),
            ct))
        {
            noSigWithdraw.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        string withdrawV2Token = TestSecurityContext.CreateSigToken(
            founderSub,
            KernelProtocolEndpoints.WithdrawProtocolVersionAction(v2Ref),
            KernelProtocolEndpoints.WithdrawProtocolVersionSemanticAction,
            issuedAt: clock.UtcNow);
        var withdrawnV2 = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"protocol-versions/{v2Ref}/withdraw", "Withdraw unneeded v2 amendment", withdrawV2Token, Key(), new { meaning = "Withdrawn" }),
            HttpStatusCode.OK,
            ct);
        withdrawnV2.GetProperty("status").GetString().Should().Be("Withdrawn");

        // 7. Register & link site SITE-301; verify K-TIME-4 blocks activation before effectivity and K-TIME-5 requires approval evidence.
        var registeredSite = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "sites", "Register clinical site SITE-301", null, Key(), new
            {
                site_code = "SITE-301",
                name = "Boston Hypertension Center",
                country = "US",
                institution_code = "INST-BOS-301",
                institution_name = "Boston Medical Research Institute",
            }),
            HttpStatusCode.Created,
            ct);
        string siteRef = StripVersion(registeredSite.GetProperty("ref").GetString()!);

        var linkedStudySite = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"studies/{studyRef}/sites", "Link SITE-301 to HELIOS-301", null, Key(), new
            {
                site_ref = siteRef,
            }),
            HttpStatusCode.Created,
            ct);
        string studySiteRef = StripVersion(linkedStudySite.GetProperty("ref").GetString()!);

        using (var prematureActivate = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, "study-sites/activate-by-code", "Activate before effectivity", null, Key(), new
            {
                study_code = "HELIOS-301",
                site_code = "SITE-301",
            }),
            ct))
        {
            prematureActivate.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(prematureActivate, ct)).Should().Be("K.Effectivity.NoEffectiveVersion");
        }

        var ethicsArtifact = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "artifacts", "Record IRB approval letter", null, Key(), new
            {
                kind = "EthicsApproval",
                title = "Central IRB Approval Letter v1",
                study_ref = studyRef,
                document_text = "Central IRB approval granted for HELIOS-301 Protocol v1 at SITE-301.",
            }),
            HttpStatusCode.Created,
            ct);
        string artifactRef = StripVersion(ethicsArtifact.GetProperty("ref").GetString()!);

        var updatedArtifact = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"artifacts/{artifactRef}/versions", "Append countersigned IRB letter", null, Key(), new
            {
                document_text = "Countersigned Central IRB approval letter v2 for HELIOS-301 Protocol v1 at SITE-301.",
            }),
            HttpStatusCode.Created,
            ct);
        string evidenceVersionRef = StripVersion(updatedArtifact.GetProperty("latest_version").GetProperty("ref").GetString()!);

        var declaredEff = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "effectivities", "Declare v1 effectivity at SITE-301", null, Key(), new
            {
                protocol_version_ref = v1Ref,
                scope_level = "StudySite",
                study_site_ref = studySiteRef,
            }),
            HttpStatusCode.Created,
            ct);
        string effRef = StripVersion(declaredEff.GetProperty("ref").GetString()!);

        // K-TIME-5: putting an effectivity into force without evidence must fail with 422 K.Effectivity.EvidenceRequired.
        clock.Advance(TimeSpan.FromSeconds(2));
        using (var noEvidenceEff = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"effectivities/{effRef}/status", "Attempt effective without evidence", null, Key(), new
            {
                status = "Effective",
                evidence_version_refs = Array.Empty<string>(),
            }),
            ct))
        {
            noEvidenceEff.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(noEvidenceEff, ct)).Should().Be("K.Effectivity.EvidenceRequired");
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        var effectiveState = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"effectivities/{effRef}/status", "Put v1 into force at SITE-301", null, Key(), new
            {
                status = "Effective",
                evidence_version_refs = new[] { evidenceVersionRef },
            }),
            HttpStatusCode.OK,
            ct);
        effectiveState.GetProperty("status").GetString().Should().Be("Effective");

        clock.Advance(TimeSpan.FromSeconds(2));
        var activatedSite = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "study-sites/activate-by-code", "Activate SITE-301", null, Key(), new
            {
                study_code = "HELIOS-301",
                site_code = "SITE-301",
            }),
            HttpStatusCode.OK,
            ct);
        activatedSite.GetProperty("status").GetString().Should().Be("Active");

        // 8. Screen participant SUBJ-001, open eCRF form (201 Created — zero 409 K.Collection.NoGoverningVersion!), and save out-of-range SYSBP = 245.
        clock.Advance(TimeSpan.FromSeconds(1));
        var enrollment = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, "enrollments/screen", "Screen SUBJ-001 at SITE-301", null, Key(), new
            {
                study_code = "HELIOS-301",
                site_code = "SITE-301",
                subject_code = "SUBJ-001",
                screened_at = clock.UtcNow,
            }),
            HttpStatusCode.Created,
            ct);
        Guid enrollmentId = Guid.Parse(enrollment.GetProperty("enrollment_id").GetString()!);

        var hostJsonOptions = host.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        using var openFormReq = BuildRawRequest(
            HttpMethod.Post,
            "/api/v1/execution/forms",
            "Open Vital Signs eCRF",
            null,
            Key(),
            JsonContent.Create(new OpenFormRequest(null, enrollmentId, formSpecId, null, clock.UtcNow), options: hostJsonOptions));
        using var openFormResp = await founder.SendAsync(openFormReq, ct);
        string openFormText = await openFormResp.Content.ReadAsStringAsync(ct);
        openFormResp.StatusCode.Should().Be(HttpStatusCode.Created, openFormText);
        var openedForm = JsonSerializer.Deserialize<FormStateResponse>(openFormText, hostJsonOptions)!;

        var outOfRangeValue = CreateQuantityOrIntegerObservation(245m, itemUnit);
        using var saveValuesReq = BuildRawRequest(
            HttpMethod.Put,
            $"/api/v1/execution/forms/{openedForm.FormId:D}/values",
            "Record elevated systolic BP",
            null,
            Key(),
            JsonContent.Create(
                new SaveFormValuesRequest([new FormValueRequest(itemSpecId, outOfRangeValue, 0, null, clock.UtcNow)]),
                options: hostJsonOptions));
        using var saveValuesResp = await founder.SendAsync(saveValuesReq, ct);
        string saveValuesText = await saveValuesResp.Content.ReadAsStringAsync(ct);
        saveValuesResp.StatusCode.Should().Be(HttpStatusCode.OK, saveValuesText);

        // 9. Trigger on-demand study validation run and triage the resulting finding through all 5 triage commands.
        clock.Advance(TimeSpan.FromSeconds(1));
        var validationRun = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"studies/{studyRef}/validation/runs", "Run on-demand collection validation", null, Key(), new
            {
                stage = "Collection",
            }),
            HttpStatusCode.OK,
            ct);
        validationRun.GetProperty("detected").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        string findingRef = StripVersion(validationRun.GetProperty("findings")[0].GetProperty("ref").GetString()!);

        var investigating = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/investigate", "Begin finding investigation", null, Key(), body: null),
            HttpStatusCode.OK,
            ct);
        investigating.GetProperty("finding").GetProperty("status").GetString().Should().Be("Investigating");

        var classified = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/classify", "Classify as NotAnIssue", null, Key(), new
            {
                classification = "NotAnIssue",
            }),
            HttpStatusCode.OK,
            ct);
        classified.GetProperty("finding").GetProperty("classification").GetString().Should().Be("NotAnIssue");

        // Missing X-Sig-Token on AcceptFinding must fail closed with 401.
        using (var noSigAccept = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/accept", "Accept without sig", null, Key(), new
            {
                rationale = "Verified cuff reading with site PI",
            }),
            ct))
        {
            noSigAccept.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        string acceptFindingToken = TestSecurityContext.CreateSigToken(
            founderSub,
            $"/api/v1/kernel/validation/findings/{findingRef}/accept",
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            issuedAt: clock.UtcNow);
        var accepted = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/accept", "Sign acceptance rationale", acceptFindingToken, Key(), new
            {
                rationale = "Verified transient white-coat reading with site PI",
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);
        accepted.GetProperty("finding").GetProperty("status").GetString().Should().Be("Accepted");

        var reopened = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/reopen", "Reopen for SDRG documentation", null, Key(), new
            {
                note = "Record in Study Data Reviewer's Guide",
            }),
            HttpStatusCode.OK,
            ct);
        reopened.GetProperty("finding").GetProperty("status").GetString().Should().Be("Open");

        // Missing X-Sig-Token on ExplainFinding must fail closed with 401.
        using (var noSigExplain = await founder.SendAsync(
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/explain", "Explain without sig", null, Key(), new
            {
                explanation = "Documented in SDRG section 4.2",
            }),
            ct))
        {
            noSigExplain.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        string explainFindingToken = TestSecurityContext.CreateSigToken(
            founderSub,
            $"/api/v1/kernel/validation/findings/{findingRef}/explain",
            KernelValidationEndpoints.ExplainFindingSemanticAction,
            issuedAt: clock.UtcNow);
        var explained = await SendJsonAsync(
            founder,
            BuildKernelRequest(HttpMethod.Post, $"validation/findings/{findingRef}/explain", "Sign SDRG explanation", explainFindingToken, Key(), new
            {
                explanation = "Documented transient baseline elevation in SDRG section 4.2",
                meaning = "Approved",
            }),
            HttpStatusCode.OK,
            ct);
        explained.GetProperty("finding").GetProperty("status").GetString().Should().Be("Explained");
    }

    private static ObservationValue CreateQuantityOrIntegerObservation(decimal numericValue, string unit)
    {
        var derived = typeof(ObservationValue)
            .GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => a.DerivedType)
            .ToArray();

        var quantityType = derived.FirstOrDefault(t => t.Name.Contains("Quantity", StringComparison.OrdinalIgnoreCase));
        if (quantityType is not null)
        {
            var ctor = quantityType.GetConstructors()[0];
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                if (pType == typeof(decimal))
                {
                    args[i] = numericValue;
                }
                else if (pType == typeof(double))
                {
                    args[i] = (double)numericValue;
                }
                else if (pType == typeof(string))
                {
                    args[i] = unit;
                }
            }

            return (ObservationValue)ctor.Invoke(args);
        }

        throw new InvalidOperationException("Quantity ObservationValue subtype was not found.");
    }

    private static HttpRequestMessage BuildKernelRequest(
        HttpMethod method,
        string relativeKernelPath,
        string? reason,
        string? sigToken,
        string? idempotencyKey,
        object? body) =>
        BuildRawRequest(
            method,
            $"/api/v1/kernel/{relativeKernelPath}",
            reason,
            sigToken,
            idempotencyKey,
            body is null ? null : JsonContent.Create(body, options: WireJson));

    private static HttpRequestMessage BuildRawRequest(
        HttpMethod method,
        string path,
        string? reason,
        string? sigToken,
        string? idempotencyKey,
        HttpContent? content)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (sigToken is not null)
        {
            request.Headers.Add(GatewayHeaders.SigToken, sigToken);
        }

        request.Content = content;
        return request;
    }

    private static async Task<JsonElement> SendJsonAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        using var response = await client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static JsonElement ParseJson(string rawJson) => JsonDocument.Parse(rawJson).RootElement.Clone();

    private static string StripVersion(string nodeRef)
    {
        var parsed = NodeRef.Parse(nodeRef);
        return new NodeRef(parsed.Kind, parsed.Id).ToString();
    }

    private static string Key() => $"idem-{Guid.NewGuid():N}";

    private sealed class RecordingKeycloakOnboardingClient : IKeycloakOnboardingClient
    {
        public ConcurrentQueue<(string SubjectId, string TenantCode, string[] Roles)> Calls { get; } = new();

        public Task AssignTrialWorkspaceAsync(
            string subjectId,
            string tenantCode,
            IReadOnlyList<string> realmRoles,
            CancellationToken cancellationToken)
        {
            Calls.Enqueue((subjectId, tenantCode, [.. realmRoles]));
            return Task.CompletedTask;
        }
    }
}
