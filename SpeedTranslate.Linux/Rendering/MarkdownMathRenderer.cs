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
        ["nu"] = "ν",
        ["rho"] = "ρ",
        ["pi"] = "π",
        ["sigma"] = "σ",
        ["tau"] = "τ",
        ["phi"] = "φ",
        ["psi"] = "ψ",
        ["omega"] = "ω",
        ["Gamma"] = "Γ",
        ["Delta"] = "Δ",
        ["Theta"] = "Θ",
        ["Lambda"] = "Λ",
        ["Xi"] = "Ξ",
        ["Pi"] = "Π",
        ["Sigma"] = "Σ",
        ["Phi"] = "Φ",
        ["Psi"] = "Ψ",
        ["Omega"] = "Ω",
        ["times"] = "×",
        ["cdot"] = "·",
        ["div"] = "÷",
        ["pm"] = "±",
        ["mp"] = "∓",
        ["le"] = "≤",
        ["leq"] = "≤",
        ["ge"] = "≥",
        ["geq"] = "≥",
        ["neq"] = "≠",
        ["approx"] = "≈",
        ["infty"] = "∞",
        ["in"] = "∈",
        ["notin"] = "∉",
        ["subset"] = "⊂",
        ["subseteq"] = "⊆",
        ["supset"] = "⊃",
        ["supseteq"] = "⊇",
        ["cup"] = "∪",
        ["cap"] = "∩",
        ["forall"] = "∀",
        ["exists"] = "∃",
        ["partial"] = "∂",
        ["nabla"] = "∇",
        ["neg"] = "¬",
        ["land"] = "∧",
        ["lor"] = "∨",
        ["to"] = "→",
        ["rightarrow"] = "→",
        ["leftarrow"] = "←",
        ["Rightarrow"] = "⇒",
        ["Leftarrow"] = "⇐",
        ["Leftrightarrow"] = "⇔",
        ["sum"] = "∑",
        ["prod"] = "∏",
        ["int"] = "∫",
        ["ldots"] = "…",
        ["dots"] = "…",
        ["log"] = "log",
        ["ln"] = "ln",
        ["sin"] = "sin",
        ["cos"] = "cos",
        ["tan"] = "tan",
        ["exp"] = "exp",
        ["min"] = "min",
        ["max"] = "max",
        ["argmin"] = "argmin",
        ["argmax"] = "argmax",
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

        var text = StripMathDelimiters(NormalizeLatexEscapes(latex.Trim()));
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
                @"\\(?:mathrm|mathbf|mathit|text|operatorname|mathbb|mathcal)\s*\{([^{}]+)\}",
                m => ToDisplayText(m.Groups[1].Value));
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

    private static string NormalizeLatexEscapes(string text) =>
        // Some providers return Markdown-visible math as \\(...\\) or \\frac.
        // Collapse those doubled slashes before delimiter stripping and command lookup.
        text.Replace(@"\\", @"\");

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
