# Builds dist\Lumora.exe (app) and dist\Lumora-Setup.exe (installer).
# Needs only the .NET Framework 4.x compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here
$lib  = Join-Path $here 'lib'
$out  = Join-Path $here 'out'
$dist = Join-Path $root 'dist'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $out, $dist | Out-Null

# WebView2 SDK (only downloaded once)
if (-not (Test-Path "$lib\WebView2Loader.dll")) {
    $tmp = Join-Path $env:TEMP 'lumora-wv2'
    New-Item -ItemType Directory -Force $tmp, $lib | Out-Null
    Invoke-WebRequest 'https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/1.0.2903.40' -OutFile "$tmp\wv2.zip"
    Expand-Archive "$tmp\wv2.zip" "$tmp\pkg" -Force
    Copy-Item "$tmp\pkg\lib\net462\Microsoft.Web.WebView2.Core.dll", "$tmp\pkg\lib\net462\Microsoft.Web.WebView2.WinForms.dll", "$tmp\pkg\runtimes\win-x64\native\WebView2Loader.dll" $lib
}

# Icon
if (-not (Test-Path "$here\lumora.ico")) { & "$here\make-icon.ps1" "$here\lumora.ico" }

# Version + update URL from update-config.txt -> generated Config.cs (shared by app and setup)
$cfg = @{}
Get-Content "$here\update-config.txt" -Encoding UTF8 | Where-Object { $_ -match '^\s*(\w+)\s*=\s*(.*?)\s*$' } | ForEach-Object { $cfg[$Matches[1]] = $Matches[2] }
$url = $cfg['url']
if (-not $url -or $url -match 'DEIN-NAME') { $url = '' } elseif (-not $url.EndsWith('/')) { $url += '/' }
$ver = $cfg['version']
# On GitHub the build number becomes the last part (1.0.0 -> 1.0.<build>), so every upload is a new version
if ($env:LUMORA_BUILD) { $ver = ($ver.Split('.')[0..1] -join '.') + '.' + $env:LUMORA_BUILD }
@"
[assembly: System.Reflection.AssemblyVersion("$ver.0")]
[assembly: System.Reflection.AssemblyFileVersion("$ver.0")]
namespace LumoraShared
{
    static class Config
    {
        public const string Version = "$ver";
        public const string UpdateBaseUrl = "$url";
    }
}
"@ | Set-Content "$out\Config.cs" -Encoding UTF8
Write-Host "Version $ver, Update-URL: $(if ($url) { $url } else { '(aus)' })"

# App
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/out:$out\Lumora.exe" "/win32icon:$here\lumora.ico" "/win32manifest:$here\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$lib\Microsoft.Web.WebView2.Core.dll" "/r:$lib\Microsoft.Web.WebView2.WinForms.dll" `
    "$here\App.cs" "$here\Online.cs" "$out\Config.cs"
if ($LASTEXITCODE) { throw 'App build failed' }

# Installer (all app files embedded)
$res = @("$out\Lumora.exe", "$root\lumora.html", "$lib\Microsoft.Web.WebView2.Core.dll", "$lib\Microsoft.Web.WebView2.WinForms.dll", "$lib\WebView2Loader.dll") |
    ForEach-Object { "/resource:$_,$(Split-Path $_ -Leaf)" }
& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ "/out:$dist\Lumora-Setup.exe" "/win32icon:$here\lumora.ico" "/win32manifest:$here\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll @res "$here\Setup.cs" "$here\Online.cs" "$out\Config.cs"
if ($LASTEXITCODE) { throw 'Setup build failed' }

# Upload these together for an update (version.txt lets installed apps detect EXE updates)
Set-Content "$dist\version.txt" $ver -Encoding ASCII -NoNewline
Copy-Item "$root\lumora.html" "$dist\lumora.html" -Force

Write-Host "OK -> $dist\Lumora-Setup.exe"

