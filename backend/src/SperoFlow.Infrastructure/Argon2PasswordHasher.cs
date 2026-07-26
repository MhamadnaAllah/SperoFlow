using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace SperoFlow.Infrastructure;

public sealed class Argon2PasswordHasher : IPasswordHasher<ApplicationUser>
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemorySizeKb = 65_536;
    private const int Iterations = 3;
    private static readonly int Parallelism = Math.Clamp(Environment.ProcessorCount, 1, 4);

    public string HashPassword(ApplicationUser user, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(password, salt, MemorySizeKb, Iterations, Parallelism, HashSize);
        return "argon2id$v=1$m=" + MemorySizeKb + ",t=" + Iterations + ",p=" + Parallelism + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
    }

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(hashedPassword) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            var parts = hashedPassword.Split('$');
            if (parts.Length != 5 || !string.Equals(parts[0], "argon2id", StringComparison.Ordinal) || !string.Equals(parts[1], "v=1", StringComparison.Ordinal))
            {
                return PasswordVerificationResult.Failed;
            }

            var parameters = ParseParameters(parts[2]);
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = DeriveHash(
                providedPassword,
                salt,
                parameters.MemorySizeKb,
                parameters.Iterations,
                parameters.Parallelism,
                expected.Length);

            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return PasswordVerificationResult.Failed;
            }

            return parameters == new Argon2Parameters(MemorySizeKb, Iterations, Parallelism)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
        catch (OverflowException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private static byte[] DeriveHash(string password, byte[] salt, int memorySizeKb, int iterations, int parallelism, int hashSize)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memorySizeKb,
        };
        return argon2.GetBytes(hashSize);
    }

    private static Argon2Parameters ParseParameters(string value)
    {
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);

        var memory = int.Parse(values["m"], System.Globalization.CultureInfo.InvariantCulture);
        var iterations = int.Parse(values["t"], System.Globalization.CultureInfo.InvariantCulture);
        var parallelism = int.Parse(values["p"], System.Globalization.CultureInfo.InvariantCulture);
        if (memory is < 8_192 or > 1_048_576 || iterations is < 1 or > 10 || parallelism is < 1 or > 16)
        {
            throw new FormatException("Argon2 parameters are outside policy bounds.");
        }

        return new Argon2Parameters(memory, iterations, parallelism);
    }

    private readonly record struct Argon2Parameters(int MemorySizeKb, int Iterations, int Parallelism);
}
