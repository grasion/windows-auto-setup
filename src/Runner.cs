using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AutoSetup
{
    public class SetupJob
    {
        public List<ProgramItem> Programs = new List<ProgramItem>();
        public List<Tweak> Tweaks = new List<Tweak>();
        public string PcName = "";
        public List<string> Winget = new List<string>();
        public bool RestorePoint;
        public bool RebootAfter;
        public bool CleanDownloads = true;   // winget 이 내려받은 설치 파일 삭제 (① 목록의 로컬 설치 파일은 삭제하지 않음)
    }

    public class JobResult
    {
        public int Ok;
        public List<string> Fails = new List<string>();
        public bool RebootNeeded;
        public bool RebootScheduled;
        public bool Cancelled;
    }

    /// <summary>작업 스레드에서 실제 설치/설정을 수행한다. 이벤트는 작업 스레드에서 발생한다.</summary>
    public class SetupRunner
    {
        public event Action<LogLevel, string> Log;
        public event Action<int, int, string> Progress;
        public event Action<string, string, bool> SerialShow;   // 파일명, 시리얼, 대화형 여부
        public event Action SerialClose;

        public volatile bool Cancel;
        public volatile bool Skip;

        void L(string msg, LogLevel lv = LogLevel.Info) { var h = Log; if (h != null) h(lv, msg); }
        void P(int v, int max, string text) { var h = Progress; if (h != null) h(v, max, text); }

        public JobResult Run(SetupJob job)
        {
            var r = new JobResult();
            try
            {
                int total = job.Programs.Count + job.Tweaks.Count + job.Winget.Count + (job.RestorePoint ? 1 : 0) + (string.IsNullOrEmpty(job.PcName) ? 0 : 1);
                int step = 0;
                L(string.Format("작업 시작 - 설치 {0}개, 윈도우 설정 {1}개, winget {2}개", job.Programs.Count, job.Tweaks.Count, job.Winget.Count), LogLevel.Step);

                // 1) 복원 지점
                if (job.RestorePoint && !Cancel)
                {
                    P(++step, total, "시스템 복원 지점 만드는 중...");
                    L("시스템 복원 지점 생성", LogLevel.Step);
                    try
                    {
                        Reg.Dw(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0);
                        string o;
                        int code = Util.RunPowerShell(
                            "$ErrorActionPreference='Stop'; Enable-ComputerRestore -Drive \"$env:SystemDrive\\\"; " +
                            "Checkpoint-Computer -Description 'AutoSetup 실행 전' -RestorePointType MODIFY_SETTINGS", out o);
                        if (code == 0) L("    복원 지점을 만들었습니다.", LogLevel.Ok);
                        else L("    복원 지점 생성 실패: " + FirstLine(o), LogLevel.Warn);
                    }
                    catch (Exception ex) { L("    복원 지점 생성 실패: " + ex.Message, LogLevel.Warn); }
                }

                // 2) 윈도우 설정
                if (job.Tweaks.Count > 0 && !Cancel)
                {
                    L("윈도우 초기 설정 적용", LogLevel.Step);
                    foreach (var t in job.Tweaks)
                    {
                        if (Cancel) break;
                        P(++step, total, "설정: " + t.Name);
                        try
                        {
                            t.Apply(s => L(s));
                            L("    " + t.Name, LogLevel.Ok);
                            r.Ok++;
                        }
                        catch (Exception ex)
                        {
                            L("    " + t.Name + " - " + ex.Message, LogLevel.Err);
                            r.Fails.Add("설정: " + t.Name);
                        }
                    }
                }

                // 3) PC 이름
                if (!string.IsNullOrEmpty(job.PcName) && !Cancel)
                {
                    P(++step, total, "PC 이름 변경");
                    if (string.Equals(job.PcName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                        L("PC 이름이 이미 '" + job.PcName + "' 입니다.");
                    else
                    {
                        try
                        {
                            Util.RenameComputer(job.PcName);
                            L("PC 이름을 '" + job.PcName + "'(으)로 변경했습니다. (재시작 후 적용)", LogLevel.Ok);
                            r.RebootNeeded = true; r.Ok++;
                        }
                        catch (Exception ex) { L("PC 이름 변경 실패: " + ex.Message, LogLevel.Err); r.Fails.Add("PC 이름 변경"); }
                    }
                }

                // 4) 설치 파일
                if (job.Programs.Count > 0 && !Cancel)
                {
                    L("프로그램 설치", LogLevel.Step);
                    int i = 0, n = job.Programs.Count;
                    foreach (var p in job.Programs)
                    {
                        if (Cancel) break;
                        i++;
                        string name = Path.GetFileName(p.Path);
                        P(++step, total, string.Format("설치 중 ({0}/{1}): {2}", i, n, name));
                        L(string.Format("[{0}/{1}] {2}", i, n, name));
                        if (!File.Exists(p.Path)) { L("    파일이 없습니다: " + p.Path, LogLevel.Err); r.Fails.Add(name); continue; }
                        try
                        {
                            string wait, mode;
                            int? c = InstallItem(p, out wait, out mode);
                            if (wait == "skipped") L("    대기를 건너뛰었습니다. (설치는 계속 진행 중일 수 있음)", LogLevel.Warn);
                            else if (wait == "cancelled") L("    작업 중지 요청으로 대기를 멈췄습니다.", LogLevel.Warn);
                            else if (c == null || c == 0) { L("    설치 완료", LogLevel.Ok); r.Ok++; }
                            else if (c == 3010 || c == 1641) { L("    설치 완료 - 재시작 필요 (코드 " + c + ")", LogLevel.Ok); r.Ok++; r.RebootNeeded = true; }
                            else if (c == 1602 || c == 1223) { L("    사용자가 설치를 취소했습니다. (코드 " + c + ")", LogLevel.Warn); r.Fails.Add(name); }
                            else if (mode == "interactive") { L("    설치 창이 닫혔습니다. (종료 코드 " + c + ")", LogLevel.Ok); r.Ok++; }
                            else { L("    종료 코드 " + c + " - 설치에 실패했을 수 있습니다. 설치 인수를 확인하세요.", LogLevel.Warn); r.Fails.Add(name + " (코드 " + c + ")"); }
                        }
                        catch (Exception ex) { L("    실행 오류: " + ex.Message, LogLevel.Err); r.Fails.Add(name); }
                        finally { var h = SerialClose; if (h != null) h(); }
                    }
                }

                // 5) winget
                if (job.Winget.Count > 0 && !Cancel)
                {
                    L("winget 온라인 설치", LogLevel.Step);
                    string wg = FindWinget();
                    if (wg == null)
                    {
                        L("    winget 을 찾을 수 없습니다. Microsoft Store 에서 \"앱 설치 관리자(App Installer)\"를 업데이트한 뒤 다시 시도하세요.", LogLevel.Err);
                        foreach (string id in job.Winget) { step++; r.Fails.Add("winget: " + id); }
                    }
                    else
                    {
                        foreach (string id in job.Winget)
                        {
                            if (Cancel) break;
                            P(++step, total, "winget 설치: " + id);
                            L("    " + id + " 설치 중...");
                            try
                            {
                                int code = RunWinget(wg, id);
                                if (code == 0) { L("    " + id + " 설치 완료", LogLevel.Ok); r.Ok++; }
                                else if (code == -1978335189 || code == -1978335135) { L("    " + id + " 이미 설치되어 있습니다.", LogLevel.Ok); r.Ok++; }
                                else { L(string.Format("    {0} 실패 (코드 0x{1:X8})", id, code), LogLevel.Err); r.Fails.Add("winget: " + id); }
                            }
                            catch (Exception ex) { L("    " + id + " 오류: " + ex.Message, LogLevel.Err); r.Fails.Add("winget: " + id); }
                        }
                        if (job.CleanDownloads) CleanWingetDownloads();
                    }
                }

                // 6) 탐색기 재시작 (설정 반영)
                if (job.Tweaks.Count > 0 && !Cancel)
                {
                    L("설정 반영을 위해 Windows 탐색기를 다시 시작합니다.");
                    RestartExplorer();
                }

                if (Cancel) L("사용자 요청으로 작업을 중지했습니다.", LogLevel.Warn);

                // 7) 재시작
                if (job.RebootAfter && !Cancel)
                {
                    Util.Run(Util.Sys32("shutdown.exe"), "/r /t 60 /c \"AutoSetup: 모든 작업이 끝났습니다. 60초 후 PC를 다시 시작합니다.\"");
                    r.RebootScheduled = true;
                    L("60초 후 PC를 다시 시작합니다. (취소: shutdown /a)", LogLevel.Warn);
                }
                else if (r.RebootNeeded)
                    L("일부 변경 사항은 PC를 다시 시작해야 적용됩니다.", LogLevel.Warn);

                P(Math.Max(1, total), Math.Max(1, total), Cancel ? "중지됨" : "완료");
            }
            catch (Exception ex)
            {
                L("치명적 오류: " + ex.Message, LogLevel.Err);
                r.Fails.Add("작업 오류");
            }
            r.Cancelled = Cancel;
            return r;
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        // ------------------------------------------------------------------ 설치 1건
        int? InstallItem(ProgramItem item, out string wait, out string mode)
        {
            string path = item.Path;
            string name = Path.GetFileName(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string dir = Path.GetDirectoryName(path);
            SerialInfo serial = Installers.ReadSerial(path);
            InstallPlan plan = Installers.Resolve(item, serial);
            bool silent = plan.Mode == "silent";
            mode = plan.Mode;
            wait = "exited";

            Util.Unblock(path);

            bool hasSerial = serial != null && !string.IsNullOrEmpty(serial.Serial);
            if (hasSerial)
            {
                L(string.Format("    시리얼 감지: {0}  ({1})", Util.Mask(serial.Serial), Path.GetFileName(serial.File)));
                var h = SerialShow; if (h != null) h(name, serial.Serial, !silent);
                if (!silent) L("    대화형 설치: 시리얼이 클립보드에 복사되었습니다. 입력란에서 Ctrl+V 로 붙여넣으세요.", LogLevel.Warn);
            }

            string file, args = "";
            switch (ext)
            {
                case ".msi":
                    file = Util.Sys32("msiexec.exe");
                    args = "/i \"" + path + "\"";
                    if (silent && plan.Args.Length > 0) args += " " + plan.Args;
                    break;
                case ".msix": case ".msixbundle": case ".appx": case ".appxbundle":
                    {
                        L("    Add-AppxPackage 로 설치합니다.");
                        string o;
                        int code = Util.RunPowerShell("Add-AppxPackage -Path '" + path.Replace("'", "''") + "' -ErrorAction Stop", out o);
                        if (code != 0) throw new Exception(FirstLine(o));
                        mode = "silent";
                        return 0;
                    }
                case ".reg":
                    file = Util.Sys32("reg.exe");
                    args = "import \"" + path + "\"";
                    break;
                case ".bat": case ".cmd":
                    file = Environment.GetEnvironmentVariable("ComSpec") ?? Util.Sys32("cmd.exe");
                    args = "/c \"\"" + path + "\" " + plan.Args + "\"";
                    break;
                case ".ps1":
                    file = Util.Sys32(@"WindowsPowerShell\v1.0\powershell.exe");
                    args = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\" " + plan.Args;
                    break;
                default:
                    file = path;
                    if (silent) args = plan.Args;
                    break;
            }

            string shown = args;
            if (hasSerial) shown = shown.Replace(serial.Serial, Util.Mask(serial.Serial));
            L(string.Format("    실행 방식: {0}   인수: {1}", silent ? "무인" : "대화형", shown.Length > 0 ? shown : "(없음)"));

            for (int attempt = 1; ; attempt++)
            {
                var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, WorkingDirectory = dir };
                using (var proc = Process.Start(psi))
                {
                    wait = WaitInstallProcess(proc);
                    int? code = null;
                    if (proc.HasExited) code = proc.ExitCode;
                    // 1618: 다른 설치가 진행 중 -> 잠시 후 재시도
                    if (code == 1618 && attempt < 4 && !Cancel)
                    {
                        L("    다른 설치가 진행 중입니다(1618). 20초 후 다시 시도합니다.", LogLevel.Warn);
                        Thread.Sleep(20000);
                        continue;
                    }
                    return code;
                }
            }
        }

        // ------------------------------------------------------------------ 설치 프로세스 대기
        static readonly Regex InstallerLike = new Regex(@"(setup|install|msiexec|unins|\.tmp$|^_is|isbew|update|redist|dotnet|bootstrap|wusa|dism)", RegexOptions.IgnoreCase);

        /// <summary>
        /// 설치 프로세스와 그 프로세스가 띄운 설치 관련 하위 프로세스(setup.tmp, msiexec 등)가 끝날 때까지 대기.
        /// 설치 후 자동 실행되는 프로그램 본체는 기다리지 않는다.
        /// </summary>
        string WaitInstallProcess(Process proc)
        {
            int rootId = proc.Id;
            var tracked = new Dictionary<int, string>();
            DateTime lastScan = DateTime.MinValue;
            while (true)
            {
                if (Skip) { Skip = false; return "skipped"; }
                if (Cancel) return "cancelled";
                if ((DateTime.Now - lastScan).TotalMilliseconds >= 1000)
                {
                    lastScan = DateTime.Now;
                    var all = ProcSnapshot.Take();
                    var parents = new HashSet<int>(tracked.Keys) { rootId };
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        foreach (var e in all)
                        {
                            if (e.Pid != rootId && !tracked.ContainsKey(e.Pid) && parents.Contains(e.ParentPid))
                            {
                                tracked[e.Pid] = e.Name;
                                parents.Add(e.Pid);
                                changed = true;
                            }
                        }
                    }
                }
                if (proc.HasExited)
                {
                    bool pending = false;
                    foreach (var kv in tracked)
                    {
                        if (!InstallerLike.IsMatch(kv.Value)) continue;
                        try
                        {
                            using (var p = Process.GetProcessById(kv.Key))
                                if (!p.HasExited) { pending = true; break; }
                        }
                        catch { }
                    }
                    if (!pending) return "exited";
                }
                Thread.Sleep(400);
            }
        }

        // ------------------------------------------------------------------ winget
        static string FindWinget()
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), "winget.exe");
                    if (dir.Trim().Length > 0 && File.Exists(p)) return p;
                }
                catch { }
            }
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
            if (File.Exists(local)) return local;
            try
            {
                string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                var d = Directory.GetDirectories(apps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe").OrderByDescending(x => x).FirstOrDefault();
                if (d != null && File.Exists(Path.Combine(d, "winget.exe"))) return Path.Combine(d, "winget.exe");
            }
            catch { }
            return null;
        }

        static readonly Regex NoiseLine = new Regex(@"^[\-\\|/ ]+$|[█▒]");

        int RunWinget(string wg, string id)
        {
            var psi = new ProcessStartInfo(wg, "install --id \"" + id + "\" --exact --silent --source winget --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    string t = e.Data.Trim();
                    if (t.Length < 3 || NoiseLine.IsMatch(t)) return;
                    L("        " + t);
                };
                p.OutputDataReceived += h;
                p.ErrorDataReceived += h;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(500))
                {
                    if (Skip) { Skip = false; L("    대기를 건너뛰었습니다.", LogLevel.Warn); return 0; }
                }
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        /// <summary>
        /// winget 이 인터넷에서 내려받은 설치 파일(%TEMP%\WinGet) 삭제.
        /// ① 설치 프로그램 목록의 파일(install_file 등)은 절대 건드리지 않는다.
        /// </summary>
        void CleanWingetDownloads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "WinGet");
            if (!Directory.Exists(dir)) return;
            long bytes = 0;
            int files = 0, failed = 0;
            foreach (string f in SafeFiles(dir))
            {
                try
                {
                    long len = new FileInfo(f).Length;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    bytes += len; files++;
                }
                catch { failed++; }
            }
            try
            {
                foreach (string d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    try { Directory.Delete(d, false); } catch { }
            }
            catch { }
            if (files > 0)
                L(string.Format("    내려받은 설치 파일 정리: {0}개, {1:0.0} MB 삭제{2}", files, bytes / 1048576.0,
                                failed > 0 ? " (사용 중인 파일 " + failed + "개는 남김)" : ""), LogLevel.Ok);
        }

        static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
            catch { return new string[0]; }
        }

        // ------------------------------------------------------------------ 탐색기 재시작
        static void RestartExplorer()
        {
            foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 8)
            {
                Thread.Sleep(500);
                if (Process.GetProcessesByName("explorer").Length > 0) return;
            }
            try { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")); } catch { }
        }
    }

    /// <summary>Toolhelp32 로 전체 프로세스의 (PID, 부모 PID, 이름) 목록을 얻는다.</summary>
    public static class ProcSnapshot
    {
        public struct Entry { public int Pid; public int ParentPid; public string Name; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(IntPtr h, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(IntPtr h, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        public static List<Entry> Take()
        {
            var list = new List<Entry>();
            IntPtr snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snap, ref e)) return list;
                do { list.Add(new Entry { Pid = (int)e.th32ProcessID, ParentPid = (int)e.th32ParentProcessID, Name = e.szExeFile ?? "" }); }
                while (Process32Next(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return list;
        }
    }
}
