using System.Collections.Immutable;

namespace Exportaramus.Features.Diagnostics;

public sealed record DiagnosticProhibitedData(bool CharacterIdentity = false, bool CollectionOrItemData = false,
    bool RetainerIdentity = false, bool Chat = false, bool NativeAddresses = false, bool ProcessInputs = false,
    bool FullPaths = false, bool PersonalIdentifiers = false, bool Secrets = false, bool Clipboard = false);
