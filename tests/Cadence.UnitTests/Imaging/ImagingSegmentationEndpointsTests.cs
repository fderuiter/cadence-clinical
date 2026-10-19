using System;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Imaging;

/// <summary>
/// Segmentation writes an annotation on a stored series. The clinical study, site and subject come from the stored scan,
/// and the caller's signed study scope must admit that study (ADR-2516, #5441).
/// </summary>
public sealed class ImagingSegmentationEndpointsTests
{
    private const string StudyUid = "1.2.840.113619.2.55.3.200.1";
    private const string SeriesUid = "1.2.840.113619.2.55.3.200.1.1";

    private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    private static async Task<ImagingDbContext> CreateContextWithStoredScanAsync()
    {
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingSegmentationTest_{Guid.NewGuid():N}")
            .Options;
        var dbContext = new ImagingDbContext(options);
        var study = new ImagingStudyEnvelope
        {
            StudyInstanceUid = StudyUid,
            StudyId = "STUDY-001",
            SiteId = "SITE-01",
            SubjectId = "SUBJ-ONC-001",
            CreatedAt = Clock.UtcNow,
            CreatedBy = "site.uploader",
            UpdatedAt = Clock.UtcNow,
        };
        dbContext.ImagingStudyEnvelopes.Add(study);
        dbContext.ImagingSeriesEnvelopes.Add(new ImagingSeriesEnvelope
        {
            StudyEnvelopeId = study.Id,
            SeriesInstanceUid = SeriesUid,
            Modality = "CT",
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return dbContext;
    }

    [Fact]
    public async Task SegmentRoiAsync_WritesTheAnnotationUnderTheStoredScansStudySiteAndSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateContextWithStoredScanAsync();
        var segmentationService = new ImagingSegmentationService(dbContext, Clock);

        var outcome = await segmentationService.SegmentRoiAsync(
            StudyUid,
            SeriesUid,
            new SegmentRoiRequest(SeedX: 150.0, SeedY: 140.0, SliceIndex: 12, LesionId: "TL1", OrganSite: "LIVER"),
            callerId: "dr.central.reader",
            canAccessStudy: studyId => studyId == "STUDY-001",
            cancellationToken: ct);

        outcome.Status.Should().Be(SegmentationStatus.Segmented);
        var result = outcome.Result!;
        result.LesionId.Should().Be("TL1");
        result.FittedCalipers.Should().HaveCount(2);
        result.Timestamp.Should().Be(Clock.UtcNow);

        var stored = await dbContext.ImagingAnnotations.AsNoTracking().SingleAsync(a => a.Id == result.AnnotationId, ct);
        stored.StudyId.Should().Be("STUDY-001");
        stored.SiteId.Should().Be("SITE-01");
        stored.SubjectId.Should().Be("SUBJ-ONC-001");
        stored.ReaderId.Should().Be("dr.central.reader");
        stored.LongestDiameterMm.Should().Be(result.LongestDiameterMm);
    }

    [Fact]
    public async Task SegmentRoiAsync_ForAStudyOutsideTheCallersScope_WritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateContextWithStoredScanAsync();
        var segmentationService = new ImagingSegmentationService(dbContext, Clock);

        var outcome = await segmentationService.SegmentRoiAsync(
            StudyUid,
            SeriesUid,
            new SegmentRoiRequest(SeedX: 150.0, SeedY: 140.0),
            callerId: "dr.central.reader",
            canAccessStudy: _ => false,
            cancellationToken: ct);

        outcome.Status.Should().Be(SegmentationStatus.OutsideCallerStudies);
        (await dbContext.ImagingAnnotations.CountAsync(ct)).Should().Be(0);
    }

    [Theory]
    [InlineData("1.2.840.999", SeriesUid)]
    [InlineData(StudyUid, "1.2.840.999")]
    public async Task SegmentRoiAsync_ForAnUnknownStudyOrSeries_WritesNothing(string studyUid, string seriesUid)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateContextWithStoredScanAsync();
        var segmentationService = new ImagingSegmentationService(dbContext, Clock);

        var outcome = await segmentationService.SegmentRoiAsync(
            studyUid,
            seriesUid,
            new SegmentRoiRequest(SeedX: 150.0, SeedY: 140.0),
            callerId: "dr.central.reader",
            canAccessStudy: _ => true,
            cancellationToken: ct);

        outcome.Status.Should().Be(SegmentationStatus.NotFound);
        (await dbContext.ImagingAnnotations.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task SegmentRoiAsync_RespectsBoundingBoxInput_WhenProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dbContext = await CreateContextWithStoredScanAsync();
        var segmentationService = new ImagingSegmentationService(dbContext, Clock);

        var outcome = await segmentationService.SegmentRoiAsync(
            StudyUid,
            SeriesUid,
            new SegmentRoiRequest(
                SeedX: 100.0,
                SeedY: 100.0,
                SliceIndex: 10,
                BoundingBox: new BoundingBoxDto(XMin: 80.0, YMin: 80.0, XMax: 120.0, YMax: 120.0),
                LesionId: "TL2",
                OrganSite: "LUNG_RIGHT",
                TimepointName: "CYCLE_2_DAY_1"),
            callerId: "dr.central.reader2",
            canAccessStudy: _ => true,
            cancellationToken: ct);

        var result = outcome.Result!;
        result.LesionId.Should().Be("TL2");
        result.OrganSite.Should().Be("LUNG_RIGHT");
        result.BoundingBox.XMin.Should().BeLessThan(100.0);
        result.BoundingBox.XMax.Should().BeGreaterThan(100.0);
    }
}
