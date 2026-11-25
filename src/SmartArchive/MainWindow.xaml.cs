using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SharpCompress.Archives;

namespace SmartArchive;

public record Row(string Name, long Size, IArchiveEntry Entry) { public string SizeText => Core.Fmt(Size); }

public partial class MainWindow : Window
{
    IArchive? _arc;
    string _path = "";
    string? _pwd;
    string _lastTry = "";
    bool _hadError;
    readonly string _mode;
    readonly string[] _paths;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SmartArchive");

    static readonly string[] ArcExt = { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cbz", ".cbr" };
    static readonly HashSet<string> TextExt = new() { ".txt", ".md", ".json", ".cs", ".xml", ".csv", ".log", ".yml", ".yaml", ".ini", ".cfg", ".config", ".js", ".ts", ".py", ".html", ".css", ".sql", ".sh", ".bat", ".csproj", ".sln", ".gitignore" };
    static readonly HashSet<string> ImgExt = new() { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff" };

    public MainWindow(string mode, string[] paths)
    {
        InitializeComponent();
        _mode = mode; _paths = paths;
        if (Core.Find7z() == null) Fmt7z.Visibility = Visibility.Collapsed;
        Closing += (_, _) => { _arc?.Dispose(); try { Directory.Delete(_tmp, true); } catch { } };
        Loaded += async (_, _) => await Startup();
    }

    async Task Startup()
    {
        switch (_mode)
        {
            case "--extract":
            case "--run":
                await Run(_paths.Where(File.Exists).ToArray(), _mode == "--run", true);
                break;
            case "--zip":
            case "--7z":
                foreach (var d in _paths.Where(Directory.Exists))
                    if (!await Compress(d, _mode == "--7z" ? "7z" : "zip", null)) _hadError = true;
                if (!_hadError) AutoClose();
                break;
            default:
                if (_paths.Length > 0 && File.Exists(_paths[0])) Load(_paths[0]);
                break;
        }
    }

    async void AutoClose() { await Task.Delay(900); Close(); }

    // ---------- Abrir / listar (con contraseña) ----------
    void Load(string path, string? pwd = null)
    {
        _lastTry = path;
        for (; ; )
        {
            IArchive? a = null;
            try
            {
                a = Core.Open(path, pwd);
                Core.CheckPassword(a, pwd);
                _arc?.Dispose(); _arc = a; _path = path; _pwd = pwd;
                break;
            }
            catch (PasswordException)
            {
                a?.Dispose();
                var np = PasswordDialog.Ask(this, Path.GetFileName(path), !string.IsNullOrEmpty(pwd));
                if (np == null) { Status.Text = "Contraseña requerida: operación cancelada."; return; }
                pwd = np;
            }
            catch (Exception ex)
            {
                a?.Dispose();
                Status.Text = "No se pudo abrir: " + ex.Message + " (si está protegido, prueba «Abrir con contraseña…»)";
                return;
            }
        }
        var files = Core.Files(_arc!);
        Lst.ItemsSource = files.Select(f => new Row(f.name, f.e.Size, f.e)).ToList();
        BtnExtract.IsEnabled = BtnRun.IsEnabled = true;
        var exe = files.Select(f => f.name).Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                       .OrderBy(n => n.Count(c => c == '/')).FirstOrDefault();
        BtnRun.Content = exe != null ? $"Extraer y abrir ▶ {Path.GetFileName(exe)}" : "Extraer y abrir carpeta";
        Status.Text = $"{Path.GetFileName(path)} — {files.Count} archivos, {Core.Fmt(files.Sum(f => f.e.Size))}"
                      + (string.IsNullOrEmpty(pwd) ? "" : " · 🔑 desbloqueado");
    }

    void BtnOpen_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Comprimidos|*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.cbz;*.cbr|Todos|*.*" };
        if (d.ShowDialog() == true) Load(d.FileName);
    }

    void BtnPwd_Click(object s, RoutedEventArgs e)
    {
        var f = _lastTry;
        if (f == "")
        {
            var d = new OpenFileDialog { Filter = "Comprimidos|*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz|Todos|*.*" };
            if (d.ShowDialog() != true) return;
            f = d.FileName;
        }
        var p = PasswordDialog.Ask(this, Path.GetFileName(f), false);
        if (p != null) Load(f, p);
    }

    void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items) return;
        var archives = items.Where(i => File.Exists(i) && ArcExt.Contains(Path.GetExtension(i).ToLowerInvariant())).ToArray();
        var dirs = items.Where(Directory.Exists).ToArray();
        if (archives.Length == 1 && dirs.Length == 0) Load(archives[0]);
        else if (archives.Length > 1) _ = Run(archives, false);
        else if (dirs.Length == 1) _ = Compress(dirs[0], FormatBox.SelectedIndex == 1 ? "7z" : "zip", PwdBox.Password);
    }

    // ---------- Vista previa sin extraer a disco ----------
    async void Lst_Sel(object s, SelectionChangedEventArgs e)
    {
        TxtBox.Visibility = ImgBox.Visibility = PdfView.Visibility = Visibility.Collapsed;
        if (Lst.SelectedItem is not Row r) return;
        var ext = Path.GetExtension(r.Name).ToLowerInvariant();
        bool pdf = ext == ".pdf", img = ImgExt.Contains(ext), txt = TextExt.Contains(ext);
        if (!pdf && !img && !txt) { ShowText($"Sin vista previa para '{ext}'"); return; }
        if (r.Size > 50_000_000) { ShowText("Archivo demasiado grande para vista previa."); return; }

        byte[] data;
        await _gate.WaitAsync(); // los RAR sólidos no toleran lecturas simultáneas
        try
        {
            data = await Task.Run(() => { using var st = r.Entry.OpenEntryStream(); using var ms = new MemoryStream(); st.CopyTo(ms); return ms.ToArray(); });
        }
        catch (Exception ex) { ShowText("Error: " + ex.Message); return; }
        finally { _gate.Release(); }
        if (!ReferenceEquals(Lst.SelectedItem, r)) return; // el usuario ya cambió de selección

        if (txt) { var t = Encoding.UTF8.GetString(data); ShowText(t.Length > 300_000 ? t[..300_000] + "\n… (recortado)" : t); }
        else if (img)
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(data); bi.EndInit(); bi.Freeze();
            ImgBox.Source = bi; ImgBox.Visibility = Visibility.Visible;
        }
        else // PDF: WebView2 necesita un archivo; va a %TEMP%, nunca a tu carpeta de destino
        {
            Directory.CreateDirectory(_tmp);
            var f = Path.Combine(_tmp, Guid.NewGuid().ToString("N") + ".pdf");
            await File.WriteAllBytesAsync(f, data);
            PdfView.Visibility = Visibility.Visible; PdfView.Source = new Uri(f);
        }
    }

    void ShowText(string t) { TxtBox.Text = t; TxtBox.Visibility = Visibility.Visible; }

    // ---------- Extraer ----------
    void BtnExtract_Click(object s, RoutedEventArgs e) { if (_path != "") _ = Run(new[] { _path }, false); }
    void BtnRun_Click(object s, RoutedEventArgs e) { if (_path != "") _ = Run(new[] { _path }, true); }

    async Task Run(string[] paths, bool open, bool auto = false)
    {
        if (paths.Length == 0) { if (auto) Close(); return; }
        Busy(true);
        var errs = new List<string>();
        bool rec = ChkRecycle.IsChecked == true;
        string? seed = paths.Contains(_path) ? _pwd : null;
        if (rec && paths.Contains(_path)) { _arc?.Dispose(); _arc = null; Lst.ItemsSource = null; BtnExtract.IsEnabled = BtnRun.IsEnabled = false; }
        var prog = new Progress<double>(v => Bar.Value = v);
        Func<string, bool, string?> ask = (file, wrong) => Dispatcher.Invoke(() => PasswordDialog.Ask(this, Path.GetFileName(file), wrong));
        var res = await Task.Run(() => Core.Batch(paths, rec, seed, ask, v => ((IProgress<double>)prog).Report(v), errs));
        var ok = res.Where(r => r != null).Select(r => r!).ToList();
        Status.Text = $"✔ {ok.Count} de {paths.Length} extraído(s)" + (rec && ok.Count > 0 ? " · comprimido(s) en la Papelera" : "")
                      + (errs.Count > 0 ? " · Errores: " + string.Join(" | ", errs) : "");
        if (ok.Count == 1)
        {
            var target = ok[0];
            if (open)
            {
                var exe = Core.FindExe(target, Path.GetFileNameWithoutExtension(paths[0]));
                if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
                else Process.Start("explorer.exe", File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"");
            }
            else if (auto) Process.Start("explorer.exe", File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"");
        }
        Busy(false);
        if (auto && errs.Count == 0) AutoClose();
    }

    // ---------- Comprimir ----------
    void BtnZip_Click(object s, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog();
        if (d.ShowDialog() == true) _ = Compress(d.FolderName, FormatBox.SelectedIndex == 1 ? "7z" : "zip", PwdBox.Password);
    }

    async Task<bool> Compress(string folder, string format, string? pwd)
    {
        Busy(true);
        try
        {
            bool ignore = ChkIgnore.IsChecked == true; // leer controles ANTES de saltar a otro hilo
            var prog = new Progress<double>(v => Bar.Value = v);
            var z = await Task.Run(() => Core.Compress(folder, format, ignore, pwd, v => ((IProgress<double>)prog).Report(v)));
            Status.Text = $"✔ {z} ({Core.Fmt(new FileInfo(z).Length)})"
                          + (string.IsNullOrEmpty(pwd) ? "" : format == "7z" ? " · cifrado AES-256 (incluye nombres)" : " · cifrado AES-256");
            return true;
        }
        catch (Exception ex) { Status.Text = "Error al comprimir: " + ex.Message; return false; }
        finally { Busy(false); }
    }

    void Busy(bool b) { IsEnabled = !b; if (b) Bar.Value = 0; }
}
