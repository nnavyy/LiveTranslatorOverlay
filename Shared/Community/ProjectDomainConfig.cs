using System.Text;

namespace LiveTranslatorOverlay.Core;

/// <summary>Project-branded email and domain endpoints for OTP authentication, support, and update notices.</summary>
public static class ProjectDomainConfig
{
    public const string DefaultDomain = "livetranslator.app";

    public static string Domain => ResolveDomain();
    public static string NoReplyAddress => $"noreply@{Domain}";
    public static string SupportAddress => $"support@{Domain}";
    public static string UpdatesAddress => $"updates@{Domain}";
    public static string SecurityAddress => $"security@{Domain}";
    public static string WebsiteUrl => $"https://{Domain}";

    public static string ResolveDomain()
    {
        string? raw = Environment.GetEnvironmentVariable("LIVETRANSLATOR_EMAIL_DOMAIN");
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultDomain;

        string candidate = raw.Trim().TrimStart('@').ToLowerInvariant();
        if (candidate.Length is < 4 or > 253 || !candidate.Contains('.', StringComparison.Ordinal))
            return DefaultDomain;

        var sb = new StringBuilder(candidate.Length);
        foreach (char ch in candidate)
        {
            if (char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch is '.' or '-')
            {
                sb.Append(ch);
            }
            else
            {
                return DefaultDomain;
            }
        }

        string cleaned = sb.ToString();
        if (cleaned.StartsWith('.') || cleaned.EndsWith('.') || cleaned.Contains("..", StringComparison.Ordinal))
            return DefaultDomain;

        return cleaned;
    }
}
