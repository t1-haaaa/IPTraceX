using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Microsoft.Extensions.Logging;

// UTF-8 everywhere (Linux default; explicit on Windows consoles).
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch (Exception ex) when (ex is IOException || ex is NotSupportedException)
{
}

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] args)
{
    CliApp.Options opts;
    try
    {
        opts = CliApp.ParseArgs(args);
    }
    catch (UsageException ex)
    {
        Console.Error.WriteLine($"[ERROR] {ex.Message}");
        return CliApp.ExitUsage;
    }

    if (opts.Help)
    {
        Console.WriteLine(CliApp.HelpText());
        return CliApp.ExitOk;
    }

    if (opts.Version)
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
        return CliApp.ExitOk;
    }

    string projectRoot = ResolveProjectRoot();

    AppConfig config;
    try
    {
        config = AppConfigLoader.Load(projectRoot);
    }
    catch (UsageException ex)
    {
        Console.Error.WriteLine($"[ERROR] {ex.Message}");
        return CliApp.ExitUsage;
    }

    if (opts.Timeout.HasValue)
    {
        double timeout = opts.Timeout.Value;
        if (double.IsNaN(timeout) || double.IsInfinity(timeout))
        {
            Console.Error.WriteLine("[ERROR] Invalid --timeout value.");
            return CliApp.ExitUsage;
        }

        config.TimeoutSeconds = Math.Min(Math.Max(timeout, 1.0), 60.0);
    }

    if (opts.Debug)
    {
        config.Debug = true;
    }

    bool noColor = opts.NoColor || config.NoColor;
    var palette = new Palette(Palette.ColorsEnabled(noColor));

    using var loggerFactory = LoggerFactory.Create(builder =>
        builder.AddProvider(new DebugLoggerProvider(config.Debug, Console.Error)));
    ILogger logger = loggerFactory.CreateLogger("IPTraceX");

    var app = new CliApp(
        config,
        palette,
        Console.In,
        Console.Out,
        Console.Error,
        logger,
        new EngineAdapter(config));
    try
    {
        return await app.RunAsync([.. args]).ConfigureAwait(false);
    }
    catch (UsageException ex)
    {
        // Defense in depth: ParseArgs already handles these.
        Console.Error.WriteLine($"[ERROR] {ex.Message}");
        return CliApp.ExitUsage;
    }
}

static string ResolveProjectRoot()
{
    // 1. Launcher-provided (works from any cwd).
    string? fromEnv = Environment.GetEnvironmentVariable("IPTRACEX_PROJECT_ROOT");
    if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
    {
        return Path.GetFullPath(fromEnv);
    }

    // 2. Walk up from the assembly looking for iptracex.sh.
    try
    {
        string? dir = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "iptracex.sh")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }
    }
    catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
        || ex is PathTooLongException)
    {
    }

    // 3. Current directory.
    return Directory.GetCurrentDirectory();
}
