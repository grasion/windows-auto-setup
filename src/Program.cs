using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// 버전(AssemblyVersion)은 build.ps1 이 src\version.txt (또는 -Version) 값으로 자동 생성합니다.
[assembly: AssemblyTitle("Windows 자동 설치 도우미")]
[assembly: AssemblyProduct("AutoSetup")]
[assembly: AssemblyDescription("설치 파일 · 시리얼 · 윈도우 초기 설정 자동화")]

namespace AutoSetup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Func<string, bool> has = name => args.Any(a => a.TrimStart('/', '-').Equals(name, StringComparison.OrdinalIgnoreCase));
            bool auto = has("auto");          // /auto : 저장된 설정으로 바로 실행
            bool updated = has("updated");    // 업데이트 직후 재시작

            // 같은 프로그램 중복 실행 방지 (업데이트 직후에는 이전 버전이 끝날 때까지 기다림)
            using (var mutex = new Mutex(false, "Global\\AutoSetup_WindowsInstallHelper"))
            {
                bool owned;
                try { owned = mutex.WaitOne(updated ? 15000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    MessageBox.Show("Windows 자동 설치 도우미가 이미 실행 중입니다.", MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    Updater.CleanupLeftovers();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (s, e) =>
                        MessageBox.Show("예기치 않은 오류가 발생했습니다.\n\n" + e.Exception.Message, MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                    string dir = Path.GetDirectoryName(Application.ExecutablePath);
                    Application.Run(new MainForm(dir, auto, updated));
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
