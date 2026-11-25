using System.Windows;

namespace SmartArchive;

/// Uso:
///   SmartArchive.exe [archivo]                 abre la ventana
///   SmartArchive.exe --extract <archivos...>   extracción inteligente (menú contextual)
///   SmartArchive.exe --run <archivo>           extraer y abrir ejecutable/carpeta
///   SmartArchive.exe --zip|--7z <carpeta>      comprimir limpio
///   SmartArchive.exe --register-shell | --unregister-shell
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--register-shell") { Shell.Register(); return 0; }
        if (args.Length > 0 && args[0] == "--unregister-shell") { Shell.Unregister(); return 0; }

        var mode = args.Length > 0 && args[0].StartsWith("--") ? args[0] : "";
        var paths = args.Where(a => !a.StartsWith("--")).ToArray();

        // Al seleccionar varios archivos, el Explorador lanza un proceso por archivo.
        // El primero espera un instante, junta las rutas de los demás y hace UN solo lote con una barra.
        if (mode is "--extract" or "--run")
        {
            var queue = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartArchive", "queue");
            Directory.CreateDirectory(queue);
            using var mutex = new Mutex(false, "SmartArchive.CtxQueue");
            if (!mutex.WaitOne(0))
            {
                File.WriteAllLines(Path.Combine(queue, Guid.NewGuid().ToString("N") + ".txt"), paths);
                return 0;
            }
            Thread.Sleep(700);
            mutex.ReleaseMutex();
            Thread.Sleep(150);
            var all = paths.ToList();
            foreach (var f in Directory.GetFiles(queue, "*.txt"))
            {
                try { all.AddRange(File.ReadAllLines(f)); File.Delete(f); } catch { }
            }
            paths = all.Distinct().ToArray();
        }

        var app = new Application();
        app.Run(new MainWindow(mode, paths));
        return 0;
    }
}
