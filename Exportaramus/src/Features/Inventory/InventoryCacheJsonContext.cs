using System.Text.Json.Serialization;

namespace Exportaramus.Features.Inventory;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(InventoryCacheDocument))]
internal partial class InventoryCacheJsonContext : JsonSerializerContext;
