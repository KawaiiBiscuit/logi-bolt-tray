namespace LogiBoltTray.Protocol;

public readonly record struct LogiBoltDeviceInfo(byte DeviceIndex, string Name, DeviceType Type, BatteryStatus Battery);
