using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AutoSetup
{
    public class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string Notes;
        public string DownloadUrl;
        public long Size;
        public string Sha256;     // 릴리스 설명에 "SHA256: ..." 가 있으면 다운로드 파일 검증
    }

    /// <summary>
    /// GitHub Releases 의 최신 릴리스를 확인해 실행 파일을 교체한다.
    ///  1) api.github.com/repos/{owner}/{repo}/releases/latest 조회
    ///  2) 태그(v1.2.3)가 현재 버전보다 높으면 AutoSetup.exe 첨부 파일을 받아 AutoSetup.exe.new 로 저장
    ///  3) 실행 중인 AutoSetup.exe 를 AutoSetup.exe.old 로 이름 변경 → .new 를 AutoSetup.exe 로 → 새 버전 실행
    ///  4) 새 버전이 시작될 때 .old 파일 삭제
    /// </summary>
    public static class Updater
    {
        static Updater()
        {
            // GitHub 는 TLS 1.2 이상만 허용
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288 /* Tls13 */; }
            catch { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
        }

        static string ExePath { get { return Application.ExecutablePath; } }

        static Version Norm(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// <summary>새 버전이 있으면 정보를, 없으면 null 을 돌려준다. 네트워크 오류는 예외.</summary>
        public static UpdateInfo Check()
        {
            if (!AppInfo.UpdateConfigured) return null;
            string api = "https://api.github.com/repos/" + AppInfo.GitHubOwner + "/" + AppInfo.GitHubRepo + "/releases/latest";
            var req = (HttpWebRequest)WebRequest.Create(api);
            req.UserAgent = "AutoSetup-Updater";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            string json;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null;   // 아직 릴리스가 없음
                throw;
            }

            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            string tag = Convert.ToString(Get(root, "tag_name"));
            Version latest;
            if (!Version.TryParse(Regex.Replace(tag ?? "", @"^[vV]", "").Split('-')[0], out latest)) return null;
            if (Norm(latest) <= Norm(AppInfo.Version)) return null;

            var assets = Get(root, "assets") as ArrayList;
            if (assets == null) return null;
            Dictionary<string, object> asset = null;
            foreach (Dictionary<string, object> a in assets)
            {
                string name = Convert.ToString(Get(a, "name"));
                if (string.Equals(name, AppInfo.ReleaseAssetName, StringComparison.OrdinalIgnoreCase)) { asset = a; break; }
                if (asset == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) asset = a;
            }
            if (asset == null) return null;

            string body = Convert.ToString(Get(root, "body")) ?? "";
            Match m = Regex.Match(body, @"SHA-?256[^0-9a-fA-F]{0,10}([0-9a-fA-F]{64})", RegexOptions.IgnoreCase);
            return new UpdateInfo
            {
                Version = latest,
                Tag = tag,
                Notes = body,
                DownloadUrl = Convert.ToString(Get(asset, "browser_download_url")),
                Size = Convert.ToInt64(Get(asset, "size") ?? 0),
                Sha256 = m.Success ? m.Groups[1].Value.ToLowerInvariant() : null,
            };
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>새 버전을 내려받아 검증한 뒤 임시 파일 경로를 돌려준다.</summary>
        public static string Download(UpdateInfo u, Action<int> progress)
        {
            string target = ExePath + ".new";
            try { if (File.Exists(target)) File.Delete(target); } catch { }
            var req = (HttpWebRequest)WebRequest.Create(u.DownloadUrl);
            req.UserAgent = "AutoSetup-Updater";
            req.Timeout = 30000;
            using (var resp = req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write))
            {
                long total = u.Size > 0 ? u.Size : resp.ContentLength;
                var buf = new byte[81920];
                long done = 0;
                int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last && progress != null) { last = pct; progress(pct); }
                }
            }

            // 검증: 크기, 실행 파일 형식(MZ), SHA256 (있을 때)
            var fi = new FileInfo(target);
            string err = null;
            if (u.Size > 0 && fi.Length != u.Size) err = "다운로드한 파일 크기가 맞지 않습니다.";
            else
            {
                using (var fs = File.OpenRead(target))
                {
                    if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') err = "다운로드한 파일이 실행 파일이 아닙니다.";
                    else if (u.Sha256 != null)
                    {
                        fs.Position = 0;
                        using (var sha = SHA256.Create())
                        {
                            string h = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                            if (h != u.Sha256) err = "다운로드한 파일의 SHA256 값이 릴리스 정보와 다릅니다.";
                        }
                    }
                }
            }
            if (err != null)
            {
                try { File.Delete(target); } catch { }
                throw new Exception(err);
            }
            return target;
        }

        /// <summary>실행 파일을 교체하고 새 버전을 실행한다. (호출 후 현재 프로그램은 종료해야 함)</summary>
        public static void ApplyAndRestart(string newFile)
        {
            string exe = ExePath;
            string old = exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); }
            catch { old = exe + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".old"; }

            File.Move(exe, old);           // 실행 중인 exe 도 이름 변경은 가능
            try { File.Move(newFile, exe); }
            catch
            {
                try { File.Move(old, exe); } catch { }   // 되돌리기
                throw;
            }
            Process.Start(new ProcessStartInfo(exe, "/updated") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// <summary>업데이트 후 남은 이전 버전(.old) 파일 정리</summary>
        public static void CleanupLeftovers()
        {
            var t = new Thread(() =>
            {
                string dir = Path.GetDirectoryName(ExePath);
                string name = Path.GetFileName(ExePath);
                for (int i = 0; i < 20; i++)
                {
                    bool left = false;
                    try
                    {
                        // .new 는 다운로드 시작 시 지우므로 여기서는 .old 만 정리 (다운로드 중인 파일과 충돌 방지)
                        foreach (string f in Directory.GetFiles(dir, name + ".*")
                                                      .Where(f => f.EndsWith(".old", StringComparison.OrdinalIgnoreCase)))
                        {
                            try { File.Delete(f); } catch { left = true; }
                        }
                    }
                    catch { }
                    if (!left) return;
                    Thread.Sleep(500);   // 이전 버전 프로세스가 끝날 때까지 잠시 대기
                }
            }) { IsBackground = true };
            t.Start();
        }
    }
}
