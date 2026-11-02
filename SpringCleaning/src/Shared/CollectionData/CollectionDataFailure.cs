#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal enum CollectionDataFailure
{
    None,
    NoCharacter,
    ProviderUnavailable,
    Busy,
    Disposed,
    EmptyPayload,
    PayloadTooLarge,
    MalformedJson,
    UnsupportedProtocol,
    InvalidProvider,
    WrongCharacter,
    InvalidRevision,
    OutOfOrder,
    InvalidTimestamp,
    InvalidSources,
    StaleCompletion,
}
