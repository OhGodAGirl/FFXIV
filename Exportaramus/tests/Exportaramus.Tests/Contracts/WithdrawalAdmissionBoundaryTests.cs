using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1;

namespace Exportaramus.Tests.Contracts;

public sealed class WithdrawalAdmissionBoundaryTests
{
    [Fact]
    public void InvalidUtf16RetainsItsFailureCodeEvenWhenReplacementWouldExceedBudget()
    {
        var payload = new string('\u00e9', 2097152) + '\ud800';
        var result = CollectionDataSemanticValidator.ValidateSnapshot(payload, "1");
        Assert.False(result.Success);
        Assert.Equal(CollectionDataValidationCode.InvalidJson, result.Code);
    }
}
