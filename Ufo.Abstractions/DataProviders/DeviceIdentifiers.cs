namespace Ufo.Abstractions.DataProviders;

/// <summary>
/// Stable identifiers of the machine a snapshot was taken on.
/// </summary>
/// <param name="HardwareUuid">Firmware/BIOS UUID of the machine, or "Unknown".</param>
/// <param name="HardwareSerialNumber">Firmware/BIOS serial number, or "Unknown".</param>
/// <param name="MachineId">Operating-system install identifier, or "Unknown".</param>
public sealed record DeviceIdentifiers(string HardwareUuid, string HardwareSerialNumber, string MachineId)
{
    public const string Unknown = "Unknown";

    public static DeviceIdentifiers Empty { get; } = new(Unknown, Unknown, Unknown);
}
