namespace BetterGlamorPlates.Mutation;

/// <summary>
/// Test-only instrumentation probe proving mutated target bytes reach the test
/// process. This file is never compiled into the shipped plugin assembly.
/// </summary>
internal static class MutationProbe
{
    internal static bool IsPositive(int value) => value > 0;
}
