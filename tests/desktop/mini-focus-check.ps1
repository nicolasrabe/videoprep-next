# MANUAL check, not part of CI (it drives the real desktop: mouse, keyboard, a Notepad window).
# Real-desktop check of the mini window (F4): keyboard focus and the right-click menu.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tests\desktop\mini-focus-check.ps1 `
#         -Exe dist\win-x64ideoprep.exe -Tools .tools -Home1 <an empty folder for its data>
# It prints PASS/FAIL lines and "RESULT ALL PASSED" at the end. It stops only what it started.
# Runs the real videoprep.exe with its own data folder (VIDEOPREP_HOME) and cookies turned off, next to a Notepad.
# Free-licence video only (Big Buck Bunny). Nothing here touches the user's settings, queue or browser cookies.
param([string]$Exe, [string]$Tools, [string]$Home1)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr e);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cls, string title);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static List<IntPtr> TopWindows(uint pid) {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) list.Add(h); return true; }, IntPtr.Zero);
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
function Cls([IntPtr]$h) { $sb = New-Object System.Text.StringBuilder 256; [void][W]::GetClassName($h, $sb, 256); $sb.ToString() }
function Rect([IntPtr]$h) { $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r); $r }
function Fg { [W]::GetForegroundWindow() }
function Describe([IntPtr]$h) { if ($h -eq [IntPtr]::Zero) { return 'none' }; $p = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$p); "'" + (Title $h) + "' pid " + $p }
function WaitUntil([scriptblock]$cond, [int]$ms = 5000) {
  $end = [DateTime]::Now.AddMilliseconds($ms)
  while ([DateTime]::Now -lt $end) { $v = & $cond; if ($v) { return $v }; Start-Sleep -Milliseconds 100 }
  return $null
}
function Give-Focus([IntPtr]$h) {
  [W]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [W]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)   # Alt press: lets us change the foreground
  if ([W]::IsIconic($h)) { [void][W]::ShowWindow($h, 9) }
  [void][W]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 300
}
function Key([byte]$vk, [int]$hold = 90) { [W]::keybd_event($vk, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds $hold; [W]::keybd_event($vk, 0, 2, [UIntPtr]::Zero) }
function Mouse([int]$x, [int]$y, [string]$what) {
  [void][W]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 120
  switch ($what) {
    'left'      { [W]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero) }
    'right'     { [W]::mouse_event(0x8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(0x10, 0, 0, 0, [UIntPtr]::Zero) }
    'leftdown'  { [W]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero) }
    'leftup'    { [W]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero) }
  }
  Start-Sleep -Milliseconds 150
}
function Notepad-Text([IntPtr]$np) {
  $edit = [W]::FindWindowEx($np, [IntPtr]::Zero, 'Edit', $null)
  $sb = New-Object System.Text.StringBuilder 70000
  [void][W]::SendMessage($edit, 0x000D, [IntPtr]65000, $sb)
  $sb.ToString()
}
$A = [System.Windows.Automation.AutomationElement]
function Uia-Find([IntPtr]$h, [string]$name, $type) {
  $el = $A::FromHandle($h)
  $c1 = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
  $c2 = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type)
  $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition($c1, $c2)))
}
function Uia-Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
$CT = [System.Windows.Automation.ControlType]

# ---------- data folder of its own: cookies OFF, no auto update, a queue with one free-licence video ----------
New-Item -ItemType Directory -Force -Path (Join-Path $Home1 'roaming'), (Join-Path $Home1 'out') | Out-Null
$out = (Join-Path $Home1 'out')
$settings = @{ version = 2; folder = $out; lang = 'es'; theme = 'light'; cookieSource = 'none'; limit = 1; sound = $false; autoUpdate = $false; openDone = $false } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $Home1 'roaming\settings.json'), $settings)
$queue = '{"version":1,"items":[{"id":1,"url":"https://www.youtube.com/watch?v=aqz-KE-bpKQ","format":"mp4","cap":480,"naming":"original","custom":"","prefix":"Video","clips":[],"subFolder":""}]}'
[IO.File]::WriteAllText((Join-Path $Home1 'roaming\queue.json'), $queue)

