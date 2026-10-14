using System.Text.Json.Serialization;

namespace Exportaramus.ConsumerKit.V1_1;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
    GenerationMode = JsonSourceGenerationMode.Metadata, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CollectionDataSnapshot))]
[JsonSerializable(typeof(CollectionDataCapabilities))]
[JsonSerializable(typeof(CollectionDataChangedNotice))]
[JsonSerializable(typeof(CollectionDataWithdrawnNotice))]
public partial class CollectionDataJsonContext : JsonSerializerContext;
