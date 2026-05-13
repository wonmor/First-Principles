using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Best-effort symbolic derivative string for common <c>f(u)</c> patterns (typed calculator). Falls back to a generic numeric note.
/// </summary>
public static class GraphingCalculatorDerivativeHint
{
    public static string TryFormatDerivativeLine(string rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
            return "";

        string s = rawExpression.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", "");

        if (Regex.IsMatch(s, @"^-?(\d+\.?\d*|\.\d+)$"))
            return "0";

        if (s == "x" || s == "u")
            return "1";

        var pow = Regex.Match(s, @"^(?:x|u)\^(-?\d+)$");
        if (pow.Success && int.TryParse(pow.Groups[1].Value, out int n))
        {
            int d = n - 1;
            if (n == 0)
                return "0";
            if (n == 1)
                return "1";
            if (n == 2)
                return "2u";
            return d == 1 ? $"{n}·u" : $"{n}·u^{d}";
        }

        if (s == "sin(x)" || s == "sin(u)")
            return "cos(u)";
        if (s == "cos(x)" || s == "cos(u)")
            return "-sin(u)";
        if (s == "tan(x)" || s == "tan(u)")
            return "sec²(u)";

        if (s == "exp(x)" || s == "exp(u)" || s == "e^x" || s == "e^u")
            return "exp(u)";

        if (s == "ln(x)" || s == "ln(u)")
            return "1/u";
        if (s == "log(x)" || s == "log(u)")
            return "1/(u·ln 10)";

        if (s == "sqrt(x)" || s == "sqrt(u)")
            return "1/(2·√u)";

        if (s == "1/x" || s == "1/u" || s == "x^-1" || s == "u^-1")
            return "-1/u²";

        return "";
    }
}
