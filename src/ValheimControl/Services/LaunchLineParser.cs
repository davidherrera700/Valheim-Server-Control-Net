using System.Text.RegularExpressions;

namespace ValheimControl.Services;

/// <summary>
/// Reads and writes specific arguments (-name, -world, -port, -password,
/// -public, -crossplay) inside the raw text of start_server.sh, without
/// needing to understand the rest of the script. Everything else in the
/// file (shebang, exports, cd, comments, line breaks) is left byte-for-byte
/// untouched - only the matched argument values are substituted in place.
///
/// This is intentionally narrow rather than a general bash parser: it only
/// needs to round-trip the specific flags this app's Form view exposes.
/// </summary>
public static class LaunchLineParser
{
    public static string? GetQuotedValue(string content, string flag)
    {
        var match = Regex.Match(content, $@"-{flag}\s+""([^""]*)""");
        return match.Success ? match.Groups[1].Value : GetBareValue(content, flag);
    }

    public static string? GetBareValue(string content, string flag)
    {
        var match = Regex.Match(content, $@"-{flag}\s+(\S+)");
        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }

    public static string SetQuotedValue(string content, string flag, string newValue)
    {
        // Strip embedded double quotes from the new value - they'd break the
        // shell string this gets substituted into.
        var safeValue = newValue.Replace("\"", "");

        var pattern = $@"(-{flag}\s+)(""[^""]*""|\S+)";
        if (Regex.IsMatch(content, pattern))
        {
            return Regex.Replace(content, pattern, m => $"{m.Groups[1].Value}\"{safeValue}\"");
        }

        // Flag wasn't present at all - this app only edits scripts that
        // already have all of these flags (confirmed against the real
        // start_server.sh), so this is a no-op rather than guessing where
        // to insert a brand-new flag.
        return content;
    }

    public static string SetBareValue(string content, string flag, string newValue)
    {
        var safeValue = Regex.Replace(newValue, @"\s+", "");
        var pattern = $@"(-{flag}\s+)(""[^""]*""|\S+)";

        return Regex.IsMatch(content, pattern)
            ? Regex.Replace(content, pattern, m => $"{m.Groups[1].Value}{safeValue}")
            : content;
    }

    public static bool GetCrossplayEnabled(string content) =>
        Regex.IsMatch(content, @"(?<![\w-])-crossplay(?![\w-])");

    /// <summary>
    /// Adds or removes the bare "-crossplay" flag. When adding, it's tacked
    /// onto the same line as -public (avoids having to manage a new
    /// backslash-continued line in scripts that format each argument on its
    /// own line) - functionally identical to bash, which doesn't care about
    /// line breaks between whitespace-separated arguments.
    /// </summary>
    public static string SetCrossplayEnabled(string content, bool enabled)
    {
        var currentlyEnabled = GetCrossplayEnabled(content);
        if (enabled == currentlyEnabled) return content;

        if (enabled)
        {
            var publicPattern = @"(-public\s+\S+)";
            return Regex.IsMatch(content, publicPattern)
                ? Regex.Replace(content, publicPattern, "$1 -crossplay")
                : content + " -crossplay";
        }

        return Regex.Replace(content, @"\s*-crossplay(?![\w-])", "");
    }

    // ------------------------------------------------------------------
    // World modifiers (-preset, -modifier, -setkey) - Valheim only reads
    // these when a world is FIRST created; they're no-ops on an existing
    // world's subsequent restarts.
    // ------------------------------------------------------------------

    public static string? GetPreset(string content)
    {
        var match = Regex.Match(content, @"-preset\s+(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Pass null/empty to remove the -preset flag entirely.</summary>
    public static string SetPreset(string content, string? preset)
    {
        var stripped = Regex.Replace(content, @"\s*-preset\s+\S+", "");
        return string.IsNullOrWhiteSpace(preset) ? stripped : AppendFlag(stripped, $"-preset {preset}");
    }

    public static string? GetModifier(string content, string key)
    {
        var match = Regex.Match(content, $@"-modifier\s+{Regex.Escape(key)}\s+(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Pass null/empty to remove this specific "-modifier key ..." pair entirely.</summary>
    public static string SetModifier(string content, string key, string? value)
    {
        var stripped = Regex.Replace(content, $@"\s*-modifier\s+{Regex.Escape(key)}\s+\S+", "");
        return string.IsNullOrWhiteSpace(value) ? stripped : AppendFlag(stripped, $"-modifier {key} {value}");
    }

    public static bool GetSetKey(string content, string key) =>
        Regex.IsMatch(content, $@"-setkey\s+{Regex.Escape(key)}(?![\w-])");

    public static string SetSetKey(string content, string key, bool enabled)
    {
        var currentlyEnabled = GetSetKey(content, key);
        if (enabled == currentlyEnabled) return content;

        return enabled
            ? AppendFlag(content, $"-setkey {key}")
            : Regex.Replace(content, $@"\s*-setkey\s+{Regex.Escape(key)}(?![\w-])", "");
    }

    /// <summary>
    /// Tacks a new flag onto the same line as -public (avoids managing a new
    /// backslash-continued line). Regex.Replace only touches the exact
    /// matched span ("-public X"), so repeated calls correctly stack new
    /// flags one after another without disturbing ones already appended.
    /// </summary>
    private static string AppendFlag(string content, string flag)
    {
        var publicPattern = @"(-public\s+\S+)";
        return Regex.IsMatch(content, publicPattern)
            ? Regex.Replace(content, publicPattern, $"$1 {flag}")
            : content.TrimEnd() + " " + flag;
    }
}
