namespace IPTraceX.CLI;

/// <summary>
/// Terminal UI -- Kali/OSINT aesthetic, ASCII-first.
/// Same contract as the former tool: ASCII art/tokens only, ANSI colors
/// decorative, every string correct with colors disabled.
/// </summary>
public sealed class Palette
{
    private readonly bool _enabled;

    public Palette(bool enabled)
    {
        _enabled = enabled;
    }

    public string Wrap(string code, string text)
        => _enabled ? $"\x1b[{code}m{text}\x1b[0m" : text;

    public string Brand(string text) => Wrap("1;33", text);
    public string Data(string text) => Wrap("36", text);
    public string TokenOk() => Wrap("1;32", "[+]");
    public string TokenInfo() => Wrap("36", "[::]");
    public string TokenAsk() => Wrap("1;33", "[?]");
    public string TokenIn() => Wrap("37", "[-]");
    public string TokenWarn() => Wrap("1;31", "[!]");
    public string TokenError() => Wrap("1;31", "[ERROR]");

    public static bool ColorsEnabled(bool noColorFlag)
    {
        if (noColorFlag)
        {
            return false;
        }

        if ((Environment.GetEnvironmentVariable("NO_COLOR") ?? "").Length != 0)
        {
            return false;
        }

        foreach (string name in new[] { "IPTRACEX_NO_COLOR", "IPGHOST_NO_COLOR" })
        {
            string value = Environment.GetEnvironmentVariable(name) ?? "";
            if (value.Equals("1", StringComparison.Ordinal)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        try
        {
            return !Console.IsOutputRedirected;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }
}
