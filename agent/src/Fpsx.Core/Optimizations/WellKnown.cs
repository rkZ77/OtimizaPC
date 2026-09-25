namespace Fpsx.Core.Optimizations;

public static class PowerSchemes
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public static bool Is(string? guid, string known) => string.Equals(guid, known, StringComparison.OrdinalIgnoreCase);
}

public static class RegistryPaths
{
    public const string GameBar = @"Software\Microsoft\GameBar";
    public const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    public const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    public const string StartupApproved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
}
