# MANUAL check, not part of CI (it drives the real desktop with UI Automation and starts real programs).
# Real replacement of videoprep.exe by the updater (F9), always on COPIES in a temporary folder:
#   -Old  a development build that says it is 0.2.9      (dotnet publish -c Debug ... -p:VersionPrefix=0.2.9)
#   -New  a development build that says it is 0.3.0      (same with 0.3.0)
#   -Broken an apphost that says 0.3.0 and cannot start (a program an antivirus blocked, or a damaged one)
#   -Work an empty folder; everything happens inside it
#   -Scenario good | badsum | nostart | crash | lockshort | locklong
#     nostart = the downloaded program cannot start at all; crash = it starts and "crashes" as it opens (development hook)
# Development builds read VIDEOPREP_UPDATE_URL (a Release build cannot, CI checks it); VIDEOPREP_HOME keeps the settings, queue
# and updates folder of this check apart from the real ones. It never touches the user's videoprep.exe: it stops only processes
# whose file is inside -Work. Prints PASS/FAIL lines and "RESULT ALL PASSED" at the end.
param([string]$Old, [string]$New, [string]$Broken, [string]$Work, [string]$Scenario = 'good')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  public static List<IntPtr> TopWindows(uint pid) {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
}
"@
[void][W]::SetProcessDPIAware()
$script:fails = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
  if ($ok) { Write-Output ("PASS  " + $name + $(if ($detail) { "  (" + $detail + ")" } else { '' })) }
  else { $script:fails++; Write-Output ("FAIL  " + $name + $(if ($detail) { "  (" + $detail + ")" } else { '' })) }
}
function Title([IntPtr]$h) { $sb = New-Object System.Text.StringBuilder 256; [void][W]::GetWindowText($h, $sb, 256); $sb.ToString() }
function WaitUntil([scriptblock]$cond, [int]$ms = 5000) {
  $end = [DateTime]::Now.AddMilliseconds($ms)
  while ([DateTime]::Now -lt $end) { $v = & $cond; if ($v) { return $v }; Start-Sleep -Milliseconds 150 }
  return $null
}
$A = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
function Uia-All([IntPtr]$h) {
  # A window that is just opening or closing can briefly not be available to UI Automation: look again a few times.
  for ($i = 0; $i -lt 6; $i++) {
    try { return $A::FromHandle($h).FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) }
    catch { Start-Sleep -Milliseconds 400 }
  }
  return @()
}
function Uia-Button([IntPtr]$h, [string]$name, [string]$id = '') {
  foreach ($e in (Uia-All $h)) {
    $c = $e.Current
    if ($c.ControlType -eq $CT::Button -and (($id -ne '' -and $c.AutomationId -eq $id) -or ($id -eq '' -and $c.Name -eq $name))) { return $e }
  }
  return $null
}
function Uia-HasText([IntPtr]$h, [string]$part) {
  foreach ($e in (Uia-All $h)) { if ($e.Current.Name -like ('*' + $part + '*')) { return $true } }
  return $false
}
function Uia-Click($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Uia-Select-Tab([IntPtr]$h, [string]$name) {
  foreach ($e in (Uia-All $h)) { if ($e.Current.ControlType -eq $CT::TabItem -and $e.Current.Name -eq $name) { $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); return $true } }
  return $false
}
function Find-Main($proc) {
  # The main window (it has the tabs); message dialogs have the same title.
  foreach ($h in (Windows-Of $proc)) {
    if ((Title $h) -ne 'videoprep') { continue }
    foreach ($e in (Uia-All $h)) { if ($e.Current.ControlType -eq $CT::TabItem -and $e.Current.Name -eq 'Ajustes') { return $h } }
  }
}
function Windows-Of($proc) { [W]::TopWindows([uint32]$proc.Id) }
function Hash($p) { (Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash.ToLowerInvariant() }
function Procs-In([string]$dir) { Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) } }

if (Test-Path $Work) { throw "-Work must not exist yet: $Work" }
foreach ($p in $Old, $New) { if (-not (Test-Path $p)) { throw "missing $p" } }
$Work = (New-Item -ItemType Directory -Force -Path $Work).FullName
$install = Join-Path $Work 'install'; $server = Join-Path $Work 'server'; $homeDir = Join-Path $Work 'home'
$dl = Join-Path $server 'download\v0.3.0'
New-Item -ItemType Directory -Force -Path $install, $dl, (Join-Path $homeDir 'roaming'), (Join-Path $homeDir 'out') | Out-Null
Write-Output ("SCENARIO " + $Scenario)

