using System.Globalization;

namespace Exportaramus.ConsumerKit.V1_2;

/// <summary>Exact decimal token arithmetic with work proportional to token length, never exponent magnitude.</summary>
internal sealed class ExactJsonNumber
{
    private readonly bool negative;
    private readonly string digits;
    private readonly string exponent;
    private ExactJsonNumber(bool negative, string digits, string exponent)
    { this.negative = negative; this.digits = digits; this.exponent = exponent; }
    private bool IsZero => digits == "0";
    public bool IsInteger => exponent[0] != '-';
    public string Canonical => IsZero ? "0" : (negative ? "-" : "") + digits + "e" + exponent;

    public static ExactJsonNumber Parse(string token)
    {
        var negative = token[0] == '-';
        var start = negative ? 1 : 0;
        var exponentIndex = token.AsSpan().IndexOfAny('e', 'E');
        var end = exponentIndex < 0 ? token.Length : exponentIndex;
        var coefficient = token.AsSpan(start, end - start);
        var point = coefficient.IndexOf('.');
        var fractionalDigits = point < 0 ? 0 : coefficient.Length - point - 1;
        var digits = coefficient.ToString().Replace(".", "", StringComparison.Ordinal);
        var first = digits.Length - digits.AsSpan().TrimStart('0').Length;
        if (first == digits.Length) return new(false, "0", "0");
        var last = digits.AsSpan().TrimEnd('0').Length - 1;
        var exponent = exponentIndex < 0 ? "0" : NormalizeExponent(token.AsSpan(exponentIndex + 1));
        exponent = AddExponent(exponent, digits.Length - last - 1 - fractionalDigits);
        return new(negative, digits[first..(last + 1)], exponent);
    }

    public int CompareTo(ExactJsonNumber other)
    {
        if (IsZero) return other.IsZero ? 0 : other.negative ? 1 : -1;
        if (other.IsZero) return negative ? -1 : 1;
        if (negative != other.negative) return negative ? -1 : 1;
        var order = CompareSigned(AddExponent(exponent, digits.Length), AddExponent(other.exponent, other.digits.Length));
        if (order == 0)
        {
            for (var i = 0; i < Math.Max(digits.Length, other.digits.Length); i++)
            {
                var left = i < digits.Length ? digits[i] : '0';
                var right = i < other.digits.Length ? other.digits[i] : '0';
                order = left.CompareTo(right);
                if (order != 0) break;
            }
        }
        return negative ? -order : order;
    }

    private static string NormalizeExponent(ReadOnlySpan<char> text)
    {
        var negative = text[0] == '-';
        var start = text[0] is '-' or '+' ? 1 : 0;
        var magnitude = text[start..].TrimStart('0');
        return magnitude.IsEmpty ? "0" : (negative ? "-" : "") + magnitude.ToString();
    }

    private static int CompareMagnitude(ReadOnlySpan<char> left, ReadOnlySpan<char> right) =>
        left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.SequenceCompareTo(right);

    private static int CompareSigned(string left, string right)
    {
        var leftNegative = left[0] == '-'; var rightNegative = right[0] == '-';
        if (leftNegative != rightNegative) return leftNegative ? -1 : 1;
        var order = CompareMagnitude(left.AsSpan(leftNegative ? 1 : 0), right.AsSpan(rightNegative ? 1 : 0));
        return leftNegative ? -order : order;
    }

    private static string AddExponent(string value, int offset)
    {
        if (offset == 0) return value;
        var negative = value[0] == '-'; var offsetNegative = offset < 0;
        var magnitude = value.AsSpan(negative ? 1 : 0);
        var adjustment = Math.Abs((long)offset).ToString(CultureInfo.InvariantCulture);
        if (negative == offsetNegative)
            return (negative ? "-" : "") + AddMagnitude(magnitude, adjustment);
        var order = CompareMagnitude(magnitude, adjustment);
        if (order == 0) return "0";
        return order > 0
            ? (negative ? "-" : "") + SubtractMagnitude(magnitude, adjustment)
            : (offsetNegative ? "-" : "") + SubtractMagnitude(adjustment, magnitude);
    }

    private static string AddMagnitude(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var result = new char[Math.Max(left.Length, right.Length) + 1];
        var i = left.Length - 1; var j = right.Length - 1; var carry = 0;
        for (var target = result.Length - 1; target >= 0; target--)
        {
            var sum = carry;
            if (i >= 0) sum += left[i--] - '0';
            if (j >= 0) sum += right[j--] - '0';
            result[target] = (char)('0' + sum % 10); carry = sum / 10;
        }
        return result[0] == '0' ? new string(result, 1, result.Length - 1) : new string(result);
    }

    // Caller supplies larger > smaller; zero and sign are handled before this subtraction.
    private static string SubtractMagnitude(ReadOnlySpan<char> larger, ReadOnlySpan<char> smaller)
    {
        var result = new char[larger.Length]; var j = smaller.Length - 1; var borrow = 0;
        for (var i = larger.Length - 1; i >= 0; i--)
        {
            var digit = larger[i] - '0' - borrow;
            if (j >= 0) digit -= smaller[j--] - '0';
            borrow = digit < 0 ? 1 : 0;
            result[i] = (char)('0' + digit + borrow * 10);
        }
        return result.AsSpan().TrimStart('0').ToString();
    }
}
