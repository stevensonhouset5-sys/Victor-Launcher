using System.Reflection;
using System.Text.Json;
using BepInEx;

namespace AmongUsPlugin;

internal sealed class ModFileService
{
    private readonly string _pluginDirectory;
    private readonly string _disabledDirectory;
    private readonly string _importDirectory;
    private readonly string _backupDirectory;
    private readonly string _stateFilePath;
    private readonly string _selfPath;

    public ModFileService()
    {
        _pluginDirectory = Paths.PluginPath;
        _disabledDirectory = Path.Combine(Paths.BepInExRootPath, "plugins-disabled");
        _importDirectory = Path.Combine(Paths.BepInExRootPath, "mod-imports");
        _backupDirectory = Path.Combine(Paths.BepInExRootPath, "victor-backups");
        _stateFilePath = Path.Combine(Paths.ConfigPath, "victor-launcher-state.json");
        _selfPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
    }

    public IReadOnlyList<ManagedModEntry> GetMods()
    {
        Directory.CreateDirectory(_pluginDirectory);
        Directory.CreateDirectory(_disabledDirectory);

        var activeMods = EnumerateDlls(_pluginDirectory)
            .Select(path => CreateEntry(path, isEnabled: true));

        var disabledMods = EnumerateDlls(_disabledDirectory)
            .Select(path => CreateEntry(path, isEnabled: false));

        return activeMods
            .Concat(disabledMods)
            .OrderBy(entry => entry.IsSelf)
            .ThenBy(entry => entry.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string ImportDirectory => _importDirectory;

    public IReadOnlyList<StagedModEntry> GetStagedMods()
    {
        Directory.CreateDirectory(_importDirectory);

        return EnumerateDlls(_importDirectory)
            .Select(path => CreateStagedEntry(path))
            .OrderBy(entry => entry.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ModActionResult StageDll(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return ModActionResult.Fail("No DLL path was selected.");
        }

        if (!File.Exists(sourcePath))
        {
            return ModActionResult.Fail("That DLL no longer exists.");
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return ModActionResult.Fail("Only .dll files can be staged here.");
        }

        Directory.CreateDirectory(_importDirectory);

        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(_importDirectory, fileName);

        File.Copy(sourcePath, destinationPath, overwrite: true);
        PersistStateSnapshot();
        return ModActionResult.Success($"{fileName} added to the install queue.");
    }

    public ModActionResult InstallStagedMod(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return ModActionResult.Fail("That staged DLL no longer exists.");
        }

        Directory.CreateDirectory(_pluginDirectory);

        var fileName = Path.GetFileName(sourcePath);
        var relativePath = Path.GetRelativePath(_importDirectory, sourcePath);
        var destinationPath = Path.Combine(_pluginDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        BackupIfPresent(destinationPath, _pluginDirectory);

        MoveFileWithFallback(sourcePath, destinationPath);

        PersistStateSnapshot();
        return ModActionResult.Success($"{fileName} installed from mod-imports. Restart the game to load it.");
    }

    public ModActionResult InstallStagedGroup(string groupKey)
    {
        var safeGroupKey = NormalizeGroupKey(groupKey);
        var sourceDirectory = string.IsNullOrWhiteSpace(safeGroupKey)
            ? _importDirectory
            : Path.Combine(_importDirectory, safeGroupKey);

        if (!Directory.Exists(sourceDirectory))
        {
            return ModActionResult.Fail("That queued folder no longer exists.");
        }

        var files = EnumerateGroupDlls(_importDirectory, safeGroupKey).ToArray();
        if (files.Length == 0)
        {
            return ModActionResult.Fail("That queued folder does not contain any DLLs.");
        }

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(_importDirectory, file);
            var destinationPath = Path.Combine(_pluginDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            BackupIfPresent(destinationPath, _pluginDirectory);

            MoveFileWithFallback(file, destinationPath);
        }

        DeleteEmptyParents(sourceDirectory, _importDirectory);
        PersistStateSnapshot();
        return ModActionResult.Success($"{GetGroupDisplayName(safeGroupKey)} installed from the queue. Restart the game to load it.");
    }

    public ModActionResult StageDownloadedDll(string groupName, string fileName, byte[] fileContents)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ModActionResult.Fail("The downloaded DLL did not include a valid file name.");
        }

        if (fileContents.Length == 0)
        {
            return ModActionResult.Fail("The downloaded DLL was empty.");
        }

        var safeFileName = Path.GetFileName(fileName);
        if (!safeFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return ModActionResult.Fail("Only .dll files can be added to the queue.");
        }

        var safeGroupName = SanitizeGroupName(groupName);
        var destinationDirectory = string.IsNullOrWhiteSpace(safeGroupName)
            ? _importDirectory
            : Path.Combine(_importDirectory, safeGroupName);
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, safeFileName);
        File.WriteAllBytes(destinationPath, fileContents);
        PersistStateSnapshot();
        return ModActionResult.Success($"{safeFileName} downloaded into the install queue.");
    }

