using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryFileCacheExecutionBoundaryTests : IDisposable
{
    private static readonly string ScratchRoot = Path.Combine(Path.GetTempPath(), "Exportaramus.Tests", "runtime-data");
    private readonly string directory = Path.Combine(ScratchRoot, Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LengthGrowthCannotWrapTheAllocationSizeOrBeginReading()
    {
        var stream = new ChangingLengthStream();
        var cache = new InventoryFileCache(directory, _ => stream);

        await Assert.ThrowsAsync<OverflowException>(() => cache.LoadAsync(1, default));

        Assert.Equal(2, stream.LengthReads);
        Assert.Equal(0, stream.ContentReads);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task AlreadyCancelledSaveDoesNotOpenReadCreateDirectoriesOrCommit()
    {
        var reads = 0;
        var commits = 0;
        var cache = new InventoryFileCache(directory,
            _ =>
            {
                reads++;
                throw new FileNotFoundException("The fixture has no prior inventory cache.");
            },
            (_, _) => commits++);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.False(Directory.Exists(directory));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.SaveAsync(InventoryServiceTests.Saved(1), cancellation.Token));

        Assert.Equal(0, reads);
        Assert.Equal(0, commits);
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(cache.CharacterPath(1)));
    }

    [Fact]
    public void SaveRequestsDiskFlushInTheCompiledWriteStateMachine()
    {
        // Ordinary readback only proves visibility through the OS cache. This is a narrow
        // compiled-code contract for the existing stable-storage request, not a power-loss test.
        var save = typeof(InventoryFileCache).GetMethod(nameof(InventoryFileCache.SaveAsync))!;
        var stateMachine = save.GetCustomAttribute<AsyncStateMachineAttribute>();
        Assert.NotNull(stateMachine);
        var moveNext = stateMachine.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var instructions = Decode(moveNext);
        var flush = typeof(FileStream).GetMethod(nameof(FileStream.Flush), [typeof(bool)])!;
        var calls = instructions.Select((instruction, index) => (instruction, index))
            .Where(value => value.instruction.Code == OpCodes.Call || value.instruction.Code == OpCodes.Callvirt)
            .Where(value => moveNext.Module.ResolveMethod(value.instruction.Token,
                moveNext.DeclaringType!.GenericTypeArguments, moveNext.GetGenericArguments()) == flush)
            .ToArray();
        var call = Assert.Single(calls);
        var argument = call.index - 1;
        while (argument >= 0 && instructions[argument].Code == OpCodes.Nop) argument--;

        Assert.True(argument >= 0, "FileStream.Flush(bool) must receive an explicit true argument.");
        Assert.Equal(OpCodes.Ldc_I4_1, instructions[argument].Code);
    }

    private static IReadOnlyList<Instruction> Decode(MethodInfo method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .GroupBy(code => unchecked((ushort)code.Value))
            .ToDictionary(group => group.Key, group => group.First());
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var result = new List<Instruction>();
        for (var offset = 0; offset < bytes.Length;)
        {
            var first = bytes[offset++];
            var value = first == 0xfe ? (ushort)(0xfe00 | bytes[offset++]) : first;
            var code = codes[value];
            var token = code.OperandType == OperandType.InlineMethod ? BitConverter.ToInt32(bytes, offset) : 0;
            var length = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
                    or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
                    or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(bytes, offset) * 4),
                _ => throw new InvalidDataException($"Unsupported IL operand type: {code.OperandType}."),
            };
            offset += length;
            Assert.InRange(offset, 0, bytes.Length);
            result.Add(new(code, token));
        }
        return result;
    }

    private readonly record struct Instruction(OpCode Code, int Token);

    private sealed class ChangingLengthStream : MemoryStream
    {
        public int LengthReads { get; private set; }
        public int ContentReads { get; private set; }

        // The unchecked value wraps to one byte, avoiding a large or negative allocation.
        // A negative newarr also throws OverflowException and would hide the conversion defect.
        public override long Length => ++LengthReads == 1 ? 1 : (1L << 32) + 1;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ContentReads++;
            throw new InvalidDataException("Content reads must not start after an overflowing size observation.");
        }
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(ScratchRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test directory escaped its dedicated scratch root.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
