using System.Globalization;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace AmongUsPlugin;

public sealed class ModManagerBehaviour : MonoBehaviour
{
    private const string LooseGroupSelectionKey = "__loose_dlls__";
    private static ModManagerBehaviour? _instance;

    private readonly ModFileService _modFileService = new();
    private readonly GameProcessService _gameProcessService = new();
    private readonly SupabasePackService _supabasePackService = new();
    private Rect _windowRect = new(34f, 26f, 840f, 700f);

    private List<ManagedModEntry> _mods = new();
    private List<StagedModEntry> _stagedMods = new();
    private string _statusMessage = "Press F7 to show or hide the mod manager.";
    private string _editableRoomCode = "";
    private Vector2 _windowScrollPosition = Vector2.zero;
    private Vector2 _modsScrollPosition = Vector2.zero;
    private Vector2 _importsScrollPosition = Vector2.zero;
    private bool _isVisible;
    private bool _lastActionSucceeded = true;
    private bool _restartPromptVisible;
    private string _restartPromptReason = "";
    private Task<ModActionResult>? _packDownloadTask;
    private string _activePackCode = "";
    private string? _selectedInstalledGroupKey;
    private string? _selectedStagedGroupKey;
    private bool _roomCodeInputFocused;
    private Rect _roomCodeInputRect;
    private float _roomCodeCursorBlinkTimer;

    private Texture2D? _cardTexture;
    private Texture2D? _screenScrimTexture;
    private Texture2D? _windowBackdropTexture;
    private Texture2D? _clearTexture;
    private Texture2D? _accentTexture;
    private Texture2D? _successTexture;
    private Texture2D? _dangerTexture;
    private Texture2D? _warningTexture;
    private Texture2D? _scrollTrackTexture;
    private Texture2D? _scrollThumbTexture;
    private Texture2D? _buttonBlueTexture;
    private Texture2D? _buttonPurpleTexture;
    private Texture2D? _buttonRedTexture;
    private Texture2D? _buttonGreenTexture;
    private Texture2D? _buttonSlateTexture;
    private Texture2D? _buttonBlueHoverTexture;
    private Texture2D? _buttonPurpleHoverTexture;
    private Texture2D? _buttonRedHoverTexture;
    private Texture2D? _buttonGreenHoverTexture;
    private Texture2D? _buttonSlateHoverTexture;
    private Texture2D? _buttonBlueActiveTexture;
    private Texture2D? _buttonPurpleActiveTexture;
    private Texture2D? _buttonRedActiveTexture;
    private Texture2D? _buttonGreenActiveTexture;
    private Texture2D? _buttonSlateActiveTexture;

    private GUIStyle? _windowStyle;
    private GUIStyle? _scrimStyle;
    private GUIStyle? _backdropStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _subtitleStyle;
    private GUIStyle? _sectionStyle;
    private GUIStyle? _bodyStyle;
    private GUIStyle? _pathStyle;
    private GUIStyle? _pillStyle;
    private GUIStyle? _statusStyle;
    private GUIStyle? _cardStyle;
    private GUIStyle? _inputBoxStyle;
    private GUIStyle? _inputTextStyle;

    private void Start()
    {
        _instance = this;
        _editableRoomCode = StarterPlugin.SupabaseRoomCode.Value?.Trim() ?? "";
        RefreshMods();
    }

    private void Update()
    {
        CompletePackDownloadIfReady();
        _roomCodeCursorBlinkTimer += Time.unscaledDeltaTime;

        if (Input.GetKeyDown(KeyCode.F7))
        {
            _isVisible = !_isVisible;
            if (_isVisible)
            {
                RefreshMods();
            }
        }
    }

