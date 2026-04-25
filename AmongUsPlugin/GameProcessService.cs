using System.Runtime.InteropServices;

namespace AmongUsPlugin;

internal sealed class GameProcessService
{
    public ModActionResult CloseAmongUs()
    {
        try
        {
            UnityEngine.Application.Quit();
            return ModActionResult.Success("Closing Among Us. Restart it manually to apply your mod changes.");
        }
        catch (Exception exception)
        {
            StarterPlugin.Log.LogError(exception);
            return ModActionResult.Fail($"Failed to close Among Us: {exception.Message}");
        }
    }

    public ModActionResult PickDllAndStage(ModFileService modFileService)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ModActionResult.Fail("The file picker is only available on the Windows game machine.");
        }

        var dialogResult = NativeFileDialog.TryPickDll();
        if (!dialogResult.Succeeded)
        {
            return ModActionResult.Fail(dialogResult.Message);
        }

        if (string.IsNullOrWhiteSpace(dialogResult.Path))
        {
            return ModActionResult.Fail("No DLL was selected.");
        }

        return modFileService.StageDll(dialogResult.Path);
    }
}

internal static class NativeFileDialog
{
    public static FileDialogResult TryPickDll()
    {
        var fileBuffer = new string('\0', 4096);
        var initialDirectory = new string('\0', 1024);

        var openFileName = new OpenFileName
        {
            structSize = Marshal.SizeOf<OpenFileName>(),
            filter = "DLL Files\0*.dll\0All Files\0*.*\0\0",
            file = fileBuffer,
            maxFile = fileBuffer.Length,
            fileTitle = new string('\0', 512),
            maxFileTitle = 512,
            initialDir = initialDirectory,
            title = "Choose a mod DLL",
            defExt = "dll",
            flags = 0x00000008 | 0x00001000 | 0x00080000
        };

        var success = GetOpenFileName(ref openFileName);
        if (!success)
        {
            var errorCode = CommDlgExtendedError();
            return errorCode == 0
                ? new FileDialogResult(false, "", "File selection cancelled.")
                : new FileDialogResult(false, "", $"File picker failed with error code {errorCode}.");
        }

        var selectedPath = openFileName.file.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return new FileDialogResult(true, selectedPath, "DLL selected.");
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName openFileName);

    [DllImport("comdlg32.dll", SetLastError = true)]
    private static extern int CommDlgExtendedError();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int structSize;
        public IntPtr dlgOwner;
        public IntPtr instance;
        public string filter;
        public string customFilter;
        public int maxCustomFilter;
        public int filterIndex;
        public string file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDir;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defExt;
        public IntPtr custData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reservedPtr;
        public int reservedInt;
        public int flagsEx;
    }
}

internal sealed record FileDialogResult(bool Succeeded, string Path, string Message);
