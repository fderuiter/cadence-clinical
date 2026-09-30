using System;

using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Validation;

public class ValidationRunIdValueConverter() : ValueConverter<ValidationRunId, Guid>(
    id => id.Value,
    guid => new ValidationRunId(guid)
);

public class ValidationFindingIdValueConverter() : ValueConverter<ValidationFindingId, Guid>(
    id => id.Value,
    guid => new ValidationFindingId(guid)
);
