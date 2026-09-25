using System.Collections.Generic;

using Cadence.Config;
using Cadence.CoreModels;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class ConfigAndCoreModelsUnitTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Config_CommonOptions_HasDefaultValuesMatchingPython()
    {
        var options = new CommonOptions();

        options.AppEnv.Should().Be("development");
        options.BrandName.Should().Be("Cadence Clinical");
        options.BrandDomain.Should().Be("ccrsoft.com");
        options.GatewaySecret.Should().Be("internal-gateway-secret-12345");
        options.SigningSecret.Should().Be("designer-amendment-secure-key-12345");
        options.AuditLogSecretKey.Should().Be("gxp-audit-secret-key-cadence-2026");
        options.PseudonymizationSalt.Should().Be("secure-clinical-salt-98765");
        options.SafetySalt.Should().Be("internal-safety-salt-12345");
        options.InboundEmailHmacSecret.Should().Be("dev-default-secret-inbound-email-hmac");
        options.GatewayUrl.Should().Be("http://localhost:8000");
        options.AiGatewayUrl.Should().Be("http://localhost:8000");
        options.DesignerUrl.Should().Be("http://localhost:8001");
        options.ExecutionUrl.Should().Be("http://localhost:8002");
        options.EtmfUrl.Should().Be("http://localhost:8003");
        options.EconsentUrl.Should().Be("http://localhost:8004");
        options.CtmsUrl.Should().Be("http://localhost:8005");
        options.NotificationsUrl.Should().Be("http://localhost:8006");
        options.OrgUrl.Should().Be("http://localhost:8010");
        options.FileshareUrl.Should().Be("http://localhost:8011");
        options.KeycloakUrl.Should().BeNull();
        options.KeycloakRealm.Should().Be("cadence");
        options.KeycloakClientId.Should().Be("cadence-clinical");
        options.RedisHost.Should().BeNull();
        options.RedisPort.Should().Be(6379);
        options.RedisPassword.Should().BeNull();
        options.RedisUrl.Should().BeNull();
        options.Neo4jUri.Should().Be("bolt://localhost:7687");
        options.Neo4jUser.Should().Be("neo4j");
        options.Neo4jPassword.Should().Be("cadence_password");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Config_All23ServiceOptions_InstantiateWithDefaults()
    {
        new NotificationsOptions().SmtpPort.Should().Be(1025);

        var econsent = new EConsentOptions();
        econsent.CacheTtl.Should().Be(300);
        econsent.EffectiveCacheTtl.Should().Be(300);
        econsent.EconsentCacheTtl = 600;
        econsent.EffectiveCacheTtl.Should().Be(600);

        new CtmsOptions().CtmsDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new FileshareOptions().ClamavPort.Should().Be(3310);
        new InteropOptions().InteropDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new EisfOptions().EisfDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new EtmfOptions().EtmfDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new OrgOptions().OrgDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new QualityOptions().QualityDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new SafetyOptions().SafetyDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");

        var tickets = new TicketsOptions();
        tickets.TicketsEscalationIntervalSeconds.Should().Be(3600.0);
        tickets.TicketsEscalationPollIntervalSeconds.Should().Be(1.0);

        new PaymentsOptions().PaymentsDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");

        var imaging = new ImagingOptions();
        imaging.ImagingStorageBucket.Should().Be("imaging-dicom");

        var aiGateway = new AiGatewayOptions();
        aiGateway.AiTier1Model.Should().Be("ollama/llama3");
        aiGateway.AiTier2Model.Should().Be("gpt-4o-mini");
        aiGateway.AiTier3Model.Should().Be("gpt-4o");

        new StorageOptions().StorageSseAlgorithm.Should().Be("AES256");

        var knowledge = new KnowledgeOptions();
        knowledge.KnowledgeServiceUrl.Should().Be("http://127.0.0.1:8006");

        var execution = new ExecutionOptions();
        execution.OutboxBatchSize.Should().Be(20);
        execution.OutboxMaxConcurrency.Should().Be(10);
        execution.RtsmMasterSecret.Should().Be("dev-rtsm-master-secret-placeholder");

        new DesignerOptions().DesignerDatabaseUrl.Should().Be("sqlite+aiosqlite:///:memory:");
        new GatewayOptions().GatewayInternalSecret.Should().Be("dev-gateway-internal-secret-placeholder");

        var security = new SecurityOptions();
        security.JwtAlgorithm.Should().Be("RS256");

        new DeidOptions().DeidSalt.Should().Be("secure-clinical-salt-98765");
        new TelemetryOptions().LogLevel.Should().Be("INFO");
        new ComplianceOptions().GxpSignerId.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Config_Options_BindsFromConfigurationDictionary()
    {
        var dict = new Dictionary<string, string?>
        {
            ["AppEnv"] = "staging",
            ["ExecutionDatabaseUrl"] = "postgresql://user:pass@localhost:5432/cadence_edc",
            ["OutboxMaxConcurrency"] = "25",
            ["RtsmMasterSecret"] = "secure-rtsm-secret-999"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        var services = new ServiceCollection();
        services.Configure<ExecutionOptions>(config);
        var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<ExecutionOptions>>().Value;

        options.AppEnv.Should().Be("staging");
        options.ExecutionDatabaseUrl.Should().Be("postgresql://user:pass@localhost:5432/cadence_edc");
        options.OutboxMaxConcurrency.Should().Be(25);
        options.RtsmMasterSecret.Should().Be("secure-rtsm-secret-999");
    }

    [Fact]
    [GxPRequirement("PRD-TCK-005")]
    public void ProtocolRAG_FormatCitationMarker_ProducesCanonicalFormat()
    {
        var marker1 = ProtocolRAGEvaluator.FormatCitationMarker("2.1", "7.3", 42);
        marker1.Should().Be("[Protocol v2.1, Section 7.3, Page 42]");

        var marker2 = ProtocolRAGEvaluator.FormatCitationMarker("v3.0", null, 15);
        marker2.Should().Be("[Protocol v3.0, Page 15]");
    }

    [Fact]
    [GxPRequirement("PRD-TCK-005")]
    public void ProtocolRAG_FormatGroundedRagPrompt_FormatsPromptWithExcerpts()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            SectionNumber: "7.3",
            SectionTitle: "SAE Reporting",
            PageNumber: 42,
            ChunkText: "Serious adverse events must be reported within 24 hours of awareness."
        );

        var prompt = ProtocolRAGEvaluator.FormatGroundedRagPrompt("What is the SAE reporting window?", new[] { chunk });

        prompt.Should().Contain("--- PROTOCOL EXCERPTS ---");
        prompt.Should().Contain("[EXCERPT 1: Protocol v2.1, Section 7.3 (SAE Reporting), Page 42]");
        prompt.Should().Contain("Serious adverse events must be reported within 24 hours of awareness.");
        prompt.Should().Contain("CLINICAL INQUIRY:\nWhat is the SAE reporting window?");
    }

    [Fact]
    [GxPRequirement("PRD-TCK-005")]
    [GxPRequirement("PRD-SYS-051")]
    public void ProtocolRAG_EvaluateFaithfulness_ValidatesGroundedAnswer()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            SectionNumber: "7.3",
            SectionTitle: "SAE Reporting",
            PageNumber: 42,
            ChunkText: "Serious adverse events must be reported within 24 hours of site awareness.",
            IsApproved: true
        );

        var query = "How soon must serious adverse events be reported?";
        var answer = "Serious adverse events must be reported within 24 hours of site awareness [Protocol v2.1, Section 7.3, Page 42].";

        var (score, citations, isGrounded) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        score.Should().BeGreaterThanOrEqualTo(0.85);
        isGrounded.Should().BeTrue();
        citations.Should().ContainSingle();
        citations[0].IsVerified.Should().BeTrue();
        citations[0].IsApproved.Should().BeTrue();
        citations[0].CitationMarker.Should().Be("[Protocol v2.1, Section 7.3, Page 42]");
    }

    [Fact]
    [GxPRequirement("PRD-TCK-005")]
    [GxPRequirement("PRD-SYS-051")]
    public void ProtocolRAG_EvaluateFaithfulness_FailsClosedForInsufficientInformation()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            PageNumber: 10,
            ChunkText: "Exclusion criteria: Active hepatic disease."
        );

        var query = "What is the pediatric dosage?";
        var answer = "The provided study protocol excerpts do not contain sufficient information to answer this inquiry.";

        var (score, citations, isGrounded) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        score.Should().Be(0.0);
        isGrounded.Should().BeFalse();
        citations.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-TCK-005")]
    [GxPRequirement("PRD-SYS-051")]
    public void ProtocolRAG_EvaluateFaithfulness_DetectsHallucinatedCitations()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            PageNumber: 10,
            ChunkText: "Adult dosage is 50mg daily."
        );

        var query = "What is the adult dosage?";
        var answer = "Adult dosage is 50mg daily [Protocol v2.1, Section 5.1, Page 99].";

        var (score, citations, isGrounded) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        score.Should().BeLessThan(0.85);
        isGrounded.Should().BeFalse();
        citations.Should().ContainSingle();
        citations[0].IsVerified.Should().BeFalse();
    }
}
