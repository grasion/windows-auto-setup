using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoSetup
{
    /// <summary>윈도우 초기 설정 항목</summary>
    public class Tweak
    {
        public string Id;
        public string Group;
        public string Name;
        public string Desc;
        public bool Default;
        public Action<Action<string>> Apply;   // 인자: 상세 로그 출력 함수
    }

    public static class Tweaks
    {
        const string ADV = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string EXP = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
        const string CDM = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

        public static readonly List<Tweak> All = Build();

        static Tweak T(string id, string group, bool def, string name, string desc, Action<Action<string>> apply)
        {
            return new Tweak { Id = id, Group = group, Default = def, Name = name, Desc = desc, Apply = apply };
        }

        static List<Tweak> Build()
        {
            var l = new List<Tweak>();

            // ---------------- 탐색기
            const string G1 = "탐색기";
            l.Add(T("ShowFileExt", G1, true, "파일 확장자 표시",
                "파일 이름 뒤의 .exe, .txt 같은 확장자를 항상 표시합니다.",
                log => Reg.Dw(ADV, "HideFileExt", 0)));
            l.Add(T("ShowHidden", G1, false, "숨김 파일 표시",
                "숨김 속성이 있는 파일과 폴더를 표시합니다.",
                log => Reg.Dw(ADV, "Hidden", 1)));
            l.Add(T("LaunchToPC", G1, true, "탐색기를 열면 '내 PC' 표시",
                "파일 탐색기 시작 위치를 '홈/즐겨찾기' 대신 '내 PC'로 바꿉니다.",
                log => Reg.Dw(ADV, "LaunchTo", 1)));
            l.Add(T("ClassicMenu", G1, true, "Windows 11 기존(전체) 우클릭 메뉴",
                "'더 많은 옵션 표시'를 누르지 않아도 전체 우클릭 메뉴가 바로 나오게 합니다. (Windows 11)",
                log => Reg.Str(@"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "")));
            l.Add(T("DesktopIcons", G1, true, "바탕화면에 내 PC·휴지통·사용자 폴더·제어판 아이콘",
                "바탕화면에 기본 시스템 아이콘을 표시합니다.",
                log =>
                {
                    string k = EXP + @"\HideDesktopIcons\NewStartPanel";
                    foreach (string g in new[] { "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "{645FF040-5081-101B-9F08-00AA002F954E}",
                                                 "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}" })
                        Reg.Dw(k, g, 0);
                }));
            l.Add(T("NoRecentFiles", G1, false, "홈 화면에 최근 파일·자주 쓰는 폴더 숨기기",
                "탐색기 홈 화면에 최근 사용한 파일 목록을 표시하지 않습니다. (개인정보 보호)",
                log => { Reg.Dw(EXP, "ShowRecent", 0); Reg.Dw(EXP, "ShowFrequent", 0); }));
            l.Add(T("NoShortcutSuffix", G1, true, "바로 가기 이름에 '- 바로 가기' 붙이지 않기",
                "바로 가기를 만들 때 파일 이름 뒤에 '- 바로 가기' 글자가 붙지 않게 합니다.",
                log => Reg.Str(EXP + @"\NamingTemplates", "ShortcutNameTemplate", "\"%s.lnk\"")));
            l.Add(T("NoAutoplay", G1, true, "USB·CD 자동 재생 끄기",
                "USB 메모리나 CD를 넣었을 때 자동으로 실행/팝업되지 않게 합니다. (바이러스 감염 예방)",
                log => Reg.Dw(EXP + @"\AutoplayHandlers", "DisableAutoplay", 1)));
            l.Add(T("FullPathTitle", G1, false, "탐색기 제목 표시줄에 전체 경로 표시",
                "탐색기 창 제목에 현재 폴더의 전체 경로(C:\\...)를 표시합니다.",
                log => Reg.Dw(EXP + @"\CabinetState", "FullPath", 1)));
            l.Add(T("CheckBoxSelect", G1, false, "파일 선택 확인란(체크박스) 표시",
                "탐색기에서 파일/폴더 앞에 체크박스를 표시해 마우스만으로 여러 개를 선택할 수 있게 합니다.",
                log => Reg.Dw(ADV, "AutoCheckSelect", 1)));

            // ---------------- 작업 표시줄 / 시작 메뉴
            const string G2 = "작업 표시줄 · 시작 메뉴";
            l.Add(T("TaskbarLeft", G2, false, "작업 표시줄 아이콘 왼쪽 정렬",
                "Windows 11 작업 표시줄의 시작 버튼과 아이콘을 예전처럼 왼쪽에 배치합니다.",
                log => Reg.Dw(ADV, "TaskbarAl", 0)));
            l.Add(T("SearchIcon", G2, true, "검색 상자를 아이콘으로 줄이기",
                "작업 표시줄의 넓은 검색 상자를 돋보기 아이콘으로 바꿉니다.",
                log => Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1)));
            l.Add(T("HideTaskView", G2, true, "'작업 보기' 버튼 숨기기",
                "작업 표시줄의 작업 보기(가상 데스크톱) 버튼을 숨깁니다. Win+Tab 으로 계속 사용할 수 있습니다.",
                log => Reg.Dw(ADV, "ShowTaskViewButton", 0)));
            l.Add(T("HideWidgets", G2, true, "위젯(뉴스·날씨) 끄기",
                "작업 표시줄의 위젯/뉴스 및 관심사 기능을 끕니다.",
                log =>
                {
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
                    try { Reg.Dw(ADV, "TaskbarDa", 0); } catch { }
                    try { Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", 2); } catch { }
                }));
            l.Add(T("HideChatCopilot", G2, true, "채팅·Copilot 버튼 숨기기",
                "작업 표시줄의 Teams 채팅 및 Copilot 버튼을 숨깁니다.",
                log => { Reg.Dw(ADV, "TaskbarMn", 0); Reg.Dw(ADV, "ShowCopilotButton", 0); }));
            l.Add(T("NoWebSearch", G2, true, "시작 메뉴 검색에서 Bing 웹 결과 끄기",
                "시작 메뉴 검색 시 인터넷(Bing) 결과를 표시하지 않아 검색이 빨라집니다.",
                log =>
                {
                    Reg.Dw(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
                }));
            l.Add(T("ShowSeconds", G2, false, "작업 표시줄 시계에 초 표시",
                "작업 표시줄 시계를 '오후 3:25:07' 처럼 초 단위까지 표시합니다.",
                log => Reg.Dw(ADV, "ShowSecondsInSystemClock", 1)));
            l.Add(T("NeverCombine", G2, false, "작업 표시줄 단추 합치지 않기 (이름 표시)",
                "같은 프로그램 창을 하나로 합치지 않고 창 이름과 함께 따로 표시합니다.",
                log => { Reg.Dw(ADV, "TaskbarGlomLevel", 2); Reg.Dw(ADV, "MMTaskbarGlomLevel", 2); }));
            l.Add(T("NoRecentApps", G2, false, "시작 메뉴에 '최근에 추가한 앱' 숨기기",
                "시작 메뉴에 새로 설치한 앱 목록이 표시되지 않게 합니다.",
                log => Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer", "HideRecentlyAddedApps", 1)));

            // ---------------- 개인정보 / 광고
            const string G3 = "개인정보 · 광고";
            l.Add(T("NoAds", G3, true, "광고 ID·맞춤형 광고 끄기",
                "앱이 광고 ID를 이용해 맞춤 광고를 표시하지 못하게 합니다.",
                log =>
                {
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0);
                }));
            l.Add(T("NoSuggestions", G3, true, "추천 앱·팁·자동 앱 설치 끄기",
                "시작 메뉴/잠금 화면의 추천 앱, 'Windows 팁', 홍보 앱 자동 설치, '설정 완료하기' 안내를 끕니다.",
                log =>
                {
                    foreach (string n in new[] { "SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled",
                                                 "PreInstalledAppsEnabled", "OemPreInstalledAppsEnabled", "SubscribedContent-310093Enabled",
                                                 "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
                                                 "SubscribedContent-338393Enabled", "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled" })
                        Reg.Dw(CDM, n, 0);
                    Reg.Dw(ADV, "Start_IrisRecommendations", 0);
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0);
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1);
                }));
            l.Add(T("Telemetry", G3, true, "진단 데이터(원격 분석) 최소화",
                "Microsoft 로 보내는 진단 데이터를 필수 수준으로 낮춥니다.",
                log =>
                {
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                    Reg.Dw(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
                }));
            l.Add(T("NoActivity", G3, true, "활동 기록 수집 끄기",
                "사용한 앱/파일 활동 기록을 수집하거나 업로드하지 않습니다.",
                log =>
                {
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
                }));
            l.Add(T("NoLockTips", G3, true, "잠금 화면 팁·광고 끄기",
                "잠금 화면에 표시되는 재미있는 정보, 팁, 광고를 끕니다.",
                log =>
                {
                    Reg.Dw(CDM, "RotatingLockScreenOverlayEnabled", 0);
                    Reg.Dw(CDM, "SubscribedContent-338387Enabled", 0);
                }));
            l.Add(T("NoRecall", G3, true, "Recall(AI 화면 기록) 끄기",
                "Copilot+ PC 의 화면 스냅샷 저장(Recall) 기능을 끕니다.",
                log =>
                {
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1);
                    Reg.Dw(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1);
                }));
            l.Add(T("NoCopilot", G3, false, "Windows Copilot 끄기",
                "Windows 의 Copilot 기능을 정책으로 끕니다.",
                log =>
                {
                    Reg.Dw(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
                }));
            l.Add(T("NoEdgeFirstRun", G3, true, "Edge 첫 실행 설정 화면 건너뛰기",
                "Microsoft Edge 를 처음 열 때 나오는 가져오기/로그인 안내 화면을 건너뜁니다.",
                log => Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", 1)));

            // ---------------- 전원 / 성능
            const string G4 = "전원 · 성능";
            l.Add(T("NoFastStartup", G4, true, "빠른 시작 끄기 (완전 종료)",
                "종료 시 완전히 꺼지도록 하여 드라이버/업데이트 관련 문제를 줄입니다.",
                log => Reg.Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0)));
            l.Add(T("NoSleepAC", G4, true, "전원 연결 시 절전 모드 사용 안 함",
                "전원 어댑터 연결 상태에서는 PC가 절전/최대 절전으로 들어가지 않습니다. (화면 끄기는 유지)",
                log =>
                {
                    Util.Run(Util.Sys32("powercfg.exe"), "/change standby-timeout-ac 0");
                    Util.Run(Util.Sys32("powercfg.exe"), "/change hibernate-timeout-ac 0");
                }));
            l.Add(T("HighPerf", G4, false, "고성능 전원 관리 옵션",
                "전원 관리 옵션을 '고성능'으로 바꿉니다. (데스크톱 권장, 노트북은 배터리 소모 증가)",
                log =>
                {
                    const string hp = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
                    if (Util.Run(Util.Sys32("powercfg.exe"), "/setactive " + hp) == 0) return;
                    string o;
                    Util.Run(Util.Sys32("powercfg.exe"), "-duplicatescheme " + hp, out o);
                    Match m = Regex.Match(o ?? "", @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
                    if (!m.Success) throw new Exception("이 기기는 고성능 전원 구성표를 지원하지 않습니다.");
                    Util.Run(Util.Sys32("powercfg.exe"), "/setactive " + m.Value);
                }));
            l.Add(T("NoHibernate", G4, false, "최대 절전 모드 끄기 (디스크 공간 확보)",
                "hiberfil.sys 를 삭제해 RAM 크기만큼 디스크 공간을 확보합니다. 빠른 시작도 함께 꺼집니다.",
                log => Util.Run(Util.Sys32("powercfg.exe"), "/hibernate off")));
            l.Add(T("NoStartupDelay", G4, true, "시작 프로그램 실행 지연 없애기",
                "로그인 후 시작 프로그램이 몇 초 늦게 실행되는 기본 지연을 없앱니다.",
                log => Reg.Dw(EXP + @"\Serialize", "StartupDelayInMSec", 0)));
            l.Add(T("FastMenu", G4, true, "메뉴 표시 속도 빠르게",
                "마우스를 올렸을 때 하위 메뉴가 열리는 지연 시간을 400ms → 100ms 로 줄입니다.",
                log => Reg.Str(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "100")));
            l.Add(T("NoGameDVR", G4, true, "게임 바 백그라운드 녹화 끄기",
                "게임 중 백그라운드 화면 녹화(Game DVR)를 꺼서 게임 성능 저하를 막습니다.",
                log =>
                {
                    Reg.Dw(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0);
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                    Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
                }));
            l.Add(T("FastShutdown", G4, false, "종료 시 응답 없는 앱 빨리 닫기",
                "PC를 끌 때 응답 없는 프로그램을 기다리지 않고 바로 닫습니다. (저장하지 않은 작업이 사라질 수 있음)",
                log =>
                {
                    Reg.Str(@"HKCU\Control Panel\Desktop", "AutoEndTasks", "1");
                    Reg.Str(@"HKCU\Control Panel\Desktop", "WaitToKillAppTimeout", "2000");
                    Reg.Str(@"HKCU\Control Panel\Desktop", "HungAppTimeout", "2000");
                }));

            // ---------------- 시스템
            const string G5 = "시스템";
            l.Add(T("NumLock", G5, true, "부팅 시 NumLock 켜기",
                "로그인 화면과 바탕화면에서 숫자 키패드가 바로 동작하도록 NumLock 을 켭니다.",
                log =>
                {
                    Reg.Str(@"HKU\.DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators", "2");
                    Reg.Str(@"HKCU\Control Panel\Keyboard", "InitialKeyboardIndicators", "2");
                }));
            l.Add(T("NoStickyKeys", G5, true, "Shift 5번 고정 키 팝업 끄기",
                "Shift 를 5번 누르면 뜨는 고정 키 팝업이 게임/작업 중에 나오지 않게 합니다.",
                log =>
                {
                    Reg.Str(@"HKCU\Control Panel\Accessibility\StickyKeys", "Flags", "506");
                    Reg.Str(@"HKCU\Control Panel\Accessibility\Keyboard Response", "Flags", "122");
                    Reg.Str(@"HKCU\Control Panel\Accessibility\ToggleKeys", "Flags", "58");
                }));
            l.Add(T("NoAutoReboot", G5, true, "로그인 중 Windows 업데이트 자동 재시작 막기",
                "사용자가 로그인해 있을 때 업데이트 때문에 PC가 강제로 재시작되지 않게 합니다.",
                log => Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1)));
            l.Add(T("TimeSync", G5, true, "인터넷 시간 즉시 동기화",
                "Windows 시간 서비스를 켜고 시계를 인터넷 시간과 바로 맞춥니다.",
                log =>
                {
                    Util.Run(Util.Sys32("sc.exe"), "config w32time start= auto");
                    Util.Run(Util.Sys32("net.exe"), "start w32time");
                    Util.Run(Util.Sys32("w32tm.exe"), "/resync /force");
                }));
            l.Add(T("LongPaths", G5, true, "긴 파일 경로(260자 이상) 허용",
                "경로가 260자를 넘는 파일도 다룰 수 있게 합니다.",
                log => Reg.Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1)));
            l.Add(T("NoMouseAccel", G5, false, "마우스 가속(포인터 정밀도 향상) 끄기",
                "마우스를 움직인 거리만큼 정확하게 포인터가 이동합니다. (게임/디자인 작업에 유리)",
                log =>
                {
                    Reg.Str(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0");
                    Reg.Str(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0");
                    Reg.Str(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0");
                }));
            l.Add(T("DarkMode", G5, false, "다크 모드",
                "Windows 와 앱을 어두운 테마로 바꿉니다.",
                log =>
                {
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0);
                    Reg.Dw(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                }));
            l.Add(T("NoP2PUpdate", G5, false, "업데이트 P2P 공유(배달 최적화) 끄기",
                "내 PC의 인터넷 대역폭으로 다른 PC에 업데이트를 전송하지 않습니다.",
                log => Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)));
            l.Add(T("RemoteDesktop", G5, false, "원격 데스크톱 허용",
                "다른 PC에서 이 PC로 원격 데스크톱 접속을 허용하고 방화벽을 엽니다. (Pro 이상 에디션)",
                log =>
                {
                    Reg.Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections", 0);
                    string o;
                    Util.RunPowerShell("Enable-NetFirewallRule -Group '@FirewallAPI.dll,-28752' -ErrorAction SilentlyContinue", out o);
                }));
            l.Add(T("BsodDetail", G5, true, "블루스크린에 상세 오류 정보 표시",
                "블루스크린 화면에 오류 코드와 원인 드라이버 정보를 함께 표시해 문제 해결을 돕습니다.",
                log => Reg.Dw(@"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", 1)));
            l.Add(T("NoDriverUpdate", G5, false, "Windows 업데이트로 드라이버 자동 설치 안 함",
                "제조사 드라이버를 직접 설치한 경우 Windows 업데이트가 다른 버전으로 바꾸지 않게 합니다.",
                log => Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1)));
            l.Add(T("NoLockScreen", G5, false, "잠금 화면 건너뛰기 (바로 로그인 화면)",
                "부팅/잠금 해제 시 사진이 나오는 잠금 화면 없이 바로 암호 입력 화면을 표시합니다. (Pro 이상 권장)",
                log => Reg.Dw(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1)));
            l.Add(T("VerboseStatus", G5, false, "시작·종료 시 상세 상태 메시지 표시",
                "부팅/종료가 오래 걸릴 때 어떤 작업 중인지 화면에 자세히 표시합니다.",
                log => Reg.Dw(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1)));

            // ---------------- 기본 앱 정리
            const string G6 = "기본 앱 정리";
            l.Add(T("RemoveBloat", G6, false, "불필요한 기본 앱 제거",
                "뉴스, 날씨, 솔리테어, Clipchamp, 시작하기, 피드백 허브, 지도, To Do, Power Automate 등을 제거합니다. (스토어에서 다시 설치 가능)",
                log =>
                {
                    const string script = @"
$apps = 'Microsoft.BingNews','Microsoft.BingWeather','Microsoft.BingSearch','Microsoft.GetHelp','Microsoft.Getstarted',
        'Microsoft.MicrosoftSolitaireCollection','Microsoft.People','Microsoft.WindowsFeedbackHub','Clipchamp.Clipchamp',
        'Microsoft.MicrosoftOfficeHub','Microsoft.549981C3F5F10','Microsoft.WindowsMaps','Microsoft.ZuneVideo',
        'MicrosoftCorporationII.MicrosoftFamily','Microsoft.Todos','Microsoft.PowerAutomateDesktop','Microsoft.Windows.DevHome',
        'Microsoft.MixedReality.Portal','Microsoft.SkypeApp','Microsoft.Microsoft3DViewer'
$prov = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue)
foreach ($a in $apps) {
    foreach ($x in @(Get-AppxPackage -AllUsers -Name $a -ErrorAction SilentlyContinue)) {
        try { Remove-AppxPackage -Package $x.PackageFullName -AllUsers -ErrorAction Stop; Write-Output ""제거: $a"" }
        catch { try { Remove-AppxPackage -Package $x.PackageFullName -ErrorAction Stop; Write-Output ""제거: $a"" } catch { } }
    }
    foreach ($p in ($prov | Where-Object { $_.DisplayName -eq $a })) {
        Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName -ErrorAction SilentlyContinue | Out-Null
    }
}";
                    string o;
                    Util.RunPowerShell(script, out o);
                    foreach (string line in (o ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        if (line.StartsWith("제거:")) log("    - " + line);
                }));
            l.Add(T("RemoveOneDrive", G6, false, "OneDrive 제거",
                "OneDrive 를 제거합니다. OneDrive 로 백업 중인 파일이 있으면 먼저 확인하세요.",
                log =>
                {
                    foreach (var p in Process.GetProcessesByName("OneDrive")) { try { p.Kill(); } catch { } }
                    string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var cands = new List<string> { Path.Combine(win, @"System32\OneDriveSetup.exe"), Path.Combine(win, @"SysWOW64\OneDriveSetup.exe") };
                    foreach (string root in new[] {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive") })
                    {
                        try { if (Directory.Exists(root)) cands.AddRange(Directory.GetFiles(root, "OneDriveSetup.exe", SearchOption.AllDirectories)); } catch { }
                    }
                    string exe = cands.FirstOrDefault(File.Exists);
                    if (exe == null) throw new Exception("OneDrive 설치 파일을 찾지 못했습니다 (이미 제거됨?).");
                    Util.Run(exe, "/uninstall");
                }));

            return l;
        }
    }
}
