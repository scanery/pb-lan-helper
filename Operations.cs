using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace PBLanHelper;

public static class Operations
{
    const int StallMs = 6000; // sem resposta por 6s = origem considerada caída

    sealed record RemoteInfo(string Smb, long Length, DateTime MtimeUtc);
    sealed record Plan(string Rel, string Local, RemoteInfo? Remote, bool NeedCopy);
    sealed record EntryInfo(string Full, long Len, DateTime M, bool IsDir);

    sealed class SourcePool
    {
        readonly object _l = new();
        readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Order { get; }
        public int Count => Order.Count;

        public SourcePool(IEnumerable<string> order) => Order = order.ToList();

        public void MarkDown(string s) { lock (_l) _down.Add(s); }

        public List<string> Available()
        {
            lock (_l) return Order.Where(s => !_down.Contains(s)).ToList();
        }

        public string? Next(HashSet<string> skip)
        {
            lock (_l)
            {
                var c = Order.FirstOrDefault(s => !_down.Contains(s) && !skip.Contains(s));
                if (c != null) return c;
                _down.Clear(); // última chance: tenta de novo as que caíram
                return Order.FirstOrDefault(s => !skip.Contains(s));
            }
        }
    }

    // ================= INSTALAR =================
    public static async Task Install(StepRunner r, CancellationToken ct)
    {
        var dest = AppSettings.Destination.Trim();
        var smbs = ActiveValues(AppSettings.LoadSmbs());
        var zips = ActiveValues(AppSettings.LoadZips());
        if (smbs.Count == 0) throw new InvalidOperationException("Nenhuma origem de rede ativa. Ative ou adicione uma nas Configurações.");
        if (zips.Count == 0) throw new InvalidOperationException("Nenhum arquivo .zip ativo. Ative ou adicione um nas Configurações.");

        var pool = new SourcePool(smbs);
        var plans = new List<Plan>();

        var sNet = r.Add("Verificando conexão com a rede");
        var sKill = r.Add("Encerrando o jogo, se estiver aberto");
        var sDir = r.Add($"Criando pasta {dest}");
        var sPlan = r.Add("Comparando arquivos locais com a rede");
        var copy = zips.Select(z => r.Add($"Copiando {Path.GetFileName(z)}")).ToArray();
        var extract = zips.Select(z => r.Add($"Extraindo {Path.GetFileName(z)}")).ToArray();
        var sLnk = r.Add("Criando atalhos na área de trabalho");

        // 1) Quais origens respondem (em paralelo, com timeout)
        await r.Run(sNet, async _ =>
        {
            var results = await Task.WhenAll(smbs.Select(async s => (Smb: s, Ok: await IsReachable(s, ct))));
            foreach (var offline in results.Where(y => !y.Ok)) pool.MarkDown(offline.Smb);
            var up = results.Where(y => y.Ok).Select(y => y.Smb).ToList();
            var down = results.Where(y => !y.Ok).Select(y => y.Smb).ToList();
            if (up.Count == 0)
            {
                sNet.Text = "Nenhuma origem de rede acessível";
                sNet.Detail = "Usando apenas arquivos locais completos, se existirem.";
            }
            else
            {
                sNet.Text = up.Count == 1 ? $"Conectado em {up[0]}" : $"Conectado em {up[0]} (+{up.Count - 1} reserva)";
                sNet.Detail = down.Count > 0 ? $"Indisponível: {string.Join(", ", down)}" : "";
            }
        });

        await r.Run(sKill, _ => Task.Run(KillGame, ct));

        await r.Run(sDir, _ =>
        {
            Directory.CreateDirectory(dest);
            return Task.CompletedTask;
        });

        // 2) Estilo rsync: o que já existe completo não é copiado de novo
        await r.Run(sPlan, async _ =>
        {
            foreach (var rel in zips)
            {
                var local = Path.Combine(dest, Path.GetFileName(rel));
                var remote = await GetRemoteInfo(pool, rel, ct);
                var complete = await Task.Run(() =>
                {
                    var fi = new FileInfo(local);
                    if (!fi.Exists || !IsValidZip(local)) return false;
                    return remote == null ||
                           (fi.Length == remote.Length && remote.MtimeUtc <= fi.LastWriteTimeUtc.AddSeconds(2));
                }, ct);

                if (!complete && remote == null)
                    throw new FileNotFoundException($"{rel} não foi encontrado em nenhuma origem e não existe cópia local completa.");

                plans.Add(new Plan(rel, local, remote, !complete));
            }
            int n = plans.Count(x => x.NeedCopy);
            sPlan.Detail = $"{n} para copiar · {plans.Count - n} já completo(s)";
        });

        // 3) Cópia com failover entre as origens
        for (int i = 0; i < zips.Count; i++)
        {
            int k = i;
            await r.Run(copy[k], async p =>
            {
                var plan = plans[k];
                var name = Path.GetFileName(plan.Rel);
                if (!plan.NeedCopy)
                {
                    copy[k].Text = $"{name} já está atualizado";
                    copy[k].Detail = "Cópia local completa, pulando.";
                    return;
                }
                var info = new Progress<string>(s => copy[k].Detail = s);
                await CopyWithFailoverAsync(pool, plan.Rel, plan.Remote!, plan.Local, p, info, ct);
            });
        }

        // 4) Extração paralela
        for (int i = 0; i < zips.Count; i++)
        {
            int k = i;
            await r.Run(extract[k], p =>
            {
                var info = new Progress<string>(s => extract[k].Detail = s);
                return Task.Run(() => ExtractZip(plans[k].Local, dest, p, info, ct), ct);
            });
        }

        // 5) Atalhos
        await r.Run(sLnk, _ => Task.Run(() =>
        {
            int made = 0;
            foreach (var plan in plans)
            {
                var name = Path.GetFileNameWithoutExtension(plan.Local);
                var exe = FindLauncher(Path.Combine(dest, name));
                if (exe == null) continue;
                CreateShortcut(name, exe);
                made++;
            }
            if (made == 0) throw new FileNotFoundException("PBLauncher.exe não encontrado em nenhuma pasta extraída.");
        }, ct));
    }

