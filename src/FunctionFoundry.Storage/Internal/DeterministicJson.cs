using System.Text.Json;

namespace FunctionFoundry.Storage.Internal;

internal static class DeterministicJson
{
    public static byte[] WriteJournal(TransactionJournalDocument document)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", document.FormatVersion);
        writer.WriteString("transactionId", document.TransactionId);
        writer.WriteString("status", document.Status);
        writer.WriteString("targetDirectory", document.TargetDirectory);
        writer.WriteStartArray("files");
        foreach (TransactionJournalFileEntry entry in document.Files)
        {
            writer.WriteStartObject();
            writer.WriteString("path", entry.Path);
            writer.WriteString("sha256", entry.Sha256);
            writer.WriteNumber("size", entry.Size);
            writer.WriteString("operation", entry.Operation);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("appliedPaths");
        foreach (string path in document.AppliedPaths)
        {
            writer.WriteStringValue(path);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    public static TransactionJournalDocument ReadJournal(ReadOnlySpan<byte> json)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        JsonElement root = document.RootElement;
        int formatVersion = root.GetProperty("formatVersion").GetInt32();
        string transactionId = root.GetProperty("transactionId").GetString() ?? string.Empty;
        string status = root.GetProperty("status").GetString() ?? string.Empty;
        string targetDirectory = root.GetProperty("targetDirectory").GetString() ?? string.Empty;
        var files = new List<TransactionJournalFileEntry>();
        foreach (JsonElement fileElement in root.GetProperty("files").EnumerateArray())
        {
            files.Add(new TransactionJournalFileEntry(
                fileElement.GetProperty("path").GetString() ?? string.Empty,
                fileElement.GetProperty("sha256").GetString() ?? string.Empty,
                fileElement.GetProperty("size").GetInt64(),
                fileElement.GetProperty("operation").GetString() ?? string.Empty));
        }

        var appliedPaths = new List<string>();
        if (root.TryGetProperty("appliedPaths", out JsonElement appliedElement))
        {
            foreach (JsonElement pathElement in appliedElement.EnumerateArray())
            {
                appliedPaths.Add(pathElement.GetString() ?? string.Empty);
            }
        }

        appliedPaths.Sort(StringComparer.Ordinal);
        files.Sort(static (left, right) => string.Compare(left.Path, right.Path, StringComparison.Ordinal));
        return new TransactionJournalDocument(formatVersion, transactionId, status, targetDirectory, files, appliedPaths);
    }
}

internal sealed record TransactionJournalDocument(
    int FormatVersion,
    string TransactionId,
    string Status,
    string TargetDirectory,
    IReadOnlyList<TransactionJournalFileEntry> Files,
    IReadOnlyList<string> AppliedPaths);

internal sealed record TransactionJournalFileEntry(
    string Path,
    string Sha256,
    long Size,
    string Operation);
