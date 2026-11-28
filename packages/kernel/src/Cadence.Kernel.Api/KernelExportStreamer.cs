using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>
/// Streamed format coordinator delegating to dedicated CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON v1.1.0,
/// HTML, PDF 1.7, and statistical bundle ZIP writers. Preserves <see cref="decimal"/> precision with
/// <see cref="CultureInfo.InvariantCulture"/> and uses caller-provided <c>IClock</c> timestamps exclusively.
/// </summary>
internal static class KernelExportStreamer
{
    public const string ProducerName = KernelExportFormatting.ProducerName;

    public static Task WriteStudyCsvAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken) =>
        KernelCsvExportWriter.WriteStudyCsvAsync(destination, export, cancellationToken);

    public static Task WriteStudyDatasetJsonAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken) =>
        KernelDatasetJsonExportWriter.WriteStudyDatasetJsonAsync(destination, export, cancellationToken);

    public static Task WriteStudyOdmXmlAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken) =>
        KernelStudyOdmXmlExportWriter.WriteStudyOdmXmlAsync(destination, export, cancellationToken);

    public static KernelCasebookResponse BuildCasebookModel(
        EnrollmentCasebookSnapshot snapshot,
        DateTimeOffset generatedAt,
        string requestedBy,
        MaskReason? enrollmentMask,
        bool mayUnblind) =>
        KernelCasebookBuilder.BuildCasebookModel(snapshot, generatedAt, requestedBy, enrollmentMask, mayUnblind);

    public static Task WriteCasebookJsonAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken) =>
        KernelCasebookBuilder.WriteCasebookJsonAsync(destination, casebook, cancellationToken);

    public static Task WriteCasebookOdmXmlAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken) =>
        KernelCasebookOdmXmlExportWriter.WriteCasebookOdmXmlAsync(destination, casebook, cancellationToken);

    public static Task WriteCasebookHtmlAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken) =>
        KernelCasebookHtmlExportWriter.WriteCasebookHtmlAsync(destination, casebook, cancellationToken);

    public static Task WriteCasebookPdfAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken) =>
        KernelCasebookPdfExportWriter.WriteCasebookPdfAsync(destination, casebook, cancellationToken);

    public static Task<KernelHiddenContentResponse> WriteStudyBundleZipAsync(
        Stream destination,
        ProjectedStudyExport export,
        StudyExportFormat format,
        string requestedBy,
        IReadOnlyList<KernelCasebookResponse> archiveCasebooks,
        CancellationToken cancellationToken) =>
        KernelStudyBundleZipWriter.WriteStudyBundleZipAsync(
            destination,
            export,
            format,
            requestedBy,
            archiveCasebooks,
            cancellationToken);
}
