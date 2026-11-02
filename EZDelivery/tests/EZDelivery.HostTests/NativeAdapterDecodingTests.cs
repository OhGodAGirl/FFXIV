using System.Reflection;
using System.Runtime.InteropServices;
using EZDelivery.Features.ExpertDelivery;
using EZDelivery.Features.Shopping;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Xunit;

namespace EZDelivery.HostTests;

/// <summary>Executes compiled adapter helpers against owned memory. Never enters a game callback.</summary>
public sealed unsafe class NativeAdapterDecodingTests
{
    private delegate bool DecodeValue(AtkValue value, out uint result);
    private delegate bool DecodeNumber(AtkUnitBase* addon, int index, out uint result);
    private delegate bool MatchTransaction(nint transaction, int offset, uint item, uint amount);
    private delegate int ReadCondition(nint transaction);
    private delegate void WriteInt(AtkValue* value, int input);
    private delegate void WriteUInt(AtkValue* value, uint input);
    private delegate void WriteBool(AtkValue* value, bool input);
    private delegate void ConfirmCurrency(AtkUnitBase* addon);
    private static nint capturedAddon;
    private static uint capturedCount;
    private static bool capturedClose;
    private static AtkValue[] capturedValues = [];

    [UnmanagedCallersOnly]
    private static byte CaptureCallback(AtkUnitBase* addon, uint count, AtkValue* values, byte close)
    {
        capturedAddon = (nint)addon;
        capturedCount = count;
        capturedClose = close != 0;
        capturedValues = new ReadOnlySpan<AtkValue>(values, checked((int)count)).ToArray();
        return 1;
    }

    private static T Method<T>(Type owner, string name) where T : Delegate =>
        owner.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();

    [Theory]
    [InlineData(0, 0, true, 0u)]
    [InlineData(0, -1, true, uint.MaxValue)]
    [InlineData(1, 0, true, 0u)]
    [InlineData(1, int.MaxValue, true, 2147483647u)]
    [InlineData(1, -1, false, 0u)]
    [InlineData(2, 1, false, 0u)]
    public void DeliveryNumericDecoderUsesNativeValueTags(int type, int input, bool expected, uint result)
    {
        // Arrange
        var decode = Method<DecodeValue>(typeof(DeliveryNativeAdapter), "TryUInt");
        var value = new AtkValue { Type = type switch { 0 => AtkValueType.UInt, 1 => AtkValueType.Int, _ => AtkValueType.Bool }, Int = input };

        // Act
        var accepted = decode(value, out var actual);

        // Assert
        Assert.Equal(expected, accepted);
        Assert.Equal(result, actual);
    }

    [Fact]
    public void ShoppingDecoderChecksArrayPointerBoundsAndNativeType()
    {
        // Arrange
        var decode = Method<DecodeNumber>(typeof(ShoppingNative), "Number");
        var addon = new AtkUnitBase();
        AtkValue* values = stackalloc AtkValue[4];
        values[0] = new() { Type = AtkValueType.UInt, UInt = uint.MaxValue };
        values[1] = new() { Type = AtkValueType.Int, Int = 27 };
        values[2] = new() { Type = AtkValueType.Int, Int = -1 };
        values[3] = new() { Type = AtkValueType.Bool, Byte = 1 };

        // Act / Assert: null storage is rejected before any indexed read.
        Assert.False(decode(&addon, 0, out var missing));
        Assert.Equal(0u, missing);
        addon.AtkValues = values;
        addon.AtkValuesCount = 4;
        Assert.False(decode(&addon, -1, out _));
        Assert.False(decode(&addon, 4, out _));
        Assert.True(decode(&addon, 0, out var unsigned));
        Assert.Equal(uint.MaxValue, unsigned);
        Assert.True(decode(&addon, 1, out var signed));
        Assert.Equal(27u, signed);
        Assert.False(decode(&addon, 2, out var negative));
        Assert.Equal(0u, negative);
        Assert.False(decode(&addon, 3, out _));
    }

