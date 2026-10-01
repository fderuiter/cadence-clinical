using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Endpoints;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public class EtmfIntelligenceEndpointsTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("protocol_v1_draft.pdf", 1, "01.01", "01.01.01", "Clinical Trial Protocol", "HIGH")]
    [InlineData("FDA_Form_1572_Signed.pdf", 5, "05.02", "05.02.01", "FDA Form 1572", "HIGH")]
    [InlineData("Financial_Disclosure_Statement.pdf", 5, "05.02", "05.02.02", "Financial Disclosure", "HIGH")]
    [InlineData("Investigator_CV_2026.pdf", 5, "05.02", "05.02.03", "Investigator CV", "HIGH")]
    [InlineData("Delegation_of_Authority_Log.pdf", 5, "05.02", "05.02.04", "Delegation of Authority Log", "HIGH")]
    [InlineData("Informed_Consent_ICF_v2.pdf", 5, "05.02", "05.02.05", "Informed Consent Form", "HIGH")]
    [InlineData("Investigator_Brochure_v3.pdf", 2, "02.01", "02.01.01", "Investigator's Brochure", "HIGH")]
    [InlineData("IRB_Approval_Letter.pdf", 4, "04.02", "04.02.01", "IRB/IEC Approval Notification", "HIGH")]
    [InlineData("SAE_Safety_Report_001.pdf", 7, "07.01", "07.01.01", "Serious Adverse Event Report", "HIGH")]
    [InlineData("Central_Lab_Certificate.pdf", 8, "08.01", "08.01.01", "Central Laboratory Certificate", "HIGH")]
    [InlineData("Statistical_Analysis_Plan_v1.pdf", 11, "11.01", "11.01.01", "Statistical Analysis Plan", "HIGH")]
    [InlineData("Unrecognized_Document_Notes.txt", 1, "01.01", "01.01.01", "General Clinical Document", "LOW")]
    public async Task ClassifyDocument_ReturnsPredictedTaxonomy(
        string filename,
        int expectedZone,
        string expectedSection,
        string expectedArtifactCode,
        string expectedArtifactType,
        string expectedTier)
    {
        var request = new ClassifyRequest(filename);
        var result = await EtmfIntelligenceEndpoints.ClassifyDocument(request, _clock);

        var okResult = (Ok<DocumentIntelligenceReport>)result;
        var report = okResult.Value!;

        report.PrimaryClassification.Zone.Should().Be(expectedZone);
        report.PrimaryClassification.Section.Should().Be(expectedSection);
        report.PrimaryClassification.ArtifactCode.Should().Be(expectedArtifactCode);
        report.PrimaryClassification.ArtifactType.Should().Be(expectedArtifactType);
        report.ConfidenceTier.Should().Be(expectedTier);
    }

    [Fact]
    public async Task LocalHeuristicFallback_ExecutesUnder100Milliseconds()
    {
        var request = new ClassifyRequest("Site_Regulatory_Batch_Document_001.pdf");
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            await EtmfIntelligenceEndpoints.ClassifyDocument(request, _clock);
        }

        sw.Stop();
        // 100 executions should complete in well under 100ms
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }
}
