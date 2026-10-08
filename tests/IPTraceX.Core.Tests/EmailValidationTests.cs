using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EmailValidationTests
{
    [Theory]
    [InlineData("user@gmail.com", "user", "gmail.com")]
    [InlineData("USER@GMAIL.COM", "USER", "gmail.com")]
    [InlineData("  user@gmail.com  ", "user", "gmail.com")]
    [InlineData("first.last+tag@sub.example.co.uk", "first.last+tag", "sub.example.co.uk")]
    public void ValidEmailsNormalize(string raw, string local, string domain)
    {
        EmailTarget target = EmailValidation.Parse(raw);
        Assert.Equal(local, target.Local);
        Assert.Equal(domain, target.Domain);
        Assert.Equal($"{local}@{domain}", target.Normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("user@example")]
    [InlineData("user@.example.com")]
    [InlineData("user@example..com")]
    [InlineData("user name@example.com")]
    [InlineData(".user@example.com")]
    [InlineData("user.@example.com")]
    [InlineData("us..er@example.com")]
    [InlineData("user@example.com\nBcc: evil@x.com")]
    public void InvalidEmailsRejected(string raw)
    {
        Assert.Throws<InvalidEmailException>(() => EmailValidation.Parse(raw));
    }

    [Fact]
    public void ControlCharactersRejected()
    {
        Assert.Throws<InvalidEmailException>(() => EmailValidation.Parse("us\x01er@example.com"));
        Assert.Throws<InvalidEmailException>(() => EmailValidation.Parse(null));
    }

    [Fact]
    public void InvalidEmailIsInvalidIpForExitCodes()
    {
        Assert.ThrowsAny<InvalidIpException>(() => EmailValidation.Parse("bad"));
    }
}