# ---------- the "installed" 0.2.9 and the "published" 0.3.0 ----------
$exe = Join-Path $install 'videoprep.exe'
Copy-Item -LiteralPath $Old -Destination $exe
$oldHash = Hash $exe
$pub = Join-Path $Work 'pub'; New-Item -ItemType Directory -Force -Path $pub | Out-Null
$source = if ($Scenario -eq 'nostart') { Join-Path $Broken 'videoprep.exe' } else { $New }
Copy-Item -LiteralPath $source -Destination (Join-Path $pub 'videoprep.exe')
$newHash = Hash (Join-Path $pub 'videoprep.exe')
$zip = Join-Path $dl 'videoprep-v0.3.0-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($pub, $zip)
$zipHash = Hash $zip
$sumHash = if ($Scenario -eq 'badsum') { ('0' * 64) } else { $zipHash }
[IO.File]::WriteAllText((Join-Path $dl 'SHA256SUMS.txt'), "$sumHash  videoprep-v0.3.0-win-x64.zip`n")
$json = '[{"tag_name":"v0.3.0","draft":false,"prerelease":true,"html_url":"https://github.com/nicolasrabe/videoprep-next/releases/tag/v0.3.0","body":"x","assets":[{"name":"videoprep-v0.3.0-win-x64.zip","size":1},{"name":"SHA256SUMS.txt","size":95}]}]'
[IO.File]::WriteAllText((Join-Path $server 'releases'), $json)
$settings = @{ version = 2; folder = (Join-Path $homeDir 'out'); lang = 'es'; theme = 'light'; cookieSource = 'none'; sound = $false; autoUpdate = $false; openDone = $false } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $homeDir 'roaming\settings.json'), $settings)

