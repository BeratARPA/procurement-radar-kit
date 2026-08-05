using System.Globalization;

namespace ProcurementRadar.Parsers;

/// <summary>
/// Reads the amounts notice pages print, for example <c>180.000,00 TL</c> or
/// <c>1.234.567,89 ₺</c>.
/// </summary>
/// <remarks>
/// Everything that is not a digit, separator or sign is dropped before parsing,
/// so no Turkish word or currency mark has to be listed in code. The remainder
/// is read with the tr-TR culture, where the dot groups thousands and the comma
/// is the decimal point - the exact opposite of the invariant culture, which is
/// why parsing this with the default culture silently turns 180.000 into 180.
/// </remarks>
public static class TurkishMoneyParser
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static decimal? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Span<char> digits = stackalloc char[text.Length];
        var length = 0;

        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character) || character is '.' or ',' or '-')
            {
                digits[length++] = character;
            }
        }

        if (length == 0)
        {
            return null;
        }

        var candidate = digits[..length].ToString().Trim('.', ',');

        return decimal.TryParse(
            candidate,
            NumberStyles.Number,
            Turkish,
            out var amount) && amount >= 0
                ? amount
                : null;
    }
}
