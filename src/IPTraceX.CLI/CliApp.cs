using IPTraceX.Core;
using IPTraceX.Infrastructure;
using IPTraceX.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace IPTraceX.CLI;

/// <summary>Analysis seam (production engine or test fake).</summary>
public interface IAnalysisEngine
{
    Task<GeoResult> AnalyzeAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default);
}

/// <summary>Full-profile seam (production profiler or test fake).</summary>
public interface IProfileEngine
{
    Task<IntelligenceProfile> AnalyzeIpAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default);

    Task<(DomainEvidence Resolution, List<IntelligenceProfile> Profiles)> AnalyzeDomainAsync(
        string domain, Action<string>? onStage = null, CancellationToken cancellationToken = default);
}

/// <summary>All CLI modes. I/O injected for testability; logic mirrors the former tool.</summary>
public sealed class CliApp
{
    public const int ExitOk = 0;
    public const int ExitGeneral = 1;
    public const int ExitUsage = 2;
    public const int ExitInvalidIp = 3;
    public const int ExitProvider = 4;
    public const int ExitConfig = 5;

    private readonly AppConfig _config;
    private readonly Palette _palette;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly ILogger _logger;
    private readonly IAnalysisEngine _engine;
    private readonly IProfileEngine _profiles;
    private readonly IEmailProfileEngine _emailProfiles;
    private readonly Infrastructure.IInvestigationStore _store;
    private readonly Func<CancellationToken, Task<string>> _detectSelf;

    public CliApp(
        AppConfig config,
        Palette palette,
        TextReader input,
        TextWriter output,
        TextWriter error,
        ILogger logger,
        IAnalysisEngine engine,
        Func<CancellationToken, Task<string>>? detectSelf = null,
        IProfileEngine? profileEngine = null,
        Infrastructure.IInvestigationStore? store = null,
        IEmailProfileEngine? emailProfiles = null)
    {
        _config = config;
        _palette = palette;
        _input = input;
        _output = output;
        _error = error;
        _logger = logger;
        _engine = engine;
        _profiles = profileEngine ?? new ProfileEngineAdapter(config);
        _emailProfiles = emailProfiles ?? new EmailProfileEngineAdapter(config);
        _store = store ?? new Infrastructure.FileInvestigationStore(config.ProjectRoot);
        _detectSelf = detectSelf ?? (ct => Infrastructure.SelfIp.DetectAsync(config.TimeoutSeconds, null, ct));
    }

    public sealed record Options(
        string? Ip,
        bool Json,
        string? File,
        bool Stdin,
        bool Map,
        bool Self,
        bool NoColor,
        bool Debug,
        double? Timeout,
        bool Version,
        bool Help,
        string? Domain,
        string? Rdns,
        string? Report,
        bool Providers,
        string? Investigate,
        string? Investigation,
        bool ListInvestigations,
        string? DeleteInvestigation,
        string[] Compare,
        string[] CompareIp,
        string? Email,
        string? EmailFile);

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        Options opts;
        try
        {
            opts = ParseArgs(args);
        }
        catch (UsageException ex)
        {
            _error.WriteLine($"[ERROR] {ex.Message}");
            return ExitUsage;
        }

        if (opts.Help)
        {
            _output.WriteLine(HelpText());
            return ExitOk;
        }

        if (opts.Version)
        {
            _output.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return ExitOk;
        }

        if (opts.Stdin)
        {
            string data = await _input.ReadToEndAsync(ct).ConfigureAwait(false);
            if (opts.Investigate is not null)
            {
                return await RunBatchInvestigateAsync(SplitLines(data), ct).ConfigureAwait(false);
            }

            return await RunBatchAsync(SplitLines(data), ct).ConfigureAwait(false);
        }

        if (opts.File is not null)
        {
            List<string> lines;
            try
            {
                lines = ReadLinesFromFile(opts.File);
            }
            catch (TraceXException ex)
            {
                _error.WriteLine(FriendlyError(ex));
                return ex.ExitCode;
            }

            if (opts.Investigate is not null)
            {
                return await RunBatchInvestigateAsync(lines, ct).ConfigureAwait(false);
            }

            return await RunBatchAsync(lines, ct).ConfigureAwait(false);
        }

        if (opts.Providers)
        {
            return ShowProviders();
        }

        if (opts.ListInvestigations)
        {
            return ListInvestigations();
        }

        if (opts.DeleteInvestigation is not null)
        {
            return DeleteInvestigation(opts.DeleteInvestigation);
        }

        if (opts.Investigation is not null)
        {
            return ShowInvestigation(opts.Investigation);
        }

        if (opts.Compare.Length == 2)
        {
            return CompareInvestigations(opts.Compare[0], opts.Compare[1]);
        }

        if (opts.CompareIp.Length == 2)
        {
            return await CompareIpsAsync(opts.CompareIp[0], opts.CompareIp[1], ct).ConfigureAwait(false);
        }

        if (opts.EmailFile is not null)
        {
            List<string> lines;
            try
            {
                lines = ReadLinesFromFile(opts.EmailFile);
            }
            catch (TraceXException ex)
            {
                _error.WriteLine(FriendlyError(ex));
                return ex.ExitCode;
            }

            return await RunEmailBatchAsync(lines, opts.Investigate is not null, ct)
                .ConfigureAwait(false);
        }

        if (opts.Email is not null)
        {
            if (opts.Report is not null)
            {
                return await RunEmailReportAsync(opts.Email, opts.Report, ct).ConfigureAwait(false);
            }

            if (opts.Investigate is not null)
            {
                return await RunEmailInvestigateAsync(opts.Email, ct).ConfigureAwait(false);
            }

            return await RunEmailAsync(opts.Email, opts.Json, ct).ConfigureAwait(false);
        }

        if (opts.EmailFile is not null)
        {
            List<string> lines;
            try
            {
                lines = ReadLinesFromFile(opts.EmailFile);
            }
            catch (TraceXException ex)
            {
                _error.WriteLine(FriendlyError(ex));
                return ex.ExitCode;
            }

            return await RunEmailBatchAsync(lines, opts.Investigate is not null, ct)
                .ConfigureAwait(false);
        }

        if (opts.Email is not null)
        {
            if (opts.Report is not null)
            {
                return await RunEmailReportAsync(opts.Email, opts.Report, ct).ConfigureAwait(false);
            }

            if (opts.Investigate is not null)
            {
                return await RunEmailInvestigateAsync(opts.Email, ct).ConfigureAwait(false);
            }

            return await RunEmailAsync(opts.Email, opts.Json, ct).ConfigureAwait(false);
        }

        if (opts.Investigate is not null && opts.Investigate.Length != 0 && opts.File is null && !opts.Stdin)
        {
            return await RunInvestigateAsync(opts.Investigate, ct).ConfigureAwait(false);
        }

