using System.Globalization;

namespace IPTraceX.Core;

/// <summary>
/// Email normalization and validation. Rejects control/format characters,
/// newline/header injection and path-unsafe input before anything else.
/// Domain is lowercased; the local part keeps its semantics.
/// </summary>
public static class EmailValidation
{
    public static EmailTarget Parse(string? raw)
    {
        if (raw is null)
        {
            throw new InvalidEmailException("Empty email address.");
        }

        if (raw.Any(c => char.IsControl(c)))
        {
            throw new InvalidEmailException("Invalid email address.");
        }

        string text = raw.Trim();
        if (text.Length == 0 || text.Length > 254)
        {
            throw new InvalidEmailException("Invalid email address.");
        }

        int at = text.IndexOf('@');
        if (at <= 0 || at != text.LastIndexOf('@') || at == text.Length - 1)
        {
            throw new InvalidEmailException("Invalid email address.");
        }

        string local = text[..at];
        string domain = text[(at + 1)..].ToLowerInvariant();
        if (local.Length == 0 || local.Length > 64 || domain.Length == 0 || domain.Length > 253)
        {
            throw new InvalidEmailException("Invalid email address.");
        }

        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
        {
            throw new InvalidEmailException("Invalid email address.");
        }

        foreach (string label in domain.Split('.'))
        {
            if (label.Length == 0 || label.Length > 63
                || label.StartsWith('-') || label.EndsWith('-')
                || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                throw new InvalidEmailException("Invalid email address.");
            }
        }

        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains(".."))
        {
            throw new InvalidEmailException("Invalid email address.");
        }

        foreach (char c in local)
        {
            if (!(char.IsLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~.".Contains(c)))
            {
                throw new InvalidEmailException("Invalid email address.");
            }
        }

        return new EmailTarget(raw, local, domain);
    }
}
