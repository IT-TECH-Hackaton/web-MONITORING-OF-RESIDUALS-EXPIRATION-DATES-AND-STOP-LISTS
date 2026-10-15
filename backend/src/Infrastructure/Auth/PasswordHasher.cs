using System.Security.Cryptography;
using System.Text;

namespace Monitoring.Infrastructure.Auth;

/// <summary>Хеш пароля PBKDF2. Наружу уходит строка, по которой можно проверить пароль и нельзя его прочитать.</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    /// <summary>Один и тот же чужой хеш, чтобы проверка несуществующего логина занимала столько же, сколько верного.</summary>
    private static readonly string Dummy = Hash("timing-placeholder");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool NeedsRehash(string stored)
    {
        var parts = stored.Split('$');
        return parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations) || iterations < Iterations;
    }

    /// <summary>Тратит столько же времени, сколько проверка настоящего хеша.</summary>
    public static void Burn(string password) => Verify(password, Dummy);

    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations) || iterations is < 1 or > 2_000_000)
            {
                Verify(password, Dummy);
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            Verify(password, Dummy);
            return false;
        }
    }
}
