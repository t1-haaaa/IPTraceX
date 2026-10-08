using IPTraceX.Core;
using IPTraceX.Infrastructure;
using IPTraceX.Infrastructure.Providers;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EmailProviderTests
{
    private static EmailTarget Target(string email = "user@gmail.com")
        => EmailValidation.Parse(email);

    private static EmailContext Ctx() => new(5, null, null);

    private static System.Text.Json.JsonElement Doc(string raw)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task DomainIntelParsesMxSpfDmarc()
    {
        var fetcher = new FakeFetcher(url =>
        {
            if (url.Contains("type=MX")) return FakeFetcher.Json(
                """{"Status":0,"Answer":[{"name":"gmail.com","type":15,"data":"5 gmail-smtp-in.l.google.com."}]}""");
            if (url.Contains("type=TXT") && url.Contains("gmail.com") && !url.Contains("_dmarc"))
                return FakeFetcher.Json(
                    """{"Status":0,"Answer":[{"name":"gmail.com","type":16,"data":"\"v=spf1 redirect=_spf.google.com\""}]}""");
            if (url.Contains("_dmarc")) return FakeFetcher.Json(
                """{"Status":0,"Answer":[{"name":"x","type":16,"data":"\"v=DMARC1; p=none;\""}]}""");
            if (url.Contains("kickbox")) return FakeFetcher.Json("""{"disposable":false}""");
            if (url.Contains("rdap.org")) return FakeFetcher.Json(
                """{"ldhName":"GMAIL.COM","status":["active"],"entities":[{"roles":["registrar"],"vcardArray":["vcard",[["version",{},"text","4.0"],["fn",{},"text","MarkMonitor Inc."]]]}],"events":[{"eventAction":"registration","eventDate":"1995-08-13T04:00:00Z"}]}""");
            return FakeFetcher.Json("""{"Status":0}""");
        });
        var provider = new EmailDomainProvider(5, fetcher);
        EmailEvidence evidence = await provider.InvestigateAsync(Target(), Ctx());
        var domain = Assert.IsType<EmailDomainEvidence>(evidence);
        Assert.True(domain.Success);
        Assert.Contains("gmail-smtp-in.l.google.com", domain.MxHosts);
        Assert.StartsWith("v=spf1", domain.SpfRecord);
        Assert.StartsWith("v=DMARC1", domain.DmarcRecord);
        Assert.False(domain.Disposable);
        Assert.True(domain.IsFreeMail);
        Assert.Equal("Google (Gmail / Workspace)", domain.MailProvider);
        Assert.Equal("MarkMonitor Inc.", domain.Registrar);
        Assert.NotNull(domain.DomainCreatedUtc);
    }

    [Fact]
    public async Task DisposableDetected()
    {
        var fetcher = new FakeFetcher(url =>
            url.Contains("kickbox") ? FakeFetcher.Json("""{"disposable":true}""")
            : FakeFetcher.Json("""{"Status":0}"""));
        var provider = new EmailDomainProvider(5, fetcher);
        EmailEvidence evidence = await provider.InvestigateAsync(
            Target("user@10minutemail.com"), Ctx());
        var domain = Assert.IsType<EmailDomainEvidence>(evidence);
        Assert.True(domain.Success);
        Assert.True(domain.Disposable);
    }

    [Fact]
    public void MailProviderClassification()
    {
        Assert.Equal("Google (Gmail / Workspace)",
            EmailDomainProvider.ClassifyMailProvider(
                ["alt1.gmail-smtp-in.l.google.com"], "gmail.com"));
        Assert.Equal("Microsoft 365 / Outlook",
            EmailDomainProvider.ClassifyMailProvider(["outlook-com.olc.protection.outlook.com"], "x.com"));
        Assert.Equal("Self-hosted (MX points at the domain itself)",
            EmailDomainProvider.ClassifyMailProvider(["example.com"], "example.com"));
        Assert.Null(EmailDomainProvider.ClassifyMailProvider(["mx.unknown-xyz.invalid"], "x.com"));
    }

    [Fact]
    public void GravatarMd5()
    {
        Assert.Equal("55502f40dc8b7c769880b10874abc9d0",
            GravatarProvider.Md5Hex("test@example.com"));
    }

    [Fact]
    public async Task GravatarFoundAndMissing()
    {
        var found = new FakeFetcher(_ => FakeFetcher.Json(
            """{"entry":[{"profileUrl":"https://gravatar.com/test","displayName":"Test"}]}"""));
        EmailEvidence ok = await new GravatarProvider(5, found)
            .InvestigateAsync(Target("test@example.com"), Ctx());
        var avatar = Assert.IsType<AvatarEvidence>(ok);
        Assert.True(avatar.Success);
        Assert.True(avatar.Found);
        Assert.Equal("https://gravatar.com/test", avatar.ProfileUrl);

        var missing = new FakeFetcher(_ => throw new NotFoundException("gone"));
        EmailEvidence none = await new GravatarProvider(5, missing)
            .InvestigateAsync(Target("test@example.com"), Ctx());
        Assert.False(Assert.IsType<AvatarEvidence>(none).Found);
    }

    [Fact]
    public async Task GitHubHandleWeakAndCommitStrong()
    {
        var fetcher = new FakeFetcher(url => url.Contains("/search/commits")
            ? FakeFetcher.Json("""{"items":[{"repository":{"full_name":"octo/repo"}}]}""")
            : FakeFetcher.Json("""{"login":"someuser","name":"Some User"}"""));
        var provider = new GitHubFootprintProvider(5, fetcher);
        var context = new EmailContext(5, null, "TOKEN");
        EmailEvidence evidence = await provider.InvestigateAsync(Target("someuser@example.com"), context);
        var footprint = Assert.IsType<FootprintEvidence>(evidence);
        Assert.True(footprint.Success);
        Assert.Equal(2, footprint.Matches.Length);
        Assert.Contains(footprint.Matches, m => m.EvidenceType == "handle-exists" && m.Confidence == "LOW");
        Assert.Contains(footprint.Matches, m => m.EvidenceType == "commit-authorship" && m.Confidence == "MEDIUM");
    }

    [Fact]
    public async Task HibpSkippedWithoutKeyAndParsesWithKey()
    {
        var provider = new HibpBreachProvider(5, new FakeFetcher(_ => null), "");
        EmailEvidence skipped = await provider.InvestigateAsync(Target(), Ctx());
        Assert.False(Assert.IsType<BreachEvidence>(skipped).Success);

        var fetcher = new FakeFetcher(_ => FakeFetcher.Json(
            """[{"Name":"ExampleBreach","Domain":"example.com","BreachDate":"2024-05-01","DataClasses":["Email addresses","Usernames"]}]"""));
        var keyed = new HibpBreachProvider(5, fetcher, "KEY");
        EmailEvidence evidence = await keyed.InvestigateAsync(Target(), Ctx());
        var breach = Assert.IsType<BreachEvidence>(evidence);
        Assert.True(breach.Success);
        Assert.Single(breach.Breaches);
        Assert.Equal("ExampleBreach", breach.Breaches[0].Name);
        Assert.Equal(["Email addresses", "Usernames"], breach.Breaches[0].Categories);
    }

    [Fact]
    public async Task ProviderFailureIsolated()
    {
        var failing = new FakeEmailProvider("boom", new ProviderException("down"));
        var orchestrator = new EmailOrchestrator([failing]);
        IReadOnlyList<EmailEvidence> results = await orchestrator.RunAsync(
            Target(), Ctx(), 2, TimeSpan.FromSeconds(10));
        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal(ProviderHealthStatus.Failed, orchestrator.Health["boom"].Status);
    }

    private sealed class FakeEmailProvider(string id, Exception error) : IEmailProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            id, id, ProviderCategory.Network, [4, 6], false, "", "");

        public Task<EmailEvidence> InvestigateAsync(
            EmailTarget target, EmailContext context, CancellationToken cancellationToken)
            => throw error;
    }
}
