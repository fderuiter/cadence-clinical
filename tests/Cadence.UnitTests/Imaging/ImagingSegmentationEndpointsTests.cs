using System;
using System.Diagnostics;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Imaging;

public sealed class ImagingSegmentationEndpointsTests
{
    [Fact(DisplayName = "SegmentRoiAsync_GeneratesContourAndFittedCalipers_AndSavesAnnotationToDatabase")]
    [GxPRequirement("PRD-IMG-002")]
    public async Task SegmentRoiAsync_GeneratesContourAndFittedCalipers_AndSavesAnnotationToDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingSegmentationTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var segmentationService = new ImagingSegmentationService(dbContext);

        var request = new SegmentRoiRequest(
            SeedX: 150.0,
            SeedY: 140.0,
            SliceIndex: 12,
            Plane: "AXIAL",
            LesionId: "TL1",
            OrganSite: "LIVER",
            LesionType: "TARGET",
            TimepointName: "BASELINE",
            StudyId: "STUDY-001",
            SiteId: "SITE-01",
            SubjectId: "SUBJ-ONC-001");

        var stopwatch = Stopwatch.StartNew();
        var result = await segmentationService.SegmentRoiAsync(
            studyUid: "1.2.840.113619.2.55.3.200.1",
            seriesUid: "1.2.840.113619.2.55.3.200.1.1",
            request: request,
            callerId: "dr.central.reader",
            cancellationToken: ct);
        stopwatch.Stop();

        // Latency constraint: < 2.0s
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000);

        result.Should().NotBeNull();
        result.AnnotationId.Should().NotBeNullOrEmpty();
        result.LesionId.Should().Be("TL1");
        result.OrganSite.Should().Be("LIVER");
        result.MeasurementType.Should().Be("AI_AUTO_CONTOUR");
        result.ModelVersion.Should().Be("UNet-ResNet34-2D-v2.1");
        result.IsAiAssisted.Should().BeTrue();
        result.LongestDiameterMm.Should().BeGreaterThan(0);
        result.PerpendicularDiameterMm.Should().BeGreaterThan(0);
        result.ContourPoints.Should().HaveCountGreaterThanOrEqualTo(16);
        result.FittedCalipers.Should().HaveCount(2);
        result.HounsfieldStats.MeanHu.Should().Be(48.5);

        // Verify entity persisted in dbContext
        var storedAnnotation = await dbContext.ImagingAnnotations.FirstOrDefaultAsync(a => a.Id == result.AnnotationId, ct);
        storedAnnotation.Should().NotBeNull();
        storedAnnotation!.MeasurementType.Should().Be("AI_AUTO_CONTOUR");
        storedAnnotation.LongestDiameterMm.Should().Be(result.LongestDiameterMm);
        storedAnnotation.PerpendicularDiameterMm.Should().Be(result.PerpendicularDiameterMm);
        storedAnnotation.ReaderId.Should().Be("dr.central.reader");
        storedAnnotation.Notes.Should().Contain("UNet-ResNet34-2D-v2.1");
    }

    [Fact(DisplayName = "SegmentRoiAsync_RespectsBoundingBoxInput_WhenProvided")]
    [GxPRequirement("PRD-IMG-002")]
    public async Task SegmentRoiAsync_RespectsBoundingBoxInput_WhenProvided()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingSegmentationBBoxTest_{Guid.NewGuid():N}")
            .Options;

        await using var dbContext = new ImagingDbContext(options);
        var segmentationService = new ImagingSegmentationService(dbContext);

        var request = new SegmentRoiRequest(
            SeedX: 100.0,
            SeedY: 100.0,
            SliceIndex: 10,
            BoundingBox: new BoundingBoxDto(XMin: 80.0, YMin: 80.0, XMax: 120.0, YMax: 120.0),
            LesionId: "TL2",
            OrganSite: "LUNG_RIGHT",
            TimepointName: "CYCLE_2_DAY_1");

        var result = await segmentationService.SegmentRoiAsync(
            studyUid: "1.2.840.113619.2.55.3.200.2",
            seriesUid: "1.2.840.113619.2.55.3.200.2.1",
            request: request,
            callerId: "dr.central.reader2",
            cancellationToken: ct);

        result.Should().NotBeNull();
        result.LesionId.Should().Be("TL2");
        result.OrganSite.Should().Be("LUNG_RIGHT");
        result.BoundingBox.XMin.Should().BeLessThan(100.0);
        result.BoundingBox.XMax.Should().BeGreaterThan(100.0);
    }
}
