using System.Text.Json.Nodes;

namespace Exportaramus.ConsumerKit.Tests;

internal static class ContractFixture
{
    public const string Character = "12345678901234567";
    public const string Session = "7be5461e-8975-4c7a-9f13-4bb33af9c82d";
    public const string OtherSession = "1b8cc216-56dd-4427-ad35-16558ef47490";
    public static readonly DateTimeOffset Now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "valid", name));

    public static string Edit(string json, Action<JsonObject> edit)
    {
        var value = JsonNode.Parse(json)!.AsObject();
        edit(value);
        return value.ToJsonString();
    }
}
