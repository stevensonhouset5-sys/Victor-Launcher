namespace LauncherCore;

public sealed class InstallPlanner
{
    public InstallPlan CreatePlan(
        string amongUsDirectory,
        ModManifest manifest,
        IReadOnlyCollection<string> installedManifestIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amongUsDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(installedManifestIds);

        var missingDependencies = manifest.Dependencies
            .Where(dependency => !installedManifestIds.Contains(dependency, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var activeConflicts = manifest.Conflicts
            .Where(conflict => installedManifestIds.Contains(conflict, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var destinationFiles = manifest.Files
            .Select(file => new PlannedFile(
                file.Path,
                Path.Combine(amongUsDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)),
                file.Url,
                file.Sha256))
            .ToArray();

        return new InstallPlan(
            manifest.Id,
            manifest.Name,
            missingDependencies,
            activeConflicts,
            destinationFiles);
    }
}

public sealed record InstallPlan(
    string ManifestId,
    string DisplayName,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> ActiveConflicts,
    IReadOnlyList<PlannedFile> Files);

public sealed record PlannedFile(
    string RelativePath,
    string DestinationPath,
    string Url,
    string Sha256);
