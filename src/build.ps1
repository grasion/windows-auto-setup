# AutoSetup.exe 빌드 스크립트
# Windows 에 기본 포함된 .NET Framework 4 C# 컴파일러(csc.exe)만 사용하므로 별도 설치가 필요 없습니다.
#   사용:  powershell -ExecutionPolicy Bypass -File src\build.ps1
#   버전:  src\version.txt 의 값 (또는 -Version 1.2.3)  → GitHub 릴리스 태그 v1.2.3 과 맞추세요
#   아이콘: src\app.ico   /   커피 QR: src\coffee_qr.png (있으면 exe 안에 포함)
param(
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'AutoSetup.exe'),
    [string]$Version,
    [switch]$AsInvoker   # 테스트용: 관리자 권한 요청 없이 실행되는 빌드
)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4) 를 찾을 수 없습니다.' }

# ---- 아이콘 (없으면 생성)
$ico = Join-Path $src 'app.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 24, 32, 48, 64, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $r = [single]($s * 0.2)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $w = [single]($s - 1)
        $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($w - $r, 0, $r, $r, 270, 90)
        $path.AddArc($w - $r, $w - $r, $r, $r, 0, 90); $path.AddArc(0, $w - $r, $r, $r, 90, 90); $path.CloseFigure()
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s),
                    [System.Drawing.Color]::FromArgb(0, 120, 215), [System.Drawing.Color]::FromArgb(0, 70, 150))
        $g.FillPath($brush, $path)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single][Math]::Max(1.5, $s * 0.09))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $u = $s / 16.0
        # 아래 화살표
        $g.DrawLine($pen, [single](8 * $u), [single](3 * $u), [single](8 * $u), [single](9.5 * $u))
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ([single](5 * $u)), ([single](7 * $u))),
                                                      (New-Object System.Drawing.PointF ([single](8 * $u)), ([single](10 * $u))),
                                                      (New-Object System.Drawing.PointF ([single](11 * $u)), ([single](7 * $u)))))
        # 받침
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ([single](3.5 * $u)), ([single](10 * $u))),
                                                      (New-Object System.Drawing.PointF ([single](3.5 * $u)), ([single](12.5 * $u))),
                                                      (New-Object System.Drawing.PointF ([single](12.5 * $u)), ([single](12.5 * $u))),
                                                      (New-Object System.Drawing.PointF ([single](12.5 * $u)), ([single](10 * $u)))))
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($ico)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close()
    Write-Host "아이콘 생성: $ico"
}

# ---- 매니페스트
$manifest = Join-Path $src 'app.manifest'
if ($AsInvoker) {
    $tmp = Join-Path $env:TEMP 'AutoSetup_asInvoker.manifest'
    (Get-Content $manifest -Raw -Encoding UTF8) -replace 'requireAdministrator', 'asInvoker' | Set-Content $tmp -Encoding UTF8
    $manifest = $tmp
}

# ---- 버전 정보 (version.txt 또는 -Version)
if (-not $Version) { $Version = (Get-Content (Join-Path $src 'version.txt') -Raw).Trim() }
$Version = $Version.TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "버전 형식이 올바르지 않습니다: '$Version' (예: 1.2.3)" }
$verCs = Join-Path $env:TEMP 'AutoSetup_Version.cs'
@"
[assembly: System.Reflection.AssemblyVersion("$Version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$Version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$Version")]
"@ | Set-Content $verCs -Encoding UTF8

$files = @(Get-ChildItem $src -Filter *.cs | ForEach-Object FullName) + $verCs
$cscArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/utf8output',
             "/out:$Out", "/win32icon:$ico", "/win32manifest:$manifest",
             '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll')

# ---- 화면 상단 로고 (있으면 exe 에 포함)
$logo = Join-Path $src 'logo.png'
if (Test-Path $logo) { $cscArgs += "/resource:$logo,logo.png" }

# ---- 커피 QR 이미지 (있으면 exe 에 포함)
$qr = Join-Path $src 'coffee_qr.png'
if (Test-Path $qr) { $cscArgs += "/resource:$qr,coffee_qr.png"; Write-Host '커피 QR 이미지 포함' }

& $csc @($cscArgs + $files)
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (코드 $LASTEXITCODE)" }
Write-Host "빌드 완료: $Out  (v$Version)"
