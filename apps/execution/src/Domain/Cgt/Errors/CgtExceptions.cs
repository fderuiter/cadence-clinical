using System;

namespace Cadence.Apps.Execution.Domain.Cgt.Errors;

public class DuplicateVerifierException : Exception
{
    public DuplicateVerifierException(string message) : base(message) { }
}

public class MissingVerifierException : Exception
{
    public MissingVerifierException(string message) : base(message) { }
}

public class MissingSignatureException : Exception
{
    public MissingSignatureException(string message) : base(message) { }
}

public class QuarantinedProductException : Exception
{
    public QuarantinedProductException(string message) : base(message) { }
}

public class DuplicateWitnessException : Exception
{
    public DuplicateWitnessException(string message) : base(message) { }
}

public class InvalidDinException : Exception
{
    public InvalidDinException(string message) : base(message) { }
}

public class BarcodeVerificationException : Exception
{
    public BarcodeVerificationException(string message) : base(message) { }
}

public class ChainOfCustodyNotFoundException : Exception
{
    public ChainOfCustodyNotFoundException(string message) : base(message) { }
}
