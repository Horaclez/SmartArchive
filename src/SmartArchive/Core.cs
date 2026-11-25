using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.VisualBasic.FileIO;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SmartArchive;

public class PasswordException : Exception
{
    public PasswordException() : base("Se requiere contraseña.") { }
    public PasswordException(Exception inner) : base("Contraseña requerida o incorrecta.", inner) { }
}

public static class Core
{
    static Core() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static bool LooksLikePasswordError(Exception ex) =>
        ex is System.Security.Cryptography.CryptographicException || ex is SharpCompress.Common.CryptographicException || Regex.IsMatch(ex.Message, "password|encrypt|crypt", RegexOptions.IgnoreCase);

    /// Abre un comprimido. Si el zip no declara UTF-8, los nombres antiguos de Windows se leen en CP850.
    public static IArchive Open(string path, string? password = null)
    {
        try
        {
            return ArchiveFactory.Open(path, new ReaderOptions
            {
                Password = string.IsNullOrEmpty(password) ? null : password,
                ArchiveEncoding = new ArchiveEncoding { Default = Encoding.GetEncoding(850) }
            });
        }
        catch (Exception ex) when (LooksLikePasswordError(ex)) { throw new PasswordException(ex); }
    }

    /// Verifica la contraseña leyendo la entrada cifrada más pequeña (mejor esfuerzo).
    public static void CheckPassword(IArchive a, string? password)
    {
        var enc = Files(a).Where(f => f.e.IsEncrypted).OrderBy(f => f.e.Size).FirstOrDefault();
        if (enc.e == null) return;
        if (string.IsNullOrEmpty(password)) throw new PasswordException();
        if (enc.e.Size > 32_000_000) return;
        try
        {
            using var s = enc.e.OpenEntryStream();
            var buf = new byte[81920];
            while (s.Read(buf, 0, buf.Length) > 0) { }
        }
        catch (Exception ex) when (ex is not IOException) { throw new PasswordException(ex); }
    }

    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// Repara mojibake (├í, Ã¡, etc.) re-interpretando los bytes como UTF-8 cuando es válido.
    public static string Fix(string name)
    {
        name = name.Replace('\\', '/');
        if (name.All(c => c < 128)) return name;
        foreach (var enc in new[] {
            Encoding.GetEncoding(850, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) })
        {
            try { var s = StrictUtf8.GetString(enc.GetBytes(name)); if (s != name) return s; }
            catch (ArgumentException) { }
        }
        return name;
    }

    public static List<(IArchiveEntry e, string name)> Files(IArchive a) =>
        a.Entries.Where(e => !e.IsDirectory).Select(e => (e, Fix(e.Key ?? ""))).ToList();

    public static string Fmt(long b) =>
        b < 1024 ? $"{b} B" : b < 1 << 20 ? $"{b / 1024.0:0.#} KB" : b < 1 << 30 ? $"{b / 1048576.0:0.#} MB" : $"{b / 1073741824.0:0.##} GB";

    static string Unique(string p)
    {
        if (!Path.Exists(p)) return p;
        var d = Path.GetDirectoryName(p)!; var n = Path.GetFileNameWithoutExtension(p); var x = Path.GetExtension(p);
        for (int i = 2; ; i++) { var c = Path.Combine(d, $"{n} ({i}){x}"); if (!Path.Exists(c)) return c; }
    }

