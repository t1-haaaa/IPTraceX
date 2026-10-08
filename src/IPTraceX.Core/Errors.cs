namespace IPTraceX.Core;

/// <summary>Base error. Carries a machine-readable process exit code.</summary>
public class TraceXException(string message) : Exception(message)
{
    public virtual int ExitCode => 1;
    public string FriendlyMessage => Message;
}

public sealed class UsageException(string message) : TraceXException(message)
{
    public override int ExitCode => 2;
}

public class InvalidIpException(string message) : TraceXException(message)
{
    public override int ExitCode => 3;
}

public sealed class NonPublicIpException(string message) : InvalidIpException(message);

/// <summary>Malformed email input (exit code 3, same usage class as bad IPs).</summary>
public sealed class InvalidEmailException(string message) : InvalidIpException(message);

public class ProviderException(string message) : TraceXException(message)
{
    public override int ExitCode => 4;
}

public sealed class NetworkException(string message) : ProviderException(message);

public sealed class ProviderTimeoutException(string message) : ProviderException(message);

public sealed class RateLimitException(string message) : ProviderException(message);

public sealed class AuthException(string message) : ProviderException(message);

public sealed class NotFoundException(string message) : ProviderException(message);

public sealed class BadResponseException(string message) : ProviderException(message);

public sealed class ConfigException(string message) : TraceXException(message)
{
    public override int ExitCode => 5;
}
