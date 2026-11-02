using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace CollectionShopper.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
public sealed unsafe class NativeWindowDrawClipperTests
{
    [Fact]
    public void ForegroundGameWindowReleasesParentAndScrollableChildrenAndRestoresThem()
    {
        var parent = (ImGuiWindow*)NativeMemory.AllocZeroed((nuint)sizeof(ImGuiWindow));
        var table = (ImGuiWindow*)NativeMemory.AllocZeroed((nuint)sizeof(ImGuiWindow));
        var other = (ImGuiWindow*)NativeMemory.AllocZeroed((nuint)sizeof(ImGuiWindow));
        var children = (ImGuiWindow**)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        try
        {
            parent->Flags = ImGuiWindowFlags.NoSavedSettings;
            table->Flags = ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoMove;
            other->Flags = ImGuiWindowFlags.NoResize;
            children[0] = table;
            *(ImVector*)&parent->DC.ChildWindows = new ImVector(1, 1, children);
            NativeWindowDrawClipper.SetTreeMouseInputBlocked(new(parent), true);
            Assert.True(parent->Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs));
            Assert.True(table->Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs));
            Assert.Equal(ImGuiWindowFlags.NoResize, other->Flags);
            // Table Begin replaces its flags; the current-frame guard must reapply.
            table->Flags = ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoMove;
            NativeWindowDrawClipper.SetMouseInputBlocked(new(table), true);
            Assert.True(table->Flags.HasFlag(ImGuiWindowFlags.NoMouseInputs));
            NativeWindowDrawClipper.SetTreeMouseInputBlocked(new(parent), false);
            Assert.Equal(ImGuiWindowFlags.NoSavedSettings, parent->Flags);
            Assert.Equal(ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoMove, table->Flags);
        }
        finally
        {
            NativeMemory.Free(children);
            NativeMemory.Free(other);
            NativeMemory.Free(table);
            NativeMemory.Free(parent);
        }
    }

    [Fact]
    public void MissingWindowAndDepthLimitDoNotDereferenceChildren()
    {
        NativeWindowDrawClipper.SetMouseInputBlocked(default, true);
        NativeWindowDrawClipper.SetTreeMouseInputBlocked(default, true);
        var window = new ImGuiWindow { Flags = ImGuiWindowFlags.NoResize };
        NativeWindowDrawClipper.SetTreeMouseInputBlocked(new(&window), true, 0);
        Assert.Equal(ImGuiWindowFlags.NoResize, window.Flags);
    }

    [Fact]
    public void ParentAndTableCommandsKeepGeometryButNeverDrawInsideForegroundInventory()
    {
        var original = new ImDrawCmd { ClipRect = new(0, 0, 100, 100), ElemCount = 6, IdxOffset = 18, VtxOffset = 4 };
        using var parent = new DrawList(original);
        using var table = new DrawList(original);
        var clipper = new NativeWindowDrawClipper();
        clipper.BeginFrame();
        clipper.Capture(default);
        clipper.Capture(parent.Pointer);
        clipper.Capture(parent.Pointer);
        clipper.Capture(table.Pointer);
        clipper.FinishFrame([new(new(25, 25), new(75, 75))]);
        foreach (var list in new[] { parent, table })
        {
            Assert.Equal(4, list.Commands.Length);
            var area = 0f;
            foreach (var command in list.Commands)
            {
                Assert.Equal(original.ElemCount, command.ElemCount);
                Assert.Equal(original.IdxOffset, command.IdxOffset);
                Assert.Equal(original.VtxOffset, command.VtxOffset);
                Assert.Equal(original.TextureId, command.TextureId);
                var clip = command.ClipRect;
                var position = new Vector2(clip.X, clip.Y);
                var size = new Vector2(clip.Z - clip.X, clip.W - clip.Y);
                Assert.False(MarketBoardTabPresentation.Overlaps(position, size, new(25, 25), new(50, 50)));
                area += size.X * size.Y;
            }
            Assert.Equal(7_500f, area);
        }
    }

    [Fact]
    public void FullCoverRemovesDrawingButRetainsCallbacksAndEmptyCommands()
    {
        using var list = new DrawList(
            new() { ClipRect = new(0, 0, 100, 100), ElemCount = 6 },
            new() { UserCallback = (void*)1, UserCallbackData = (void*)2 },
            new() { IdxOffset = 6 });
        var clipper = new NativeWindowDrawClipper();
        clipper.Capture(list.Pointer);
        clipper.FinishFrame([new(new(0, 0), new(100, 100))]);
        Assert.Equal(2, list.Commands.Length);
        Assert.Equal((nint)1, (nint)list.Commands[0].UserCallback);
        Assert.Equal((nint)2, (nint)list.Commands[0].UserCallbackData);
        Assert.Equal(6u, list.Commands[1].IdxOffset);
    }

    [Fact]
    public void NoOcclusionAndNextFrameDoNotRewriteUnrelatedLists()
    {
        using var list = new DrawList(new ImDrawCmd { ClipRect = new(0, 0, 100, 100), ElemCount = 6 });
        var clipper = new NativeWindowDrawClipper();
        clipper.Capture(list.Pointer);
        clipper.FinishFrame([]);
        Assert.Single(list.Commands);
        clipper.BeginFrame();
        clipper.FinishFrame([new(new(0, 0), new(100, 100))]);
        Assert.Single(list.Commands);
        Assert.Equal(6u, list.Commands[0].ElemCount);
    }

    [Fact]
    public void SplittingSeveralCommandsPreservesSubmissionOrderAndNonemptyCallbacks()
    {
        var first = new ImDrawCmd { ClipRect = new(0, 0, 100, 100), ElemCount = 6, IdxOffset = 18, VtxOffset = 4 };
        var callback = new ImDrawCmd
        {
            ClipRect = new(0, 0, 100, 100),
            ElemCount = 3,
            UserCallback = (void*)1,
            UserCallbackData = (void*)2,
        };
        var second = new ImDrawCmd { ClipRect = new(0, 0, 100, 100), ElemCount = 12, IdxOffset = 24, VtxOffset = 10 };
        using var list = new DrawList(first, callback, second);
        var clipper = new NativeWindowDrawClipper();
        clipper.Capture(list.Pointer);
        clipper.FinishFrame([new(new(40, 0), new(60, 100))]);

        var commands = list.Commands;
        Assert.Equal(5, commands.Length);
        AssertFragment(commands[0], first, new(0, 0, 40, 100));
        AssertFragment(commands[1], first, new(60, 0, 100, 100));
        Assert.Equal(callback.ClipRect, commands[2].ClipRect);
        Assert.Equal(callback.ElemCount, commands[2].ElemCount);
        Assert.Equal((nint)callback.UserCallback, (nint)commands[2].UserCallback);
        Assert.Equal((nint)callback.UserCallbackData, (nint)commands[2].UserCallbackData);
        AssertFragment(commands[3], second, new(0, 0, 40, 100));
        AssertFragment(commands[4], second, new(60, 0, 100, 100));

        static void AssertFragment(ImDrawCmd actual, ImDrawCmd original, Vector4 expectedClip)
        {
            Assert.Equal(expectedClip, actual.ClipRect);
            Assert.Equal(original.ElemCount, actual.ElemCount);
            Assert.Equal(original.IdxOffset, actual.IdxOffset);
            Assert.Equal(original.VtxOffset, actual.VtxOffset);
            Assert.Equal(original.TextureId, actual.TextureId);
        }
    }

    [Fact]
    public void NewFrameClipsOnlyItsCapturedListAfterPreviousFrameWasFullyCovered()
    {
        var original = new ImDrawCmd { ClipRect = new(0, 0, 100, 100), ElemCount = 6 };
        using var previous = new DrawList(original);
        using var current = new DrawList(original);
        var clipper = new NativeWindowDrawClipper();
        clipper.Capture(previous.Pointer);
        clipper.FinishFrame([new(new(0, 0), new(100, 100))]);
        Assert.Empty(previous.Commands);

        clipper.BeginFrame();
        clipper.Capture(current.Pointer);
        clipper.FinishFrame([new(new(0, 0), new(50, 100))]);
        Assert.Empty(previous.Commands);
        var command = Assert.Single(current.Commands);
        Assert.Equal(new Vector4(50, 0, 100, 100), command.ClipRect);
        Assert.Equal(original.ElemCount, command.ElemCount);
    }

    // Preallocate command storage so tests exercise the real command transformation
    // without a game, renderer, ImGui context, or native allocator invocation.
    private sealed class DrawList : IDisposable
    {
        private readonly ImDrawList* draw;
        private readonly ImDrawCmd* storage;
        internal DrawList(params ImDrawCmd[] commands)
        {
            draw = (ImDrawList*)NativeMemory.AllocZeroed((nuint)sizeof(ImDrawList));
            storage = (ImDrawCmd*)NativeMemory.AllocZeroed(128, (nuint)sizeof(ImDrawCmd));
            *(ImVector*)&draw->CmdBuffer = new ImVector(commands.Length, 128, storage);
            commands.CopyTo(new Span<ImDrawCmd>(storage, commands.Length));
        }
        internal ImDrawListPtr Pointer => new(draw);
        internal ImDrawCmd[] Commands => new Span<ImDrawCmd>(storage, draw->CmdBuffer.Size).ToArray();
        public void Dispose()
        {
            NativeMemory.Free(storage);
            NativeMemory.Free(draw);
        }
    }
}
