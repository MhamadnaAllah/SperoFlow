using System.Security.Cryptography;
using System.Text;

namespace SperoFlow.Infrastructure;

/// <summary>
/// Shared validation for direct uploads. Object-store metadata is not trusted by itself:
/// the application checks the approved byte size, media type, SHA-256, and the magic
/// signatures that are unambiguous for the accepted binary document formats.
/// </summary>
internal static class UploadedObjectValidation
{
    internal const long MaximumDatasetUploadBytes = 100L * 1024 * 1024;

    internal static string NormalizeContentType(string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        return contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    internal static void ValidateExpected(string expectedContentType, long expectedSizeBytes, string expectedSha256)
    {
        _ = NormalizeContentType(expectedContentType);
        if (expectedSizeBytes is < 1 or > MaximumDatasetUploadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSizeBytes), "Dataset uploads must be between 1 byte and 100 MB.");
        }

        var checksum = NormalizeSha256(expectedSha256);
        if (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The expected SHA-256 checksum must be a 64-character hexadecimal value.");
        }
    }

    internal static void ValidateMetadata(
        long actualSizeBytes,
        string? actualContentType,
        string expectedContentType,
        long expectedSizeBytes)
    {
        if (actualSizeBytes != expectedSizeBytes)
        {
            throw new InvalidOperationException("The uploaded object size does not match the approved size.");
        }

        var normalizedExpected = NormalizeContentType(expectedContentType);
        var normalizedActual = string.IsNullOrWhiteSpace(actualContentType)
            ? string.Empty
            : NormalizeContentType(actualContentType);
        if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The uploaded object content type does not match the approved type.");
        }
    }

    internal static string ComputeSha256AndValidateSignature(Stream source, string contentType)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var signature = new byte[1024];
        var signatureLength = 0;
        var buffer = new byte[64 * 1024];

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (signatureLength < signature.Length)
            {
                var bytesToCopy = Math.Min(signature.Length - signatureLength, read);
                buffer.AsSpan(0, bytesToCopy).CopyTo(signature.AsSpan(signatureLength));
                signatureLength += bytesToCopy;
            }

            hash.AppendData(buffer, 0, read);
        }

        ValidateContentSignature(signature.AsSpan(0, signatureLength), NormalizeContentType(contentType));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static void EnsureHashMatches(string actualSha256, string expectedSha256)
    {
        var actual = Encoding.ASCII.GetBytes(NormalizeSha256(actualSha256));
        var expected = Encoding.ASCII.GetBytes(NormalizeSha256(expectedSha256));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new InvalidOperationException("The uploaded object checksum does not match the approved checksum.");
        }
    }

    private static string NormalizeSha256(string checksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);
        return checksum.Trim().ToLowerInvariant();
    }

    private static void ValidateContentSignature(ReadOnlySpan<byte> bytes, string contentType)
    {
        // CSV, JSON, Markdown, and text have no reliable universal magic value. PDF and
        // OOXML DOCX do, so reject obvious MIME spoofing for those binary formats.
        if (contentType == "application/pdf" && bytes.IndexOf("%PDF-"u8) < 0)
        {
            throw new InvalidOperationException("The uploaded file does not have a valid PDF signature.");
        }

        if (contentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            && !(bytes.Length >= 4 && bytes[..4].SequenceEqual("PK\x03\x04"u8)))
        {
            throw new InvalidOperationException("The uploaded file does not have a valid DOCX container signature.");
        }
    }
}
