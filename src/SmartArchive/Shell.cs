using Microsoft.Win32;

namespace SmartArchive;

/// Menú contextual del Explorador. Todo va en HKCU: no requiere administrador y se desinstala limpio.
public static class Shell
{
    static readonly string[] Exts = { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cbz", ".cbr" };
    static readonly string[] ArchiveVerbs = { "SmartArchive.1Extract", "SmartArchive.2Run", "SmartArchive.3Open" };
    static readonly string[] FolderVerbs = { "SmartArchive.1Zip", "SmartArchive.27z" };

    public static void Register()
    {
        var exe = Environment.ProcessPath!;
        foreach (var x in Exts)
        {
            var b = $@"Software\Classes\SystemFileAssociations\{x}\shell\";
            Add(b + ArchiveVerbs[0], "Extraer aquí (Smart Archive)", exe, $"\"{exe}\" --extract \"%1\"");
            Add(b + ArchiveVerbs[1], "Extraer y abrir (Smart Archive)", exe, $"\"{exe}\" --run \"%1\"");
            Add(b + ArchiveVerbs[2], "Ver contenido (Smart Archive)", exe, $"\"{exe}\" \"%1\"");
        }
        var d = @"Software\Classes\Directory\shell\";
        Add(d + FolderVerbs[0], "Comprimir limpio a .zip (Smart Archive)", exe, $"\"{exe}\" --zip \"%1\"");
        Add(d + FolderVerbs[1], "Comprimir limpio a .7z (Smart Archive)", exe, $"\"{exe}\" --7z \"%1\"");
    }

    public static void Unregister()
    {
        foreach (var x in Exts)
            foreach (var v in ArchiveVerbs)
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{x}\shell\{v}", false);
        foreach (var v in FolderVerbs)
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\shell\{v}", false);
    }

    static void Add(string key, string label, string icon, string cmd)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue("", label);
        k.SetValue("Icon", icon);
        using var c = k.CreateSubKey("command");
        c.SetValue("", cmd);
    }
}