    // ---------------------------------------------------------------- Extracción
    /// 1 carpeta raíz -> directa; 1 archivo -> directo; muchos sueltos -> carpeta con el nombre del comprimido.
    public static string Extract(string path, string? password, Action<long> progress)
    {
        using var a = Open(path, password);
        var files = Files(a);
        if (files.Count == 0) throw new InvalidDataException("El archivo está vacío.");
        bool enc = files.Any(f => f.e.IsEncrypted);
        if (enc && string.IsNullOrEmpty(password)) throw new PasswordException();

        var dest = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var stage = Path.Combine(dest, ".sa_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stage);
        try
        {
            var buf = new byte[81920];
            foreach (var (e, name) in files)
            {
                var target = Path.GetFullPath(Path.Combine(stage, name));
                if (!target.StartsWith(stage + Path.DirectorySeparatorChar)) continue; // anti zip-slip
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var src = e.OpenEntryStream();
                using var dst = File.Create(target);
                int n; while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); progress(n); }
            }
            var tops = files.Select(f => f.name.Split('/')[0]).Distinct().ToList();
            string final;
            if (tops.Count == 1 && files.All(f => f.name.Contains('/')))
            {
                final = Unique(Path.Combine(dest, tops[0]));
                Directory.Move(Path.Combine(stage, tops[0]), final); Directory.Delete(stage, true);
            }
            else if (files.Count == 1)
            {
                final = Unique(Path.Combine(dest, tops[0]));
                File.Move(Path.Combine(stage, tops[0]), final); Directory.Delete(stage, true);
            }
            else
            {
                var baseName = Path.GetFileNameWithoutExtension(path);
                if (baseName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) baseName = baseName[..^4];
                final = Unique(Path.Combine(dest, baseName));
                Directory.Move(stage, final);
            }
            return final;
        }
        catch (Exception ex) when (enc && ex is not IOException && ex is not PasswordException) { throw new PasswordException(ex); }
        finally { if (Directory.Exists(stage)) try { Directory.Delete(stage, true); } catch { } }
    }

    /// Extrae varios en paralelo con un único progreso. Pide contraseña (vía `ask`) solo a los que la necesitan,
    /// reutiliza la última contraseña correcta, y manda a la Papelera solo los que salieron bien.
    public static string?[] Batch(string[] paths, bool recycle, string? seedPassword,
        Func<string, bool, string?> ask, Action<double> prog, List<string> errs)
    {
        long total = 0;
        foreach (var p in paths)
            try { using var a = Open(p); total += a.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size); } catch { }
        long done = 0;
        string? session = seedPassword;
        var gate = new object();
        var res = new string?[paths.Length];

        string? AskLocked(string p, bool wrong, string? tried)
        {
            lock (gate)
            {
                if (session != null && session != tried) return session; // otro archivo ya obtuvo una contraseña nueva
                return ask(p, wrong);
            }
        }

        Parallel.For(0, paths.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, i =>
        {
            var p = paths[i]; var name = Path.GetFileName(p);
            string? pwd = session;
            for (; ; )
            {
                long mine = 0;
                try
                {
                    res[i] = Extract(p, pwd, n => { mine += n; prog(Math.Min(100, 100.0 * Interlocked.Add(ref done, n) / Math.Max(1, total))); });
                    if (!string.IsNullOrEmpty(pwd)) session = pwd;
                    if (recycle) FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    break;
                }
                catch (PasswordException)
                {
                    Interlocked.Add(ref done, -mine);
                    var tried = pwd;
                    pwd = AskLocked(p, !string.IsNullOrEmpty(tried), tried);
                    if (pwd == null) { lock (errs) errs.Add($"{name}: cancelado (requiere contraseña)"); break; }
                }
                catch (Exception ex)
                {
                    Interlocked.Add(ref done, -mine);
                    lock (errs) errs.Add($"{name}: {ex.Message}");
                    break;
                }
            }
        });
        prog(100);
        return res;
    }

    /// Busca el .exe principal: el menos profundo, parecido al nombre del comprimido y el más pesado.
    public static string? FindExe(string final, string archiveName)
    {
        if (File.Exists(final)) return final.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? final : null;
        return Directory.EnumerateFiles(final, "*.exe", System.IO.SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(Path.GetFileName(f), "^(unins|vc_?redist|dxsetup|crashpad|update)", RegexOptions.IgnoreCase))
            .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
            .ThenByDescending(f => Path.GetFileName(f).Contains(archiveName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    // ---------------------------------------------------------------- Compresión
    static readonly string[] JunkDirs = { ".git", "node_modules", "bin", "obj", "__pycache__", ".tmp", ".vs", ".idea" };
    static readonly HashSet<string> Junk = new(JunkDirs, StringComparer.OrdinalIgnoreCase);

    static IEnumerable<string> Walk(string dir, bool ignore)
    {
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (ignore && f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            yield return f;
        }
        foreach (var s in Directory.EnumerateDirectories(dir))
        {
            if (ignore && Junk.Contains(Path.GetFileName(s))) continue;
            foreach (var f in Walk(s, ignore)) yield return f;
        }
    }

    public static string? Find7z()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "7zr.exe");
        if (File.Exists(local)) return local;
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
        return File.Exists(pf) ? pf : null;
    }

    public static string Compress(string folder, string format, bool ignore, string? password, Action<double> prog) =>
        format == "7z" ? Make7z(folder, ignore, password, prog) : MakeZip(folder, ignore, password, prog);

    /// ZIP en UTF-8; con contraseña usa AES-256 (compatible con 7-Zip, WinRAR y este programa).
    static string MakeZip(string folder, bool ignore, string? pwd, Action<double> prog)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var zipPath = Unique(root + ".zip");
        var files = Walk(root, ignore).ToList();
        var top = Path.GetFileName(root);
        bool secure = !string.IsNullOrEmpty(pwd);
        using (var fs = File.Create(zipPath))
        using (var zs = new ZipOutputStream(fs))
        {
            zs.SetLevel(6);
            if (secure) zs.Password = pwd;
            var buf = new byte[81920];
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    var fi = new FileInfo(files[i]);
                    using var src = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var entry = new ZipEntry(top + "/" + Path.GetRelativePath(root, files[i]).Replace('\\', '/'))
                    { DateTime = fi.LastWriteTime, Size = fi.Length, IsUnicodeText = true };
                    if (secure) entry.AESKeySize = 256;
                    zs.PutNextEntry(entry);
                    int n; while ((n = src.Read(buf, 0, buf.Length)) > 0) zs.Write(buf, 0, n);
                    zs.CloseEntry();
                }
                catch (IOException) { } // archivo bloqueado: se omite
                prog(100.0 * (i + 1) / files.Count);
            }
        }
        return zipPath;
    }

    /// .7z vía 7zr.exe (incluido con el instalador) o 7z.exe si está instalado. Con contraseña cifra también los nombres.
    static string Make7z(string folder, bool ignore, string? pwd, Action<double> prog)
    {
        var exe = Find7z() ?? throw new FileNotFoundException("No se encontró 7zr.exe / 7z.exe para crear .7z.");
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var outPath = Unique(root + ".7z");
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(root)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in new[] { "a", "-t7z", "-mx=5", "-bso0", "-bsp1", outPath, Path.GetFileName(root) }) psi.ArgumentList.Add(a);
        if (ignore)
        {
            foreach (var j in JunkDirs) psi.ArgumentList.Add("-xr!" + j);
            psi.ArgumentList.Add("-xr!*.tmp");
        }
        if (!string.IsNullOrEmpty(pwd)) { psi.ArgumentList.Add("-p" + pwd); psi.ArgumentList.Add("-mhe=on"); }

        using var p = Process.Start(psi)!;
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) err.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        var rx = new Regex(@"(\d{1,3})%"); var cbuf = new char[256]; int n;
        while ((n = p.StandardOutput.Read(cbuf, 0, cbuf.Length)) > 0)
        {
            var m = rx.Matches(new string(cbuf, 0, n));
            if (m.Count > 0) prog(double.Parse(m[^1].Groups[1].Value));
        }
        p.WaitForExit();
        if (p.ExitCode > 1) throw new InvalidOperationException("7-Zip falló: " + err.ToString().Trim());
        return outPath;
    }
}
