using System.Collections.Immutable;
using System.Text;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Main;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Diagnostics;

public sealed class DiagnosticSamplingTests
{
    private static DiagnosticUiObservation Observed => new(-25, 30, 760, 560, 740, 1.5, true, 8, 31, true, 42);

    [Fact]
    public void ObservedGeometryAndReadinessReplaceDefaultsWithoutMakingUnobservedSectionsComplete()
    {
        var ui = Assert.IsType<DiagnosticUi>(DiagnosticSamplingPolicy.PrepareUi(Observed));
        Assert.Equal(new(-25, 30), ui.WindowPosition); Assert.Equal(new(760, 560), ui.WindowSize);
        Assert.Equal(740, ui.ContentWidth); Assert.Equal(1.5, ui.UiScale); Assert.True(ui.WideActions);
        Assert.Equal(3, ui.CollectionColumnCount); Assert.True(ui.FontReady); Assert.Equal(8, ui.PushedStyleVarCount);
        Assert.Equal(31, ui.PushedColorCount); Assert.True(ui.ReduceMotion); Assert.Equal(42, ui.ViewStateRevision);
        var input = MainSnapshotPresenter.DiagnosticInput(null, "0.1.0.0", "10.0.0", Guid.Empty, false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);
        var result = DiagnosticRuntimeEnricher.Apply(input, new(), new(), new(), new(), ImmutableDictionary<string, double>.Empty, false, ui);
        Assert.Same(ui, result.Ui); Assert.DoesNotContain("ui", result.IncompleteSections); Assert.Contains("runtime", result.IncompleteSections);
        DiagnosticReportSerializer.Prepare(DiagnosticReportPreparer.Prepare(result, SnapshotFixture.Now));
        Assert.Null(DiagnosticSamplingPolicy.PrepareUi(null));
    }

    [Fact]
    public void MeasuredFalseAndSchemaBoundaryValuesArePreserved()
    {
        var value = Observed with
        {
            X = -100000,
            Y = 100000,
            Width = 100000,
            Height = 100000,
            ContentWidth = 0,
            Scale = 16,
            FontReady = false,
            StyleVariables = 64,
            Colors = 128,
            ReduceMotion = false,
            Revision = 9007199254740991
        };
        var ui = Assert.IsType<DiagnosticUi>(DiagnosticSamplingPolicy.PrepareUi(value));
        Assert.False(ui.FontReady); Assert.False(ui.ReduceMotion); Assert.False(ui.WideActions); Assert.Equal(1, ui.CollectionColumnCount);
        Assert.Equal(-100000, ui.WindowPosition.X); Assert.Equal(100000, ui.WindowPosition.Y); Assert.Equal(100000, ui.WindowSize.X);
        Assert.Equal(100000, ui.WindowSize.Y); Assert.Equal(0, ui.ContentWidth); Assert.Equal(16, ui.UiScale);
        Assert.Equal(64, ui.PushedStyleVarCount); Assert.Equal(128, ui.PushedColorCount); Assert.Equal(9007199254740991, ui.ViewStateRevision);
        Assert.NotNull(DiagnosticSamplingPolicy.PrepareUi(Observed with { X = 100000, Y = -100000, ContentWidth = 3600000, StyleVariables = 0, Colors = 0, Revision = 0 }));
    }

    [Theory]
    [MemberData(nameof(InvalidObservations))]
    public void InvalidObservationKeepsUiUnavailable(DiagnosticUiObservation value) => Assert.Null(DiagnosticSamplingPolicy.PrepareUi(value));

    public static IEnumerable<object[]> InvalidObservations()
    {
        DiagnosticUiObservation[] values = [Observed with { X = double.NaN }, Observed with { X = -100001 }, Observed with { X = 100001 },
            Observed with { Y = double.PositiveInfinity }, Observed with { Y = -100001 }, Observed with { Y = 100001 },
            Observed with { Width = double.NaN }, Observed with { Width = 0 }, Observed with { Width = 100001 },
            Observed with { Height = double.NaN }, Observed with { Height = 0 }, Observed with { Height = 100001 },
            Observed with { ContentWidth = float.NaN }, Observed with { ContentWidth = -1 }, Observed with { ContentWidth = 3600001 },
            Observed with { Scale = double.NaN }, Observed with { Scale = 0 }, Observed with { Scale = 16.01 },
            Observed with { StyleVariables = -1 }, Observed with { StyleVariables = 65 }, Observed with { Colors = -1 }, Observed with { Colors = 129 },
            Observed with { Revision = -1 }, Observed with { Revision = 9007199254740992 }];
        return values.Select(value => new object[] { value });
    }

    [Theory]
    [InlineData("2026.08.11.0000.0000\0", "2026.08.11.0000.0000")]
    [InlineData("2026.08.11.0000.0000\0unread-tail", "2026.08.11.0000.0000")]
    [InlineData("2026.08.11.0000.0000", "Unavailable")]
    [InlineData("\0", "Unavailable")]
    [InlineData("2026.08.11.0000.00000\0", "Unavailable")]
    [InlineData("202608.11.0000.00000\0", "Unavailable")]
    [InlineData("2026x08.11.0000.0000\0", "Unavailable")]
    [InlineData("2026.08x11.0000.0000\0", "Unavailable")]
    [InlineData("2026.08.11x0000.0000\0", "Unavailable")]
    [InlineData("2026.08.11.0000x0000\0", "Unavailable")]
    [InlineData("/026.08.11.0000.0000\0", "Unavailable")]
    [InlineData(":026.08.11.0000.0000\0", "Unavailable")]
    public void OnlyBoundedNullTerminatedNumericClientVersionIsReported(string bytes, string expected) =>
        Assert.Equal(expected, DiagnosticSamplingPolicy.ClientVersion(Encoding.ASCII.GetBytes(bytes)));
}
