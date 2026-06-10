using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SpeedTranslate.Linux.Rendering;

public static class MarkdownMathRenderer
{
    private static readonly Dictionary<string, string> Commands = new()
    {
        ["alpha"] = "α",
        ["beta"] = "β",
        ["gamma"] = "γ",
        ["delta"] = "δ",
        ["epsilon"] = "ε",
        ["theta"] = "θ",
        ["lambda"] = "λ",
        ["mu"] = "μ",
        ["pi"] = "π",
        ["sigma"] = "σ",
        ["phi"] = "φ",
        ["omega"] = "ω",
        ["Delta"] = "Δ",
        ["Theta"] = "Θ",
        ["Lambda"] = "Λ",
        ["Pi"] = "Π",
        ["Sigma"] = "Σ",
        ["Omega"] = "Ω",
        ["times"] = "×",
        ["cdot"] = "·",
        ["pm"] = "±",
        ["mp"] = "∓",
        ["le"] = "≤",
        ["leq"] = "≤",
        ["ge"] = "≥",
        ["geq"] = "≥",
        ["neq"] = "≠",
        ["approx"] = "≈",
        ["infty"] = "∞",
        ["to"] = "→",
        ["rightarrow"] = "→",
        ["leftarrow"] = "←",
        ["sum"] = "∑",
        ["prod"] = "∏",
        ["int"] = "∫",
    };

    private static readonly Dictionary<char, char> Superscript = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
        ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
        ['n'] = 'ⁿ', ['i'] = 'ⁱ',
    };

    private static readonly Dictionary<char, char> Subscript = new()
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
        ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ', ['j'] = 'ⱼ',
        ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ', ['o'] = 'ₒ',
        ['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ',
        ['v'] = 'ᵥ', ['x'] = 'ₓ',
    };

    public static string ToDisplayText(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return "";

        var text = StripMathDelimiters(latex.Trim());
        text = text
            .Replace(@"\left", "")
            .Replace(@"\right", "")
            .Replace(@"\,", " ")
            .Replace(@"\;", " ")
            .Replace(@"\:", " ");

        // Run a few passes so simple nested constructs such as \frac{x_1}{y^2} settle.
        for (var i = 0; i < 4; i++)
        {
            text = Regex.Replace(
                text,
                @"\\frac\s*\{([^{}]+)\}\s*\{([^{}]+)\}",
                m => $"{ToDisplayText(m.Groups[1].Value)}/{ToDisplayText(m.Groups[2].Value)}");
            text = Regex.Replace(
                text,
                @"\\sqrt\s*\{([^{}]+)\}",
                m => $"√({ToDisplayText(m.Groups[1].Value)})");
        }

        text = Regex.Replace(text, @"\^\{([^{}]+)\}", m => ToScript(m.Groups[1].Value, Superscript, "^"));
        text = Regex.Replace(text, @"_\{([^{}]+)\}", m => ToScript(m.Groups[1].Value, Subscript, "_"));
        text = Regex.Replace(text, @"\^([A-Za-z0-9+\-=()])", m => ToScript(m.Groups[1].Value, Superscript, "^"));
        text = Regex.Replace(text, @"_([A-Za-z0-9+\-=()])", m => ToScript(m.Groups[1].Value, Subscript, "_"));

        text = Regex.Replace(text, @"\\([A-Za-z]+)", m =>
            Commands.TryGetValue(m.Groups[1].Value, out var symbol) ? symbol : m.Groups[1].Value);

        text = text
            .Replace(@"\{", "{")
            .Replace(@"\}", "}")
            .Replace(@"\ ", " ");

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string StripMathDelimiters(string text)
    {
        if (text.StartsWith("$$") && text.EndsWith("$$") && text.Length >= 4)
            return text[2..^2].Trim();
        if (text.StartsWith("$") && text.EndsWith("$") && text.Length >= 2)
            return text[1..^1].Trim();
        if (text.StartsWith(@"\(") && text.EndsWith(@"\)") && text.Length >= 4)
            return text[2..^2].Trim();
        if (text.StartsWith(@"\[") && text.EndsWith(@"\]") && text.Length >= 4)
            return text[2..^2].Trim();
        return text;
    }

    private static string ToScript(string value, IReadOnlyDictionary<char, char> map, string fallbackPrefix)
    {
        value = ToDisplayText(value);
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            if (!map.TryGetValue(value[i], out chars[i]))
                return $"{fallbackPrefix}({value})";
        }
        return new string(chars);
    }
}
