namespace IFers.RCloneDriver;

public enum FilterOptionType
{
    FilesMatchingPattern = 0, // Filter files matching pattern
    ReadFileFilterPatternsFromFile = 1, // Filter file filter patterns from file
    DirectoriesIfFilenamePresented = 2 // Exclude directories if filename is present
}

[Serializable]
public record FilterOption(FilterOptionType Type, string Pattern)
{
    public string[] ToStringArray()
    {
        return new[]
        {
            Type switch
            {
                FilterOptionType.FilesMatchingPattern => "--filter",
                FilterOptionType.ReadFileFilterPatternsFromFile => "--filter-from",
                FilterOptionType.DirectoriesIfFilenamePresented => "--exclude-if-present",
                _ => throw new ArgumentOutOfRangeException()
            },
            Pattern.Contains(' ') ? $"\"{Pattern}\"" : Pattern
        };
    }
}

[Serializable]
public record RcdConfig(
    string Remote,
    DateTime Timestamp,
    List<FilterOption> Excludes
);