    // ================= LIMPAR =================
    public static async Task Clean(StepRunner r, CancellationToken ct)
    {
        var dest = AppSettings.Destination.Trim();
        AssertSafe(dest);

        var appData = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppSettings.AppDataFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppSettings.AppDataFolder),
        };

        var sKill = r.Add("Encerrando o jogo, se estiver aberto");
        var sDest = r.Add($"Apagando {dest}");
        var sApp = r.Add("Removendo pasta Zepetto do AppData");
        var sChk = r.Add("Verificando itens restantes");

        await r.Run(sKill, _ => Task.Run(KillGame, ct));
        await r.Run(sDest, _ => Task.Run(() => DeleteDir(dest, ct), ct));
        await r.Run(sApp, _ => Task.Run(() => { foreach (var d in appData) DeleteDir(d, ct); }, ct));
        await r.Run(sChk, _ => Task.Run(() =>
        {
            var left = appData.Append(dest).Where(Directory.Exists).ToList();
            if (left.Count > 0) throw new IOException("Não foi possível remover: " + string.Join(", ", left));
        }, ct));
    }

    // ================= REDE =================
    static List<string> ActiveValues(IEnumerable<Entry> list) =>
        list.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Value.Trim()).ToList();

    // Timeout "duro": se a operação travar (SMB caído), abandona e segue em frente
    static async Task<T> WithTimeout<T>(Task<T> task, int ms, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(ms, cts.Token);
        if (await Task.WhenAny(task, delay) == task)
        {
            cts.Cancel();
            return await task;
        }
        _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException();
    }

    static async Task<bool> IsReachable(string dir, CancellationToken ct)
    {
        try { return await WithTimeout(Task.Run(() => Directory.Exists(dir), ct), StallMs, ct); }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { return false; }
    }

    static async Task<RemoteInfo?> GetRemoteInfo(SourcePool pool, string rel, CancellationToken ct)
    {
        foreach (var smb in pool.Available())
        {
            try
            {
                var path = Path.Combine(smb, rel);
                var info = await WithTimeout(Task.Run(() =>
                {
                    var fi = new FileInfo(path);
                    return fi.Exists ? new RemoteInfo(smb, fi.Length, fi.LastWriteTimeUtc) : null;
                }, ct), StallMs, ct);
                if (info != null) return info;
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
            {
                pool.MarkDown(smb);
            }
        }
        return null;
    }

    static async Task CopyWithFailoverAsync(SourcePool pool, string rel, RemoteInfo remote, string dstFinal,
        IProgress<double> p, IProgress<string> info, CancellationToken ct)
    {
        const int Chunk = 4 * 1024 * 1024;
        var part = dstFinal + ".part";
        long len = remote.Length, done = 0, lastBytes = 0, lastTick = 0;
        int fails = 0;
        string? current = remote.Smb; // começa pela origem onde o arquivo foi encontrado
        var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // origens com arquivo de tamanho diferente
        var sw = Stopwatch.StartNew();

        Directory.CreateDirectory(Path.GetDirectoryName(dstFinal)!);
        try { if (File.Exists(part)) File.Delete(part); } catch { }

        var dst = new FileStream(part, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0,
            Options = FileOptions.Asynchronous,
            PreallocationSize = len
        });

        try
        {
            while (done < len)
            {
                ct.ThrowIfCancellationRequested();
                var smb = current ?? pool.Next(bad);
                current = null;
                if (smb == null) throw new IOException("Nenhuma origem disponível com este arquivo.");
                if (fails > pool.Count * 3 + 3) throw new IOException("Todas as origens falharam durante a cópia.");

                FileStream? src = null;
                try
                {
                    var path = Path.Combine(smb, rel);
                    src = await WithTimeout(Task.Run(() => new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 0, FileOptions.Asynchronous | FileOptions.SequentialScan), ct), StallMs, ct);

                    if (src.Length != len) { bad.Add(smb); continue; }
                    src.Seek(done, SeekOrigin.Begin); // retoma exatamente de onde parou

                    var buf = new byte[Chunk]; // buffer novo por origem
                    while (done < len)
                    {
                        int want = (int)Math.Min(Chunk, len - done);
                        int n = await WithTimeout(src.ReadAsync(buf.AsMemory(0, want), ct).AsTask(), StallMs, ct);
                        if (n == 0) throw new IOException("Fim inesperado do arquivo de origem.");
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;

                        long now = sw.ElapsedMilliseconds;
                        if (now - lastTick >= 400 || done == len)
                        {
                            double speed = (done - lastBytes) / Math.Max(0.001, (now - lastTick) / 1000.0);
                            lastTick = now;
                            lastBytes = done;
                            p.Report(done / (double)len);
                            info.Report($"{FormatSize(done)} / {FormatSize(len)} · {FormatSize((long)speed)}/s · {smb}");
                        }
                    }
                }
                catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
                {
                    fails++;
                    pool.MarkDown(smb);
                    info.Report($"Origem {smb} indisponível, trocando…");
                }
                finally
                {
                    if (src != null)
                    {
                        var s = src;
                        _ = Task.Run(() => { try { s.Dispose(); } catch { } }); // dispose não pode travar a tela
                    }
                }
            }
        }
        catch
        {
            await dst.DisposeAsync();
            try { File.Delete(part); } catch { }
            throw;
        }

        await dst.DisposeAsync();
        File.SetLastWriteTimeUtc(part, remote.MtimeUtc);
        File.Move(part, dstFinal, true); // só vira .zip definitivo quando está completo
    }

    // ================= EXTRAÇÃO =================
    static void ExtractZip(string zipPath, string destRoot, IProgress<double> p, IProgress<string> info, CancellationToken ct)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        List<EntryInfo> list;
        bool wrapped;
        using (var za = ZipFile.OpenRead(zipPath))
        {
            list = za.Entries.Select(e => new EntryInfo(e.FullName, e.Length, e.LastWriteTime.DateTime,
                e.FullName.EndsWith('/') || e.FullName.EndsWith('\\'))).ToList();
            // Se o zip já vem com a pasta raiz (ex.: LIVE/...), extrai em destRoot; senão cria destRoot\LIVE
            wrapped = list.Count > 0 && list.All(e => e.Full.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase));
        }

        var target = wrapped ? destRoot : Path.Combine(destRoot, name);
        Directory.CreateDirectory(target);
        var rootFull = Path.GetFullPath(target).TrimEnd('\\') + "\\";

        long total = Math.Max(1, list.Sum(e => e.Len)), done = 0, skipped = 0, extracted = 0;
        int lastPct = -1;
        int workers = Math.Clamp(Environment.ProcessorCount, 2, 6);

        try
        {
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct }, w =>
            {
                using var za = ZipFile.OpenRead(zipPath); // cada worker com seu próprio leitor
                var entries = za.Entries;
                for (int i = w; i < list.Count; i += workers)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = list[i];
                    var outPath = Path.GetFullPath(Path.Combine(target, item.Full));
                    if (!outPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Entrada inválida no zip: " + item.Full);

                    if (item.IsDir)
                    {
                        Directory.CreateDirectory(outPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    if (UpToDate(outPath, item))
                    {
                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        ExtractEntry(entries[i], outPath);
                        Interlocked.Increment(ref extracted);
                    }

                    long d = Interlocked.Add(ref done, item.Len);
                    int pc = (int)(d * 100 / total);
                    if (Interlocked.Exchange(ref lastPct, pc) != pc)
                    {
                        p.Report(d / (double)total);
                        info.Report($"{Interlocked.Read(ref extracted)} extraídos · {Interlocked.Read(ref skipped)} já estavam iguais");
                    }
                }
            });
        }
        catch (AggregateException ae)
        {
            throw ae.InnerExceptions[0];
        }

        p.Report(1);
        info.Report($"{extracted} extraídos · {skipped} já estavam iguais");
    }

    static bool UpToDate(string path, EntryInfo e)
    {
        var fi = new FileInfo(path);
        return fi.Exists && fi.Length == e.Len && Math.Abs((fi.LastWriteTime - e.M).TotalSeconds) <= 2;
    }

    static void ExtractEntry(ZipArchiveEntry e, string outPath)
    {
        try { if (File.Exists(outPath)) File.SetAttributes(outPath, FileAttributes.Normal); } catch { }
        var opts = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0,
            Options = FileOptions.SequentialScan,
            PreallocationSize = e.Length
        };
        using (var fs = new FileStream(outPath, opts))
        using (var s = e.Open())
            s.CopyTo(fs, 1 << 20);
        File.SetLastWriteTime(outPath, e.LastWriteTime.DateTime);
    }

    // ================= HELPERS =================
    static bool IsValidZip(string path)
    {
        try { using var z = ZipFile.OpenRead(path); return true; }
        catch { return false; }
    }

    static string FormatSize(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" :
        b >= 1L << 20 ? $"{b / (double)(1L << 20):0} MB" :
        $"{b / 1024.0:0} KB";

    static string? FindLauncher(string baseDir)
    {
        if (!Directory.Exists(baseDir)) return null;
        var exe = Path.Combine(baseDir, AppSettings.LauncherRelPath);
        if (File.Exists(exe)) return exe;
        return Directory.EnumerateFiles(baseDir, Path.GetFileName(AppSettings.LauncherRelPath), SearchOption.AllDirectories)
                        .FirstOrDefault();
    }

    static void KillGame()
    {
        foreach (var name in AppSettings.ProcessNames)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(true); p.WaitForExit(5000); } catch { }
                finally { p.Dispose(); }
            }
    }

    static void CreateShortcut(string name, string target)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(Path.Combine(desktop, name + ".lnk"));
        lnk.TargetPath = target;
        lnk.WorkingDirectory = Path.GetDirectoryName(target);
        lnk.IconLocation = target + ",0";
        lnk.Save();
        Marshal.FinalReleaseComObject(lnk);
        Marshal.FinalReleaseComObject(shell);
    }

    static void DeleteDir(string path, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(path)) return;
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (!Directory.Exists(path)) return;
                if (attempt >= 3) throw;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Thread.Sleep(500);
            }
        }
    }

    // Proteção: nunca apagar raiz do disco, pasta do usuário ou do Windows
    static void AssertSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Pasta de destino vazia.");
        var full = Path.GetFullPath(path).TrimEnd('\\');
        var root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            full.Equals(profile, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(win, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Pasta de destino insegura para limpeza: {path}");
    }
}