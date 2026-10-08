using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class InvestigationTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _previousCache;

    public InvestigationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _previousCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _previousCache);
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static IntelligenceProfile Profile() => Sample.Profile();

    private FileInvestigationStore Store() => new(_dir);

    private static Investigation Make(string target = "35.94.45.221") => new(
        InvestigationId.New(), DateTimeOffset.UtcNow, "1.1.0",
        target, "ip", Profile(), [], Investigation.CurrentSchema);

    [Fact]
    public void IdFormatAndValidation()
    {
        for (int i = 0; i < 25; i++)
        {
            string id = InvestigationId.New();
            Assert.True(InvestigationId.IsValid(id), id);
            Assert.DoesNotContain("/", id);
            Assert.DoesNotContain("\\", id);
            Assert.DoesNotContain("..", id);
        }

        Assert.False(InvestigationId.IsValid(null));
        Assert.False(InvestigationId.IsValid(""));
        Assert.False(InvestigationId.IsValid("IPX-20261008-abc"));
        Assert.False(InvestigationId.IsValid("../evil"));
        Assert.False(InvestigationId.IsValid("IPX-20261008-A7F31C.json"));
        Assert.Throws<UsageException>(() => InvestigationId.RequireValid("../../x"));
        Assert.Equal("IPX-20261008-A7F31C",
            InvestigationId.RequireValid("IPX-20261008-A7F31C"));
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++)
        {
            ids.Add(InvestigationId.New());
        }

        Assert.Equal(200, ids.Count);
    }

    [Fact]
    public void SaveGetListDeleteRoundtrip()
    {
        var store = Store();
        Investigation saved = Make();
        store.Save(saved);
        Assert.True(store.Exists(saved.Id));
        Investigation loaded = store.Get(saved.Id);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.Target, loaded.Target);
        Assert.Equal("2.1", loaded.SchemaVersion);
        Assert.Equal("United States", loaded.Profile!.Geo.Geolocation.Country);
        Assert.Single(store.List());
        store.Delete(saved.Id);
        Assert.False(store.Exists(saved.Id));
        Assert.Empty(store.List());
    }

    [Fact]
    public void MissingInvestigationErrors()
    {
        var store = Store();
        Assert.False(store.Exists("IPX-20261008-AAAAAA"));
        Assert.Throws<NotFoundException>(() => store.Get("IPX-20261008-AAAAAA"));
        Assert.Throws<UsageException>(() => store.Get("not-an-id"));
        Assert.Throws<UsageException>(() => store.Delete("../evil"));
    }

    [Fact]
    public void CorruptFileHandled()
    {
        var store = Store();
        Investigation saved = Make();
        store.Save(saved);
        string file = Directory.GetFiles(
            Path.Combine(_dir, "investigations"), "*.json", SearchOption.AllDirectories)[0];
        File.WriteAllText(file, "{not valid json{{");
        Assert.Throws<BadResponseException>(() => store.Get(saved.Id));
        // Listing skips corrupt files instead of crashing.
        Assert.Empty(store.List());
    }

    [Fact]
    public void WrongSchemaRejected()
    {
        var store = Store();
        Investigation saved = Make();
        store.Save(saved);
        string file = Directory.GetFiles(
            Path.Combine(_dir, "investigations"), "*.json", SearchOption.AllDirectories)[0];
        string text = File.ReadAllText(file);
        File.WriteAllText(file, text.Replace("\"schema_version\": \"2.1\"", "\"schema_version\": \"9.9\""));
        Assert.Throws<BadResponseException>(() => store.Get(saved.Id));
    }

    [Fact]
    public void NoSecretsInInvestigationFile()
    {
        var store = Store();
        store.Save(Make());
        string file = Directory.GetFiles(
            Path.Combine(_dir, "investigations"), "*.json", SearchOption.AllDirectories)[0];
        string text = File.ReadAllText(file).ToLowerInvariant();
        Assert.DoesNotContain("bearer", text);
        Assert.DoesNotContain("ipinfo_token", text);
        Assert.DoesNotContain("abuseipdb_key", text);
        Assert.DoesNotContain("api_key", text);
    }

    [Fact]
    public void VotesPreservedForEvidence()
    {
        var store = Store();
        var profile = Profile();
        profile.Geo.Votes.Add(Sample.Result());
        var investigation = Make() with { Profile = profile };
        store.Save(investigation);
        Investigation loaded = store.Get(investigation.Id);
        Assert.NotEmpty(loaded.Profile!.Geo.Votes);
        Assert.NotEmpty(Evidence.BuildMatrix(loaded.Profile));
    }

    [Fact]
    public void HistoryOrdersByTime()
    {
        var store = Store();
        var first = Make() with { TimestampUtc = DateTimeOffset.UtcNow.AddDays(-2) };
        var second = Make() with { TimestampUtc = DateTimeOffset.UtcNow };
        store.Save(second);
        store.Save(first);
        var history = store.HistoryFor("35.94.45.221").ToList();
        Assert.Equal(2, history.Count);
        Assert.True(history[0].TimestampUtc <= history[1].TimestampUtc);
        Assert.Empty(store.HistoryFor("9.9.9.9"));
    }
}
