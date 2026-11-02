using System.Text;

namespace BetterGlamorPlates.Shared.Ui;

/// <summary>Text conversion and fitting independent of native widgets or fonts.</summary>
internal static class UiText
{
    internal static string Read(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    internal static void Write(byte[] buffer, string value)
    {
        Array.Clear(buffer);
        if (buffer.Length == 0) return;
        // Keep the terminating zero and do not cut a multi-byte character in half.
        var offset = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (!rune.TryEncodeToUtf8(buffer.AsSpan(offset, buffer.Length - offset - 1), out var written))
                break;
            offset += written;
        }
    }

    internal static string Fit(string text, float width, Func<string, float> measure)
    {
        if (measure(text) <= width) return text;
        var length = text.Length;
        while (length > 0 && measure(string.Concat(text.AsSpan(0, length), "…")) > width)
            length--;
        return string.Concat(text.AsSpan(0, length), "…");
    }
}
