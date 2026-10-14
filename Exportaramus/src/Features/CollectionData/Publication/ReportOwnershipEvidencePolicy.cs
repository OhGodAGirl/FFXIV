using System.Globalization;
using System.Text;
namespace Exportaramus.Features.CollectionData.Publication;

public static class ReportOwnershipEvidencePolicy
{
    public static bool TryNormalize(string? contentId, uint itemId, string? reporter, out ulong character, out string normalized)
    {
        character = 0;
        normalized = string.Empty;
        if (itemId == 0 || string.IsNullOrEmpty(contentId) || contentId[0] == '0' ||
            !ulong.TryParse(contentId, NumberStyles.None, CultureInfo.InvariantCulture, out character) || reporter is null)
            return false;
        try
        {
            normalized = reporter.Trim().Normalize(NormalizationForm.FormC);
            return normalized.Length > 0 && !normalized.Any(char.IsControl) && new UTF8Encoding(false, true).GetByteCount(normalized) <= 64;
        }
        catch (ArgumentException) { normalized = string.Empty; return false; }
    }
}
