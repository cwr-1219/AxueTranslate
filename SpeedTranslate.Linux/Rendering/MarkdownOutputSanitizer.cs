using System.Text.RegularExpressions;

namespace SpeedTranslate.Linux.Rendering;

public static class MarkdownOutputSanitizer
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    public static string RemoveColorTags(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var previous = "";
        var current = text;
        while (previous != current)
        {
            previous = current;
            current = Regex.Replace(
                current,
                @"<(?<name>hl|mark)\s+type=['""](?<type>[A-Za-z]+)['""]>(?<body>.*?)</\k<name>>",
                "${body}",
                Options);
            current = Regex.Replace(
                current,
                @"<(?<type>key|term|accent|warn|warning|ok|success|note|info|emphasis)>(?<body>.*?)</\k<type>>",
                "${body}",
                Options);
        }

        return current;
    }
}
