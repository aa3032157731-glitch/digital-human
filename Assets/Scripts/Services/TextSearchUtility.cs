using System;
using System.Collections.Generic;
using System.Text;

public static class TextSearchUtility
{
    public static List<string> ExtractTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var word = new StringBuilder();
        char previousCjk = '\0';

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (IsAsciiWordChar(ch))
            {
                word.Append(char.ToLowerInvariant(ch));
                previousCjk = '\0';
                continue;
            }

            FlushWord(word, tokens);

            if (IsCjk(ch))
            {
                tokens.Add(ch.ToString());
                if (previousCjk != '\0')
                {
                    tokens.Add(new string(new[] { previousCjk, ch }));
                }

                previousCjk = ch;
            }
            else
            {
                previousCjk = '\0';
            }
        }

        FlushWord(word, tokens);
        return new List<string>(tokens);
    }

    public static string NormalizeForSearch(string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : text.ToLowerInvariant();
    }

    public static float ScoreText(string normalizedText, IReadOnlyList<string> queryTokens)
    {
        if (string.IsNullOrEmpty(normalizedText) || queryTokens == null || queryTokens.Count == 0)
        {
            return 0f;
        }

        var score = 0f;
        foreach (var token in queryTokens)
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (normalizedText.IndexOf(token.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
            {
                score += token.Length > 1 ? 3f : 1f;
            }
        }

        return score;
    }

    public static string CompactWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasWhitespace)
                {
                    builder.Append(' ');
                    lastWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static void FlushWord(StringBuilder word, HashSet<string> tokens)
    {
        if (word.Length >= 2)
        {
            tokens.Add(word.ToString());
        }

        word.Length = 0;
    }

    private static bool IsAsciiWordChar(char ch)
    {
        return ch >= '0' && ch <= '9'
            || ch >= 'a' && ch <= 'z'
            || ch >= 'A' && ch <= 'Z'
            || ch == '_' || ch == '-';
    }

    private static bool IsCjk(char ch)
    {
        return ch >= 0x4e00 && ch <= 0x9fff
            || ch >= 0x3400 && ch <= 0x4dbf
            || ch >= 0xf900 && ch <= 0xfaff;
    }
}
