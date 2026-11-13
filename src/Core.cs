using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace AutoSetup
{
    public enum LogLevel { Info, Ok, Warn, Err, Step }

    /// <summary>설치 목록의 한 항목</summary>
    public class ProgramItem
    {
        public string Path;
        public bool Enabled = true;
        public string Args = "";
        public string Mode = "auto";          // auto | silent | interactive
        public string Kind = "?";
        public string Label = "";
        public string DefaultArgs = "";
    }

    public class InstallerInfo
    {
        public string Kind;
        public string Label;
        public string Args;
        public InstallerInfo(string kind, string label, string args) { Kind = kind; Label = label; Args = args; }
    }

    /// <summary>설치 파일과 같은 이름의 .txt 에서 읽은 시리얼 정보</summary>
    public class SerialInfo
    {
        public string File;
        public string Serial;
        public string Args;
        public string Mode;
        public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class InstallPlan
    {
        public string Mode;
        public string Args;
        public string DisplayArgs;
        public bool ArgsFromTxt;
        public bool SerialInArgs;
    }

    public class WingetApp
    {
        public string Name;
        public string Id;
        public WingetApp(string name, string id) { Name = name; Id = id; }
    }

    public static class Installers
    {
        public static readonly string[] SupportedExt = { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".reg", ".bat", ".cmd", ".ps1" };
        static readonly Dictionary<string, InstallerInfo> cache = new Dictionary<string, InstallerInfo>(StringComparer.OrdinalIgnoreCase);

        public static readonly WingetApp[] WingetCatalog =
        {
            new WingetApp("Google Chrome", "Google.Chrome"),
            new WingetApp("네이버 웨일", "Naver.Whale"),
            new WingetApp("Mozilla Firefox", "Mozilla.Firefox"),
            new WingetApp("반디집", "Bandisoft.Bandizip"),
            new WingetApp("7-Zip", "7zip.7zip"),
            new WingetApp("카카오톡", "Kakao.KakaoTalk"),
            new WingetApp("팟플레이어", "Daum.PotPlayer"),
            new WingetApp("VLC 미디어 플레이어", "VideoLAN.VLC"),
            new WingetApp("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit"),
            new WingetApp("Notepad++", "Notepad++.Notepad++"),
            new WingetApp("Visual Studio Code", "Microsoft.VisualStudioCode"),
            new WingetApp("Microsoft PowerToys", "Microsoft.PowerToys"),
            new WingetApp("Everything (빠른 파일 검색)", "voidtools.Everything"),
            new WingetApp("ShareX (화면 캡처)", "ShareX.ShareX"),
            new WingetApp("Zoom", "Zoom.Zoom"),
            new WingetApp("Discord", "Discord.Discord"),
            new WingetApp("Steam", "Valve.Steam"),
            new WingetApp("TeamViewer", "TeamViewer.TeamViewer"),
            new WingetApp("Git", "Git.Git"),
            new WingetApp("Python 3.12", "Python.Python.3.12"),
            new WingetApp("Visual C++ 재배포 패키지 (x64)", "Microsoft.VCRedist.2015+.x64"),
            new WingetApp("Visual C++ 재배포 패키지 (x86)", "Microsoft.VCRedist.2015+.x86"),
            new WingetApp(".NET 데스크톱 런타임 8", "Microsoft.DotNet.DesktopRuntime.8"),
        };

        public static bool IsSupported(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return SupportedExt.Contains(ext);
        }

        public static void ClearCache() { lock (cache) cache.Clear(); }

        /// <summary>설치 파일 종류를 감지해 무인 설치 인수를 정한다.</summary>
        public static InstallerInfo Detect(string path)
        {
            lock (cache)
            {
                InstallerInfo hit;
                if (cache.TryGetValue(path, out hit)) return hit;
            }
            InstallerInfo r = null;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".msi": r = new InstallerInfo("MSI", "MSI", "/qn /norestart"); break;
                case ".msix": case ".msixbundle": case ".appx": case ".appxbundle": r = new InstallerInfo("APPX", "MSIX/APPX", ""); break;
                case ".reg": r = new InstallerInfo("REG", "레지스트리", ""); break;
                case ".bat": case ".cmd": r = new InstallerInfo("BAT", "배치 파일", ""); break;
                case ".ps1": r = new InstallerInfo("PS1", "PowerShell", ""); break;
            }
            if (r == null)
            {
                r = new InstallerInfo("EXE", "EXE · 기타", "/S");
                try
                {
                    byte[] buf;
                    int read = 0;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        int len = (int)Math.Min(fs.Length, 4 * 1024 * 1024);
                        buf = new byte[len];
                        while (read < len) { int n = fs.Read(buf, read, len - read); if (n <= 0) break; read += n; }
                    }
                    string s = Encoding.GetEncoding(28591).GetString(buf, 0, read);
                    string head = s.Substring(0, Math.Min(4096, s.Length));
                    if (head.Contains(".wixburn")) r = new InstallerInfo("BURN", "EXE · WiX Burn", "/quiet /norestart");
                    else if (s.Contains("Inno Setup") || s.Contains("JR.Inno.Setup")) r = new InstallerInfo("INNO", "EXE · Inno Setup", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-");
                    else if (s.Contains("Nullsoft")) r = new InstallerInfo("NSIS", "EXE · NSIS", "/S");
                    else if (s.Contains("InstallShield") || s.Contains(U16("InstallShield"))) r = new InstallerInfo("ISHIELD", "EXE · InstallShield", "/s /v\"/qn REBOOT=ReallySuppress\"");
                    else if (s.Contains("Advanced Installer") || s.Contains(U16("Advanced Installer"))) r = new InstallerInfo("ADVINST", "EXE · Advanced Installer", "/exenoui /qn /norestart");
                    else if (s.Contains("Squirrel") || s.Contains(U16("Squirrel"))) r = new InstallerInfo("SQUIRREL", "EXE · Squirrel", "--silent");
                    else if (s.Contains("Setup Factory")) r = new InstallerInfo("SETUPFAC", "EXE · Setup Factory", "/S");
                }
                catch { }
            }
            lock (cache) cache[path] = r;
            return r;
        }

        static string U16(string t) { return string.Join("\0", t.ToCharArray().Select(c => c.ToString()).ToArray()); }

        /// <summary>메모장에서 UTF-8 / ANSI(CP949) 어느 쪽으로 저장해도 읽는다.</summary>
        public static string ReadTextAuto(string path)
        {
            byte[] b = System.IO.File.ReadAllBytes(path);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch { return Encoding.Default.GetString(b); }
        }

        public static string SerialFilePath(string installerPath)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(installerPath),
                                          System.IO.Path.GetFileNameWithoutExtension(installerPath) + ".txt");
        }

        static readonly Regex KnownKey = new Regex(@"^(?i:(serial|key|sn|productkey|license|args|arguments|mode|name|company|user|email|시리얼|시리얼번호|제품키|인수|모드|이름|회사))\s*=\s*(.*)$");
        static readonly Regex CustomKey = new Regex(@"^([A-Z][A-Z0-9_]*)\s*=\s*(.*)$");

        /// <summary>
        /// 설치 파일과 같은 이름의 .txt 를 읽는다.
        ///  - 첫 줄에 시리얼만 적어도 되고
        ///  - SERIAL= / ARGS= / MODE= / NAME= ... 형식도 지원 ({키} 는 ARGS 에서 치환)
        /// </summary>
        public static SerialInfo ReadSerial(string installerPath)
        {
            string file = null;
            foreach (string c in new[] { SerialFilePath(installerPath), installerPath + ".txt" })
                if (System.IO.File.Exists(c)) { file = c; break; }
            if (file == null) return null;

            var info = new SerialInfo { File = file };
            string text;
            try { text = ReadTextAuto(file); } catch { return info; }
            var plain = new List<string>();
            foreach (string raw in Regex.Split(text, "\r?\n"))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                Match m = KnownKey.Match(line);
                if (!m.Success) m = CustomKey.Match(line);
                if (!m.Success) { plain.Add(line); continue; }
                string key = m.Groups[1].Value.ToUpperInvariant();
                string val = m.Groups[2].Value.Trim();
                switch (key)
                {
                    case "SERIAL": case "KEY": case "SN": case "PRODUCTKEY": case "LICENSE": case "시리얼": case "시리얼번호": case "제품키":
                        if (val.Length > 0) { info.Serial = val; info.Values["SERIAL"] = val; }
                        break;
                    case "ARGS": case "ARGUMENTS": case "인수":
                        if (val.Length > 0) info.Args = val;
                        break;
                    case "MODE": case "모드":
                        string v = val.ToLowerInvariant();
                        if (v == "silent" || v == "quiet" || v == "무인" || v == "자동설치") info.Mode = "silent";
                        else if (v == "interactive" || v == "manual" || v == "gui" || v == "대화형" || v == "수동") info.Mode = "interactive";
                        else info.Mode = "auto";
                        break;
                    case "이름": info.Values["NAME"] = val; break;
                    case "회사": info.Values["COMPANY"] = val; break;
                    default: info.Values[key] = val; break;
                }
            }
            if (string.IsNullOrEmpty(info.Serial) && plain.Count > 0)
            {
                info.Serial = plain[0];
                info.Values["SERIAL"] = info.Serial;
            }
            return info;
        }

        /// <summary>항목 + 시리얼 정보로 실제 실행 방식과 인수를 정한다.</summary>
        public static InstallPlan Resolve(ProgramItem item, SerialInfo s)
        {
            string args = item.Args ?? "";
            string mode = string.IsNullOrEmpty(item.Mode) ? "auto" : item.Mode;
            bool fromTxt = false;
            if (s != null)
            {
                if (!string.IsNullOrEmpty(s.Args)) { args = s.Args; fromTxt = true; }
                if (!string.IsNullOrEmpty(s.Mode)) mode = s.Mode;
            }
            bool serialInArgs = Regex.IsMatch(args, @"\{SERIAL\}", RegexOptions.IgnoreCase);
            string display = args;
            if (s != null)
            {
                foreach (var kv in s.Values)
                {
                    string val = kv.Value ?? "";
                    args = Regex.Replace(args, Regex.Escape("{" + kv.Key + "}"), m => val, RegexOptions.IgnoreCase);
                }
            }
            if (mode == "auto")
                mode = (s != null && !string.IsNullOrEmpty(s.Serial) && !serialInArgs) ? "interactive" : "silent";
            return new InstallPlan { Mode = mode, Args = args, DisplayArgs = display, ArgsFromTxt = fromTxt, SerialInArgs = serialInArgs };
        }

        public static string SerialTemplate(string installerPath)
        {
            return string.Join("\r\n", new[]
            {
                "# " + System.IO.Path.GetFileName(installerPath) + " 의 시리얼 정보",
                "# 가장 간단한 방법: 아래 SERIAL= 뒤에 시리얼 번호를 적고 저장하세요.",
                "#  -> 설치 시 시리얼이 클립보드에 복사되고, 설치 창이 열리면 Ctrl+V 로 붙여넣으면 됩니다.",
                "SERIAL=",
                "",
                "# (선택) 설치 프로그램이 시리얼을 명령줄 인수로 받을 수 있다면 무인 설치도 가능합니다.",
                "# {SERIAL} 자리에 시리얼이 들어갑니다. 예)",
                "# ARGS=/S /SERIAL={SERIAL}",
                "# ARGS=/qn PIDKEY={SERIAL}",
                "",
                "# (선택) MODE=auto | silent | interactive",
                "# (선택) NAME=사용자이름   COMPANY=회사명   -> ARGS 에서 {NAME}, {COMPANY} 로 사용",
                ""
            });
        }
    }

    public static class Util
    {
        public static string Mask(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            if (t.Length <= 4) return new string('*', t.Length);
            return t.Substring(0, 4) + new string('*', Math.Min(12, t.Length - 4));
        }

        /// <summary>창 없이 실행하고 종료 코드와 출력을 돌려준다.</summary>
        public static int Run(string file, string args, out string output, int timeoutMs = 600000)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { output = sb.ToString(); return -1; }
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        public static int Run(string file, string args)
        {
            string o;
            return Run(file, args, out o);
        }

        public static string Sys32(string exe)
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);
        }

        /// <summary>Windows PowerShell 스크립트를 실행 (Appx/복원 지점 등 PowerShell 전용 기능에 사용)</summary>
        public static int RunPowerShell(string script, out string output)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8;\n" + script));
            string ps = Sys32(@"WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);

        /// <summary>인터넷에서 받은 파일의 '보안 경고' 표시(Zone.Identifier) 제거</summary>
        public static void Unblock(string path)
        {
            try { DeleteFile(path + ":Zone.Identifier"); } catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetComputerNameEx(int nameType, string name);

        public static void RenameComputer(string name)
        {
            if (!SetComputerNameEx(5 /* ComputerNamePhysicalDnsHostname */, name))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public static bool IsAdmin()
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        /// <summary>읽기 쉬운 JSON 들여쓰기</summary>
        public static string PrettyJson(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false, esc = false;
            foreach (char c in json)
            {
                if (inStr)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{': case '[':
                        sb.Append(c); indent++; sb.Append("\r\n").Append(new string(' ', indent * 2)); break;
                    case '}': case ']':
                        indent--; sb.Append("\r\n").Append(new string(' ', indent * 2)).Append(c); break;
                    case ',': sb.Append(c).Append("\r\n").Append(new string(' ', indent * 2)); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            // 빈 배열/객체 정리
            return Regex.Replace(sb.ToString(), @"([\[{])\s+([\]}])", "$1$2");
        }
    }

    /// <summary>사용자가 직접 추가하는 레지스트리 항목</summary>
    public class RegEntry
    {
        public bool Enabled = true;
        public string Key = "";          // 예: HKCU\Software\Microsoft\...
        public string Name = "";         // 빈 값 = (기본값)
        public string Type = "DWORD";    // DWORD | QWORD | SZ | EXPAND_SZ | BINARY
        public string Value = "";
        public bool DeleteValue;         // true 면 값 삭제

        public string Display()
        {
            string n = string.IsNullOrEmpty(Name) ? "(기본값)" : Name;
            return DeleteValue ? Key + "\\" + n + "  → 삭제" : Key + "\\" + n + " = " + Value + "  (" + Type + ")";
        }
    }

    /// <summary>레지스트리 쓰기 (64비트 뷰 고정)</summary>
    public static class Reg
    {
        public static readonly string[] Types = { "DWORD", "QWORD", "SZ", "EXPAND_SZ", "BINARY" };
        public static readonly string[] TypeLabels = { "DWORD (32비트 숫자)", "QWORD (64비트 숫자)", "문자열 (REG_SZ)", "확장 문자열 (REG_EXPAND_SZ)", "이진값 (REG_BINARY)" };

        /// <summary>regedit 주소 표시줄 형식 등 여러 표기를 HKCU\... 형식으로 정리. 잘못되면 null.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p = path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
            foreach (string prefix in new[] { "컴퓨터\\", "Computer\\" })
                if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) p = p.Substring(prefix.Length);
            int i = p.IndexOf('\\');
            string hive = (i < 0 ? p : p.Substring(0, i)).ToUpperInvariant();
            string rest = i < 0 ? "" : p.Substring(i + 1).Trim('\\');
            switch (hive)
            {
                case "HKEY_CURRENT_USER": case "HKCU": hive = "HKCU"; break;
                case "HKEY_LOCAL_MACHINE": case "HKLM": hive = "HKLM"; break;
                case "HKEY_USERS": case "HKU": hive = "HKU"; break;
                case "HKEY_CLASSES_ROOT": case "HKCR": hive = "HKCR"; break;
                case "HKEY_CURRENT_CONFIG": case "HKCC": hive = "HKCC"; break;
                default: return null;
            }
            if (rest.Length == 0) return null;
            return hive + "\\" + rest;
        }

        static RegistryKey OpenRoot(string fullPath, out string sub)
        {
            string p = Normalize(fullPath);
            if (p == null) throw new ArgumentException("알 수 없는 레지스트리 경로: " + fullPath);
            int i = p.IndexOf('\\');
            string hive = p.Substring(0, i);
            sub = p.Substring(i + 1);
            RegistryHive h;
            switch (hive)
            {
                case "HKCU": h = RegistryHive.CurrentUser; break;
                case "HKLM": h = RegistryHive.LocalMachine; break;
                case "HKU": h = RegistryHive.Users; break;
                case "HKCR": h = RegistryHive.ClassesRoot; break;
                default: h = RegistryHive.CurrentConfig; break;
            }
            return RegistryKey.OpenBaseKey(h, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }

        public static void Set(string fullPath, string name, object value, RegistryValueKind kind)
        {
            string sub;
            using (var b = OpenRoot(fullPath, out sub))
            using (var k = b.CreateSubKey(sub))
            {
                k.SetValue(name, value, kind);
            }
        }

        public static void Dw(string path, string name, int value) { Set(path, name, value, RegistryValueKind.DWord); }
        public static void Str(string path, string name, string value) { Set(path, name, value, RegistryValueKind.String); }

        /// <summary>입력값을 레지스트리 형식으로 변환. 잘못되면 예외.</summary>
        public static object ParseValue(string type, string value, out RegistryValueKind kind)
        {
            string v = (value ?? "").Trim();
            switch (type)
            {
                case "DWORD":
                    kind = RegistryValueKind.DWord;
                    return unchecked((int)(v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToUInt32(v.Substring(2), 16) : uint.Parse(v)));
                case "QWORD":
                    kind = RegistryValueKind.QWord;
                    return unchecked((long)(v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToUInt64(v.Substring(2), 16) : ulong.Parse(v)));
                case "EXPAND_SZ": kind = RegistryValueKind.ExpandString; return value ?? "";
                case "BINARY":
                    kind = RegistryValueKind.Binary;
                    string hex = Regex.Replace(v, @"[\s,\-]", "");
                    if (hex.Length % 2 != 0 || !Regex.IsMatch(hex, "^[0-9a-fA-F]*$")) throw new FormatException("이진값은 16진수로 입력하세요. 예) 01 00 ff");
                    var bytes = new byte[hex.Length / 2];
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    return bytes;
                default: kind = RegistryValueKind.String; return value ?? "";
            }
        }

        /// <summary>항목 검사. 문제가 없으면 null, 있으면 오류 메시지.</summary>
        public static string Validate(RegEntry e)
        {
            if (Normalize(e.Key) == null) return "레지스트리 경로가 올바르지 않습니다.\n예) HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced";
            if (e.DeleteValue) return null;
            try { RegistryValueKind k; ParseValue(e.Type, e.Value, out k); return null; }
            catch (FormatException ex) { return ex.Message.Contains("16진수") ? ex.Message : "값 형식이 올바르지 않습니다. 숫자는 10진수 또는 0x 로 시작하는 16진수로 입력하세요."; }
            catch (OverflowException) { return "숫자가 너무 큽니다."; }
        }

        public static void Apply(RegEntry e)
        {
            string sub;
            using (var b = OpenRoot(e.Key, out sub))
            {
                if (e.DeleteValue)
                {
                    using (var k = b.OpenSubKey(sub, true))
                        if (k != null) k.DeleteValue(e.Name ?? "", false);
                    return;
                }
                RegistryValueKind kind;
                object val = ParseValue(e.Type, e.Value, out kind);
                using (var k = b.CreateSubKey(sub)) k.SetValue(e.Name ?? "", val, kind);
            }
        }
    }

    /// <summary>settings.json 형식</summary>
    public class SettingsData
    {
        public List<StoredProgram> Programs = new List<StoredProgram>();
        public Dictionary<string, bool> Tweaks = new Dictionary<string, bool>();
        public string PcName = "";
        public List<string> Winget = new List<string>();
        public string WingetCustom = "";
        public bool RestorePoint = true;
        public bool RebootAfter = false;
        public bool Recurse = false;
        public bool CleanDownloads = true;
        public bool CheckUpdates = true;
        public List<RegEntry> CustomRegs = new List<RegEntry>();

        public static SettingsData Load(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                string json = System.IO.File.ReadAllText(path, Encoding.UTF8);
                var s = new JavaScriptSerializer().Deserialize<SettingsData>(json);
                if (s.Programs == null) s.Programs = new List<StoredProgram>();
                if (s.Tweaks == null) s.Tweaks = new Dictionary<string, bool>();
                if (s.Winget == null) s.Winget = new List<string>();
                if (s.CustomRegs == null) s.CustomRegs = new List<RegEntry>();
                return s;
            }
            catch { return null; }
        }

        public void Save(string path)
        {
            string json = Util.PrettyJson(new JavaScriptSerializer().Serialize(this));
            System.IO.File.WriteAllText(path, json, new UTF8Encoding(true));
        }
    }

    public class StoredProgram
    {
        public string Path;
        public bool Enabled = true;
        public string Args;
        public string Mode;
    }
}
