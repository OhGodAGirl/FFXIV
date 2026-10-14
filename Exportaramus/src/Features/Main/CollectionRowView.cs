using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;

namespace Exportaramus.Features.Main;

public sealed record CollectionRowView(string Key, string Label, string Icon, string Count, UiTone CountTone, string Marker, string Delta, UiTone DeltaTone, string Detail = "");