    private void OnGUI()
    {
        if (!_isVisible)
        {
            return;
        }

        EnsureStyles();
        HandleRoomCodeInput(Event.current);

        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), new GUIContent(""), _scrimStyle!);
        GUI.Box(_windowRect, new GUIContent(""), _backdropStyle!);
        GUILayout.BeginArea(_windowRect);
        DrawWindowContents();
        GUILayout.EndArea();
    }

    [HideFromIl2Cpp]
    private void DrawWindowContents()
    {
        GUILayout.Space(6f);
        GUILayout.Label("Victor Launcher", _titleStyle!);
        GUILayout.Space(10f);

        _windowScrollPosition = GUILayout.BeginScrollView(_windowScrollPosition, GUILayout.Height(_windowRect.height - 78f));

        GUILayout.BeginHorizontal();
        DrawStatChip("Installed", _mods.Count.ToString(CultureInfo.InvariantCulture), _accentTexture!);
        GUILayout.Space(8f);
        DrawStatChip("Ready To Install", _stagedMods.Count.ToString(CultureInfo.InvariantCulture), _warningTexture!);
        GUILayout.FlexibleSpace();

        if (FancyButton("Pick DLL", _buttonPurpleTexture!, _buttonPurpleHoverTexture!, _buttonPurpleActiveTexture!, 120f))
        {
            var result = SafeRun(() => _gameProcessService.PickDllAndStage(_modFileService));
            ApplyActionResult(result);
            RefreshMods();
        }

        GUILayout.Space(8f);
        if (FancyButton("Refresh Lists", _buttonSlateTexture!, _buttonSlateHoverTexture!, _buttonSlateActiveTexture!, 120f))
        {
            RefreshMods();
        }

        GUILayout.Space(8f);
        if (FancyButton("Close Game", _buttonRedTexture!, _buttonRedHoverTexture!, _buttonRedActiveTexture!, 130f))
        {
            CloseGameNow();
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(14f);

        if (_restartPromptVisible)
        {
            DrawRestartPrompt();
            GUILayout.Space(12f);
        }

        DrawDownloadByCodeSection();
        GUILayout.Space(12f);

        GUILayout.Label(_selectedInstalledGroupKey == null ? "Installed Folders" : "Installed Folder", _sectionStyle!);
        GUILayout.Label(
            _selectedInstalledGroupKey == null
                ? "Each room or folder appears once here. Open a folder to see the DLLs inside it."
                : "This submenu shows only the DLLs inside the selected installed folder.",
            _bodyStyle!);
        GUILayout.Space(6f);
        DrawModsList();
        GUILayout.Space(12f);

        GUILayout.Label(_selectedStagedGroupKey == null ? "Queued Folders" : "Queued Folder", _sectionStyle!);
        GUILayout.Label(
            _selectedStagedGroupKey == null
                ? "Queued room downloads appear as folders first. Open one to see the DLLs waiting inside it."
                : "This submenu shows only the DLLs inside the selected queued folder.",
            _bodyStyle!);
        GUILayout.Label(_modFileService.ImportDirectory, _pathStyle!);
        GUILayout.Space(6f);
        DrawStagedModsList();
        GUILayout.Space(12f);

        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.Label(_lastActionSucceeded ? "Ship Status: Ready" : "Ship Status: Warning", _sectionStyle!);
        GUILayout.Label(_statusMessage, _bodyStyle!);
        GUILayout.Space(6f);
        GUILayout.Label("Hotkey: F7  |  Restart Among Us manually after mod changes", _subtitleStyle!);
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    [HideFromIl2Cpp]
    private void DrawDownloadByCodeSection()
    {
        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.Label("Download By Code", _sectionStyle!);
        GUILayout.Label("Enter a room code to pull down the mod pack linked to that room.", _bodyStyle!);
        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Room Code", _subtitleStyle!, GUILayout.Width(88f));
        _roomCodeInputRect = GUILayoutUtility.GetRect(new GUIContent(""), _inputBoxStyle!, GUILayout.Width(180f), GUILayout.Height(34f));
        DrawRoomCodeInput();
        GUILayout.Space(12f);

        var downloadInProgress = _packDownloadTask != null;
        GUI.enabled = !downloadInProgress;
        if (FancyButton(downloadInProgress ? "Downloading..." : "Download Pack", _buttonBlueTexture!, _buttonBlueHoverTexture!, _buttonBlueActiveTexture!, 140f))
        {
            StartPackDownload();
        }

        GUI.enabled = true;
        GUILayout.Space(8f);
        if (FancyButton("Reload Config", _buttonSlateTexture!, _buttonSlateHoverTexture!, _buttonSlateActiveTexture!, 110f))
        {
            _editableRoomCode = StarterPlugin.SupabaseRoomCode.Value?.Trim() ?? "";
            _roomCodeInputFocused = false;
            _statusMessage = "Reloaded the room code from the config file.";
            _lastActionSucceeded = true;
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(8f);
        GUILayout.Label("Create or manage room codes on the Victor Launcher room manager site, then enter the code here.", _bodyStyle!);
        GUILayout.EndVertical();
    }

    [HideFromIl2Cpp]
    private void DrawRoomCodeInput()
    {
        if (GUI.Button(_roomCodeInputRect, new GUIContent(""), _inputBoxStyle!))
        {
            _roomCodeInputFocused = true;
            _roomCodeCursorBlinkTimer = 0f;
        }

        GUI.Box(_roomCodeInputRect, new GUIContent(""), _inputBoxStyle!);

        var displayValue = string.IsNullOrEmpty(_editableRoomCode) && !_roomCodeInputFocused
            ? "Enter room code"
            : GetRoomCodeDisplayText();

        var oldColor = GUI.color;
        if (string.IsNullOrEmpty(_editableRoomCode) && !_roomCodeInputFocused)
        {
            GUI.color = new Color(0.66f, 0.76f, 0.88f, 0.82f);
        }

        var labelRect = new Rect(_roomCodeInputRect.x + 12f, _roomCodeInputRect.y + 8f, _roomCodeInputRect.width - 24f, _roomCodeInputRect.height - 12f);
        GUI.Label(labelRect, displayValue, _inputTextStyle!);
        GUI.color = oldColor;
    }

    [HideFromIl2Cpp]
    private void DrawModsList()
    {
        _modsScrollPosition = GUILayout.BeginScrollView(_modsScrollPosition, GUILayout.Height(240f));

        if (_mods.Count == 0)
        {
            GUILayout.BeginVertical(new GUIContent(""), _cardStyle!);
            GUILayout.Label("No DLL mods found yet.", _sectionStyle!);
            GUILayout.Label("Once your plugins folder has DLLs in it, they'll show up here.", _bodyStyle!);
            GUILayout.EndVertical();
        }
        else if (_selectedInstalledGroupKey != null)
        {
            var selectedGroup = _mods
                .GroupBy(entry => entry.GroupKey)
                .FirstOrDefault(group => string.Equals(ToSelectionKey(group.Key), _selectedInstalledGroupKey, StringComparison.OrdinalIgnoreCase));

            if (selectedGroup == null)
            {
                _selectedInstalledGroupKey = null;
            }
            else
            {
                DrawInstalledGroupDetail(selectedGroup.Key, selectedGroup.ToList());
            }
        }
        else
        {
            foreach (var modGroup in _mods.GroupBy(entry => entry.GroupKey).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                DrawInstalledGroupCard(modGroup.Key, modGroup.ToList());
            }
        }

        GUILayout.EndScrollView();
    }

    [HideFromIl2Cpp]
    private void DrawInstalledGroupCard(string groupKey, IReadOnlyList<ManagedModEntry> mods)
    {
        if (mods.Count == 0)
        {
            return;
        }

        var groupName = mods[0].GroupName;
        var enabledCount = mods.Count(entry => entry.IsEnabled);
        var disabledCount = mods.Count - enabledCount;

        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.BeginHorizontal();
        GUILayout.Label(groupName, _sectionStyle!);
        GUILayout.FlexibleSpace();
        DrawPill($"{mods.Count} DLL", _accentTexture!);
        GUILayout.EndHorizontal();
        GUILayout.Label($"Contains {mods.Count} DLLs", _subtitleStyle!);
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUI.enabled = enabledCount > 0 && mods.Any(entry => !entry.IsSelf);
        if (FancyButton("Disable All", _buttonRedTexture!, _buttonRedHoverTexture!, _buttonRedActiveTexture!, 120f))
        {
            var result = SafeRun(() => _modFileService.DisableGroup(groupKey));
            ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{groupName} was disabled.");
            RefreshMods();
        }

        GUI.enabled = disabledCount > 0;
        GUILayout.Space(8f);
        if (FancyButton("Enable All", _buttonGreenTexture!, _buttonGreenHoverTexture!, _buttonGreenActiveTexture!, 120f))
        {
            var result = SafeRun(() => _modFileService.EnableGroup(groupKey));
            ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{groupName} was enabled.");
            RefreshMods();
        }

        GUI.enabled = true;
        GUILayout.Space(8f);
        if (FancyButton("Open Folder", _buttonBlueTexture!, _buttonBlueHoverTexture!, _buttonBlueActiveTexture!, 130f))
        {
            _selectedInstalledGroupKey = ToSelectionKey(groupKey);
            _modsScrollPosition = Vector2.zero;
            GUIUtility.ExitGUI();
        }

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.Space(10f);
    }

    [HideFromIl2Cpp]
    private void DrawInstalledGroupDetail(string groupKey, IReadOnlyList<ManagedModEntry> mods)
    {
        if (mods.Count == 0)
        {
            _selectedInstalledGroupKey = null;
            return;
        }

        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.BeginHorizontal();
        if (FancyButton("Back To Folders", _buttonSlateTexture!, _buttonSlateHoverTexture!, _buttonSlateActiveTexture!, 140f))
        {
            _selectedInstalledGroupKey = null;
            _modsScrollPosition = Vector2.zero;
            GUIUtility.ExitGUI();
        }

        GUILayout.Space(8f);
        GUILayout.Label(mods[0].GroupName, _sectionStyle!);
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);
        GUILayout.Label($"Enabled: {mods.Count(entry => entry.IsEnabled)}  |  Disabled: {mods.Count(entry => !entry.IsEnabled)}", _subtitleStyle!);
        GUILayout.Space(8f);

        foreach (var mod in mods)
        {
            DrawModRow(mod);
        }

        GUILayout.EndVertical();
        GUILayout.Space(10f);
    }

    [HideFromIl2Cpp]
    private void DrawModRow(ManagedModEntry mod)
    {
        var state = mod.IsEnabled ? "Enabled" : "Disabled";
        var selfSuffix = mod.IsSelf ? " (this manager)" : "";

        GUILayout.BeginVertical(new GUIContent(""), _cardStyle!);
        GUILayout.BeginHorizontal();
        GUILayout.Label(mod.Name + selfSuffix, _sectionStyle!);
        GUILayout.FlexibleSpace();
        DrawPill(state, mod.IsEnabled ? _successTexture! : _dangerTexture!);
        GUILayout.EndHorizontal();

        GUILayout.Label(mod.RelativePath, _pathStyle!);
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();

        if (mod.IsEnabled)
        {
            GUI.enabled = !mod.IsSelf;
            if (FancyButton("Disable", _buttonRedTexture!, _buttonRedHoverTexture!, _buttonRedActiveTexture!, 130f))
            {
                var result = SafeRun(() => _modFileService.Disable(mod.FullPath));
                ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{mod.Name} was disabled.");
                RefreshMods();
            }

            GUI.enabled = true;
        }
        else
        {
            if (FancyButton("Enable", _buttonGreenTexture!, _buttonGreenHoverTexture!, _buttonGreenActiveTexture!, 120f))
            {
                var result = SafeRun(() => _modFileService.Enable(mod.FullPath));
                ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{mod.Name} was enabled.");
                RefreshMods();
            }

            GUILayout.Space(8f);
            if (FancyButton("Delete", _buttonRedTexture!, _buttonRedHoverTexture!, _buttonRedActiveTexture!, 110f))
            {
                var result = SafeRun(() => _modFileService.DeleteDisabled(mod.FullPath));
                ApplyActionResult(result);
                RefreshMods();
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    [HideFromIl2Cpp]
    private void DrawStagedModsList()
    {
        _importsScrollPosition = GUILayout.BeginScrollView(_importsScrollPosition, GUILayout.Height(250f));

        if (_stagedMods.Count == 0)
        {
            GUILayout.BeginVertical(new GUIContent(""), _cardStyle!);
            GUILayout.Label("No staged DLLs found yet.", _sectionStyle!);
            GUILayout.Label("Put downloaded mod DLLs in the import folder and they will appear here.", _bodyStyle!);
            GUILayout.EndVertical();
        }
        else if (_selectedStagedGroupKey != null)
        {
            var selectedGroup = _stagedMods
                .GroupBy(entry => entry.GroupKey)
                .FirstOrDefault(group => string.Equals(ToSelectionKey(group.Key), _selectedStagedGroupKey, StringComparison.OrdinalIgnoreCase));

            if (selectedGroup == null)
            {
                _selectedStagedGroupKey = null;
            }
            else
            {
                DrawStagedGroupDetail(selectedGroup.Key, selectedGroup.ToList());
            }
        }
        else
        {
            foreach (var stagedGroup in _stagedMods.GroupBy(entry => entry.GroupKey).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                DrawStagedGroupCard(stagedGroup.Key, stagedGroup.ToList());
            }
        }

        GUILayout.EndScrollView();
    }

    [HideFromIl2Cpp]
    private void DrawStagedGroupCard(string groupKey, IReadOnlyList<StagedModEntry> stagedMods)
    {
        if (stagedMods.Count == 0)
        {
            return;
        }

        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.BeginHorizontal();
        GUILayout.Label(stagedMods[0].GroupName, _sectionStyle!);
        GUILayout.FlexibleSpace();
        DrawPill($"{stagedMods.Count} Queued", _warningTexture!);
        GUILayout.EndHorizontal();
        GUILayout.Label($"Contains {stagedMods.Count} DLLs", _subtitleStyle!);
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (FancyButton("Install Folder", _buttonGreenTexture!, _buttonGreenHoverTexture!, _buttonGreenActiveTexture!, 130f))
        {
            var result = SafeRun(() => _modFileService.InstallStagedGroup(groupKey));
            ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{stagedMods[0].GroupName} was installed.");
            RefreshMods();
            GUIUtility.ExitGUI();
        }

        GUILayout.Space(8f);
        if (FancyButton("Open Folder", _buttonBlueTexture!, _buttonBlueHoverTexture!, _buttonBlueActiveTexture!, 130f))
        {
            _selectedStagedGroupKey = ToSelectionKey(groupKey);
            _importsScrollPosition = Vector2.zero;
            GUIUtility.ExitGUI();
        }

        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUILayout.Space(10f);
    }

    [HideFromIl2Cpp]
    private void DrawStagedGroupDetail(string groupKey, IReadOnlyList<StagedModEntry> stagedMods)
    {
        if (stagedMods.Count == 0)
        {
            _selectedStagedGroupKey = null;
            return;
        }

        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.BeginHorizontal();
        if (FancyButton("Back To Folders", _buttonSlateTexture!, _buttonSlateHoverTexture!, _buttonSlateActiveTexture!, 140f))
        {
            _selectedStagedGroupKey = null;
            _importsScrollPosition = Vector2.zero;
            GUIUtility.ExitGUI();
        }

        GUILayout.Space(8f);
        GUILayout.Label(stagedMods[0].GroupName, _sectionStyle!);
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);
        GUILayout.Label($"{stagedMods.Count} DLLs waiting in this folder", _subtitleStyle!);
        GUILayout.Space(8f);
        if (FancyButton("Install Whole Folder", _buttonGreenTexture!, _buttonGreenHoverTexture!, _buttonGreenActiveTexture!, 170f))
        {
            var result = SafeRun(() => _modFileService.InstallStagedGroup(groupKey));
            ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{stagedMods[0].GroupName} was installed.");
            RefreshMods();
            GUIUtility.ExitGUI();
        }
        GUILayout.Space(8f);

        foreach (var stagedMod in stagedMods)
        {
            GUILayout.BeginVertical(new GUIContent(""), _cardStyle!);
            GUILayout.BeginHorizontal();
            GUILayout.Label(stagedMod.Name, _sectionStyle!);
            GUILayout.FlexibleSpace();
            DrawPill("Ready", _warningTexture!);
            GUILayout.EndHorizontal();
            GUILayout.Label(stagedMod.RelativePath, _pathStyle!);
            GUILayout.Space(6f);
            if (FancyButton("Install", _buttonBlueTexture!, _buttonBlueHoverTexture!, _buttonBlueActiveTexture!, 150f))
            {
                var result = SafeRun(() => _modFileService.InstallStagedMod(stagedMod.FullPath));
                ApplyActionResult(result, promptForRestart: result.Succeeded, restartReason: $"{stagedMod.Name} was installed.");
                RefreshMods();
            }

            GUILayout.EndVertical();
        }

        GUILayout.EndVertical();
        GUILayout.Space(10f);
    }

    [HideFromIl2Cpp]
    private void RefreshMods()
    {
        _mods = _modFileService.GetMods().ToList();
        _stagedMods = _modFileService.GetStagedMods().ToList();

        if (_selectedInstalledGroupKey != null &&
            !_mods.Any(entry => string.Equals(ToSelectionKey(entry.GroupKey), _selectedInstalledGroupKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedInstalledGroupKey = null;
        }

        if (_selectedStagedGroupKey != null &&
            !_stagedMods.Any(entry => string.Equals(ToSelectionKey(entry.GroupKey), _selectedStagedGroupKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedStagedGroupKey = null;
        }
    }

    [HideFromIl2Cpp]
    private static string ToSelectionKey(string? groupKey)
    {
        return string.IsNullOrWhiteSpace(groupKey) ? LooseGroupSelectionKey : groupKey;
    }

    [HideFromIl2Cpp]
    private void StartPackDownload()
    {
        if (_packDownloadTask != null)
        {
            StarterPlugin.Log.LogInfo("Victor Launcher ignored download click because a download is already running.");
            return;
        }

        _activePackCode = _editableRoomCode.Trim().ToUpperInvariant();
        StarterPlugin.Log.LogInfo($"Victor Launcher StartPackDownload with room code '{_activePackCode}'.");
        if (string.IsNullOrWhiteSpace(_activePackCode))
        {
            ApplyActionResult(ModActionResult.Fail("Enter a room code first."));
            return;
        }

        _statusMessage = $"Looking up room code {_activePackCode}...";
        _lastActionSucceeded = true;
        _packDownloadTask = _supabasePackService.DownloadPackAsync(_activePackCode, _modFileService);
    }

    [HideFromIl2Cpp]
    private void CompletePackDownloadIfReady()
    {
        if (_packDownloadTask == null || !_packDownloadTask.IsCompleted)
        {
            return;
        }

        ModActionResult result;
        try
        {
            result = _packDownloadTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            StarterPlugin.Log.LogError(exception);
            result = ModActionResult.Fail("The pack download failed unexpectedly.");
        }

        _packDownloadTask = null;
        ApplyActionResult(result);
        RefreshMods();
        StarterPlugin.Log.LogInfo($"Victor Launcher download finished: {result.Message}");
    }

    [HideFromIl2Cpp]
    private void ApplyActionResult(ModActionResult result, bool promptForRestart = false, string restartReason = "")
    {
        _lastActionSucceeded = result.Succeeded;
        _statusMessage = result.Message;

        if (!promptForRestart)
        {
            return;
        }

        _restartPromptVisible = true;
        _restartPromptReason = restartReason;
    }

    [HideFromIl2Cpp]
    private void DrawRestartPrompt()
    {
        GUILayout.BeginVertical(new GUIContent(""), _statusStyle!);
        GUILayout.Label("Restart Required", _sectionStyle!);
        GUILayout.Label(
            string.IsNullOrWhiteSpace(_restartPromptReason)
                ? "A mod change was made. Close and restart Among Us manually to apply it."
                : _restartPromptReason + " Close and restart Among Us manually to apply the change.",
            _bodyStyle!);
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        if (FancyButton("Close Now", _buttonRedTexture!, _buttonRedHoverTexture!, _buttonRedActiveTexture!, 140f))
        {
            CloseGameNow();
        }

        GUILayout.Space(8f);
        if (FancyButton("Later", _buttonSlateTexture!, _buttonSlateHoverTexture!, _buttonSlateActiveTexture!, 100f))
        {
            _restartPromptVisible = false;
            _restartPromptReason = "";
            _statusMessage = "Restart postponed. Your changes will apply on the next launch.";
            _lastActionSucceeded = true;
        }

        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    [HideFromIl2Cpp]
    private void CloseGameNow()
    {
        var result = SafeRun(_gameProcessService.CloseAmongUs);
        _restartPromptVisible = false;
        _restartPromptReason = "";
        ApplyActionResult(result);
    }

    [HideFromIl2Cpp]
    private void DrawStatChip(string label, string value, Texture2D texture)
    {
        var chipStyle = new GUIStyle(_cardStyle!);
        chipStyle.normal.background = texture;

        GUILayout.BeginVertical(new GUIContent(""), chipStyle, GUILayout.Width(150f), GUILayout.Height(54f));
        GUILayout.Label(label, _subtitleStyle!);
        GUILayout.Label(value, _titleStyle!);
        GUILayout.EndVertical();
    }

    [HideFromIl2Cpp]
    private void DrawPill(string text, Texture2D texture)
    {
        var pill = new GUIStyle(_pillStyle!)
        {
            normal = { background = texture }
        };

        GUILayout.Label(text, pill, GUILayout.Width(92f), GUILayout.Height(24f));
    }

    [HideFromIl2Cpp]
    private void EnsureStyles()
    {
        EnsureTextures();

        _scrimStyle = new GUIStyle(GUI.skin.box);
        _scrimStyle.normal.background = _screenScrimTexture;

        _backdropStyle = new GUIStyle(GUI.skin.box);
        _backdropStyle.normal.background = _windowBackdropTexture;

        _windowStyle = new GUIStyle(GUI.skin.window);
        _windowStyle.normal.background = _windowBackdropTexture;
        _windowStyle.normal.textColor = Color.white;

        _titleStyle = new GUIStyle(GUI.skin.label);
        _titleStyle.fontSize = 24;
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.normal.textColor = Color.white;

        _subtitleStyle = new GUIStyle(GUI.skin.label);
        _subtitleStyle.fontSize = 12;
        _subtitleStyle.normal.textColor = new Color(0.61f, 0.70f, 0.82f, 1f);

        _sectionStyle = new GUIStyle(GUI.skin.label);
        _sectionStyle.fontSize = 17;
        _sectionStyle.fontStyle = FontStyle.Bold;
        _sectionStyle.normal.textColor = new Color(0.94f, 0.97f, 1f, 1f);

        _bodyStyle = new GUIStyle(GUI.skin.label);
        _bodyStyle.wordWrap = true;
        _bodyStyle.fontSize = 12;
        _bodyStyle.normal.textColor = new Color(0.76f, 0.83f, 0.91f, 1f);

        _pathStyle = new GUIStyle(_bodyStyle);
        _pathStyle.fontSize = 11;
        _pathStyle.wordWrap = true;
        _pathStyle.normal.textColor = new Color(0.44f, 0.73f, 0.96f, 1f);

        _pillStyle = new GUIStyle(GUI.skin.label);
        _pillStyle.alignment = TextAnchor.MiddleCenter;
        _pillStyle.fontStyle = FontStyle.Bold;
        _pillStyle.fontSize = 11;
        _pillStyle.normal.textColor = Color.white;

        _statusStyle = new GUIStyle(GUI.skin.box);
        _statusStyle.normal.background = _cardTexture;

        _cardStyle = new GUIStyle(GUI.skin.box);
        _cardStyle.normal.background = CreateRoundedTexture(240, 88, new Color(0.07f, 0.12f, 0.20f, 0.98f), 14, 1.2f);

        _inputBoxStyle = new GUIStyle(GUI.skin.box);
        _inputBoxStyle.normal.background = CreateRoundedTexture(180, 34, new Color(0.05f, 0.10f, 0.17f, 1f), 12, 1.2f);

        _inputTextStyle = new GUIStyle(GUI.skin.label);
        _inputTextStyle.fontSize = 14;
        _inputTextStyle.fontStyle = FontStyle.Bold;
        _inputTextStyle.alignment = TextAnchor.MiddleLeft;
        _inputTextStyle.normal.textColor = Color.white;

        GUI.skin.verticalScrollbar.normal.background = _scrollTrackTexture;
        GUI.skin.verticalScrollbarThumb.normal.background = _scrollThumbTexture;
    }

    [HideFromIl2Cpp]
    private void EnsureTextures()
    {
        if (_cardTexture != null &&
            _screenScrimTexture != null &&
            _windowBackdropTexture != null &&
            _clearTexture != null &&
            _scrollTrackTexture != null &&
            _scrollThumbTexture != null &&
            _buttonBlueTexture != null &&
            _buttonBlueHoverTexture != null &&
            _buttonBlueActiveTexture != null)
        {
            return;
        }

        _cardTexture = CreateRoundedTexture(256, 144, new Color(0.06f, 0.11f, 0.18f, 0.98f), 22, 1.8f);
        _screenScrimTexture = CreateTexture(new Color(0f, 0f, 0f, 0.38f));
        _windowBackdropTexture = CreateRoundedTexture(840, 700, new Color(0.03f, 0.07f, 0.12f, 0.985f), 34, 2.2f);
        _clearTexture = CreateTexture(new Color(0f, 0f, 0f, 0f));
        _accentTexture = CreateRoundedTexture(120, 54, new Color(0.16f, 0.49f, 0.93f, 1f), 14, 1.2f);
        _successTexture = CreateRoundedTexture(92, 24, new Color(0.10f, 0.60f, 0.42f, 1f), 12, 1.2f);
        _dangerTexture = CreateRoundedTexture(92, 24, new Color(0.78f, 0.24f, 0.33f, 1f), 12, 1.2f);
        _warningTexture = CreateRoundedTexture(92, 24, new Color(0.88f, 0.58f, 0.14f, 1f), 12, 1.2f);
        _scrollTrackTexture = CreateRoundedTexture(16, 96, new Color(0.05f, 0.10f, 0.16f, 0.92f), 8, 1.2f);
        _scrollThumbTexture = CreateRoundedTexture(16, 48, new Color(0.24f, 0.35f, 0.50f, 1f), 8, 1.2f);
        _buttonBlueTexture = CreateRoundedTexture(140, 36, new Color(0.16f, 0.49f, 0.93f, 1f), 14, 1.2f);
        _buttonPurpleTexture = CreateRoundedTexture(140, 36, new Color(0.42f, 0.31f, 0.78f, 1f), 14, 1.2f);
        _buttonRedTexture = CreateRoundedTexture(140, 36, new Color(0.72f, 0.24f, 0.31f, 1f), 14, 1.2f);
        _buttonGreenTexture = CreateRoundedTexture(140, 36, new Color(0.11f, 0.58f, 0.39f, 1f), 14, 1.2f);
        _buttonSlateTexture = CreateRoundedTexture(140, 36, new Color(0.19f, 0.27f, 0.39f, 1f), 14, 1.2f);
        _buttonBlueHoverTexture = CreateRoundedTexture(140, 36, new Color(0.22f, 0.55f, 0.98f, 1f), 14, 1.2f);
        _buttonPurpleHoverTexture = CreateRoundedTexture(140, 36, new Color(0.49f, 0.37f, 0.84f, 1f), 14, 1.2f);
        _buttonRedHoverTexture = CreateRoundedTexture(140, 36, new Color(0.80f, 0.29f, 0.37f, 1f), 14, 1.2f);
        _buttonGreenHoverTexture = CreateRoundedTexture(140, 36, new Color(0.15f, 0.65f, 0.45f, 1f), 14, 1.2f);
        _buttonSlateHoverTexture = CreateRoundedTexture(140, 36, new Color(0.25f, 0.34f, 0.47f, 1f), 14, 1.2f);
        _buttonBlueActiveTexture = CreateRoundedTexture(140, 36, new Color(0.11f, 0.40f, 0.79f, 1f), 14, 1.2f);
        _buttonPurpleActiveTexture = CreateRoundedTexture(140, 36, new Color(0.35f, 0.25f, 0.67f, 1f), 14, 1.2f);
        _buttonRedActiveTexture = CreateRoundedTexture(140, 36, new Color(0.60f, 0.18f, 0.25f, 1f), 14, 1.2f);
        _buttonGreenActiveTexture = CreateRoundedTexture(140, 36, new Color(0.08f, 0.49f, 0.32f, 1f), 14, 1.2f);
        _buttonSlateActiveTexture = CreateRoundedTexture(140, 36, new Color(0.14f, 0.20f, 0.30f, 1f), 14, 1.2f);
    }

    [HideFromIl2Cpp]
    private string GetRoomCodeDisplayText()
    {
        var showCursor = _roomCodeInputFocused && Mathf.Repeat(_roomCodeCursorBlinkTimer, 1f) < 0.5f;
        return showCursor ? _editableRoomCode + "|" : _editableRoomCode;
    }

    [HideFromIl2Cpp]
    private void HandleRoomCodeInput(Event currentEvent)
    {
        if (!_isVisible || currentEvent == null)
        {
            _roomCodeInputFocused = false;
            return;
        }

        if (currentEvent.type == EventType.MouseDown)
        {
            if (_roomCodeInputRect.Contains(currentEvent.mousePosition))
            {
                _roomCodeInputFocused = true;
                _roomCodeCursorBlinkTimer = 0f;
                currentEvent.Use();
            }
            else if (!_windowRect.Contains(currentEvent.mousePosition))
            {
                _roomCodeInputFocused = false;
            }
        }

        if (!_roomCodeInputFocused || currentEvent.type != EventType.KeyDown)
        {
            return;
        }

        if (currentEvent.keyCode == KeyCode.Backspace)
        {
            if (_editableRoomCode.Length > 0)
            {
                _editableRoomCode = _editableRoomCode[..^1];
                StarterPlugin.SupabaseRoomCode.Value = _editableRoomCode;
            }

            currentEvent.Use();
            return;
        }

        if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
        {
            _roomCodeInputFocused = false;
            StartPackDownload();
            currentEvent.Use();
            return;
        }

        if (currentEvent.keyCode == KeyCode.Escape)
        {
            _roomCodeInputFocused = false;
            currentEvent.Use();
            return;
        }

        var character = currentEvent.character;
        if (character == '\0' || character == '\b' || character == '\n' || character == '\r')
        {
            return;
        }

        if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
        {
            return;
        }

        if (_editableRoomCode.Length >= 20)
        {
            currentEvent.Use();
            return;
        }

        _editableRoomCode += char.ToUpperInvariant(character);
        StarterPlugin.SupabaseRoomCode.Value = _editableRoomCode;
        _roomCodeCursorBlinkTimer = 0f;
        currentEvent.Use();
    }

    [HideFromIl2Cpp]
    private static Texture2D CreateTexture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, color);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply();
        return texture;
    }

    [HideFromIl2Cpp]
    private bool FancyButton(string label, Texture2D background, Texture2D hoverBackground, Texture2D activeBackground, float width)
    {
        var buttonStyle = new GUIStyle(GUI.skin.button);
        var rect = GUILayoutUtility.GetRect(new GUIContent(label), buttonStyle, GUILayout.Width(width), GUILayout.Height(34f));
        var currentEvent = Event.current;
        var mouseOver = currentEvent != null && rect.Contains(currentEvent.mousePosition);
        var mouseDown = mouseOver && currentEvent != null && currentEvent.type == EventType.MouseDown;
        var selectedBackground = mouseDown ? activeBackground : mouseOver ? hoverBackground : background;

        var backgroundStyle = new GUIStyle(GUI.skin.box);
        backgroundStyle.normal.background = selectedBackground;
        GUI.Box(rect, new GUIContent(""), backgroundStyle);

        buttonStyle.normal.background = _clearTexture;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.fontSize = 12;
        buttonStyle.alignment = TextAnchor.MiddleCenter;

        return GUI.Button(rect, new GUIContent(label), buttonStyle);
    }

    [HideFromIl2Cpp]
    private static Texture2D CreateRoundedTexture(int width, int height, Color fillColor, int radius, float edgeSoftness)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = CalculateRoundedAlpha(x, y, width, height, radius, edgeSoftness);
                texture.SetPixel(x, y, new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * alpha));
            }
        }

        texture.Apply();
        return texture;
    }

    [HideFromIl2Cpp]
    private static Texture2D CreateRoundedGradientTexture(int width, int height, Color topColor, Color bottomColor, int radius)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (var y = 0; y < height; y++)
        {
            var t = height <= 1 ? 0f : (float)y / (height - 1);
            var rowColor = Color.Lerp(topColor, bottomColor, t);

            for (var x = 0; x < width; x++)
            {
                var inside = IsInsideRoundedRect(x, y, width, height, radius);
                texture.SetPixel(x, y, inside ? rowColor : new Color(0f, 0f, 0f, 0f));
            }
        }

        texture.Apply();
        return texture;
    }

    [HideFromIl2Cpp]
    private static float CalculateRoundedAlpha(int x, int y, int width, int height, int radius, float edgeSoftness)
    {
        if (IsInsideRoundedRect(x, y, width, height, radius))
        {
            return 1f;
        }

        var samples = 0;
        var insideSamples = 0;
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                samples++;
                var sampleX = x + (int)(offsetX * edgeSoftness);
                var sampleY = y + (int)(offsetY * edgeSoftness);
                if (IsInsideRoundedRect(sampleX, sampleY, width, height, radius))
                {
                    insideSamples++;
                }
            }
        }

        return insideSamples / (float)samples;
    }

    [HideFromIl2Cpp]
    private static bool IsInsideRoundedRect(int x, int y, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            return true;
        }

        var left = radius;
        var right = width - radius - 1;
        var top = radius;
        var bottom = height - radius - 1;

        if (x >= left && x <= right)
        {
            return true;
        }

        if (y >= top && y <= bottom)
        {
            return true;
        }

        var corners = new[]
        {
            (cx: left, cy: top),
            (cx: right, cy: top),
            (cx: left, cy: bottom),
            (cx: right, cy: bottom)
        };

        foreach (var corner in corners)
        {
            var dx = x - corner.cx;
            var dy = y - corner.cy;
            if (dx * dx + dy * dy <= radius * radius)
            {
                return true;
            }
        }

        return false;
    }

    [HideFromIl2Cpp]
    private static ModActionResult SafeRun(Func<ModActionResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            StarterPlugin.Log.LogError(exception);
            return ModActionResult.Fail(exception.Message);
        }
    }

    [HideFromIl2Cpp]
    internal static bool ShouldBlockGameClickthrough()
    {
        return _instance?._isVisible == true;
    }
}
