using System;
using System.Collections.Generic;
using System.Globalization;

// -----------------------------------------------------------------------------
// MathExpressionCompiler: the calculator grammar of MathExpressionEvaluator, compiled once into
// a delegate tree over named variables (x, y, z, t, u, v and sliders a, b, c...).
// -----------------------------------------------------------------------------
// Same operators, functions, constants and implicit multiplication as the 2D evaluator
// (so anything typed in the 2D calculator means the same here), plus:
//   - any number of single-letter variables, adjacent without '*' ("xy", "2ab", "u cos(v)")
//   - compiled once, evaluated many times (3D meshes sample thousands of points per frame)
// Pure C# (no UnityEngine) so the 3D math can be unit-tested outside the editor too.
// -----------------------------------------------------------------------------

/// <summary>A parsed expression ready for fast repeated evaluation.</summary>
public sealed class CompiledExpression
{
    readonly Func<float[], float> fn;

    /// <summary>Variable names in slot order; <see cref="Evaluate"/> takes values in the same order.</summary>
    public readonly string[] Variables;

    /// <summary>Which of <see cref="Variables"/> actually appear in the source text.</summary>
    public readonly bool[] Uses;

    public readonly string Source;

    internal CompiledExpression(string source, string[] variables, bool[] uses, Func<float[], float> fn)
    {
        Source = source;
        Variables = variables;
        Uses = uses;
        this.fn = fn;
    }

    public float Evaluate(float[] values) => fn(values);

    public bool UsesVariable(string name)
    {
        int i = Array.IndexOf(Variables, name);
        return i >= 0 && Uses[i];
    }
}

public static class MathExpressionCompiler
{
    /// <param name="variables">Single-letter, lower-case variable names (slot order for evaluation).</param>
    public static bool TryCompile(string expression, string[] variables, out CompiledExpression compiled, out string error)
    {
        compiled = null;
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Empty expression";
            return false;
        }

