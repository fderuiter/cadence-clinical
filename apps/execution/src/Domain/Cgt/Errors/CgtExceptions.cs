using System;

namespace Cadence.Apps.Execution.Domain.Cgt.Errors;

public class DuplicateVerifierException(string message) : Exception(message)
{
}

public class DuplicateWitnessException(string message) : Exception(message)
{
}

public class InvalidDinException(string message) : Exception(message)
{
}

public class BarcodeVerificationException(string message) : Exception(message)
{
}

public class ChainOfCustodyNotFoundException(string message) : Exception(message)
{
}

public class CallerIdentityMismatchException(string message) : Exception(message)
{
}
