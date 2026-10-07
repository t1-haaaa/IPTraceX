using IPTraceX.Core;
using Microsoft.Extensions.Logging;

namespace IPTraceX.CLI;

/// <summary>Analysis seam (production engine or test fake).</summary>
public interface IAnalysisEngine
{
    Task<GeoResult> AnalyzeAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default);
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
    private readonly Func<CancellationToken, Task<string>> _detectSelf;

    public CliApp(
        AppConfig config,
        Palette palette,
        TextReader input,
        TextWriter output,
        TextWriter error,
        ILogger logger,
        IAnalysisEngine engine,
        Func<CancellationToken, Task<string>>? detectSelf = null)
    {
        _config = config;
        _palette = palette;
        _input = input;
        _output = output;
        _error = error;
        _logger = logger;
        _engine = engine;
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
        bool Help);

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

            return await RunBatchAsync(lines, ct).ConfigureAwait(false);
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

        if (opts.Self)
        {
            string? target = await ResolveSelfAsync(ct).ConfigureAwait(false);
            if (target is null)
            {
                return ExitProvider;
            }

            if (!opts.Json)
            {
                _output.WriteLine(
                    $"{_palette.TokenInfo()} Note: result reflects your public exit IP (VPN/proxy aware).");
            }

            return await RunSingleAsync(target, opts.Json, ct).ConfigureAwait(false);
        }

        if (opts.Ip is not null)
        {
            return await RunSingleAsync(opts.Ip, opts.Json, ct).ConfigureAwait(false);
        }

        if (opts.Json)
        {
            _error.WriteLine("[ERROR] --json requires an IP address.");
            return ExitUsage;
        }

        return await RunInteractiveAsync(ct).ConfigureAwait(false);
    }

    public static Options ParseArgs(string[] args)
    {
        string? ip = null;
        bool json = false, stdin = false, map = false, self = false;
        bool noColor = false, debug = false, version = false, help = false;
        string? file = null;
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
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("--file requires a PATH value.");
                    }

                    file = args[++i];
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

        return new Options(ip, json, file, stdin, map, self, noColor, debug, timeout, version, help);
    }

    public static string HelpText() => string.Join("\n", new[]
    {
        "usage: iptracex.sh [-h] [--json] [--file PATH] [--stdin] [--map] [--self]",
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
        "  --stdin            Read IPs from STDIN.",
        "  --map              Print Google Maps URL and exit.",
        "  --self             Detect this machine's public exit IP (HTTPS) and analyze it.",
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
        _output.WriteLine(Formatting.Startup(_palette, AppInfo.Version));
        GeoResult? current = null;

        void Progress(string stage)
        {
            string? line = stage switch
            {
                "validating" => Formatting.StageLine(_palette, "validating"),
                _ when stage.StartsWith("querying:", StringComparison.Ordinal)
                    => $"{_palette.TokenInfo()} Querying {stage["querying:".Length..]}...",
                _ when stage.StartsWith("cached:", StringComparison.Ordinal)
                    => $"{_palette.TokenInfo()} Loading cached result ({stage["cached:".Length..]})...",
                _ => Formatting.StageLine(_palette, stage),
            };
            if (line is not null)
            {
                _output.WriteLine(line);
            }
        }

        while (true)
        {
            string? raw;
            try
            {
                _output.Write(Formatting.InputPrompt(_palette));
                _output.Flush();
                raw = await _input.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException
                || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _output.WriteLine("\nBye.");
                return ExitOk;
            }

            if (raw is null)
            {
                _output.WriteLine("\nBye.");
                return ExitOk;
            }

            raw = raw.Trim();
            if (raw.Length == 0)
            {
                _error.WriteLine("[ERROR] Empty IP address.");
                continue;
            }

            try
            {
                current = await _engine.AnalyzeAsync(raw, Progress, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_config.Debug)
                {
                    _error.WriteLine(ex.ToString());
                    _error.WriteLine($"[debug] raw input: {Repr(raw)}");
                }

                _error.WriteLine(FriendlyError(ex));
                continue;
            }

            _output.WriteLine(Formatting.FormatReport(current, _palette));
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
                    return ExitOk;
                }

                if (choice is null)
                {
                    _output.WriteLine("\nBye.");
                    return ExitOk;
                }

                if (choice is "1" or "01")
                {
                    break; // another IP
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
                    try
                    {
                        string path = ExportJson(current);
                        _output.WriteLine($"[+] JSON exported to {path}");
                    }
                    catch (TraceXException ex)
                    {
                        _error.WriteLine(FriendlyError(ex));
                    }

                    continue;
                }

                if (choice is "4" or "04")
                {
                    try
                    {
                        string path = SaveReport(current);
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
                    return ExitOk;
                }

                _output.WriteLine("[?] Unknown option. Choose 01/02/03/04/00.");
            }
        }
    }
}
