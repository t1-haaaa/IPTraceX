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
    public string[] IntelProviders { get; set; } =
    [
        "ripestat", "doh-cloudflare", "doh-google", "system-dns",
        "tor-exits", "cloud-ranges",
    ];
    public int MaxConcurrency { get; set; } = 4;
    public double GlobalTimeoutSeconds { get; set; } = 90.0;
    public string RiskWeights { get; set; } = "";
    public string AbuseIpDbKey { get; set; } = "";
    public string HibpApiKey { get; set; } = "";
    public string GitHubToken { get; set; } = "";
    public string EmailRiskWeights { get; set; } = "";

    public static readonly string[] DefaultProviders = ["ipwho.is", "ipapi.co", "ipinfo.io"];
}
