using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CollectionShopper;

internal static class DiagnosticPolicy
{
    internal static IReadOnlyList<DiagnosticCheck> BuildChecks(DiagnosticInputs inputs)
        =>
        [
            inputs.ConfigurationVersion == inputs.ExpectedConfigurationVersion
                ? new("Configuration", DiagnosticResultKind.Pass,
                    $"Configuration schema {inputs.ConfigurationVersion} is current.")
                : new("Configuration", DiagnosticResultKind.Failure,
                    "The saved configuration schema is not supported by this build.",
                    "Reload the plugin. If the failure remains, copy this report and open an issue."),
            inputs.PlayerLoaded
                ? new("Character session", DiagnosticResultKind.Pass,
                    "A logged-in character session is available.")
                : new("Character session", DiagnosticResultKind.Waiting,
                    "No logged-in character session is available.",
                    "Log in to a character, then run the checks again."),
            inputs.CatalogReady
                ? new("Bundled outfit data", DiagnosticResultKind.Pass,
                    "The bundled outfit definitions loaded successfully.")
                : new("Bundled outfit data", DiagnosticResultKind.NotAvailable,
                    "The bundled outfit definitions have not been observed in the current collection cache.",
                    "Refresh the collection to load the bundled outfit definitions."),
            inputs.CollectionRefreshing
                ? new("Collection data", DiagnosticResultKind.Waiting,
                    "A collection refresh is currently running.",
                    "Wait for Refresh to finish, then run the checks again.")
                : inputs.CollectionLoaded
                    ? new("Collection data", DiagnosticResultKind.Pass,
                        "The current collection list has been loaded.")
                    : new("Collection data", DiagnosticResultKind.Warning,
                        "The collection list has not been loaded in this session.",
                        "Open Settings and select Refresh."),
            inputs.MarketBoardOpen
                ? new("Market Board", DiagnosticResultKind.Pass,
                    "The normal Market Board browsing context is open.")
                : new("Market Board", DiagnosticResultKind.NotAvailable,
                    "The normal Market Board browsing context is not open.",
                    "Open a Market Board before testing shopping controls."),
            inputs.Purchasing
                ? new("Automated purchasing", DiagnosticResultKind.Warning,
                    "An automated purchase run is active. Diagnostics did not change it.",
                    "Use Cancel Automated Purchasing beside the progress bar if you need to stop it.")
                : new("Automated purchasing", DiagnosticResultKind.Pass,
                    "No automated purchase run is active."),
            inputs.ProviderSnapshotReady
                ? new("Exportaramus ownership snapshot", DiagnosticResultKind.Pass,
                    "Exportaramus supplied character-bound ownership from its canonical sources.")
                : new("Exportaramus ownership snapshot", DiagnosticResultKind.NotAvailable,
                    "No accepted Exportaramus ownership snapshot is available yet.",
                    "Keep Exportaramus loaded; ownership arrives automatically when available."),
            CollectionDataOwnershipPolicy.BuildDiagnosticCheck(inputs.CollectionDataOwnership),
            new("FFXIV Collect catalog connectivity", DiagnosticResultKind.NotAvailable,
                "Not tested. Run Checks never contacts the public FFXIV Collect catalog.",
                "Use Refresh when you deliberately want to refresh catalog metadata."),
            new("Universalis connectivity", DiagnosticResultKind.NotAvailable,
                "Not tested. Run Checks never contacts Universalis.",
                "Use Refresh at a Market Board when you deliberately want current price estimates."),
        ];

}