    public ModActionResult Disable(string fullPath)
    {
        if (IsSelf(fullPath))
        {
            return ModActionResult.Fail("This manager cannot disable itself while it is running.");
        }

        return MoveBetweenRoots(fullPath, _pluginDirectory, _disabledDirectory, "disabled");
    }

    public ModActionResult Enable(string fullPath)
    {
        return MoveBetweenRoots(fullPath, _disabledDirectory, _pluginDirectory, "enabled");
    }

    public ModActionResult DisableGroup(string groupKey)
    {
        return MoveGroupBetweenRoots(_pluginDirectory, _disabledDirectory, groupKey, "disabled");
    }

    public ModActionResult EnableGroup(string groupKey)
    {
        return MoveGroupBetweenRoots(_disabledDirectory, _pluginDirectory, groupKey, "enabled");
    }

    public ModActionResult DeleteDisabled(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return ModActionResult.Fail("That disabled DLL no longer exists on disk.");
        }

        var normalizedDisabledRoot = EnsureTrailingSeparator(Path.GetFullPath(_disabledDirectory));
        var normalizedPath = Path.GetFullPath(fullPath);
        if (!normalizedPath.StartsWith(normalizedDisabledRoot, StringComparison.OrdinalIgnoreCase))
        {
            return ModActionResult.Fail("Only disabled DLLs can be deleted from here.");
        }

