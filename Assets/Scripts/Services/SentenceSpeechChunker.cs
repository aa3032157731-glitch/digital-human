using System;
using System.Text;

public sealed class SentenceSpeechChunker
{
    private readonly StringBuilder pending = new StringBuilder();
    private long since;
    public bool IsEmpty => pending.Length == 0;
    public void Append(string text, long now)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (pending.Length == 0) since = now;
        if (pending.Length + text.Length > 65536) throw new InvalidOperationException("待合成文字过长。");
        pending.Append(text);
    }
    public string Take(long now, bool complete)
    {
        if (pending.Length == 0) return null;
        int cap = Math.Min(80, pending.Length), count = 0, soft = 0;
        for (int i = 0; i < cap; ++i)
        {
            char c = pending[i];
            bool period = c == '.' && (i + 1 < pending.Length ? char.IsWhiteSpace(pending[i + 1]) : complete || now - since >= 1200);
            if ("。！？；!?;\n".IndexOf(c) >= 0 || period) { count = i + 1; break; }
            if ("，、：,:".IndexOf(c) >= 0 && !(c == ',' && i > 0 && i + 1 < pending.Length && char.IsDigit(pending[i - 1]) && char.IsDigit(pending[i + 1]))) soft = i + 1;
        }
        if (count == 0 && pending.Length >= 40 && soft > 0) count = soft;
        if (count == 0)
        {
            if (!complete && pending.Length < 80 && now - since < 1200) return null;
            count = cap;
            if (count < pending.Length && Word(pending[count - 1]) && Word(pending[count]))
            {
                int boundary = count;
                while (boundary > 0 && Word(pending[boundary - 1])) --boundary;
                if (boundary > 0) count = boundary;
                else { while (count < pending.Length && Word(pending[count]) && count < 400) ++count; }
            }
        }
        if (count < pending.Length && char.IsHighSurrogate(pending[count - 1])) --count;
        if (count == pending.Length && !complete && char.IsHighSurrogate(pending[count - 1])) --count;
        if (count == 0) return null;
        var result = pending.ToString(0, count); pending.Remove(0, count); since = now; return result;
    }
    private static bool Word(char c) => c < 128 && (char.IsLetterOrDigit(c) || c == '\'' || c == '.' || c == ',');
}
