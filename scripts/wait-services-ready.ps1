# Check health of all services and wait for readiness

param(
    [int]$TimeoutSeconds = 60,
    [int]$IntervalSeconds = 2
)

$nemoUrl = "http://127.0.0.1:8088/.well-known/agent-card.json"
$mafUrl = "http://127.0.0.1:5055/health"
$webUrl = "http://127.0.0.1:5000/health"

Write-Host "Checking service health..." -ForegroundColor Cyan
Write-Host "Timeout: ${TimeoutSeconds}s`n"

$elapsed = 0
$services = @{
    "NeMo Agent" = $nemoUrl
    "MAF Agent" = $mafUrl
    "Web UI" = $webUrl
}

while ($elapsed -lt $TimeoutSeconds) {
    Write-Host "[$([Math]::Round($elapsed))s] Checking services..." -ForegroundColor Yellow
    
    $allHealthy = $true
    
    foreach ($service in $services.GetEnumerator()) {
        try {
            $response = Invoke-WebRequest -Uri $service.Value -TimeoutSec 5 -ErrorAction Stop
            Write-Host "  ✓ $($service.Name): OK" -ForegroundColor Green
        }
        catch {
            Write-Host "  ✗ $($service.Name): Not ready" -ForegroundColor Red
            $allHealthy = $false
        }
    }
    
    if ($allHealthy) {
        Write-Host "`n✓ All services are healthy!" -ForegroundColor Green
        Write-Host "`nService Endpoints:" -ForegroundColor Yellow
        Write-Host "  Web UI: http://127.0.0.1:5000" -ForegroundColor Cyan
        Write-Host "  NeMo Agent: http://127.0.0.1:8088" -ForegroundColor Cyan
        Write-Host "  MAF Agent: http://127.0.0.1:5055" -ForegroundColor Cyan
        exit 0
    }
    
    Start-Sleep -Seconds $IntervalSeconds
    $elapsed += $IntervalSeconds
}

Write-Host "`n✗ Timeout: Services did not become healthy within ${TimeoutSeconds}s" -ForegroundColor Red
exit 1
