using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
namespace Exportaramus.Shared.Serialization;

public static class JsonOutput
{
    public static byte[] Build(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        Write(stream, write);
        return stream.ToArray();
    }
    public static string Text(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        Write(stream, write);
        return Encoding.UTF8.GetString(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
    public static string Fingerprint(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        Write(stream, write);
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
    private static void Write(MemoryStream stream, Action<Utf8JsonWriter> write)
    {
        using var writer = new Utf8JsonWriter(stream);
        write(writer);
        writer.Flush();
    }
    public static void Timestamp(Utf8JsonWriter writer, string property, DateTimeOffset value) => writer.WriteString(property, value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}
