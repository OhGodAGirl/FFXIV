using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record OutfitPlan(
    Outfit Outfit,
    IReadOnlyList<OutfitPiece> EligibleMissingPieces,
    int OwnedPieceCount,
    int RemainingEligiblePieceCount);

