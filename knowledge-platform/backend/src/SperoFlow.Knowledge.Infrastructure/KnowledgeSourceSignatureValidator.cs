using System.Text;

namespace SperoFlow.Knowledge.Infrastructure;

public static class KnowledgeSourceSignatureValidator
{
    public static void Validate(string fileName, string contentType, ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
        {
            throw new InvalidOperationException("The uploaded source is empty.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var normalizedContentType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        switch (extension)
        {
            case ".pdf":
                Require(normalizedContentType == "application/pdf" && sample.StartsWith("%PDF-"u8), "The uploaded PDF does not have a valid PDF signature.");
                return;
            case ".docx":
                Require(
                    normalizedContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" && sample.StartsWith("PK\x03\x04"u8),
                    "The uploaded DOCX does not have a valid Office ZIP signature.");
                return;
            case ".json":
                Require(normalizedContentType is "application/json" or "text/json", "The uploaded JSON has an invalid content type.");
                var firstJsonByte = FirstNonWhitespaceByte(sample);
                Require(firstJsonByte is (byte)'{' or (byte)'[', "The uploaded JSON does not start with an object or array.");
                return;
            case ".csv":
                Require(normalizedContentType is "text/csv" or "application/csv" or "application/vnd.ms-excel", "The uploaded CSV has an invalid content type.");
                Require(!LooksBinary(sample), "The uploaded CSV appears to be binary data.");
                return;
            case ".md":
                Require(normalizedContentType is "text/markdown" or "text/plain", "The uploaded Markdown has an invalid content type.");
                Require(!LooksBinary(sample), "The uploaded Markdown appears to be binary data.");
                return;
            case ".txt":
                Require(normalizedContentType == "text/plain", "The uploaded text file has an invalid content type.");
                Require(!LooksBinary(sample), "The uploaded text file appears to be binary data.");
                return;
            default:
                throw new InvalidOperationException("The uploaded source has an unsupported file extension.");
        }
    }

    private static byte FirstNonWhitespaceByte(ReadOnlySpan<byte> value)
    {
        var index = value.StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        while (index < value.Length && value[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            index++;
        }

        return index < value.Length ? value[index] : (byte)0;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> value)
    {
        var controlCount = 0;
        foreach (var current in value)
        {
            if (current == 0 || (current < 0x08 && current is not (byte)'\t' and not (byte)'\n' and not (byte)'\r'))
            {
                controlCount++;
            }
        }

        return controlCount > Math.Max(4, value.Length / 50);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}