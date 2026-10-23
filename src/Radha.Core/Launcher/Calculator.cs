using System.Globalization;
using System.Text;

namespace Radha.Core.Launcher;

/// <summary>
/// Arithmetic evaluator for the launcher.
///
/// Hand-written recursive descent rather than a data-table or an external package: the launch path
/// must never send a keystroke to a shell, so this parses to a double and has no side effects at
/// all. Unknown identifiers, unbalanced input and non-finite results are all rejected.
/// </summary>
public static class Calculator
{
    private const double DegPerRad = Math.PI / 180.0;

    /// <summary>True when the text is worth evaluating — keeps ordinary searches off this path.</summary>
    public static bool LooksLikeExpression(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim();
        var hasDigit = text.Any(char.IsDigit);
        var hasOperator = text.Any(c => c is '+' or '-' or '*' or '/' or '%' or '^' or '(' or ')');

        if (!hasDigit && !hasOperator) return false;
        if (text.Length > 200) return false;

        // A bare number is not a calculation the user asked for.
        return hasDigit && hasOperator;
    }

    public static bool TryEvaluate(string? input, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        try
        {
            var parser = new Parser(input);
            var result = parser.ParseExpression();
            parser.EnsureEnd();
            if (double.IsNaN(result) || double.IsInfinity(result)) return false;
            value = result;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Human-facing form: trims float noise without switching to scientific notation early.</summary>
    public static string Format(double value)
    {
        if (Math.Abs(value) >= 1e15 || Math.Abs(value) < 1e-9 && value != 0)
            return value.ToString("G10", CultureInfo.InvariantCulture);

        var rounded = Math.Round(value, 10);
        if (Math.Abs(rounded - Math.Round(rounded)) < 1e-9)
            return Math.Round(rounded).ToString("0", CultureInfo.InvariantCulture);

        return rounded.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _index;

        public Parser(string text) => _text = text;

        private char Current => _index < _text.Length ? _text[_index] : '\0';

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
        }

        private bool Take(char c)
        {
            SkipWhitespace();
            if (Current != c) return false;
            _index++;
            return true;
        }

        private static FormatException Fail(string what) => new(what);

        public void EnsureEnd()
        {
            SkipWhitespace();
            if (_index != _text.Length) throw Fail($"unexpected '{_text[_index]}'");
        }

        public double ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Take('+')) left += ParseTerm();
                else if (Take('-')) left -= ParseTerm();
                else return left;
            }
        }

        private double ParseTerm()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Current == '*' && Peek(1) != '*') { _index++; left *= ParseUnary(); }
                else if (Take('/'))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0) throw Fail("division by zero");
                    left /= divisor;
                }
                else if (Take('%'))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0) throw Fail("division by zero");
                    left %= divisor;
                }
                else return left;
            }
        }

        private char Peek(int offset) => _index + offset < _text.Length ? _text[_index + offset] : '\0';

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Take('-')) return -ParseUnary();
            if (Take('+')) return ParseUnary();
            if (Current == '*') throw Fail("'**' is not supported; use '^'");
            return ParsePower();
        }

        private double ParsePower()
        {
            var left = ParsePrimary();
            SkipWhitespace();
            if (Take('^')) return Math.Pow(left, ParseUnary()); // right-associative
            return left;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (_index >= _text.Length) throw Fail("unexpected end of expression");

            if (Take('('))
            {
                var inner = ParseExpression();
                if (!Take(')')) throw Fail("missing ')'");
                return inner;
            }

            if (char.IsDigit(Current) || Current == '.') return ParseNumber();
            if (char.IsLetter(Current)) return ParseIdentifier();

            throw Fail($"unexpected '{Current}'");
        }

        private double ParseNumber()
        {
            var start = _index;
            while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] == '.')) _index++;

            // exponent
            if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                var save = _index;
                _index++;
                if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;
                if (_index < _text.Length && char.IsDigit(_text[_index]))
                    while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
                else
                    _index = save;
            }

            var slice = _text[start.._index];
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw Fail($"invalid number '{slice}'");
            return value;
        }

        private double ParseIdentifier()
        {
            var start = _index;
            while (_index < _text.Length && (char.IsLetter(_text[_index]) || char.IsDigit(_text[_index]) || _text[_index] == '_')) _index++;
            var name = _text[start.._index].ToLowerInvariant();

            SkipWhitespace();
            var callable = Current == '(';

            if (!callable)
            {
                return name switch
                {
                    "pi" => Math.PI,
                    "tau" => Math.Tau,
                    "e" => Math.E,
                    _ => throw Fail($"unknown name '{name}'")
                };
            }

            _index++; // consume '('
            var args = new List<double>();
            SkipWhitespace();
            if (!Take(')'))
            {
                do { args.Add(ParseExpression()); }
                while (Take(','));
                if (!Take(')')) throw Fail("missing ')'");
            }

            return Apply(name, args);
        }

        private static double Apply(string name, List<double> args)
        {
            double One() => args.Count == 1 ? args[0] : throw Fail($"{name}() takes one argument");
            void Arity(int n) { if (args.Count != n) throw Fail($"{name}() takes {n} arguments"); }

            return name switch
            {
                "sqrt" => Math.Sqrt(One()),
                "cbrt" => Math.Cbrt(One()),
                "abs" => Math.Abs(One()),
                "sin" => Math.Sin(One()),
                "cos" => Math.Cos(One()),
                "tan" => Math.Tan(One()),
                "sind" => Math.Sin(One() * DegPerRad),
                "cosd" => Math.Cos(One() * DegPerRad),
                "asind" => Math.Asin(One()) / DegPerRad,
                "atan" => Math.Atan(One()),
                "log" => Math.Log10(One()),
                "log2" => Math.Log2(One()),
                "ln" => Math.Log(One()),
                "exp" => Math.Exp(One()),
                "floor" => Math.Floor(One()),
                "ceil" => Math.Ceiling(One()),
                "round" => Math.Round(One(), MidpointRounding.AwayFromZero),
                "pow" => Pow(),
                "min" => MinMax(takeMin: true),
                "max" => MinMax(takeMin: false),
                "hypot" => Hypot(),
                _ => throw Fail($"unknown function '{name}'")
            };

            double Pow()
            {
                Arity(2);
                return Math.Pow(args[0], args[1]);
            }

            double Hypot()
            {
                Arity(2);
                return Math.Sqrt(args[0] * args[0] + args[1] * args[1]);
            }

            double MinMax(bool takeMin)
            {
                if (args.Count == 0) throw Fail("needs at least one argument");
                var acc = args[0];
                foreach (var a in args.Skip(1)) acc = takeMin ? Math.Min(acc, a) : Math.Max(acc, a);
                return acc;
            }
        }
    }

    /// <summary>Pretty-print of a rejected expression, used by the launcher's error row.</summary>
    public static string DescribeError(string input)
    {
        var builder = new StringBuilder("not a calculation: ");
        builder.Append(input.Length > 40 ? input[..40] + "…" : input);
        return builder.ToString();
    }
}
