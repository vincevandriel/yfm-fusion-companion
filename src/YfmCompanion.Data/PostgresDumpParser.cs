using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YfmCompanion.Data;

internal sealed record InsertBatch(string Table, IReadOnlyList<string> Columns, IReadOnlyList<object?>[] Rows);

internal static partial class PostgresDumpParser
{
    [GeneratedRegex(@"INSERT\s+INTO\s+(?<table>[a-z_][a-z0-9_]*)\s*\((?<columns>[^)]*)\)\s*VALUES", RegexOptions.IgnoreCase)]
    private static partial Regex InsertHeaderRegex();

    public static IEnumerable<InsertBatch> Parse(string sql, ISet<string> includedTables)
    {
        var searchSurface = BuildSearchSurface(sql);
        foreach (Match match in InsertHeaderRegex().Matches(searchSurface))
        {
            var table = match.Groups["table"].Value.ToLowerInvariant();
            if (!includedTables.Contains(table))
            {
                continue;
            }

            var statementEnd = FindStatementEnd(sql, match.Index + match.Length);
            var values = sql.AsSpan(match.Index + match.Length, statementEnd - match.Index - match.Length);
            var columns = match.Groups["columns"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            yield return new InsertBatch(table, columns, ParseRows(values));
        }
    }

    private static string BuildSearchSurface(string sql)
    {
        var surface = sql.ToCharArray();
        var index = 0;
        while (index < sql.Length)
        {
            if (sql[index] == '\'')
            {
                MaskSingleQuotedString(sql, surface, ref index);
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '-' && sql[index + 1] == '-')
            {
                var start = index;
                index += 2;
                while (index < sql.Length && sql[index] is not '\r' and not '\n')
                {
                    index++;
                }

                Mask(surface, start, index);
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                MaskBlockComment(sql, surface, ref index);
                continue;
            }

            if (sql[index] == '$' && TryReadDollarTag(sql, index, out var tag))
            {
                var end = sql.IndexOf(tag, index + tag.Length, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new InvalidDataException($"Unterminated PostgreSQL dollar quote at offset {index}.");
                }

                var start = index;
                index = end + tag.Length;
                Mask(surface, start, index);
                continue;
            }

            index++;
        }

        return new string(surface);
    }

    private static void MaskSingleQuotedString(string sql, char[] surface, ref int index)
    {
        var start = index++;
        while (index < sql.Length)
        {
            if (sql[index++] != '\'')
            {
                continue;
            }

            if (index < sql.Length && sql[index] == '\'')
            {
                index++;
                continue;
            }

            Mask(surface, start, index);
            return;
        }

        throw new InvalidDataException($"Unterminated SQL string at offset {start}.");
    }

    private static void MaskBlockComment(string sql, char[] surface, ref int index)
    {
        var start = index;
        var depth = 1;
        index += 2;
        while (index < sql.Length && depth > 0)
        {
            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                depth++;
                index += 2;
            }
            else if (index + 1 < sql.Length && sql[index] == '*' && sql[index + 1] == '/')
            {
                depth--;
                index += 2;
            }
            else
            {
                index++;
            }
        }

        if (depth != 0)
        {
            throw new InvalidDataException($"Unterminated block comment at offset {start}.");
        }

        Mask(surface, start, index);
    }

    private static bool TryReadDollarTag(string sql, int start, out string tag)
    {
        var end = sql.IndexOf('$', start + 1);
        if (end < 0)
        {
            tag = string.Empty;
            return false;
        }

        var name = sql.AsSpan(start + 1, end - start - 1);
        foreach (var character in name)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                tag = string.Empty;
                return false;
            }
        }

        tag = sql[start..(end + 1)];
        return true;
    }

    private static void Mask(char[] surface, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (surface[index] is not '\r' and not '\n')
            {
                surface[index] = ' ';
            }
        }
    }

    private static int FindStatementEnd(string sql, int start)
    {
        var inString = false;
        for (var i = start; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                if (inString && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inString = !inString;
            }
            else if (sql[i] == ';' && !inString)
            {
                return i;
            }
        }

        throw new InvalidDataException("Unterminated INSERT statement.");
    }

    private static IReadOnlyList<object?>[] ParseRows(ReadOnlySpan<char> input)
    {
        var rows = new List<IReadOnlyList<object?>>();
        var index = 0;
        while (true)
        {
            SkipWhitespaceAndCommas(input, ref index);
            if (index >= input.Length)
            {
                break;
            }

            Expect(input, ref index, '(');
            var row = new List<object?>();
            while (true)
            {
                SkipWhitespace(input, ref index);
                row.Add(ParseScalar(input, ref index));
                SkipWhitespace(input, ref index);
                if (index >= input.Length)
                {
                    throw new InvalidDataException("Unterminated VALUES row.");
                }

                if (input[index] == ')')
                {
                    index++;
                    break;
                }

                Expect(input, ref index, ',');
            }

            rows.Add(row);
        }

        return [.. rows];
    }

    private static object? ParseScalar(ReadOnlySpan<char> input, ref int index)
    {
        if (input[index] == '\'')
        {
            return ParseString(input, ref index);
        }

        var start = index;
        while (index < input.Length && input[index] != ',' && input[index] != ')')
        {
            index++;
        }

        var token = input[start..index].Trim().ToString();
        if (token.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (token.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (token.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        throw new InvalidDataException($"Unsupported SQL scalar: {token}");
    }

    private static string ParseString(ReadOnlySpan<char> input, ref int index)
    {
        Expect(input, ref index, '\'');
        var builder = new StringBuilder();
        while (index < input.Length)
        {
            var value = input[index++];
            if (value != '\'')
            {
                builder.Append(value);
                continue;
            }

            if (index < input.Length && input[index] == '\'')
            {
                builder.Append('\'');
                index++;
                continue;
            }

            return builder.ToString();
        }

        throw new InvalidDataException("Unterminated SQL string.");
    }

    private static void SkipWhitespace(ReadOnlySpan<char> input, ref int index)
    {
        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }
    }

    private static void SkipWhitespaceAndCommas(ReadOnlySpan<char> input, ref int index)
    {
        while (index < input.Length && (char.IsWhiteSpace(input[index]) || input[index] == ','))
        {
            index++;
        }
    }

    private static void Expect(ReadOnlySpan<char> input, ref int index, char expected)
    {
        if (index >= input.Length || input[index] != expected)
        {
            throw new InvalidDataException($"Expected '{expected}' at VALUES offset {index}.");
        }

        index++;
    }
}
