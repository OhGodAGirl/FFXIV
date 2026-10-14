using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCommitCancellationBoundaryTests
{
    [Fact]
    public void ValidatedReadbackChecksCancellationBeforeAnyCommitDispatch()
    {
        // Neither existing hook runs between final readback validation and commit. This
        // compiled-code contract avoids a timing-dependent cancellation race or a new seam.
        var save = typeof(InventoryFileCache).GetMethod(nameof(InventoryFileCache.SaveAsync))!;
        var stateMachine = save.GetCustomAttribute<AsyncStateMachineAttribute>();
        Assert.NotNull(stateMachine);
        var moveNext = stateMachine.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var instructions = Decode(moveNext);
        var parse = typeof(InventoryFileCache).GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
        var readback = typeof(File).GetMethod(nameof(File.ReadAllBytesAsync), [typeof(string), typeof(CancellationToken)])!;
        var cancel = typeof(CancellationToken).GetMethod(nameof(CancellationToken.ThrowIfCancellationRequested))!;
        var calls = instructions.Select((instruction, index) => (instruction, index))
            .Where(value => value.instruction.Code == OpCodes.Call || value.instruction.Code == OpCodes.Callvirt)
            .Select(value => (value.index, Method: moveNext.Module.ResolveMethod(value.instruction.Token,
                moveNext.DeclaringType!.GenericTypeArguments, moveNext.GetGenericArguments())!))
            .ToArray();
        var read = Assert.Single(calls, call => call.Method == readback);
        var validation = calls.Last(call => call.Method == parse);
        Assert.True(validation.index > read.index);
        var dispatches = calls.Where(call =>
            call.Method.DeclaringType == typeof(Action<string, string>) && call.Method.Name == nameof(Action.Invoke)
            || call.Method.DeclaringType == typeof(File) && call.Method.Name is nameof(File.Replace) or nameof(File.Move))
            .ToArray();
        Assert.NotEmpty(dispatches);
        Assert.All(dispatches, dispatch => Assert.True(dispatch.index > validation.index));
        var firstDispatch = dispatches.Min(call => call.index);
        var check = Assert.Single(calls, call => call.Method == cancel
            && call.index > validation.index && call.index < firstDispatch);

        // A check hidden in only one conditional arm would not protect all commit choices.
        Assert.DoesNotContain(instructions.Skip(validation.index + 1).Take(check.index - validation.index - 1),
            instruction => instruction.Code.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch
                or FlowControl.Return or FlowControl.Throw);
        var tokenArgument = instructions.Take(check.index).Last(instruction => instruction.Code != OpCodes.Nop);
        Assert.Equal(OpCodes.Ldflda, tokenArgument.Code);
        var tokenField = moveNext.Module.ResolveField(tokenArgument.Token,
            moveNext.DeclaringType!.GenericTypeArguments, moveNext.GetGenericArguments())!;
        Assert.Equal(typeof(CancellationToken), tokenField.FieldType);
        Assert.Equal("cancellationToken", tokenField.Name);
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
            var token = code.OperandType is OperandType.InlineMethod or OperandType.InlineField
                ? BitConverter.ToInt32(bytes, offset) : 0;
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
}
