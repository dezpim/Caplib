$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Error "C# compiler not found at $csc"
    exit 1
}

$outExe = Join-Path $PSScriptRoot "Caplib.exe"

& $csc `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /out:$outExe `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    (Join-Path $PSScriptRoot "NativeMethods.cs") `
    (Join-Path $PSScriptRoot "ClickHook.cs") `
    (Join-Path $PSScriptRoot "ScreenCapturer.cs") `
    (Join-Path $PSScriptRoot "CloudUploader.cs") `
    (Join-Path $PSScriptRoot "MainForm.cs") `
    (Join-Path $PSScriptRoot "Program.cs")

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Build succeeded: $outExe"
