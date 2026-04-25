namespace LauncherCore;

public sealed class ModManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public string Loader { get; init; } = "BepInEx";
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ModFile> Files { get; init; } = Array.Empty<ModFile>();
}

public sealed class ModFile
{
    public string Path { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha256 { get; init; } = "";
}
