using System;
using System.Reflection;

namespace AutoSetup
{
    /// <summary>
    /// 배포 관련 설정 - GitHub 에 올린 뒤 아래 값만 채우고 다시 빌드하면 됩니다.
    /// </summary>
    public static class AppInfo
    {
        /// <summary>GitHub 사용자(또는 조직) 이름. 예) "hong-gildong"  (비워 두면 자동 업데이트 꺼짐)</summary>
        public const string GitHubOwner = "grasion";

        /// <summary>GitHub 저장소 이름. 예) "AutoSetup"</summary>
        public const string GitHubRepo = "windows-auto-setup";

        /// <summary>(선택) 후원 링크 - 토스/카카오페이 송금 링크 등. 비워 두면 QR 이미지만 표시</summary>
        public const string CoffeeUrl = "";

        /// <summary>릴리스에 올리는 실행 파일 이름</summary>
        public const string ReleaseAssetName = "AutoSetup.exe";

        public static Version Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string VersionText
        {
            get { var v = Version; return "v" + v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }
        }

        public static bool UpdateConfigured
        {
            get { return GitHubOwner.Length > 0 && GitHubRepo.Length > 0; }
        }

        public static string RepoUrl
        {
            get { return "https://github.com/" + GitHubOwner + "/" + GitHubRepo; }
        }
    }
}
