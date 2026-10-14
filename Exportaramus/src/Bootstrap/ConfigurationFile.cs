using System.Text;
using Exportaramus.Shared.Serialization;
namespace Exportaramus.Bootstrap;

public static class ConfigurationFile
{
    public static string Serialize(bool reduceMotion, string typeName) => JsonOutput.Text(writer =>
    {
        writer.WriteStartObject(); writer.WriteString("$type", typeName);
        writer.WriteNumber("Version", 1); writer.WriteBoolean("ReduceMotion", reduceMotion); writer.WriteEndObject();
    });
    public static Task WriteAsync(string exactPath, string json, CancellationToken cancellationToken = default)
        => WriteAsync(exactPath, json, File.WriteAllTextAsync, cancellationToken);

    public static async Task WriteAsync(string exactPath, string json,
        Func<string, string, Encoding, CancellationToken, Task> writeTemporary, CancellationToken cancellationToken = default)
    {
        var temporary = exactPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exactPath)!);
            await writeTemporary(temporary, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(exactPath)) File.Replace(temporary, exactPath, null); else File.Move(temporary, exactPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
