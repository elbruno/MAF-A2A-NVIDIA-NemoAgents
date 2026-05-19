param(
    [int[]]$Ports = @(8088, 5055, 5000),
    [switch]$AsJson
)

function Get-PortConflictInfo {
    param(
        [int[]]$TargetPorts
    )

    $connections = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in $TargetPorts } |
        Sort-Object LocalPort, OwningProcess -Unique

    $results = @()
    foreach ($conn in $connections) {
        $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
        $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId = $($conn.OwningProcess)" -ErrorAction SilentlyContinue).CommandLine

        $results += [PSCustomObject]@{
            Port        = $conn.LocalPort
            Address     = $conn.LocalAddress
            ProcessId   = $conn.OwningProcess
            ProcessName = if ($proc) { $proc.ProcessName } else { "<exited>" }
            CommandLine = $cmdLine
        }
    }

    return $results
}

$conflicts = Get-PortConflictInfo -TargetPorts $Ports

if ($AsJson) {
    $conflicts | ConvertTo-Json -Depth 5
}
else {
    if (-not $conflicts -or $conflicts.Count -eq 0) {
        Write-Host "✓ No port conflicts detected for ports: $($Ports -join ', ')" -ForegroundColor Green
    }
    else {
        Write-Host "⚠ Port conflicts detected:" -ForegroundColor Yellow
        $conflicts | Format-Table Port, Address, ProcessId, ProcessName -AutoSize
        Write-Host "`nUse .\scripts\stop-port-conflicts.ps1 to stop these processes." -ForegroundColor Cyan
    }
}

if ($conflicts -and $conflicts.Count -gt 0) {
    exit 1
}

exit 0