        File.Delete(fullPath);
        DeleteEmptyParents(Path.GetDirectoryName(fullPath), _disabledDirectory);
        PersistStateSnapshot();
        return ModActionResult.Success($"{Path.GetFileName(fullPath)} deleted from disabled mods.");
    }

    private ModActionResult MoveBetweenRoots(string fullPath, string sourceRoot, string destinationRoot, string verb)
    {
        if (!File.Exists(fullPath))
        {
            return ModActionResult.Fail("That DLL no longer exists on disk.");
        }

        var normalizedSourceRoot = EnsureTrailingSeparator(Path.GetFullPath(sourceRoot));
        var normalizedPath = Path.GetFullPath(fullPath);

        if (!normalizedPath.StartsWith(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return ModActionResult.Fail("That DLL is outside the managed plugin folders.");
        }

        var relativePath = Path.GetRelativePath(sourceRoot, fullPath);
        var destinationPath = Path.Combine(destinationRoot, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        BackupIfPresent(destinationPath, destinationRoot);
        MoveFileWithFallback(fullPath, destinationPath);
        PersistStateSnapshot();
        return ModActionResult.Success($"{Path.GetFileName(fullPath)} {verb}. Restart the game to apply the change.");
    }

    private ModActionResult MoveGroupBetweenRoots(string sourceRoot, string destinationRoot, string groupKey, string verb)
    {
        var safeGroupKey = NormalizeGroupKey(groupKey);
        var sourceDirectory = string.IsNullOrWhiteSpace(safeGroupKey)
            ? sourceRoot
            : Path.Combine(sourceRoot, safeGroupKey);

        if (!Directory.Exists(sourceDirectory))
        {
            return ModActionResult.Fail("That mod folder no longer exists.");
        }

        var files = EnumerateGroupDlls(sourceRoot, safeGroupKey)
            .Where(path => !IsSelf(path))
            .ToArray();

        if (files.Length == 0)
        {
            return ModActionResult.Fail("There were no movable DLLs in that folder.");
        }

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            BackupIfPresent(destinationPath, destinationRoot);
            MoveFileWithFallback(file, destinationPath);
        }

        DeleteEmptyParents(sourceDirectory, sourceRoot);
        var displayName = GetGroupDisplayName(safeGroupKey);
        PersistStateSnapshot();
        return ModActionResult.Success($"{displayName} {verb}. Restart the game to apply the change.");
    }

    private void BackupIfPresent(string destinationPath, string root)
    {
        if (!File.Exists(destinationPath))
        {
            return;
        }

        var backupRoot = Path.Combine(_backupDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var relativePath = Path.GetRelativePath(root, destinationPath);
        var backupPath = Path.Combine(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(destinationPath, backupPath, overwrite: true);
    }

    private static void MoveFileWithFallback(string sourcePath, string destinationPath)
    {
        try
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
            File.Delete(sourcePath);
        }
    }

    private void PersistStateSnapshot()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);

        var snapshot = new
        {
            generatedAtUtc = DateTime.UtcNow,
            active = EnumerateDlls(_pluginDirectory)
                .Select(path => Path.GetRelativePath(_pluginDirectory, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            disabled = EnumerateDlls(_disabledDirectory)
                .Select(path => Path.GetRelativePath(_disabledDirectory, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            staged = EnumerateDlls(_importDirectory)
                .Select(path => Path.GetRelativePath(_importDirectory, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private ManagedModEntry CreateEntry(string fullPath, bool isEnabled)
    {
        var root = isEnabled ? _pluginDirectory : _disabledDirectory;
        var relativePath = Path.GetRelativePath(root, fullPath);
        var groupKey = Path.GetDirectoryName(relativePath) ?? string.Empty;
        return new ManagedModEntry(
            Path.GetFileNameWithoutExtension(fullPath),
            Path.GetFullPath(fullPath),
            relativePath,
            NormalizeGroupKey(groupKey),
            GetGroupDisplayName(groupKey),
            isEnabled,
            IsSelf(fullPath));
    }

    private StagedModEntry CreateStagedEntry(string fullPath)
    {
        var relativePath = Path.GetRelativePath(_importDirectory, fullPath);
        var groupKey = Path.GetDirectoryName(relativePath) ?? string.Empty;
        return new StagedModEntry(
            Path.GetFileNameWithoutExtension(fullPath),
            Path.GetFullPath(fullPath),
            relativePath,
            NormalizeGroupKey(groupKey),
            GetGroupDisplayName(groupKey));
    }

    private bool IsSelf(string fullPath)
    {
        return string.Equals(Path.GetFullPath(fullPath), _selfPath, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateDlls(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateGroupDlls(string root, string groupKey)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(groupKey))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            yield break;
        }

        var groupDirectory = Path.Combine(root, groupKey);
        if (!Directory.Exists(groupDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(groupDirectory, "*.dll", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string NormalizeGroupKey(string? groupKey)
    {
        return (groupKey ?? string.Empty)
            .Trim()
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string GetGroupDisplayName(string? groupKey)
    {
        var normalized = NormalizeGroupKey(groupKey);
        return string.IsNullOrWhiteSpace(normalized)
            ? "Loose DLLs"
            : normalized.Replace(Path.DirectorySeparatorChar.ToString(), " / ", StringComparison.Ordinal);
    }

    private static string SanitizeGroupName(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return string.Empty;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars().Concat(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        var sanitized = groupName.Trim();
        foreach (var invalidCharacter in invalidCharacters)
        {
            sanitized = sanitized.Replace(invalidCharacter, '_');
        }

        return sanitized;
    }

    private static void DeleteEmptyParents(string? directoryPath, string stopRoot)
    {
        var current = directoryPath;
        var normalizedStopRoot = Path.GetFullPath(stopRoot);
        while (!string.IsNullOrWhiteSpace(current) &&
               current.StartsWith(normalizedStopRoot, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(Path.GetFullPath(current), normalizedStopRoot, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(current) &&
               !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }
}

internal sealed record ManagedModEntry(
    string Name,
    string FullPath,
    string RelativePath,
    string GroupKey,
    string GroupName,
    bool IsEnabled,
    bool IsSelf);

internal sealed record StagedModEntry(
    string Name,
    string FullPath,
    string RelativePath,
    string GroupKey,
    string GroupName);

internal sealed record ModActionResult(bool Succeeded, string Message)
{
    public static ModActionResult Success(string message) => new(true, message);

    public static ModActionResult Fail(string message) => new(false, message);
}
