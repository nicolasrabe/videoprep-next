# Compila y corre las pruebas rapidas (sin ffmpeg real, sin las marcadas como inestables).
# Uso:  pwsh scripts/check.ps1   (o)   powershell -File scripts/check.ps1
$ErrorActionPreference = 'Continue'
Set-Location (Split-Path -Parent $PSScriptRoot)
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$build = dotnet build videoprep.slnx -c Release -v q 2>&1
if ($LASTEXITCODE -ne 0) {
    $build | Where-Object { $_ -match 'error' } | Select-Object -Unique -First 15 | ForEach-Object { Write-Host $_ }
    Write-Host ("FALLO al compilar ({0:n0} s)" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Red
    exit 1
}

$test = dotnet test videoprep.slnx -c Release --no-build --filter "Category!=Integration&Inestable!=si" 2>&1
$code = $LASTEXITCODE
$test | Where-Object { $_ -match '^\s*(failed|erró?neo|error)\b|\[FAIL\]' } | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
$test | Where-Object { $_ -match '(Total|Correctas|Superado|Con errores|Err\w+|Failed|Passed|Skipped|Omitid\w+):?\s+\d+' } | ForEach-Object { Write-Host $_.Trim() }

if ($code -eq 0) { Write-Host ("PASO ({0:n0} s)" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green }
else { Write-Host ("FALLO ({0:n0} s)" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Red }
exit $code