        try
        {
            var p = new Parser(Normalize(expression), variables);
            var node = p.ParseExpression();
            p.SkipWs();
            if (!p.Eof())
            {
                error = $"Unexpected text: \"{p.PeekRest()}\"";
                return false;
            }

            compiled = new CompiledExpression(expression, variables, p.Uses, node);
            return true;
        }
        catch (ParseException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Typographic minus / times / dot and superscript-free cleanup for pasted text.</summary>
    public static string Normalize(string s)
    {
        if (s == null)
            return "";
        return s.Trim()
            .Replace('−', '-')    // minus sign
            .Replace('×', '*')    // multiplication sign
            .Replace('⋅', '*')    // dot operator
            .Replace("π", "pi")   // π
            .Replace("²", "^2")   // ²
            .Replace("³", "^3");  // ³
    }

    /// <summary>Splits on top-level commas (not inside parentheses), e.g. "(cos(t), sin(t), min(t,1))".</summary>
    public static List<string> SplitTopLevel(string s, char sep = ',')
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(' || c == '[')
                depth++;
            else if (c == ')' || c == ']')
                depth--;
            else if (c == sep && depth == 0)
            {
                parts.Add(s.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        parts.Add(s.Substring(start).Trim());
        return parts;
    }

    sealed class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
    }

    static readonly string[] Fn1Names =
        { "sinh", "cosh", "tanh", "asin", "acos", "atan", "sin", "cos", "tan", "sqrt", "abs", "log", "ln", "exp" };

    sealed class Parser
    {
        readonly string s;
        readonly string[] vars;
        public readonly bool[] Uses;
        int i;

        public Parser(string src, string[] variables)
        {
            s = src;
            vars = variables ?? new string[0];
            Uses = new bool[vars.Length];
        }

        public bool Eof() => i >= s.Length;

        public string PeekRest()
        {
            SkipWs();
            return i < s.Length ? s.Substring(i, Math.Min(24, s.Length - i)) : "";
        }

        public void SkipWs()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        bool Match(char c)
        {
            SkipWs();
            if (i < s.Length && s[i] == c)
            {
                i++;
                return true;
            }

            return false;
        }

        bool MatchTwo(char c)
        {
            SkipWs();
            if (i + 1 < s.Length && s[i] == c && s[i + 1] == c)
            {
                i += 2;
                return true;
            }

            return false;
        }

        /// <summary>Whole-word match (same rule as the 2D evaluator: "sin" not inside "sinx").</summary>
        bool MatchWord(string word)
        {
            SkipWs();
            if (i + word.Length > s.Length)
                return false;
            for (int k = 0; k < word.Length; k++)
            {
                if (char.ToLowerInvariant(s[i + k]) != word[k])
                    return false;
            }

            if (i + word.Length < s.Length && char.IsLetterOrDigit(s[i + word.Length]))
                return false;
            i += word.Length;
            return true;
        }

        /// <summary>Single letter, no word boundary needed (so "xy" is x*y).</summary>
        bool MatchLetter(char letter)
        {
            SkipWs();
            if (i < s.Length && char.ToLowerInvariant(s[i]) == letter)
            {
                i++;
                return true;
            }

            return false;
        }

        public Func<float[], float> ParseExpression() => ParseAddSub();

        Func<float[], float> ParseAddSub()
        {
            var v = ParseMulDiv();
            while (true)
            {
                SkipWs();
                if (Match('+'))
                {
                    var a = v;
                    var b = ParseMulDiv();
                    v = x => a(x) + b(x);
                }
                else if (Match('-'))
                {
                    var a = v;
                    var b = ParseMulDiv();
                    v = x => a(x) - b(x);
                }
                else
                    break;
            }

            return v;
        }

        Func<float[], float> ParseMulDiv()
        {
            var v = ParseUnary();
            while (true)
            {
                SkipWs();
                if (Match('*'))
                {
                    var a = v;
                    var b = ParseUnary();
                    v = x => a(x) * b(x);
                }
                else if (Match('/'))
                {
                    var a = v;
                    var b = ParseUnary();
                    v = x =>
                    {
                        float d = b(x);
                        return Math.Abs(d) < 1e-12f ? float.NaN : a(x) / d;
                    };
                }
                else if (ImplicitMultiplyFollows())
                {
                    var a = v;
                    var b = ParseUnary();
                    v = x => a(x) * b(x);
                }
                else
                    break;
            }

            return v;
        }

        bool ImplicitMultiplyFollows()
        {
            SkipWs();
            if (i >= s.Length)
                return false;
            char c = s[i];
            return char.IsDigit(c) || c == '.' || c == '(' || char.IsLetter(c);
        }

        Func<float[], float> ParseUnary()
        {
            SkipWs();
            if (Match('+'))
                return ParseUnary();
            if (Match('-'))
            {
                var a = ParseUnary();
                return x => -a(x);
            }

            return ParsePow();
        }

        Func<float[], float> ParsePow()
        {
            var b = ParsePrimary();
            SkipWs();
            if (Match('^') || MatchTwo('*'))
            {
                var e = ParsePow(); // right-associative
                return x => Pow(b(x), e(x));
            }

            return b;
        }

        static float Pow(float b, float e)
        {
            if (Math.Abs(e - (float)Math.Round(e)) < 1e-5f && e > -30f && e < 30f)
            {
                int n = (int)Math.Round(e);
                if (n == 0)
                    return 1f;
                float r = 1f;
                int an = Math.Abs(n);
                for (int k = 0; k < an; k++)
                    r *= b;
                return n < 0 ? 1f / r : r;
            }

            return (float)Math.Pow(b, e);
        }

        Func<float[], float> ParsePrimary()
        {
            SkipWs();
            if (i >= s.Length)
                throw new ParseException("Unexpected end of expression");

            if (Match('('))
            {
                var v = ParseExpression();
                if (!Match(')'))
                    throw new ParseException("Missing ')'");
                return v;
            }

            if (char.IsDigit(s[i]) || (s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                float n = ReadNumber();
                return _ => n;
            }

            return ParseIdentOrCall();
        }

        float ReadNumber()
        {
            int start = i;
            bool dot = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsDigit(c))
                    i++;
                else if (c == '.' && !dot)
                {
                    dot = true;
                    i++;
                }
                else
                    break;
            }

            if (start == i)
                throw new ParseException("Expected number");
            if (!float.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                throw new ParseException("Bad number");
            return v;
        }

        Func<float[], float> ParseIdentOrCall()
        {
            foreach (var name in Fn1Names)
            {
                if (!MatchWord(name))
                    continue;
                var a = ExpectFn1Arg();
                switch (name)
                {
                    case "sinh": return x => (float)Math.Sinh(Clamp(a(x), 4f));
                    case "cosh": return x => (float)Math.Cosh(Clamp(a(x), 8f));
                    case "tanh": return x => (float)Math.Tanh(Clamp(a(x), 8f));
                    case "asin": return x => (float)Math.Asin(a(x));
                    case "acos": return x => (float)Math.Acos(a(x));
                    case "atan": return x => (float)Math.Atan(a(x));
                    case "sin": return x => (float)Math.Sin(a(x));
                    case "cos": return x => (float)Math.Cos(a(x));
                    case "tan": return x => (float)Math.Tan(a(x));
                    case "sqrt": return x => (float)Math.Sqrt(a(x));
                    case "abs": return x => Math.Abs(a(x));
                    case "log":
                        return x =>
                        {
                            float q = a(x);
                            return q <= 0f ? float.NaN : (float)Math.Log10(q);
                        };
                    case "ln":
                        return x =>
                        {
                            float q = a(x);
                            return q <= 0f ? float.NaN : (float)Math.Log(q);
                        };
                    case "exp": return x => (float)Math.Exp(Clamp(a(x), 20f));
                }
            }

            if (MatchWord("min"))
            {
                ExpectFn2Args(out var a, out var b);
                return x => Math.Min(a(x), b(x));
            }

            if (MatchWord("max"))
            {
                ExpectFn2Args(out var a, out var b);
                return x => Math.Max(a(x), b(x));
            }

            if (MatchWord("pi"))
                return _ => (float)Math.PI;

            for (int k = 0; k < vars.Length; k++)
            {
                if (vars[k].Length == 1 ? MatchLetter(vars[k][0]) : MatchWord(vars[k]))
                {
                    Uses[k] = true;
                    int slot = k;
                    return x => x[slot];
                }
            }

            if (MatchLetter('e'))
                return _ => (float)Math.E;

            throw new ParseException($"Unknown symbol near \"{PeekRest()}\"");
        }

        static float Clamp(float v, float lim) => v < -lim ? -lim : v > lim ? lim : v;

        Func<float[], float> ExpectFn1Arg()
        {
            if (!Match('('))
                throw new ParseException("Expected '(' after function");
            var v = ParseExpression();
            if (!Match(')'))
                throw new ParseException("Missing ')'");
            return v;
        }

        void ExpectFn2Args(out Func<float[], float> a, out Func<float[], float> b)
        {
            if (!Match('('))
                throw new ParseException("Expected '(' after function");
            a = ParseExpression();
            if (!Match(','))
                throw new ParseException("Expected ','");
            b = ParseExpression();
            if (!Match(')'))
                throw new ParseException("Missing ')'");
        }
    }
}
