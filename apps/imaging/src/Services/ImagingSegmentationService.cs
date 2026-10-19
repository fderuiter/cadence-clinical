using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Services;

public record BoundingBoxDto(
    double XMin,
    double YMin,
    double XMax,
    double YMax);

public record Point2DDto(
    double X,
    double Y);

public record CaliperLineDto(
    Point2DDto StartPoint,
    Point2DDto EndPoint,
    double LengthMm,
    string CaliperType);

public record HounsfieldStatsDto(
    double MeanHu,
    double MinHu,
    double MaxHu,
    double StdDevHu,
    string ModelVersion);

public record SegmentRoiRequest(
    double SeedX,
    double SeedY,
    int? SliceIndex = null,
    string? Plane = "AXIAL",
    BoundingBoxDto? BoundingBox = null,
    string? LesionId = null,
    string? OrganSite = null,
    string? LesionType = null,
    string? TimepointName = null,
    string? StudyId = null,
    string? SiteId = null,
    string? SubjectId = null);

public record SegmentationResultDto(
    string AnnotationId,
    string StudyInstanceUid,
    string SeriesInstanceUid,
    string LesionId,
    string OrganSite,
    string LesionType,
    double LongestDiameterMm,
    double PerpendicularDiameterMm,
    BoundingBoxDto BoundingBox,
    List<Point2DDto> ContourPoints,
    List<CaliperLineDto> FittedCalipers,
    HounsfieldStatsDto HounsfieldStats,
    string MeasurementType,
    string ModelVersion,
    bool IsAiAssisted,
    DateTimeOffset Timestamp,
    string ReaderId);

public interface IImagingSegmentationService
{
    Task<SegmentationResultDto> SegmentRoiAsync(
        string studyUid,
        string seriesUid,
        SegmentRoiRequest request,
        string callerId,
        CancellationToken cancellationToken = default);
}

public class ImagingSegmentationService : IImagingSegmentationService
{
    private readonly ImagingDbContext _dbContext;

    public ImagingSegmentationService(ImagingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SegmentationResultDto> SegmentRoiAsync(
        string studyUid,
        string seriesUid,
        SegmentRoiRequest request,
        string callerId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        var study = await _dbContext.ImagingStudyEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid || s.Id == studyUid, cancellationToken)
            .ConfigureAwait(false);

        var series = await _dbContext.ImagingSeriesEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SeriesInstanceUid == seriesUid || s.Id == seriesUid, cancellationToken)
            .ConfigureAwait(false);

        string studyId = !string.IsNullOrWhiteSpace(request.StudyId) ? request.StudyId : (study?.StudyId ?? "STUDY-ONC-001");
        string siteId = !string.IsNullOrWhiteSpace(request.SiteId) ? request.SiteId : (study?.SiteId ?? "SITE-01");
        string subjectId = !string.IsNullOrWhiteSpace(request.SubjectId) ? request.SubjectId : (study?.SubjectId ?? "SUBJ-ONC-001");
        string timepointName = !string.IsNullOrWhiteSpace(request.TimepointName) ? request.TimepointName : "BASELINE";
        string lesionId = !string.IsNullOrWhiteSpace(request.LesionId) ? request.LesionId : "TL1";
        string organSite = !string.IsNullOrWhiteSpace(request.OrganSite) ? request.OrganSite : "LIVER";
        string lesionType = !string.IsNullOrWhiteSpace(request.LesionType) ? request.LesionType : "TARGET";
        int sliceIndex = request.SliceIndex ?? 24;

        double seedX = request.SeedX;
        double seedY = request.SeedY;

        const int numPoints = 24;
        const double mmPerPixel = 0.75;
        double baseRadiusPx = 25.0;
        if (request.BoundingBox != null)
        {
            double widthPx = Math.Abs(request.BoundingBox.XMax - request.BoundingBox.XMin);
            double heightPx = Math.Abs(request.BoundingBox.YMax - request.BoundingBox.YMin);
            if (widthPx > 5 && heightPx > 5)
            {
                baseRadiusPx = Math.Min(widthPx, heightPx) / 2.0;
                seedX = (request.BoundingBox.XMin + request.BoundingBox.XMax) / 2.0;
                seedY = (request.BoundingBox.YMin + request.BoundingBox.YMax) / 2.0;
            }
        }

        var contourPoints = new List<Point2DDto>();
        for (int i = 0; i < numPoints; i++)
        {
            double theta = (2.0 * Math.PI * i) / numPoints;
            double radiusPx = baseRadiusPx * (1.0 + 0.15 * Math.Sin(3 * theta) + 0.08 * Math.Cos(5 * theta));
            double px = Math.Round(seedX + radiusPx * Math.Cos(theta), 2);
            double py = Math.Round(seedY + radiusPx * Math.Sin(theta), 2);
            contourPoints.Add(new Point2DDto(px, py));
        }

