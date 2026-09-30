using System;

using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Standards;

public class StandardVersionIdValueConverter() : ValueConverter<StandardVersionId, Guid>(
    id => id.Value,
    guid => new StandardVersionId(guid)
);

public class LibraryItemIdValueConverter() : ValueConverter<LibraryItemId, Guid>(
    id => id.Value,
    guid => new LibraryItemId(guid)
);

public class StudyStandardsProfileIdValueConverter() : ValueConverter<StudyStandardsProfileId, Guid>(
    id => id.Value,
    guid => new StudyStandardsProfileId(guid)
);
