using Microsoft.Win32;

namespace FanControl.MinisforumUMSeries;

internal static class HostIdentity
{
    private const string BiosKey =
        @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";

    internal static HostIdentitySnapshot Read() => new(
        ReadRequiredString("SystemProductName"),
        ReadOptionalString("SystemVersion"),
        ReadOptionalString("SystemSKU"),
        ReadOptionalString("SystemFamily"),
        ReadRequiredString("BaseBoardProduct"),
        ReadRequiredString("BaseBoardVersion"),
        ReadRequiredString("BIOSVersion"),
        ReadRequiredByte("ECFirmwareMajorRelease"),
        ReadRequiredByte("ECFirmwareMinorRelease"));

    private static string ReadOptionalString(string name) => Convert.ToString(
        Registry.GetValue(BiosKey, name, null))?.Trim() ?? string.Empty;

    private static string ReadRequiredString(string name)
    {
        object? value = Registry.GetValue(BiosKey, name, null);
        string text = Convert.ToString(value)?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new InvalidOperationException(
                $"Required BIOS registry value {name} is missing or empty.");
        }
        return text;
    }

    private static int ReadRequiredByte(string name)
    {
        object? value = Registry.GetValue(BiosKey, name, null);
        if (value is null)
        {
            throw new InvalidOperationException(
                $"Required BIOS registry value {name} is missing.");
        }

        int result;
        try
        {
            result = Convert.ToInt32(value);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException(
                $"BIOS registry value {name} is not an integer.",
                exception);
        }

        if (result is < byte.MinValue or > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"BIOS registry value {name} is outside the byte range: {result}.");
        }
        return result;
    }
}