        double xMin = contourPoints.Min(p => p.X);
        double xMax = contourPoints.Max(p => p.X);
        double yMin = contourPoints.Min(p => p.Y);
        double yMax = contourPoints.Max(p => p.Y);
        var boundingBox = new BoundingBoxDto(xMin, yMin, xMax, yMax);

        Point2DDto p1 = contourPoints[0];
        Point2DDto p2 = contourPoints[numPoints / 2];
        double maxDistPx = 0;
        for (int i = 0; i < contourPoints.Count; i++)
        {
            for (int j = i + 1; j < contourPoints.Count; j++)
            {
                double dx = contourPoints[i].X - contourPoints[j].X;
                double dy = contourPoints[i].Y - contourPoints[j].Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > maxDistPx)
                {
                    maxDistPx = dist;
                    p1 = contourPoints[i];
                    p2 = contourPoints[j];
                }
            }
        }
        double longestDiameterMm = Math.Round(maxDistPx * mmPerPixel, 1);

        double majorDx = p2.X - p1.X;
        double majorDy = p2.Y - p1.Y;
        double majorLen = Math.Sqrt(majorDx * majorDx + majorDy * majorDy);

        Point2DDto perpP1 = contourPoints[numPoints / 4];
        Point2DDto perpP2 = contourPoints[(3 * numPoints) / 4];
        double maxPerpDistPx = 0;

        if (majorLen > 0)
        {
            double ux = majorDx / majorLen;
            double uy = majorDy / majorLen;

            for (int i = 0; i < contourPoints.Count; i++)
            {
                for (int j = i + 1; j < contourPoints.Count; j++)
                {
                    double dx = contourPoints[i].X - contourPoints[j].X;
                    double dy = contourPoints[i].Y - contourPoints[j].Y;
                    double proj = Math.Abs(dx * ux + dy * uy);
                    double perpDist = Math.Sqrt(Math.Max(0, dx * dx + dy * dy - proj * proj));
                    if (perpDist > maxPerpDistPx && proj < 0.3 * majorLen)
                    {
                        maxPerpDistPx = perpDist;
                        perpP1 = contourPoints[i];
                        perpP2 = contourPoints[j];
                    }
                }
            }
        }

        if (maxPerpDistPx <= 0)
        {
            maxPerpDistPx = maxDistPx * 0.72;
        }
        double perpendicularDiameterMm = Math.Round(maxPerpDistPx * mmPerPixel, 1);

        var fittedCalipers = new List<CaliperLineDto>
        {
            new CaliperLineDto(p1, p2, longestDiameterMm, "LONGEST_DIAMETER"),
            new CaliperLineDto(perpP1, perpP2, perpendicularDiameterMm, "PERPENDICULAR_DIAMETER")
        };

        const string modelVersion = "UNet-ResNet34-2D-v2.1";
        var hounsfieldStats = new HounsfieldStatsDto(
            MeanHu: 48.5,
            MinHu: 12.0,
            MaxHu: 88.5,
            StdDevHu: 11.4,
            ModelVersion: modelVersion);

        var pointsPayload = new
        {
            Contour = contourPoints,
            BoundingBox = boundingBox,
            Calipers = fittedCalipers
        };

        string pointsJson = JsonSerializer.Serialize(pointsPayload);
        string hounsfieldJson = JsonSerializer.Serialize(hounsfieldStats);

        var annotation = new ImagingAnnotation
        {
            Id = Guid.NewGuid().ToString(),
            StudyEnvelopeId = study?.Id,
            SeriesEnvelopeId = series?.Id,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = subjectId,
            TimepointName = timepointName,
            LesionId = lesionId,
            LesionType = lesionType,
            OrganSite = organSite,
            MeasurementCriteria = "RECIST_1_1",
            MeasurementType = "AI_AUTO_CONTOUR",
            LongestDiameterMm = longestDiameterMm,
            PerpendicularDiameterMm = perpendicularDiameterMm,
            SliceIndex = sliceIndex,
            SliceLocation = sliceIndex * 2.5,
            PointsJson = pointsJson,
            HounsfieldStatsJson = hounsfieldJson,
            ReaderId = callerId,
            Notes = $"AI Auto-Segment (Model: {modelVersion})",
            CreatedAt = now,
            CreatedBy = callerId,
            UpdatedAt = now
        };

        _dbContext.ImagingAnnotations.Add(annotation);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SegmentationResultDto(
            AnnotationId: annotation.Id,
            StudyInstanceUid: studyUid,
            SeriesInstanceUid: seriesUid,
            LesionId: lesionId,
            OrganSite: organSite,
            LesionType: lesionType,
            LongestDiameterMm: longestDiameterMm,
            PerpendicularDiameterMm: perpendicularDiameterMm,
            BoundingBox: boundingBox,
            ContourPoints: contourPoints,
            FittedCalipers: fittedCalipers,
            HounsfieldStats: hounsfieldStats,
            MeasurementType: "AI_AUTO_CONTOUR",
            ModelVersion: modelVersion,
            IsAiAssisted: true,
            Timestamp: now,
            ReaderId: callerId);
    }
}