# ---------- Notepad first, then videoprep ----------
$np = Start-Process notepad.exe -PassThru
$npH = [IntPtr]::Zero
[void](WaitUntil { $np.Refresh(); $script:npH = $np.MainWindowHandle; $np.MainWindowHandle -ne [IntPtr]::Zero } 8000)
$npH = $np.MainWindowHandle
Check 'Notepad opened' ($npH -ne [IntPtr]::Zero)

$env:VIDEOPREP_HOME = $Home1
$oldPath = $env:PATH
$env:PATH = $Tools + ';' + $env:PATH
$app = Start-Process -FilePath $Exe -PassThru
$env:PATH = $oldPath
Remove-Item Env:\VIDEOPREP_HOME

try {
  $mainH = WaitUntil { foreach ($h in [W]::TopWindows($app.Id)) { if ((Title $h) -eq 'videoprep' -and [W]::IsWindowVisible($h)) { return $h } } } 40000
  Check 'videoprep main window opened' ($null -ne $mainH)
  Start-Sleep -Seconds 2

  # The big window took the focus when it opened (normal). Put the "user" back in Notepad, as if typing there.
  Give-Focus $npH
  Check 'Notepad has the focus before the mini opens' ((Fg) -eq $npH) (Describe (Fg))

  $dl = Uia-Find $mainH 'Descargar' $CT::Button
  $mini = Uia-Find $mainH 'Mini' $CT::Button
  Check 'found the Descargar and Mini buttons' (($null -ne $dl) -and ($null -ne $mini))
  Uia-Invoke $dl
  Start-Sleep -Seconds 6
  # UI Automation "Invoke" on the big window's button activates that window (normal: the user clicked it). Info only.
  Write-Output ("INFO  after Descargar the focus is in: " + (Describe (Fg)))
  Give-Focus $npH

  # ---------- F4: open the mini while the user is in Notepad ----------
  Uia-Invoke $mini
  $miniH = WaitUntil { foreach ($h in [W]::TopWindows($app.Id)) { if ((Title $h) -eq 'videoprep mini' -and [W]::IsWindowVisible($h)) { return $h } } } 8000
  Check 'mini window appeared' ($null -ne $miniH)
  Start-Sleep -Milliseconds 800
  Check 'opening the mini did NOT take the focus' ((Fg) -eq $npH) (Describe (Fg))
  Check 'the big window was minimized (one taskbar button)' ([W]::IsIconic($mainH))
  $ex = [W]::GetWindowLongPtr($miniH, -20).ToInt64()
  Check 'mini has the "do not activate" style' (($ex -band 0x08000000) -ne 0)
  Check 'mini is always on top' (($ex -band 0x8) -ne 0)
  Check 'mini has no taskbar button (not an app window)' (($ex -band 0x40000) -eq 0)
  $r = Rect $miniH
  Check 'mini is one small line' ((($r.R - $r.L) -ge 280) -and (($r.R - $r.L) -le 520) -and (($r.B - $r.T) -le 60)) ("{0}x{1} px" -f ($r.R - $r.L), ($r.B - $r.T))

  # ---------- typing in Notepad while the mini updates ----------
  $before = (Notepad-Text $npH).Length
  $typed = 0; $lost = $false; $stolen = 0; $texts = New-Object System.Collections.Generic.HashSet[string]
  for ($i = 0; $i -lt 40; $i++) {
    # Never type unless Notepad has the focus: a key must not land in somebody else's window.
    if ((Fg) -eq $npH) { Key 0x41; $typed++ } else { $stolen++ }
    Start-Sleep -Milliseconds 350
    if ((Fg) -ne $npH) { $stolen++ }
    $t = $A::FromHandle($miniH).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Text)))
    foreach ($e in $t) { [void]$texts.Add($e.Current.Name) }
  }
  $after = (Notepad-Text $npH).Length
  Check 'every key typed in Notepad arrived while the mini was updating' (($after - $before) -eq $typed) ("typed $typed, arrived " + ($after - $before))
  Check 'the focus never left Notepad during 14 s of updates' ($stolen -eq 0) ("moments out of focus: $stolen")
  $pct = @($texts | Where-Object { $_ -match '^\d+ %$' })
  Check 'the mini showed live percentages' ($pct.Count -ge 3) (($pct | Select-Object -First 6) -join ', ')
  Check 'the mini never showed a link' (@($texts | Where-Object { $_ -match 'http|youtube\.com|youtu\.be' }).Count -eq 0) (($texts | Select-Object -First 8) -join ' | ')

  # ---------- menu: opens, Esc closes, click outside closes, focus stays ----------
  function Menu-Open { foreach ($h in [W]::TopWindows($app.Id)) { $e = Uia-Find $h 'Cerrar videoprep' $CT::MenuItem; if ($e) { return $e } }; return $null }
  function Open-Menu {
    $rr = Rect $miniH
    Mouse ($rr.L + 40) (($rr.T + $rr.B) / 2) 'right'
    WaitUntil { Menu-Open } 3000
  }
  $textBefore = Notepad-Text $npH

  $m = Open-Menu
  Check 'right-click opens the menu' ($null -ne $m)
  Check 'opening the menu did NOT take the focus' ((Fg) -eq $npH) (Describe (Fg))
  if ((Fg) -eq $npH) { Key 0x1B }   # Esc, sent to whoever has the focus: it must be Notepad
  $gone = WaitUntil { -not (Menu-Open) } 2500
  Check 'Esc closes the menu' ($null -ne $gone -and $gone -ne $false)
  Check 'after Esc the focus is still in Notepad' ((Fg) -eq $npH) (Describe (Fg))

  $m = Open-Menu
  Check 'menu opens again' ($null -ne $m)
  $nr = Rect $npH
  Mouse ($nr.L + 200) ($nr.T + 200) 'left'   # click in the Notepad text area
  $gone = WaitUntil { -not (Menu-Open) } 2500
  Check 'a click outside closes the menu' ($null -ne $gone -and $gone -ne $false)
  Check 'after the click outside the focus is in Notepad' ((Fg) -eq $npH) (Describe (Fg))

  # always on top, from the menu, live
  $m = Open-Menu
  $item = $null
  foreach ($h in [W]::TopWindows($app.Id)) { $e = Uia-Find $h 'Siempre encima' $CT::MenuItem; if ($e) { $item = $e } }
  Check 'menu has "Siempre encima"' ($null -ne $item)
  if ($item) {
    $b = $item.Current.BoundingRectangle
    $exPre = [W]::GetWindowLongPtr($miniH, -20).ToInt64()
    Write-Output ("INFO  topmost flag before the click: " + (($exPre -band 0x8) -ne 0))
    Mouse ([int]($b.X + $b.Width / 2)) ([int]($b.Y + $b.Height / 2)) 'left'
    Start-Sleep -Milliseconds 400
    Write-Output ("INFO  saved setting after the click: " + ((Get-Content (Join-Path $Home1 'roaming\settings.json') | Select-String 'miniOnTop') -join ''))
    foreach ($ms in 100, 300, 900, 2000) {
      Start-Sleep -Milliseconds $ms
      $exx = [W]::GetWindowLongPtr($miniH, -20).ToInt64()
      Write-Output ("INFO  topmost flag +" + $ms + " ms: " + (($exx -band 0x8) -ne 0) + "  menu open: " + ($null -ne (Menu-Open)))
    }
    $ex2 = [W]::GetWindowLongPtr($miniH, -20).ToInt64()
    Check 'clicking it turns "always on top" OFF live' (($ex2 -band 0x8) -eq 0)
    Check 'the menu closed after the click' ($null -eq (Menu-Open))
    Check 'the focus is still in Notepad after the menu item' ((Fg) -eq $npH) (Describe (Fg))
    $m = Open-Menu
    $item = $null
    foreach ($h in [W]::TopWindows($app.Id)) { $e = Uia-Find $h 'Siempre encima' $CT::MenuItem; if ($e) { $item = $e } }
    $b = $item.Current.BoundingRectangle
    Mouse ([int]($b.X + $b.Width / 2)) ([int]($b.Y + $b.Height / 2)) 'left'
    Start-Sleep -Milliseconds 600
    $ex3 = [W]::GetWindowLongPtr($miniH, -20).ToInt64()
    Check '... and ON again' (($ex3 -band 0x8) -ne 0)
  }
  $textAfter = Notepad-Text $npH
  Check 'the menu keys and clicks added nothing to what is typed in Notepad' ($textAfter -eq $textBefore) ("before " + $textBefore.Length + ", after " + $textAfter.Length)

  # ---------- typing still goes to Notepad right after the menu ----------
  $b2 = (Notepad-Text $npH).Length
  $sent = 0
  1..5 | ForEach-Object { if ((Fg) -eq $npH) { Key 0x42; $sent++ }; Start-Sleep -Milliseconds 100 }
  Check 'typing right after the menu still goes to Notepad' (($sent -eq 5) -and (((Notepad-Text $npH).Length - $b2) -eq 5)) ("sent $sent")

  # ---------- drag from anywhere ----------
  $r0 = Rect $miniH
  Mouse ($r0.L + 40) (($r0.T + $r0.B) / 2) 'leftdown'
  foreach ($k in 1..12) { [void][W]::SetCursorPos($r0.L + 40 + 12 * $k, ($r0.T + $r0.B) / 2 + 8 * $k); Start-Sleep -Milliseconds 40 }
  Mouse ($r0.L + 40 + 144) (($r0.T + $r0.B) / 2 + 96) 'leftup'
  Start-Sleep -Milliseconds 400
  $r1 = Rect $miniH
  Check 'the mini can be dragged' ((($r1.L - $r0.L) -ge 100) -and (($r1.T - $r0.T) -ge 60)) ("moved " + ($r1.L - $r0.L) + "," + ($r1.T - $r0.T))
  Check 'dragging did not take the focus' ((Fg) -eq $npH) (Describe (Fg))

  # ---------- Cerrar videoprep with a download running asks first ----------
  $m = Open-Menu
  $quit = $null
  foreach ($h in [W]::TopWindows($app.Id)) { $e = Uia-Find $h 'Cerrar videoprep' $CT::MenuItem; if ($e) { $quit = $e } }
  $b = $quit.Current.BoundingRectangle
  Mouse ([int]($b.X + $b.Width / 2)) ([int]($b.Y + $b.Height / 2)) 'left'
  # The question text has accents: look for a piece of it (this file is plain ASCII on purpose).
  $ask = WaitUntil {
    foreach ($h in [W]::TopWindows($app.Id)) {
      $all = $A::FromHandle($h).FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $CT::Text)))
      foreach ($e in $all) { if ($e.Current.Name -match 'Detenerlo') { return $h } }
    }
  } 4000
  Check '"Cerrar videoprep" asks first while something is downloading' ($null -ne $ask)
  Check 'the app is still running after asking' (-not $app.HasExited)
  if ($ask) {
    $no = Uia-Find $ask 'No' $CT::Button
    if ($no) { Uia-Invoke $no }
    Start-Sleep -Milliseconds 600
    Check 'answering No keeps videoprep open with the mini' ((-not $app.HasExited) -and [W]::IsWindow($miniH))
  }

  # ---------- Cerrar modo mini brings the big window back ----------
  $m = Open-Menu
  $close = $null
  foreach ($h in [W]::TopWindows($app.Id)) { $e = Uia-Find $h 'Cerrar modo mini' $CT::MenuItem; if ($e) { $close = $e } }
  $b = $close.Current.BoundingRectangle
  Mouse ([int]($b.X + $b.Width / 2)) ([int]($b.Y + $b.Height / 2)) 'left'
  Start-Sleep -Seconds 1
  $miniGone = -not ([W]::IsWindow($miniH) -and [W]::IsWindowVisible($miniH))
  Check '"Cerrar modo mini" closes the mini' $miniGone
  Check 'the big window is back' (-not [W]::IsIconic($mainH) -and [W]::IsWindowVisible($mainH))
}
finally {
  Write-Output ("RESULT  " + $(if ($script:fails -eq 0) { 'ALL PASSED' } else { "$($script:fails) FAILED" }))
  # Stop what this script started (and only that): videoprep with the downloader it started, and the Notepad.
  if ($app -and -not $app.HasExited) { & taskkill.exe /F /T /PID $app.Id | Out-Null }
  if ($np -and -not $np.HasExited) { Stop-Process -Id $np.Id -Force }
}
