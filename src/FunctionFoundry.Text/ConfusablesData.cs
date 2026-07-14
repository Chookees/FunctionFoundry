namespace FunctionFoundry.Text;

/// <summary>
/// Built-in confusables dataset version identifier.
/// </summary>
public static class ConfusablesData
{
    /// <summary>
    /// Version string for the compact built-in confusables subset.
    /// </summary>
    public const string Version = "FunctionFoundry.Text.Confusables/v1";

    private static readonly Dictionary<int, int> Map = BuildMap();

    /// <summary>
    /// Gets the dataset version.
    /// </summary>
    public static string DataVersion => Version;

    /// <summary>
    /// Maps a code point to its skeleton base code point when known.
    /// </summary>
    public static bool TryMapCodePoint(int codePoint, out int mapped)
    {
        return Map.TryGetValue(codePoint, out mapped);
    }

    private static Dictionary<int, int> BuildMap()
    {
        var map = new Dictionary<int, int>();
        AddPair(map, 'a', 'а'); // Cyrillic
        AddPair(map, 'c', 'с');
        AddPair(map, 'e', 'е');
        AddPair(map, 'o', 'о');
        AddPair(map, 'p', 'р');
        AddPair(map, 'x', 'х');
        AddPair(map, 'y', 'у');
        AddPair(map, 'A', 'А');
        AddPair(map, 'B', 'В');
        AddPair(map, 'C', 'С');
        AddPair(map, 'E', 'Е');
        AddPair(map, 'H', 'Н');
        AddPair(map, 'K', 'К');
        AddPair(map, 'M', 'М');
        AddPair(map, 'O', 'О');
        AddPair(map, 'P', 'Р');
        AddPair(map, 'T', 'Т');
        AddPair(map, 'X', 'Х');
        AddPair(map, '0', 'О');
        AddPair(map, '0', 'о');
        AddPair(map, '1', 'I');
        AddPair(map, '|', 'I');
        AddPair(map, '-', '−');
        return map;
    }

    private static void AddPair(Dictionary<int, int> map, int ascii, int confusable)
    {
        map[confusable] = ascii;
    }
}
