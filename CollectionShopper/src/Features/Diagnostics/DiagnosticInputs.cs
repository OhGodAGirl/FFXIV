using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CollectionShopper;

internal sealed record DiagnosticInputs(
    int ConfigurationVersion,
    int ExpectedConfigurationVersion,
    bool PlayerLoaded,
    bool CatalogReady,
    bool CollectionLoaded,
    bool CollectionRefreshing,
    bool MarketBoardOpen,
    bool Purchasing,
    bool ProviderSnapshotReady,
    CollectionDataOwnershipState CollectionDataOwnership);

