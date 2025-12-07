using System.Text.RegularExpressions;

namespace RemoteViewer.Client.Services;

public static partial class CredentialParser
{
    public static (string? Id, string? Password) TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);

        text = text.Trim();

        // Try labeled format first: "ID: xxx" / "Password: yyy" (with various label names)
        var labeledMatch = LabeledCredentialsRegex().Match(text);
        if (labeledMatch.Success)
        {
            return (labeledMatch.Groups[1].Value.Trim(), labeledMatch.Groups[2].Value.Trim());
        }

        // Try unlabeled format: numeric ID (with optional spaces) followed by password
        // Pattern: "1 234 567 890 abc123" or "1234567890 abc123"
        var unlabeledMatch = UnlabeledCredentialsRegex().Match(text);
        if (unlabeledMatch.Success)
        {
            return (unlabeledMatch.Groups[1].Value.Trim(), unlabeledMatch.Groups[2].Value.Trim());
        }

        // Try two lines: first line is ID, second is password
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 2)
        {
            return (lines[0].Trim(), lines[1].Trim());
        }

        return (null, null);
    }

    [GeneratedRegex(
        @"(?:id)\s*[:=\-]?\s*(.+?)[\r\n]+\s*(?:password|pass|pwd)\s*[:=\-]?\s*(.+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LabeledCredentialsRegex();

    [GeneratedRegex(@"^([\d\s]+\d)\s+(\S+)$")]
    private static partial Regex UnlabeledCredentialsRegex();
}