    [Fact]
    public void TransactionEntryRequiresOneExactSixteenByteEntry()
    {
        // Arrange
        var matches = Method<MatchTransaction>(typeof(ShoppingNative), "TransactionEntryMatches");
        byte* transaction = stackalloc byte[0x80];
        byte* entry = stackalloc byte[16];
        new Span<byte>(transaction, 0x80).Clear();
        new Span<byte>(entry, 16).Clear();
        *(uint*)entry = 123;
        *(uint*)(entry + 4) = 4200;

        // Act / Assert
        Assert.False(matches((nint)transaction, 0x38, 123, 4200));
        *(nint*)(transaction + 0x38) = (nint)entry;
        *(nint*)(transaction + 0x40) = (nint)entry + 16;
        Assert.True(matches((nint)transaction, 0x38, 123, 4200));
        Assert.False(matches((nint)transaction, 0x38, 124, 4200));
        Assert.False(matches((nint)transaction, 0x38, 123, 4201));
        *(nint*)(transaction + 0x40) = (nint)entry + 32;
        Assert.False(matches((nint)transaction, 0x38, 123, 4200));
    }

    [Fact]
    public void TransactionConditionReadsWrappedSlotAndRejectsInvalidStorage()
    {
        // Arrange
        var read = Method<ReadCondition>(typeof(ShoppingNative), "TransactionCondition");
        byte* transaction = stackalloc byte[0x80];
        byte* condition = stackalloc byte[16];
        nint* slots = stackalloc nint[4];
        new Span<byte>(transaction, 0x80).Clear();
        new Span<nint>(slots, 4).Clear();
        *(ushort*)(condition + 8) = 1005;

        // Act / Assert
        Assert.Equal(-1, read((nint)transaction));
        *(nint*)(transaction + 0x10) = (nint)slots;
        foreach (var capacity in new ulong[] { 0, 3, 4097, ulong.MaxValue })
        {
            *(ulong*)(transaction + 0x18) = capacity;
            Assert.Equal(-1, read((nint)transaction));
        }
        *(ulong*)(transaction + 0x18) = 4;
        *(ulong*)(transaction + 0x20) = 5;
        Assert.Equal(-1, read((nint)transaction));
        slots[1] = (nint)condition;
        Assert.Equal(1005, read((nint)transaction));
    }

    [Fact]
    public void CallbackValueWritersSetExactTagAndPayload()
    {
        // Arrange
        var writeInt = Method<WriteInt>(typeof(ShoppingNative), "Int");
        var writeUInt = Method<WriteUInt>(typeof(ShoppingNative), "UInt");
        var writeBool = Method<WriteBool>(typeof(ShoppingNative), "Bool");
        var value = new AtkValue();

        // Act / Assert
        writeInt(&value, -3);
        Assert.Equal(AtkValueType.Int, value.Type);
        Assert.Equal(-3, value.Int);
        writeUInt(&value, uint.MaxValue);
        Assert.Equal(AtkValueType.UInt, value.Type);
        Assert.Equal(uint.MaxValue, value.UInt);
        writeBool(&value, true);
        Assert.Equal(AtkValueType.Bool, value.Type);
        Assert.Equal((byte)1, value.Byte);
        writeBool(&value, false);
        Assert.Equal((byte)0, value.Byte);
    }

    [Fact]
    public void CurrencyConfirmationSendsOneCopyAndClosesTheOwnedModal()
    {
        // Replace only this test process's unresolved interop address with our
        // managed capture stub. No game process or callback is entered.
        var confirm = Method<ConfirmCurrency>(typeof(ShoppingNative), "FireCurrencyConfirmation");
        var previous = AtkUnitBase.Addresses.FireCallback.Value;
        var addon = new AtkUnitBase();
        capturedValues = [];
        try
        {
            AtkUnitBase.Addresses.FireCallback.Value =
                (nint)(delegate* unmanaged<AtkUnitBase*, uint, AtkValue*, byte, byte>)&CaptureCallback;

            confirm(&addon);

            Assert.Equal((nint)(&addon), capturedAddon);
            Assert.Equal(2u, capturedCount);
            Assert.True(capturedClose);
            Assert.Equal(2, capturedValues.Length);
            Assert.All(capturedValues, value => Assert.Equal(AtkValueType.Int, value.Type));
            Assert.Equal(0, capturedValues[0].Int);
            Assert.Equal(1, capturedValues[1].Int);
        }
        finally { AtkUnitBase.Addresses.FireCallback.Value = previous; }
    }
}
