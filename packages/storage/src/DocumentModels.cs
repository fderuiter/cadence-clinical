using System;
using System.Collections.Generic;

namespace Cadence.Storage;

public record DocumentMetadata(
    string DocumentId,
    string ContentType,
    long SizeBytes,
    string Sha256Hash,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    IReadOnlyDictionary<string, string>? CustomMetadata = null
);

public record StorageLocation(
    string BucketOrContainer,
    string KeyPath,
    string StorageClass = "STANDARD"
);
