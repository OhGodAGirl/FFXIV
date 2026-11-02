using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed record CollectionDataRefreshCompletion(
    ulong ContentId,
    CollectionDataParseResult ParseResult,
    CollectionDataSnapshot? LatestSnapshot,
    Exception? Error);
