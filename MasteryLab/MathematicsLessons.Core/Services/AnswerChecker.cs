namespace MathematicsLessons.Services;

/// <summary>
/// Grades short student answers (lesson form + practice page).
/// </summary>
public static class AnswerChecker
{
    private const string PracticeSentinel = "practice";

    public static bool IsCorrect(string? solution, string? given)
    {
        var expected = Normalize(solution);
        var actual = Normalize(given);

        if (expected.Length == 0) return false;
        if (expected == PracticeSentinel) return true;

        if (actual == expected) return true;
        if (actual.Contains(expected, StringComparison.Ordinal)) return true;

        // yes / да / true
        if (IsYes(expected) && IsYes(actual)) return true;

        // fractions & decimals: 1/2 == 0.5
        if (TryNumber(expected, out var e) && TryNumber(actual, out var a))
            return Math.Abs(e - a) < 0.0001;

        // alternates in solution separated by |
        foreach (var part in expected.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = Normalize(part);
            if (p.Length == 0) continue;
            if (actual == p || actual.Contains(p, StringComparison.Ordinal)) return true;
            if (IsYes(p) && IsYes(actual)) return true;
            if (TryNumber(p, out var pe) && TryNumber(actual, out var pa) && Math.Abs(pe - pa) < 0.0001)
                return true;
        }

        return false;
    }

    private static bool IsYes(string s) =>
        s is "yes" or "y" or "да" or "da" or "true" or "1" or "ok";

    private static string Normalize(string? value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        s = s.Replace(" ", "").Replace(",", ".");
        return s;
    }

    private static bool TryNumber(string s, out double n)
    {
        n = 0;
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Contains('/'))
        {
            var parts = s.Split('/');
            if (parts.Length == 2
                && double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var a)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var b)
                && Math.Abs(b) > 1e-12)
            {
                n = a / b;
                return true;
            }
            return false;
        }
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out n);
    }
}
