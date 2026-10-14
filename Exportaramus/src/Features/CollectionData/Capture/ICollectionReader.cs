using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Features.CollectionData.Capture;

public interface ICollectionReader
{
    bool IdentityMatches(ulong contentId);
    bool IsAvailable(string key, string version);
    bool ReadUnlock(string key, CatalogItem row);
    QuestState ReadQuest(uint id, bool completed);
    CurrentItemPresenceState ReadPresence(string source, CollectionCatalog catalog, ulong contentId, long generation, DateTimeOffset now);
}