# ---------- a local server (nothing leaves this PC) ----------
$port = Get-Random -Minimum 20000 -Maximum 40000
$py = (Get-Command python).Source
$srv = Start-Process -FilePath $py -ArgumentList @('-I', '-m', 'http.server', $port, '--bind', '127.0.0.1', '--directory', $server) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 1
try { $r = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/releases"; Check 'the local server answers' ($r.StatusCode -eq 200) } catch { Check 'the local server answers' $false $_.Exception.Message }

# ---------- a locked file, as an antivirus would hold it ----------
$lock = $null
if ($Scenario -in 'lockshort', 'locklong') {
  # The updater copies the new program to videoprep.exe.new first: hold that name open for a while.
  $lock = [IO.File]::Open((Join-Path $install 'videoprep.exe.new'), 'Create', 'ReadWrite', 'None')
}

$env:VIDEOPREP_HOME = $homeDir
$env:VIDEOPREP_UPDATE_URL = "http://127.0.0.1:$port/"
if ($Scenario -eq 'crash') { $env:VIDEOPREP_TEST_CRASH_VERSION = '0.3.0' }   # only a 0.3.0 development build reacts to this
$app = Start-Process -FilePath $exe -PassThru
Remove-Item Env:\VIDEOPREP_HOME; Remove-Item Env:\VIDEOPREP_UPDATE_URL; Remove-Item Env:\VIDEOPREP_TEST_CRASH_VERSION -ErrorAction SilentlyContinue

try {
  $mainH = WaitUntil { Find-Main $app } 40000
  Check 'videoprep 0.2.9 opened' ($null -ne $mainH)
  $banner = WaitUntil { Uia-HasText $mainH 'Hay una versión nueva: 0.3.0' } 20000
  Check 'the banner "Hay una versión nueva: 0.3.0" appeared by itself' ([bool]$banner)
  Check 'nothing was installed without a click' ((Hash $exe) -eq $oldHash)

  $install1 = Uia-Button $mainH '' 'UpdateInstallButton'
  Check 'found the Actualizar button' ($null -ne $install1)
  Uia-Click $install1
  # The question: a second window of the same program with its own Actualizar button.
  $dialogH = WaitUntil { foreach ($h in (Windows-Of $app)) { if ($h -ne $mainH -and (Uia-Button $h 'Actualizar')) { return $h } } } 10000
  Check 'the confirmation question appeared' ($null -ne $dialogH)
  Check 'the question explains the .old copy' (Uia-HasText $dialogH 'videoprep.exe.old')
  Uia-Click (Uia-Button $dialogH 'Actualizar')

  if ($Scenario -eq 'nostart') {
    $failH = WaitUntil { foreach ($h in (Windows-Of $app)) { if ($h -ne $mainH -and (Uia-HasText $h 'No se pudo iniciar el instalador')) { return $h } } } 30000
    Check 'it said the installer could not start' ($null -ne $failH)
    if ($failH) { Uia-Click (Uia-Button $failH 'Cancelar') }
    Start-Sleep -Seconds 1
    Check 'the old videoprep did NOT close (the person is never left with nothing)' (-not $app.HasExited)
    Check 'videoprep.exe is untouched' ((Hash $exe) -eq $oldHash)
    Check 'there is no .old and no .new' ((-not (Test-Path "$exe.old")) -and (-not (Test-Path "$exe.new")))
  }
  elseif ($Scenario -eq 'badsum') {
    $failH = WaitUntil { foreach ($h in (Windows-Of $app)) { if ($h -ne $mainH -and (Uia-HasText $h 'no coincide con su comprobación')) { return $h } } } 30000
    Check 'a checksum failure was explained' ($null -ne $failH)
    if ($failH) { Uia-Click (Uia-Button $failH 'Cancelar') }
    Start-Sleep -Seconds 1
    Check 'the old videoprep is still the same file' ((Hash $exe) -eq $oldHash)
    Check 'the old videoprep is still running' (-not $app.HasExited)
    Check 'there is no .old and no .new' ((-not (Test-Path "$exe.old")) -and (-not (Test-Path "$exe.new")))
    Check 'the downloaded zip was not left in the updates folder' (-not (Test-Path (Join-Path $homeDir 'local\updates\0.3.0')))
  }
  else {
    if ($Scenario -eq 'lockshort') { Start-Sleep -Seconds 4; $lock.Dispose(); $lock = $null }
    $closed = WaitUntil { $app.Refresh(); $app.HasExited } 40000
    Check 'the old videoprep closed by itself' ([bool]$closed)

    if ($Scenario -in 'good', 'lockshort') {
      $newApp = WaitUntil { Procs-In $install | Where-Object { $_.Id -ne $app.Id } | Select-Object -First 1 } 60000
      Check 'the new videoprep started from the same place' ($null -ne $newApp)
      Check 'videoprep.exe is now the new file' ((Hash $exe) -eq $newHash)
      Check 'videoprep.exe.old is the old file (the way back)' ((Test-Path "$exe.old") -and ((Hash "$exe.old") -eq $oldHash))
      Check 'no videoprep.exe.new left' (-not (Test-Path "$exe.new"))
      Check 'the installed file says 0.3.0' (([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion) -like '0.3.0*') ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion)
      $newMain = WaitUntil { Find-Main $newApp } 40000
      Check 'the new videoprep window opened' ($null -ne $newMain)
      $msgH = WaitUntil { if ($newApp) { foreach ($h in (Windows-Of $newApp)) { if ($h -ne $newMain -and (Uia-HasText $h 'se actualizó a la versión 0.3.0')) { return $h } } } } 30000
      Check 'it told the person it updated to 0.3.0' ($null -ne $msgH)
      if ($msgH) { Uia-Click (Uia-Button $msgH 'Aceptar') }
      Check 'the result file was used up' (-not (Test-Path (Join-Path $homeDir 'local\updates\last-result.json')))
      if ($newMain) {
        $btn = WaitUntil { [void](Uia-Select-Tab $newMain 'Ajustes'); Uia-Button $newMain 'Buscar actualizaciones ahora' } 15000
        Check 'the button "Buscar actualizaciones ahora" is in Ajustes' ($null -ne $btn)
        if ($btn) {
          Uia-Click $btn
          $same = WaitUntil { Uia-HasText $newMain 'Ya tenés la última versión (0.3.0)' } 15000
          Check 'it says "Ya tenés la última versión (0.3.0)"' ([bool]$same)
        }
        Check 'no new banner (the server has nothing newer)' (-not (Uia-HasText $newMain 'Hay una versión nueva'))
      }
    }
    elseif ($Scenario -eq 'crash') {
      $back = WaitUntil { Procs-In $install | Select-Object -First 1 } 60000
      Check 'the old videoprep was opened again' ($null -ne $back)
      Start-Sleep -Seconds 2
      Check 'videoprep.exe is the old file again' ((Hash $exe) -eq $oldHash)
      Check 'no .new left' (-not (Test-Path "$exe.new"))
      $backMain = WaitUntil { Find-Main $back } 40000
      $msgH = WaitUntil { if ($back) { foreach ($h in (Windows-Of $back)) { if ($h -ne $backMain -and (Uia-HasText $h 'se cerró sola al abrirse')) { return $h } } } } 30000
      Check 'it told the person the new version closed by itself and the old one was put back' ($null -ne $msgH)
      if ($msgH) { Uia-Click (Uia-Button $msgH 'Aceptar') }
    }
    elseif ($Scenario -eq 'locklong') {
      # Held for longer than the updater waits: it must give up, change nothing and open the old one again.
      $back = WaitUntil { Procs-In $install | Where-Object { $_.Id -ne $app.Id } | Select-Object -First 1 } 90000
      Check 'the old videoprep was opened again after giving up' ($null -ne $back)
      Check 'videoprep.exe is the old file, untouched' ((Hash $exe) -eq $oldHash)
      Check 'no .old was made' (-not (Test-Path "$exe.old"))
      $backMain = WaitUntil { Find-Main $back } 40000
      $msgH = WaitUntil { if ($back) { foreach ($h in (Windows-Of $back)) { if ($h -ne $backMain -and (Uia-HasText $h 'ocupado')) { return $h } } } } 30000
      Check 'it told the person the file was busy and the old version keeps working' ($null -ne $msgH)
      if ($msgH) { Uia-Click (Uia-Button $msgH 'Aceptar') }
    }
  }
}
finally {
  if ($lock) { $lock.Dispose() }
  # Stop only what lives inside the work folder (never the user's own videoprep).
  foreach ($p in (Procs-In $Work)) { try { Stop-Process -Id $p.Id -Force } catch { } }
  if ($srv -and -not $srv.HasExited) { Stop-Process -Id $srv.Id -Force }
}
if ($script:fails -eq 0) { Write-Output 'RESULT ALL PASSED' } else { Write-Output ("RESULT " + $script:fails + " FAILED") }
