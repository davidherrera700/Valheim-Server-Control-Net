namespace ValheimControl.Services;

/// <summary>
/// Parses/builds the simple line-based format Valheim uses for
/// adminlist.txt, permittedlist.txt, and bannedlist.txt: one Steam ID per
/// line, with optional "//"-prefixed comment lines.
///
/// Comment lines are kept separately and always written back at the top -
/// deliberately NOT supporting trailing same-line comments after an ID
/// (e.g. "12345 // David"), since it's not confirmed Valheim's own parser
/// safely ignores trailing content on an ID line, and getting that wrong
/// would silently break admin/whitelist/ban entries.
/// </summary>
public static class ListFileParser
{
    public static (List<string> Comments, List<string> Entries) Parse(string content)
    {
        var comments = new List<string>();
        var entries = new List<string>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("//"))
            {
                comments.Add(line);
            }
            else
            {
                entries.Add(line);
            }
        }

        return (comments, entries);
    }

    public static string Build(List<string> comments, List<string> entries)
    {
        var lines = new List<string>();
        lines.AddRange(comments);

        if (comments.Count > 0 && entries.Count > 0)
        {
            lines.Add("");
        }

        lines.AddRange(entries);

        return lines.Count > 0 ? string.Join("\n", lines) + "\n" : "";
    }
}