        if (opts.Map)
        {
            if (opts.Ip is null && !opts.Self)
            {
                _error.WriteLine("[ERROR] --map requires an IP address.");
                return ExitUsage;
            }

            string? target = opts.Self ? await ResolveSelfAsync(ct).ConfigureAwait(false) : opts.Ip;
            if (target is null)
            {
                return ExitProvider;
            }

            return await RunMapAsync(target, ct).ConfigureAwait(false);
        }

        if (opts.Rdns is not null)
        {
            return await RunRdnsAsync(opts.Rdns, ct).ConfigureAwait(false);
        }

        if (opts.Domain is not null)
        {
            if (opts.Report is not null)
            {
                return await RunDomainReportAsync(opts.Domain, opts.Report, ct).ConfigureAwait(false);
            }

            return await RunDomainAsync(opts.Domain, opts.Json, ct).ConfigureAwait(false);
        }

        if (opts.Self)
        {
            string? target = await ResolveSelfAsync(ct).ConfigureAwait(false);
            if (target is null)
            {
                return ExitProvider;
            }

            if (opts.Report is not null)
            {
                return await RunProfileReportAsync(target, opts.Report, ct).ConfigureAwait(false);
            }

            if (!opts.Json)
            {
                _output.WriteLine(
                    $"{_palette.TokenInfo()} Note: result reflects your public exit IP (VPN/proxy aware).");
            }

            return await RunProfileAsync(target, opts.Json, null, ct).ConfigureAwait(false);
        }

        if (opts.Ip is not null)
        {
            if (opts.Report is not null)
            {
                return await RunProfileReportAsync(opts.Ip, opts.Report, ct).ConfigureAwait(false);
            }

            return await RunProfileAsync(opts.Ip, opts.Json, null, ct).ConfigureAwait(false);
        }

        if (opts.Json)
        {
            _error.WriteLine("[ERROR] --json requires an IP address.");
            return ExitUsage;
        }

        if (opts.Report is not null)
        {
            _error.WriteLine("[ERROR] --report requires an IP address.");
            return ExitUsage;
        }

