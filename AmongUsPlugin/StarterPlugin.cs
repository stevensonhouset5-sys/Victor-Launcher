using HarmonyLib;

namespace AmongUsPlugin;

[BepInEx.BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class StarterPlugin : BepInEx.Unity.IL2CPP.BasePlugin
{
    internal const string DefaultSupabaseUrl = "";
    internal const string DefaultSupabaseAnonKey = "";
    internal const string DefaultManifestSigningPublicKeyPem = """
                                                                  -----BEGIN PUBLIC KEY-----
                                                                  MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAzR9LkNcG6pfwzvgKsImv
                                                                  oXwme875XnIRsZxdJp0L5X92UMY1dn/ylHfwr4s/ddePbwvXsUGLupHYGqFDUE4P
                                                                  XkpBDrERfeBi+A3W1foZIcyiqsAyTm6ni1JIoGj4dVolqkSW4E5oA5FQ8+YaRKcH
                                                                  yid6X6RHny4ivFXXrQr3SIU8JYbdFtmt3AC3AJ0w4+qCWWv3COc3IspYQSbm/x5f
                                                                  S7YIv4cOhzuRQ/n1lxehRENJuakW/xWc2LciH+0gW4me95byj7zsA+BUEvGBWClt
                                                                  mvxiKvpARyj5DmLbBaaTm66Z22MOa3h0XBCJARXln++2VK6oNImxrOuFfNC3Ilmc
                                                                  uwIDAQAB
                                                                  -----END PUBLIC KEY-----
                                                                  """;
    internal static new BepInEx.Logging.ManualLogSource Log { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigEntry<string> SupabaseUrl { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigEntry<string> SupabaseAnonKey { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigEntry<string> SupabaseManifestRpc { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigEntry<string> SupabaseRoomCode { get; private set; } = null!;
    internal static BepInEx.Configuration.ConfigEntry<string> ManifestSigningPublicKeyPem { get; private set; } = null!;
    private static bool _attachAttempted;

    private Harmony? _harmony;

    public override void Load()
    {
        Log = base.Log;
        SupabaseUrl = Config.Bind("Supabase", "ProjectUrl", DefaultSupabaseUrl, "Your Supabase project URL, for example https://your-project.supabase.co");
        SupabaseAnonKey = Config.Bind("Supabase", "AnonKey", DefaultSupabaseAnonKey, "Your Supabase anon key used to call the Victor Launcher room endpoints.");
        SupabaseManifestRpc = Config.Bind("Supabase", "ManifestRpc", "room-manifest", "The Supabase Edge Function Victor Launcher calls to fetch a signed room manifest by code.");
        SupabaseRoomCode = Config.Bind("Supabase", "RoomCode", "", "The current room code Victor Launcher should use when you press Download Pack.");
        ManifestSigningPublicKeyPem = Config.Bind("Supabase", "ManifestSigningPublicKeyPem", DefaultManifestSigningPublicKeyPem, "The PEM-encoded public key Victor Launcher uses to verify signed room manifests. Rotate this before public release.");
        Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded.");

        _harmony = new Harmony(PluginInfo.Guid);
        _harmony.PatchAll();
        TryAttachManagerUi("plugin load");

        Log.LogInfo("Harmony patches applied.");
        Log.LogInfo("Victor Launcher UI registered. Press F7 in game to open it.");
    }

    internal static void TryAttachManagerUi(string source)
    {
        if (_attachAttempted)
        {
            return;
        }

        _attachAttempted = true;

        try
        {
            BepInEx.Unity.IL2CPP.IL2CPPChainloader.AddUnityComponent(typeof(ModManagerBehaviour));
            Log.LogInfo($"Requested mod manager UI attach from {source}.");
        }
        catch (Exception exception)
        {
            Log.LogError($"Failed to attach mod manager UI from {source}.");
            Log.LogError(exception);
        }
    }

    internal static void HandleF7Pressed(HudManager hudManager)
    {
        Log.LogInfo("F7 pressed while HudManager was active.");

        try
        {
            hudManager.Notifier?.AddDisconnectMessage("Starter Mod Manager hotkey detected");
        }
        catch (Exception exception)
        {
            Log.LogError("Failed to show F7 debug notification.");
            Log.LogError(exception);
        }
    }
}
