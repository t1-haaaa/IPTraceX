namespace IPTraceX.Core;

/// <summary>Application configuration POCO. Populated by the loader.</summary>
public sealed class AppConfig
{
    public string ApiKey { get; set; } = "";
    public double TimeoutSeconds { get; set; } = 10.0;
    public int CacheTtlSeconds { get; set; } = 3600;
    public bool NoColor { get; set; }
    public bool Debug { get; set; }
    public string ProjectRoot { get; set; } = ".";
    public string[] Providers { get; set; } = ["ipwho.is", "ipapi.co", "ipinfo.io"];
    public string IpInfoToken { get; set; } = "";

    public static readonly string[] DefaultProviders = ["ipwho.is", "ipapi.co", "ipinfo.io"];
}