        return await RunInteractiveAsync(ct).ConfigureAwait(false);
    }

    public static Options ParseArgs(string[] args)
    {
        string? ip = null;
        bool json = false, stdin = false, map = false, self = false;
        bool noColor = false, debug = false, version = false, help = false;
        bool providers = false;
        bool listInvestigations = false;
        string? file = null;
        string? domain = null;
        string? rdns = null;
        string? report = null;
        string? investigate = null;
        string? investigation = null;
        string? deleteInvestigation = null;
        var compare = new List<string>();
        var compareIp = new List<string>();
        string? email = null;
        string? emailFile = null;
        double? timeout = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help": help = true; break;
                case "-v":
                case "--version": version = true; break;
                case "--json": json = true; break;
                case "--stdin": stdin = true; break;
                case "--map": map = true; break;
                case "--self": self = true; break;
                case "--no-color": noColor = true; break;
                case "--debug": debug = true; break;
                case "--file":
                case "--batch":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--file requires a PATH value.");
                    }

                    file = args[++i];
                    break;
                case "--domain":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--domain requires a domain value.");
                    }

                    domain = args[++i];
                    break;
                case "--rdns":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--rdns requires an IP value.");
                    }

                    rdns = args[++i];
                    break;
                case "--report":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--report requires a format (txt, json, html).");
                    }

                    report = args[++i];
                    break;
                case "--providers": providers = true; break;
                case "--email":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--email requires an address value.");
                    }

                    email = args[++i];
                    break;
                case "--email-file":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--email-file requires a PATH value.");
                    }

                    emailFile = args[++i];
                    break;
                case "--investigate":
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        investigate = args[++i];
                    }
                    else
                    {
                        investigate = "";
                    }

                    break;
                case "--investigation":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--investigation requires an ID value.");
                    }

                    investigation = args[++i];
                    break;
                case "--list-investigations": listInvestigations = true; break;
                case "--delete-investigation":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--delete-investigation requires an ID value.");
                    }

                    deleteInvestigation = args[++i];
                    break;
                case "--compare":
                    if (i + 2 >= args.Length)
                    {
                        throw new UsageException("--compare requires two investigation IDs.");
                    }

                    compare.Add(args[++i]);
                    compare.Add(args[++i]);
                    break;
                case "--compare-ip":
                    if (i + 2 >= args.Length)
                    {
                        throw new UsageException("--compare-ip requires two IP addresses.");
                    }

                    compareIp.Add(args[++i]);
                    compareIp.Add(args[++i]);
                    break;
                case "--timeout":
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--timeout requires a value in seconds.");
                    }

                    string rawTimeout = args[++i];
                    if (!double.TryParse(rawTimeout,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double parsed))
                    {
                        throw new UsageException("Invalid --timeout value.");
                    }

                    timeout = parsed;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new UsageException($"Unknown option: {arg}");
                    }

                    if (ip is not null)
                    {
                        throw new UsageException($"Unexpected argument: {arg}");
                    }

                    ip = arg;
                    break;
            }
        }

        return new Options(ip, json, file, stdin, map, self, noColor, debug, timeout, version, help,
            domain, rdns, report, providers, investigate, investigation,
            listInvestigations, deleteInvestigation, [.. compare], [.. compareIp],
            email, emailFile);
    }

    public static string HelpText() => string.Join("\n", new[]
    {
        "usage: iptracex.sh [-h] [--json] [--file PATH] [--stdin] [--map] [--self]",
        "                   [--domain DOMAIN] [--rdns IP] [--report FORMAT] [--providers]",
        "                   [--no-color] [--debug] [--timeout TIMEOUT] [-v] [ip]",
        "",
        "IPTraceX -- Multi-provider IP intelligence and approximate geolocation CLI.",
        "Approximate IP-based geolocation only; not exact tracking.",
        "",
        "positional arguments:",
        "  ip                 Public IPv4/IPv6 address to look up.",
        "",
        "options:",
        "  -h, --help         show this help message and exit",
        "  --json             Machine-readable JSON output.",
        "  --file PATH        Batch lookup, one IP per line.",
        "  --batch PATH       Alias for --file.",
        "  --stdin            Read IPs from STDIN.",
        "  --map              Print Google Maps URL and exit.",
        "  --self             Detect this machine's public exit IP (HTTPS) and analyze it.",
        "  --domain DOMAIN    Resolve a domain and analyze every public IP found.",
        "  --rdns IP          Reverse-DNS lookup only.",
        "  --report FORMAT    Save an investigation report (txt, json, html).",
        "  --providers        List providers with health and capability notes.",
        "  --investigate [IP] Analyze and save an investigation (or save batch).",
        "  --investigation ID Show a saved investigation.",
        "  --list-investigations  List saved investigations.",
        "  --delete-investigation ID  Delete a saved investigation.",
        "  --compare ID1 ID2  Compare two investigations.",
        "  --compare-ip A B   Compare two IP addresses directly.",
        "  --email ADDRESS    Analyze an email address (public OSINT only).",
        "  --email-file PATH  Batch email analysis, one address per line.",
        "  --no-color         Disable ANSI colors.",
        "  --debug            Verbose technical details.",
        "  --timeout TIMEOUT  Provider timeout in seconds.",
        "  -v, --version      Show version.",
        "",
        "Examples:",
        "  ./iptracex.sh",
        "  ./iptracex.sh 8.8.8.8",
        "  ./iptracex.sh --json 8.8.8.8",
        "  ./iptracex.sh --map 8.8.8.8",
        "  ./iptracex.sh --self",
        "  ./iptracex.sh --domain example.com",
        "  ./iptracex.sh --report html 8.8.8.8",
        "  ./iptracex.sh --file ips.txt",
        "  cat ips.txt | ./iptracex.sh --stdin",
    });

    public static string SafeFilename(string ipText, string ext)
    {
        string safe = System.Text.RegularExpressions.Regex.Replace(
            ipText.Trim(), @"[^A-Za-z0-9_.-]", "_");
        safe = safe.Replace(":", "_");
        if (safe.Length > 64)
        {
            safe = safe[..64];
        }

        if (safe is "." or ".." or "")
        {
            safe = "report";
        }

        return safe + ext;
    }

    public static string FriendlyError(Exception ex)
        => ex switch
        {
            InvalidEmailException =>
                "[ERROR] Invalid email address.\n[!] Please enter a valid email address (user@example.com).",
            NonPublicIpException npe =>
                $"[!] This is not a public routable IP.\n    {npe.Message}",
            InvalidIpException =>
                "[ERROR] Invalid IP address.\n[!] Please enter a valid public IPv4 or IPv6 address.",
            RateLimitException =>
                "[!] API rate limit reached.\n    Please wait and try again later.",
            ProviderTimeoutException =>
                "[ERROR] Provider request timed out.",
            AuthException or NotFoundException or NetworkException
                or BadResponseException or ProviderException or TraceXException =>
                $"[ERROR] {((TraceXException)ex).Message}",
            _ => "[ERROR] Unexpected error occurred.",
        };

    public static int ExitFor(Exception ex)
        => ex switch
        {
            UsageException => ExitUsage,
            InvalidIpException => ExitInvalidIp,
            TraceXException tx => tx.ExitCode,
            _ => ExitGeneral,
        };

    private static List<string> SplitLines(string data)
        => data.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    public static List<string> ReadLinesFromFile(string pathText)
    {
        if (!File.Exists(pathText))
        {
            throw new UsageException($"File not found: {pathText}");
        }

        try
        {
            string content = File.ReadAllText(pathText, System.Text.Encoding.UTF8);
            return SplitLines(content);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new UsageException($"Cannot read file: {pathText}");
        }
    }

    private async Task<string?> ResolveSelfAsync(CancellationToken ct)
    {
        try
        {
            return await _detectSelf(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return null;
        }
    }

    private readonly List<ProviderOutcome> _lastOutcomes = [];

    public async Task<int> RunProfileAsync(
        string ipText, bool asJson, Action<string>? onStage, CancellationToken ct)
    {
        IntelligenceProfile profile;
        try
        {
            profile = await _profiles.AnalyzeIpAsync(ipText, onStage, ct).ConfigureAwait(false);
            _lastOutcomes.Clear();
            _lastOutcomes.AddRange(profile.Providers);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
                _error.WriteLine($"[debug] raw input: {Repr(ipText)}");
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        if (asJson)
        {
            _output.WriteLine(GeoJson.ProfileToJsonString(profile, indented: true));
        }
        else
        {
            _output.WriteLine(Formatting.FormatProfile(profile, _palette));
        }

        return ExitOk;
    }

    public async Task<int> RunDomainAsync(string domain, bool asJson, CancellationToken ct)
    {
        (DomainEvidence resolution, List<IntelligenceProfile> profiles) result;
        try
        {
            result = await _profiles.AnalyzeDomainAsync(domain, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        if (result.profiles.Count == 0)
        {
            _output.WriteLine($"[!] No public IPs resolved for {result.resolution.Domain}.");
            return ExitGeneral;
        }

        _output.WriteLine("");
        _output.WriteLine($"{_palette.TokenOk()} {_palette.Brand("DOMAIN")}");
        _output.WriteLine($"    Domain       : {_palette.Data(result.resolution.Domain)}");
        _output.WriteLine($"    A            : {_palette.Data(result.resolution.A.Length == 0 ? "none" : string.Join(", ", result.resolution.A))}");
        _output.WriteLine($"    AAAA         : {_palette.Data(result.resolution.Aaaa.Length == 0 ? "none" : string.Join(", ", result.resolution.Aaaa))}");
        _output.WriteLine("");
        foreach (IntelligenceProfile profile in result.profiles)
        {
            _lastOutcomes.Clear();
            _lastOutcomes.AddRange(profile.Providers);
            if (asJson)
            {
                _output.WriteLine(GeoJson.ProfileToJsonString(profile, indented: true));
            }
            else
            {
                _output.WriteLine(Formatting.FormatProfile(profile, _palette));
            }
        }

        return ExitOk;
    }

    public async Task<int> RunDomainReportAsync(string domain, string format, CancellationToken ct)
    {
        (DomainEvidence resolution, List<IntelligenceProfile> profiles) result;
        try
        {
            result = await _profiles.AnalyzeDomainAsync(domain, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        if (result.profiles.Count == 0)
        {
            _output.WriteLine($"[!] No public IPs resolved for {result.resolution.Domain}.");
            return ExitGeneral;
        }

        foreach (IntelligenceProfile profile in result.profiles)
        {
            try
            {
                string path = Infrastructure.ReportService.SaveProfile(profile, format, _config.ProjectRoot);
                _output.WriteLine($"[+] Report saved to {path}");
            }
            catch (TraceXException ex)
            {
                _error.WriteLine(FriendlyError(ex));
                return ex.ExitCode;
            }
        }

        return ExitOk;
    }

    public async Task<int> RunProfileReportAsync(string ipText, string format, CancellationToken ct)
    {
        IntelligenceProfile profile;
        try
        {
            profile = await _profiles.AnalyzeIpAsync(ipText, null, ct).ConfigureAwait(false);
            _lastOutcomes.Clear();
            _lastOutcomes.AddRange(profile.Providers);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        try
        {
            string path = Infrastructure.ReportService.SaveProfile(profile, format, _config.ProjectRoot);
            _output.WriteLine($"[+] Report saved to {path}");
            return ExitOk;
        }
        catch (TraceXException ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex.ExitCode;
        }
    }

    public async Task<int> RunRdnsAsync(string ipText, CancellationToken ct)
    {
        IntelligenceProfile profile;
        try
        {
            profile = await _profiles.AnalyzeIpAsync(ipText, null, ct).ConfigureAwait(false);
            _lastOutcomes.Clear();
            _lastOutcomes.AddRange(profile.Providers);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        _output.WriteLine("");
        _output.WriteLine($"{_palette.TokenOk()} {_palette.Brand("REVERSE DNS")}");
        _output.WriteLine($"    IP           : {_palette.Data(profile.Geo.Ip)}");
        _output.WriteLine($"    PTR          : {_palette.Data(profile.Dns.PtrHostnames.Length == 0 ? "none observed" : string.Join(", ", profile.Dns.PtrHostnames))}");
        _output.WriteLine($"    Confidence   : {_palette.Data(profile.Dns.Confidence)}");
        _output.WriteLine($"    Sources      : {_palette.Data(profile.Dns.Sources.Length == 0 ? "none" : string.Join(", ", profile.Dns.Sources))}");
        _output.WriteLine("");
        return ExitOk;
    }

    public async Task<int> RunEmailAsync(string raw, bool asJson, CancellationToken ct)
    {
        EmailProfile profile;
        try
        {
            EmailValidation.Parse(raw);
            profile = await _emailProfiles.AnalyzeEmailAsync(raw, null, ct).ConfigureAwait(false);
            _lastOutcomes.Clear();
            _lastOutcomes.AddRange(profile.Providers.Select(o => new ProviderOutcome(
                o.ProviderId, o.Status == "success" ? "success" : "failed", null, o.Summary)));
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        if (asJson)
        {
            _output.WriteLine(EmailJson.ToJsonString(profile, indented: true));
        }
        else
        {
            _output.WriteLine(Formatting.FormatEmailProfile(profile, _palette));
            await PostEmailMenuAsync(profile, ct).ConfigureAwait(false);
        }

        return ExitOk;
    }

    private async Task PostEmailMenuAsync(EmailProfile profile, CancellationToken ct)
    {
        while (true)
        {
            _output.WriteLine("");
            _output.WriteLine($"{_palette.TokenInfo()} Email actions");
            _output.WriteLine("");
            _output.WriteLine("[1] View evidence");
            _output.WriteLine("[2] View providers");
            _output.WriteLine("[3] View domain intelligence");
            _output.WriteLine("[4] Generate report");
            _output.WriteLine("[5] Save investigation");
            _output.WriteLine("[0] Back");
            _output.WriteLine("");
            _output.WriteLine($"{_palette.TokenAsk()} Select an option:");
            string? choice;
            try
            {
                _output.Write($"{_palette.TokenIn()} ");
                _output.Flush();
                choice = (await _input.ReadLineAsync(ct).ConfigureAwait(false))?.Trim();
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException
                || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _output.WriteLine("\nBye.");
                return;
            }

            if (choice is null)
            {
                _output.WriteLine("\nBye.");
                return;
            }

            if (choice is "1" or "01")
            {
                _output.WriteLine(Formatting.FormatEmailEvidence(profile, _palette));
                continue;
            }

            if (choice is "2" or "02")
            {
                foreach (ProviderOutcome outcome in profile.Providers)
                {
                    _output.WriteLine($"    {_palette.Data(outcome.ProviderId.PadRight(14))} : {outcome.Status}"
                        + (outcome.Error is null ? "" : $" ({outcome.Error})"));
                }

                continue;
            }

            if (choice is "3" or "03")
            {
                _output.WriteLine(Formatting.FormatEmailDomain(profile, _palette));
                continue;
            }

            if (choice is "4" or "04")
            {
                string? format = await PromptAsync(
                    $"{_palette.TokenAsk()} Format (txt/json/html):\n{_palette.TokenIn()} ", ct)
                    .ConfigureAwait(false);
                if (format is null)
                {
                    _output.WriteLine("\nBye.");
                    return;
                }

                await RunEmailReportAsync(profile.Target, format, ct).ConfigureAwait(false);
                continue;
            }

            if (choice is "5" or "05")
            {
                SaveEmailInvestigation(profile);
                continue;
            }

            if (choice is "0" or "00")
            {
                return;
            }

            _output.WriteLine("[?] Unknown option. Choose 1-5 or 0.");
        }
    }

    private void SaveEmailInvestigation(EmailProfile profile)
    {
        try
        {
            var errors = profile.Providers
                .Where(o => o.Status != "success")
                .Select(o => $"{o.ProviderId}: {o.Error ?? "failed"}")
                .ToList();
            var investigation = new Investigation(
                InvestigationId.New("EMX"),
                DateTimeOffset.UtcNow,
                AppInfo.Version,
                profile.Target,
                "email",
                null,
                errors,
                Investigation.CurrentSchema,
                EmailJson.FromEmailProfile(profile));
            _store.Save(investigation);
            _output.WriteLine($"[+] Investigation saved: {investigation.Id}");
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
        }
    }

    public async Task<int> RunEmailInvestigateAsync(string raw, CancellationToken ct)
    {
        EmailProfile profile;
        try
        {
            EmailValidation.Parse(raw);
            profile = await _emailProfiles.AnalyzeEmailAsync(raw, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        try
        {
            var errors = profile.Providers
                .Where(o => o.Status != "success")
                .Select(o => $"{o.ProviderId}: {o.Error ?? "failed"}")
                .ToList();
            var investigation = new Investigation(
                InvestigationId.New("EMX"),
                DateTimeOffset.UtcNow,
                AppInfo.Version,
                profile.Target,
                "email",
                null,
                errors,
                Investigation.CurrentSchema,
                EmailJson.FromEmailProfile(profile));
            _store.Save(investigation);
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex is TraceXException tx ? tx.ExitCode : ExitGeneral;
        }

        _output.WriteLine(Formatting.FormatEmailProfile(profile, _palette));
        return ExitOk;
    }

    public async Task<int> RunEmailBatchAsync(
        IEnumerable<string> items, bool save, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (string raw in items)
        {
            string text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            if (seen.Add(text.ToLowerInvariant()))
            {
                unique.Add(text);
            }
        }

        if (unique.Count == 0)
        {
            _error.WriteLine("[ERROR] No email addresses found in input.");
            return ExitUsage;
        }

        _output.WriteLine($"{_palette.TokenInfo()} Processing {unique.Count} email addresses...");
        _output.WriteLine("");
        int ok = 0, failed = 0, invalid = 0, index = 0;
        foreach (string candidate in unique)
        {
            index++;
            ct.ThrowIfCancellationRequested();
            _output.WriteLine($"{Formatting.BatchItem(_palette, index)} {candidate}");
            EmailProfile profile;
            try
            {
                EmailValidation.Parse(candidate);
                profile = await _emailProfiles.AnalyzeEmailAsync(candidate, null, ct)
                    .ConfigureAwait(false);
            }
            catch (InvalidEmailException)
            {
                _output.WriteLine("[!] Invalid email address.\n");
                invalid++;
                failed++;
                continue;
            }
            catch (Exception)
            {
                _output.WriteLine("[ERROR] Unexpected error for this address.\n");
                failed++;
                continue;
            }

            _output.WriteLine(Formatting.FormatEmailSummary(profile, _palette));
            if (save)
            {
                try
                {
                    var errors = profile.Providers
                        .Where(o => o.Status != "success")
                        .Select(o => $"{o.ProviderId}: {o.Error ?? "failed"}")
                        .ToList();
                    var investigation = new Investigation(
                        InvestigationId.New("EMX"),
                        DateTimeOffset.UtcNow,
                        AppInfo.Version,
                        profile.Target,
                        "email",
                        null,
                        errors,
                        Investigation.CurrentSchema,
                        EmailJson.FromEmailProfile(profile));
                    _store.Save(investigation);
                    _output.WriteLine($"[+] Investigation saved: {investigation.Id}");
                    ok++;
                }
                catch (Exception ex)
                {
                    _error.WriteLine(FriendlyError(ex));
                    failed++;
                }
            }
            else
            {
                ok++;
            }
        }

        _output.WriteLine($"{_palette.TokenInfo()} Processed: {unique.Count}");
        _output.WriteLine($"{_palette.TokenInfo()} Valid: {ok}");
        _output.WriteLine($"{_palette.TokenInfo()} Invalid: {invalid}");
        if (save)
        {
            _output.WriteLine($"{_palette.TokenInfo()} Investigations: {ok}");
        }

        return failed == 0 ? ExitOk : ExitGeneral;
    }

    public async Task<int> RunEmailReportAsync(string raw, string format, CancellationToken ct)
    {
        EmailProfile profile;
        try
        {
            EmailValidation.Parse(raw);
            profile = await _emailProfiles.AnalyzeEmailAsync(raw, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        try
        {
            string path = Infrastructure.ReportService.SaveEmailProfile(
                profile, format, _config.ProjectRoot);
            _output.WriteLine($"[+] Report saved to {path}");
            return ExitOk;
        }
        catch (TraceXException ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex.ExitCode;
        }
    }

    public int ShowProviders()
    {
        var health = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ProviderOutcome outcome in _lastOutcomes)
        {
            health[outcome.ProviderId] = outcome.Status == "success" ? "OK" : "ERROR";
        }

        _output.WriteLine(Formatting.FormatProviders(_palette, Infrastructure.ProviderCatalog.All, health));
        _output.WriteLine("[::] Health reflects this process; run a lookup to populate it.");
        return ExitOk;
    }

    private Investigation BuildInvestigation(string target, string targetType, IntelligenceProfile profile)
    {
        var errors = profile.Providers
            .Where(o => o.Status != "success")
            .Select(o => $"{o.ProviderId}: {o.Error ?? "failed"}")
            .ToList();
        return new Investigation(
            InvestigationId.New(),
            DateTimeOffset.UtcNow,
            AppInfo.Version,
            target,
            targetType,
            profile,
            errors,
            Investigation.CurrentSchema);
    }

    public async Task<int> RunInvestigateAsync(string ipText, CancellationToken ct)
    {
        IntelligenceProfile? profile = await AnalyzeProfileAsync(ipText, null, ct)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return ExitGeneral;
        }

        Investigation investigation;
        try
        {
            investigation = BuildInvestigation(profile.Geo.Ip, "ip", profile);
            _store.Save(investigation);
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex is TraceXException tx ? tx.ExitCode : ExitGeneral;
        }

        _output.WriteLine($"[+] Investigation saved: {investigation.Id}");
        _output.WriteLine(Formatting.FormatProfile(profile, _palette));
        return ExitOk;
    }

    public async Task<int> RunBatchInvestigateAsync(IEnumerable<string> items, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (string raw in items)
        {
            string text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            if (seen.Add(text))
            {
                unique.Add(text);
            }
        }

        if (unique.Count == 0)
        {
            _error.WriteLine("[ERROR] No IP addresses found in input.");
            return ExitUsage;
        }

        _output.WriteLine(Formatting.BatchHeader(_palette, unique.Count));
        _output.WriteLine("");
        int ok = 0, failed = 0, index = 0;
        foreach (string candidate in unique)
        {
            index++;
            ct.ThrowIfCancellationRequested();
            _output.WriteLine($"{Formatting.BatchItem(_palette, index)} {candidate}");
            IntelligenceProfile? profile = await AnalyzeProfileAsync(candidate, null, ct)
                .ConfigureAwait(false);
            if (profile is null)
            {
                failed++;
                continue;
            }

            try
            {
                Investigation investigation = BuildInvestigation(profile.Geo.Ip, "ip", profile);
                _store.Save(investigation);
                _output.WriteLine($"[+] Investigation saved: {investigation.Id}\n");
                ok++;
            }
            catch (Exception ex)
            {
                _error.WriteLine(FriendlyError(ex));
                failed++;
            }
        }

        _output.WriteLine(Formatting.BatchSummary(_palette, ok, failed));
        return failed == 0 ? ExitOk : ExitGeneral;
    }

    public int ListInvestigations()
    {
        IReadOnlyList<InvestigationSummary> items;
        try
        {
            items = _store.List();
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ExitGeneral;
        }

        if (items.Count == 0)
        {
            _output.WriteLine("[::] No saved investigations.");
            return ExitOk;
        }

        _output.WriteLine("");
        _output.WriteLine($"{_palette.TokenOk()} {_palette.Brand("INVESTIGATIONS")}");
        foreach (InvestigationSummary item in items)
        {
            _output.WriteLine($"    {_palette.Data(item.Id)}  {item.TimestampUtc:yyyy-MM-dd HH:mm}  {item.Target}");
        }

        _output.WriteLine("");
        return ExitOk;
    }

    public int DeleteInvestigation(string id)
    {
        try
        {
            _store.Delete(id);
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex is TraceXException tx ? tx.ExitCode : ExitGeneral;
        }

        _output.WriteLine($"[+] Deleted investigation {id.Trim()}.");
        return ExitOk;
    }

    public int ShowInvestigation(string id)
    {
        Investigation investigation;
        try
        {
            investigation = _store.Get(id);
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex is TraceXException tx ? tx.ExitCode : ExitGeneral;
        }

        _output.WriteLine(Formatting.FormatInvestigation(investigation, _palette));
        IReadOnlyList<Investigation> history;
        try
        {
            history = _store.HistoryFor(investigation.Target);
        }
        catch (Exception)
        {
            history = [];
        }

        if (history.Count > 1)
        {
            _output.WriteLine(Formatting.FormatTimeline(history, _palette));
        }

        return ExitOk;
    }

    public int CompareInvestigations(string firstId, string secondId)
    {
        Investigation first, second;
        try
        {
            first = _store.Get(firstId);
            second = _store.Get(secondId);
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return ex is TraceXException tx ? tx.ExitCode : ExitGeneral;
        }

        _output.WriteLine(Formatting.FormatComparison(first, second, _palette));
        return ExitOk;
    }

    public async Task<int> CompareIpsAsync(string firstIp, string secondIp, CancellationToken ct)
    {
        IntelligenceProfile? first = await AnalyzeProfileAsync(firstIp, null, ct)
            .ConfigureAwait(false);
        IntelligenceProfile? second = await AnalyzeProfileAsync(secondIp, null, ct)
            .ConfigureAwait(false);
        if (first is null || second is null)
        {
            return ExitGeneral;
        }

        _output.WriteLine(Formatting.FormatProfileComparison(first, second, _palette));
        return ExitOk;
    }

    public async Task<int> RunSingleAsync(string ipText, bool asJson, CancellationToken ct)
    {
        GeoResult info;
        try
        {
            info = await _engine.AnalyzeAsync(ipText, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
                _error.WriteLine($"[debug] raw input: {Repr(ipText)}");
            }

            _error.WriteLine(FriendlyError(ex));
            return ex is InvalidIpException ? ExitInvalidIp
                : ex is TraceXException tx ? tx.ExitCode
                : ex is ProviderException ? ExitProvider
                : ExitProvider;
        }

        if (asJson)
        {
            PrintJson(info);
        }
        else
        {
            _output.WriteLine(Formatting.FormatReport(info, _palette));
        }

        return ExitOk;
    }

    public async Task<int> RunMapAsync(string ipText, CancellationToken ct)
    {
        GeoResult info;
        try
        {
            info = await _engine.AnalyzeAsync(ipText, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
                _error.WriteLine($"[debug] raw input: {Repr(ipText)}");
            }

            _error.WriteLine(FriendlyError(ex));
            return ExitFor(ex);
        }

        string? url = info.GoogleMapsUrl
            ?? Maps.BuildMapsUrl(info.Geolocation.Latitude, info.Geolocation.Longitude);
        if (url is null)
        {
            _output.WriteLine("[!] Google Maps location unavailable.");
            return ExitOk;
        }

        _output.WriteLine("[+] GOOGLE MAPS\n\n    " + url);
        return ExitOk;
    }

    public async Task<int> RunBatchAsync(IEnumerable<string> items, CancellationToken ct)
    {
        // Deduplicate but keep order; one bad IP never stops the batch.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (string raw in items)
        {
            string text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            if (seen.Add(text))
            {
                unique.Add(text);
            }
        }

        if (unique.Count == 0)
        {
            _error.WriteLine("[ERROR] No IP addresses found in input.");
            return ExitUsage;
        }

        _output.WriteLine(Formatting.BatchHeader(_palette, unique.Count));
        _output.WriteLine("");
        int ok = 0, failed = 0;
        int index = 0;
        foreach (string candidate in unique)
        {
            index++;
            ct.ThrowIfCancellationRequested();
            _output.WriteLine($"{Formatting.BatchItem(_palette, index)} {candidate}");
            try
            {
                GeoResult info = await _engine.AnalyzeAsync(candidate, null, ct).ConfigureAwait(false);
                _output.WriteLine(Formatting.FormatReport(info, _palette));
                ok++;
                continue;
            }
            catch (RateLimitException)
            {
                _output.WriteLine("[!] API rate limit reached.");
                _output.WriteLine("    Please wait and try again later.\n");
            }
            catch (InvalidIpException ex)
            {
                _output.WriteLine(FriendlyError(ex) + "\n");
            }
            catch (TraceXException ex)
            {
                _output.WriteLine($"[ERROR] {ex.Message}\n");
            }
            catch (Exception)
            {
                _output.WriteLine("[ERROR] Unexpected error for this IP.\n");
            }

            failed++;
        }

        _output.WriteLine(Formatting.BatchSummary(_palette, ok, failed));
        return failed == 0 ? ExitOk : ExitGeneral;
    }

    private string ExportJson(GeoResult info)
    {
        string outDir = Path.Combine(_config.ProjectRoot, "output");
        Directory.CreateDirectory(outDir);
        string resolved = SafeResolve(outDir, SafeFilename(info.Ip, ".json"));
        File.WriteAllText(resolved, JsonText(info) + "\n", System.Text.Encoding.UTF8);
        return resolved;
    }

    private string SaveReport(GeoResult info)
    {
        var plain = new Palette(false);
        string outDir = Path.Combine(_config.ProjectRoot, "reports");
        Directory.CreateDirectory(outDir);
        string resolved = SafeResolve(outDir, SafeFilename(info.Ip, ".txt"));
        File.WriteAllText(resolved, Formatting.FormatReport(info, plain) + "\n", System.Text.Encoding.UTF8);
        return resolved;
    }

    public static string SafeResolve(string outDir, string fileName)
    {
        string fullOut = Path.GetFullPath(outDir);
        string resolved = Path.GetFullPath(Path.Combine(fullOut, Path.GetFileName(fileName)));
        if (!resolved.StartsWith(fullOut + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolved, fullOut, StringComparison.Ordinal))
        {
            throw new UsageException("Unsafe output filename.");
        }

        return resolved;
    }

    public static string JsonText(GeoResult info)
        => GeoJson.ToJsonString(info, indented: true);

    private void PrintJson(GeoResult info)
    {
        // Pure JSON on stdout -- no decorations, no colors.
        _output.WriteLine(JsonText(info));
    }

    public static string Repr(string value)
    {
        var sb = new System.Text.StringBuilder("'");
        foreach (char c in value)
        {
            sb.Append(c switch
            {
                '\'' => "\\'",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when c < 0x20 || c == 0x7F => $"\\x{(int)c:x2}",
                _ when c > 0x7E && c < 0xA0 => $"\\x{(int)c:x2}",
                _ when c > 0xFFFF => $"\\U{(int)c:x8}",
                _ when c > 0x7E => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        sb.Append('\'');
        return sb.ToString();
    }

    public async Task<int> RunInteractiveAsync(CancellationToken ct)
    {
        if (!Formatting.StartupShownByLauncher())
        {
            _output.WriteLine(Formatting.Startup(_palette, AppInfo.Version));
        }

        while (true)
        {
            _output.WriteLine(Formatting.MainMenu(_palette));
            string? choice;
            try
            {
                _output.Write($"{_palette.TokenIn()} ");
                _output.Flush();
                choice = (await _input.ReadLineAsync(ct).ConfigureAwait(false))?.Trim();
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException
                || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _output.WriteLine("\nBye.");
                return ExitOk;
            }

            if (choice is null)
            {
                _output.WriteLine("\nBye.");
                return ExitOk;
            }

            switch (choice)
            {
                case "1":
                case "01":
                    if (await AnalyzeIpFlowAsync(ct).ConfigureAwait(false))
                    {
                        return ExitOk;
                    }

                    break;
                case "2":
                case "02":
                    await AnalyzeDomainFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "3":
                case "03":
                    await AnalyzeEmailFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "4":
                case "04":
                    await SelfFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "5":
                case "05":
                    await ReverseDnsFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "6":
                case "06":
                    ShowProviders();
                    break;
                case "7":
                case "07":
                    await BatchFileFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "8":
                case "08":
                    await InvestigationsMenuFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "9":
                case "09":
                    await InvestigationReportFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "10":
                    ShowConfiguration();
                    break;
                case "0":
                case "00":
                    _output.WriteLine("Bye.");
                    return ExitOk;
                default:
                    _output.WriteLine("[?] Unknown option. Choose 01-10 or 00.");
                    break;
            }
        }
    }

    private async Task InvestigationsMenuFlowAsync(CancellationToken ct)
    {
        while (true)
        {
            _output.WriteLine(Formatting.InvestigationsMenu(_palette));
            string? choice;
            try
            {
                _output.Write($"{_palette.TokenIn()} ");
                _output.Flush();
                choice = (await _input.ReadLineAsync(ct).ConfigureAwait(false))?.Trim();
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException
                || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _output.WriteLine("\nBye.");
                return;
            }

            if (choice is null)
            {
                _output.WriteLine("\nBye.");
                return;
            }

            switch (choice)
            {
                case "1":
                case "01":
                    await NewInvestigationFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "2":
                case "02":
                    await OpenInvestigationFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "3":
                case "03":
                    ListInvestigations();
                    break;
                case "4":
                case "04":
                    await CompareInvestigationsFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "5":
                case "05":
                    await InvestigationReportMenuFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "6":
                case "06":
                    await DeleteInvestigationFlowAsync(ct).ConfigureAwait(false);
                    break;
                case "0":
                case "00":
                    return; // back to main menu
                default:
                    _output.WriteLine("[?] Unknown option. Choose 01-06 or 00.");
                    break;
            }
        }
    }

    private async Task NewInvestigationFlowAsync(CancellationToken ct)
    {
        string? raw = await PromptAsync(Formatting.InputPrompt(_palette), ct).ConfigureAwait(false);
        if (raw is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        if (raw.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty IP address.");
            return;
        }

        IntelligenceProfile? profile = await AnalyzeProfileAsync(raw, null, ct)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        try
        {
            Investigation investigation = BuildInvestigation(profile.Geo.Ip, "ip", profile);
            _store.Save(investigation);
            _output.WriteLine($"[+] Investigation saved: {investigation.Id}");
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return;
        }

        _output.WriteLine(Formatting.FormatProfile(profile, _palette));
    }

    private async Task OpenInvestigationFlowAsync(CancellationToken ct)
    {
        string? id = await PromptAsync(
            $"{_palette.TokenAsk()} Enter investigation ID:\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (id is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        if (id.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty investigation ID.");
            return;
        }

        ShowInvestigation(id);
    }

    private async Task CompareInvestigationsFlowAsync(CancellationToken ct)
    {
        string? first = await PromptAsync(
            $"{_palette.TokenAsk()} First investigation ID:\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (first is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        string? second = await PromptAsync(
            $"{_palette.TokenAsk()} Second investigation ID:\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (second is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        CompareInvestigations(first, second);
    }

    private async Task InvestigationReportMenuFlowAsync(CancellationToken ct)
    {
        string? id = await PromptAsync(
            $"{_palette.TokenAsk()} Investigation ID:\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (id is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        Investigation investigation;
        try
        {
            investigation = _store.Get(id);
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return;
        }

        string? format = await PromptAsync(
            $"{_palette.TokenAsk()} Format (txt/json/html):\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (format is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        try
        {
            string path = investigation.Email is not null
                ? Infrastructure.ReportService.SaveEmailReport(
                    investigation.Email, investigation.Target, investigation.Id,
                    format, _config.ProjectRoot)
                : investigation.Profile is not null
                    ? Infrastructure.ReportService.SaveProfile(
                        investigation.Profile, format, _config.ProjectRoot)
                    : throw new UsageException("Investigation has no reportable data.");
            _output.WriteLine($"[+] Report saved to {path}");
        }
        catch (Exception ex)
        {
            _error.WriteLine(FriendlyError(ex));
        }
    }

    private async Task DeleteInvestigationFlowAsync(CancellationToken ct)
    {
        string? id = await PromptAsync(
            $"{_palette.TokenAsk()} Investigation ID to delete:\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (id is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        DeleteInvestigation(id);
    }

    private async Task<string?> PromptAsync(string prompt, CancellationToken ct)
    {
        try
        {
            _output.Write(prompt);
            _output.Flush();
            return (await _input.ReadLineAsync(ct).ConfigureAwait(false))?.Trim();
        }
        catch (Exception ex) when (ex is IOException || ex is OperationCanceledException
            || ex is InvalidOperationException || ex is ObjectDisposedException)
        {
            return null;
        }
    }

    private async Task<bool> AnalyzeIpFlowAsync(CancellationToken ct)
    {
        string? raw = await PromptAsync(Formatting.InputPrompt(_palette), ct).ConfigureAwait(false);
        if (raw is null)
        {
            _output.WriteLine("\nBye.");
            return true;
        }

        if (raw.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty IP address.");
            return false;
        }

        IntelligenceProfile? profile = await AnalyzeProfileAsync(raw, null, ct)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return false;
        }

        _output.WriteLine(Formatting.FormatProfile(profile, _palette));
        return await PostLookupMenuAsync(profile, ct).ConfigureAwait(false);
    }

    private async Task<IntelligenceProfile?> AnalyzeProfileAsync(
        string raw, Action<string>? onStage, CancellationToken ct)
    {
        try
        {
            return await _profiles.AnalyzeIpAsync(raw, onStage, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                _error.WriteLine(ex.ToString());
                _error.WriteLine($"[debug] raw input: {Repr(raw)}");
            }

            _error.WriteLine(FriendlyError(ex));
            return null;
        }
    }

    private async Task AnalyzeEmailFlowAsync(CancellationToken ct)
    {
        string? raw = await PromptAsync(
            $"{_palette.TokenAsk()} Enter email address:\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (raw is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        if (raw.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty email address.");
            return;
        }

        await RunEmailAsync(raw, false, ct).ConfigureAwait(false);
    }

    private async Task AnalyzeDomainFlowAsync(CancellationToken ct)
    {
        string? domain = await PromptAsync(
            $"{_palette.TokenAsk()} Enter domain:\n{_palette.TokenIn()} ", ct).ConfigureAwait(false);
        if (domain is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        if (domain.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty domain.");
            return;
        }

        await RunDomainAsync(domain, false, ct).ConfigureAwait(false);
    }

    private async Task SelfFlowAsync(CancellationToken ct)
    {
        string? target = await ResolveSelfAsync(ct).ConfigureAwait(false);
        if (target is null)
        {
            return;
        }

        _output.WriteLine(
            $"{_palette.TokenInfo()} Note: result reflects your public exit IP (VPN/proxy aware).");
        IntelligenceProfile? profile = await AnalyzeProfileAsync(target, null, ct)
            .ConfigureAwait(false);
        if (profile is not null)
        {
            _output.WriteLine(Formatting.FormatProfile(profile, _palette));
        }
    }

    private async Task ReverseDnsFlowAsync(CancellationToken ct)
    {
        string? raw = await PromptAsync(Formatting.InputPrompt(_palette), ct).ConfigureAwait(false);
        if (raw is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        if (raw.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty IP address.");
            return;
        }

        await RunRdnsAsync(raw, ct).ConfigureAwait(false);
    }

    private async Task BatchFileFlowAsync(CancellationToken ct)
    {
        string? path = await PromptAsync(
            $"{_palette.TokenAsk()} Enter file path:\n{_palette.TokenIn()} ", ct).ConfigureAwait(false);
        if (path is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        List<string> lines;
        try
        {
            lines = ReadLinesFromFile(path);
        }
        catch (TraceXException ex)
        {
            _error.WriteLine(FriendlyError(ex));
            return;
        }

        await RunBatchAsync(lines, ct).ConfigureAwait(false);
    }

    private async Task InvestigationReportFlowAsync(CancellationToken ct)
    {
        string? raw = await PromptAsync(Formatting.InputPrompt(_palette), ct).ConfigureAwait(false);
        if (raw is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        if (raw.Length == 0)
        {
            _error.WriteLine("[ERROR] Empty IP address.");
            return;
        }

        string? format = await PromptAsync(
            $"{_palette.TokenAsk()} Format (txt/json/html):\n{_palette.TokenIn()} ", ct)
            .ConfigureAwait(false);
        if (format is null)
        {
            _output.WriteLine("\nBye.");
            return;
        }

        await RunProfileReportAsync(raw, format, ct).ConfigureAwait(false);
    }

    private void ShowConfiguration()
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        _output.WriteLine("");
        _output.WriteLine($"{_palette.TokenOk()} {_palette.Brand("CONFIGURATION")}");
        _output.WriteLine($"    Providers    : {_palette.Data(string.Join(",", _config.Providers))}");
        _output.WriteLine($"    Intel        : {_palette.Data(string.Join(",", _config.IntelProviders))}");
        _output.WriteLine($"    Timeout      : {_palette.Data(_config.TimeoutSeconds.ToString(invariant) + "s")}");
        _output.WriteLine($"    Concurrency  : {_palette.Data(_config.MaxConcurrency.ToString(invariant))}");
        _output.WriteLine($"    Cache TTL    : {_palette.Data(_config.CacheTtlSeconds.ToString(invariant) + "s")}");
        _output.WriteLine($"    IPINFO_TOKEN : {_palette.Data(_config.IpInfoToken.Length == 0 ? "not set" : "set")}");
        _output.WriteLine($"    ABUSEIPDB    : {_palette.Data(_config.AbuseIpDbKey.Length == 0 ? "not set" : "set")}");
        _output.WriteLine("");
    }

    private async Task<bool> PostLookupMenuAsync(IntelligenceProfile profile, CancellationToken ct)
    {
        GeoResult current = profile.Geo;
        while (true)
        {
            bool mapsOk = current.GoogleMapsUrl is not null
                || Maps.BuildMapsUrl(current.Geolocation.Latitude, current.Geolocation.Longitude) is not null;
            _output.WriteLine(Formatting.InteractiveMenu(_palette, mapsOk));
            string? choice;
            try
            {
                _output.Write($"{_palette.TokenIn()} ");
                _output.Flush();
                choice = (await _input.ReadLineAsync(ct).ConfigureAwait(false))?.Trim();
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException
                || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _output.WriteLine("\nBye.");
                return true;
            }

            if (choice is null)
            {
                _output.WriteLine("\nBye.");
                return true;
            }

            if (choice is "1" or "01")
            {
                return false; // back to main menu
            }

            if (choice is "2" or "02")
            {
                string? url = current.GoogleMapsUrl
                    ?? Maps.BuildMapsUrl(current.Geolocation.Latitude, current.Geolocation.Longitude);
                if (url is null)
                {
                    _output.WriteLine("[!] Google Maps unavailable for this result.");
                    continue;
                }

                _output.WriteLine("[+] GOOGLE MAPS\n\n    " + url);
                var (opened, message) = Maps.OpenInBrowser(url);
                if (opened)
                {
                    _output.WriteLine("[+] Opened in browser.");
                }
                else
                {
                    _output.WriteLine($"[!] {message}");
                    _output.WriteLine("[+] Google Maps:\n    " + url);
                }

                continue;
            }

            if (choice is "3" or "03")
            {
                _output.WriteLine(GeoJson.ProfileToJsonString(profile, indented: true));
                continue;
            }

            if (choice is "4" or "04")
            {
                try
                {
                    string path = Infrastructure.ReportService.SaveProfile(profile, "txt", _config.ProjectRoot);
                    _output.WriteLine($"[+] Report saved to {path}");
                }
                catch (TraceXException ex)
                {
                    _error.WriteLine(FriendlyError(ex));
                }

                continue;
            }

            if (choice is "0" or "00")
            {
                _output.WriteLine("Bye.");
                return true;
            }

            _output.WriteLine("[?] Unknown option. Choose 01/02/03/04/00.");
        }
    }
}

