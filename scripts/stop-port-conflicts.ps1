[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int[]]$Ports = @(8088, 5055, 5000),
    [switch]$Force
)

$currentPid = $PID

$connections = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -in $Ports } |
    Sort-Object LocalPort, OwningProcess -Unique

if (-not $connections -or $connections.Count -eq 0) {
    Write-Host "✓ No listening processes found on ports: $($Ports -join ', ')" -ForegroundColor Green
    exit 0
}

$targets = foreach ($conn in $connections) {
    $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        Port        = $conn.LocalPort
        ProcessId   = $conn.OwningProcess
        ProcessName = if ($proc) { $proc.ProcessName } else { "<exited>" }
    }
}

Write-Host "Found blocking listeners:" -ForegroundColor Yellow
$targets | Format-Table Port, ProcessId, ProcessName -AutoSize

$uniquePids = $targets.ProcessId | Sort-Object -Unique
foreach ($targetPid in $uniquePids) {
    if ($targetPid -eq $currentPid) {
        Write-Host "Skipping current PowerShell process ($targetPid)." -ForegroundColor DarkYellow
        continue
    }

    $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
    if (-not $proc) {
        continue
    }

    $targetName = "$($proc.ProcessName) (PID $targetPid)"
    if ($PSCmdlet.ShouldProcess($targetName, "Stop process")) {
        if ($Force) {
            Stop-Process -Id $targetPid -Force -ErrorAction Stop
        }
        else {
            Stop-Process -Id $targetPid -ErrorAction Stop
        }
        Write-Host "Stopped $targetName" -ForegroundColor Green
    }
}

Write-Host "Done. Re-run .\scripts\check-port-conflicts.ps1 to confirm." -ForegroundColor Cyan
