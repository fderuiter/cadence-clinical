using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Services;

/// <summary>A reader's box around the lesion, in pixels.</summary>
public record BoundingBoxDto(
    double XMin,
    double YMin,
    double XMax,
    double YMax);

/// <summary>A request to segment the lesion at a seed point on a stored series.</summary>
public record SegmentRoiRequest(
    double SeedX,
    double SeedY,
    int? SliceIndex = null,
    string? Plane = "AXIAL",
    BoundingBoxDto? BoundingBox = null,
    string? LesionId = null,
    string? OrganSite = null,
    string? LesionType = null,
    string? TimepointName = null);

/// <summary>Why a segmentation request was not carried out.</summary>
public enum SegmentationStatus
{
    /// <summary>
    /// The request is in scope, but no segmentation model reads the image, so nothing is measured or written (#5426).
    /// A measurement no model computed must never become study evidence.
    /// </summary>
    ModelUnavailable,

    /// <summary>No stored scan has that study instance UID, or the series is not one of its series.</summary>
    NotFound,

    /// <summary>The stored scan belongs to a clinical study outside the caller's signed study scope.</summary>
    OutsideCallerStudies,
}

/// <summary>The outcome of a segmentation request.</summary>
public sealed record SegmentationOutcome(SegmentationStatus Status);

/// <summary>Lesion segmentation on stored series.</summary>
public interface IImagingSegmentationService
{
    /// <summary>
    /// Segments a lesion on a stored series. The clinical study comes from the stored scan, never from the request, and
    /// <paramref name="canAccessStudy"/> must admit it (ADR-2516). No segmentation model is available, so an in-scope
    /// request is refused with <see cref="SegmentationStatus.ModelUnavailable"/> and nothing is written (#5426); a model
    /// will run through the Intelligence module and land as a Proposal a reader approves (ADR-2630).
    /// </summary>
    Task<SegmentationOutcome> SegmentRoiAsync(
        string studyUid,
        string seriesUid,
        SegmentRoiRequest request,
        string callerId,
        Func<string, bool> canAccessStudy,
        CancellationToken cancellationToken = default);
}

/// <summary>Checks segmentation requests against the stored scan and the caller's scope; measures nothing (#5426).</summary>
public class ImagingSegmentationService : IImagingSegmentationService
{
    private readonly ImagingDbContext _dbContext;

    public ImagingSegmentationService(ImagingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SegmentationOutcome> SegmentRoiAsync(
        string studyUid,
        string seriesUid,
        SegmentRoiRequest request,
        string callerId,
        Func<string, bool> canAccessStudy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(canAccessStudy);

        var study = await _dbContext.ImagingStudyEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid || s.Id == studyUid, cancellationToken)
            .ConfigureAwait(false);
        if (study is null || string.IsNullOrWhiteSpace(study.StudyId))
        {
            return new SegmentationOutcome(SegmentationStatus.NotFound);
        }

        if (!canAccessStudy(study.StudyId))
        {
            return new SegmentationOutcome(SegmentationStatus.OutsideCallerStudies);
        }

        var series = await _dbContext.ImagingSeriesEnvelopes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.StudyEnvelopeId == study.Id && (s.SeriesInstanceUid == seriesUid || s.Id == seriesUid),
                cancellationToken)
            .ConfigureAwait(false);
        if (series is null)
        {
            return new SegmentationOutcome(SegmentationStatus.NotFound);
        }

        // There is no model that reads the pixels. The earlier code drew a fixed shape around the seed and stored its
        // "diameters" and constant Hounsfield statistics as an AI-assisted RECIST measurement under the reader's name
        // (#5422). Refuse instead: readers measure with the calipers until a qualified model exists.
        return new SegmentationOutcome(SegmentationStatus.ModelUnavailable);
    }
}
