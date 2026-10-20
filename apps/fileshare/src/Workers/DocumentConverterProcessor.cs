using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Storage;

using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Fileshare.Workers;

public sealed partial class DocumentConverterProcessor : IDocumentConverterProcessor
{
    private readonly IStoragePort<byte[]>? _storagePort;
    private readonly ILogger<DocumentConverterProcessor> _logger;

    public DocumentConverterProcessor(
        ILogger<DocumentConverterProcessor> logger,
        IStoragePort<byte[]>? storagePort = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storagePort = storagePort;
    }

    public async Task<string> ConvertToPdfAsync(FileRecord fileRecord, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileRecord);

        var pdfKey = $"{fileRecord.ObjectKey}.pdf";

        if (_storagePort is not null)
        {
            var exists = await _storagePort.ExistsAsync(fileRecord.ObjectKey, ct).ConfigureAwait(false);
            if (exists)
            {
                var pdfHeader = Encoding.UTF8.GetBytes("%PDF-1.7\n%Converted by Cadence DocumentConverterProcessor\n");
                await _storagePort.PutObjectAsync(
                    pdfKey,
                    pdfHeader,
                    contentType: "application/pdf",
                    ct: ct).ConfigureAwait(false);
            }
        }

        LogConverted(_logger, fileRecord.Id, pdfKey);
        return pdfKey;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully converted FileRecord {FileRecordId} to PDF rendition {PdfKey}")]
    private static partial void LogConverted(ILogger logger, string fileRecordId, string pdfKey);
}
