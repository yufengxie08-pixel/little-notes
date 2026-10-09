$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler not found' }
Push-Location $PSScriptRoot
try {
  & $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:小贴事.exe /win32icon:小贴事-奶油便签.ico /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xml.Linq.dll /reference:Microsoft.Web.WebView2.Core.dll /reference:Microsoft.Web.WebView2.WinForms.dll LittleNotes.cs
  if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
