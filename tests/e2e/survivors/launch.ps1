# Started by the test project's build when the fixture is armed: leaves a daemon running the way a pull request's build could
# (Start-Process, outside the build's own process tree), waiting for the judge to write something it can forge. Waits until it
# has started, so the judge's sweep finds it running.
$markers = $env:LOCKFLIGHT_FIXTURE_MARKERS
$token = "$PID-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
Start-Process -WindowStyle Hidden -FilePath python -ArgumentList @("`"$PSScriptRoot\daemon.py`"", 'exec', $token) | Out-Null
$started = Join-Path $markers "started-exec-$token"
for ($i = 0; $i -lt 100 -and -not (Test-Path $started); $i++) { Start-Sleep -Milliseconds 100 }
exit 0
