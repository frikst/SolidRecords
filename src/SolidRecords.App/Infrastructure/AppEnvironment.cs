using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SolidRecords.App.Infrastructure;

internal static class AppEnvironment
{
    private static readonly string? DevelopmentDataDirectory = typeof(AppEnvironment).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(attribute => attribute.Key == "DevelopmentDataDirectory")?.Value;

    public static string DataDirectory { get; } = DevelopmentDataDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolidRecords");

    public static string InstanceSuffix { get; } = DevelopmentDataDirectory is null
        ? ""
        : ".Dev." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())));
}
