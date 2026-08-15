using System.Security.Cryptography;
using System.Text;

namespace Vtt.EngineeringFixture.Infrastructure;

public static class StableHash
{
    public static string Sha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static bool FixedTimeEquals(string leftHex, string rightHex)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(leftHex),
                Convert.FromHexString(rightHex));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Projection(Guid id, int value, long version) =>
        Sha256($"{id:N}|{value}|{version}");
}
