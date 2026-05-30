param(
    [string]$ApiVersion = ""
)

$cfg = Get-Content (Join-Path $PSScriptRoot '..\aspire.config.json') -Raw | ConvertFrom-Json
$ep = $cfg.Parameters.'gpt-image-endpoint'.TrimEnd('/')
$key = $cfg.Parameters.'gpt-image-api-key'
$dep = $cfg.Parameters.'gpt-image-deployment'

function Invoke-ImageTest {
    param([string]$Url, [string]$Body, [hashtable]$Headers)
    Write-Host "POST $Url" -ForegroundColor Cyan
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Post -Headers $Headers -Body $Body -TimeoutSec 180 -ErrorAction Stop
        Write-Host "  STATUS: $($r.StatusCode)" -ForegroundColor Green
        Write-Host ("  BODY: " + $r.Content.Substring(0, [Math]::Min(160, $r.Content.Length)))
    }
    catch {
        $code = $null
        try { $code = $_.Exception.Response.StatusCode.value__ } catch {}
        Write-Host "  ERR STATUS: $code" -ForegroundColor Yellow
        try {
            $s = $_.Exception.Response.GetResponseStream()
            $sr = New-Object System.IO.StreamReader($s)
            Write-Host ("  ERR BODY: " + $sr.ReadToEnd())
        }
        catch { Write-Host "  (no body) $($_.Exception.Message)" }
    }
    Write-Host ""
}

$body = @{ model = $dep; prompt = "a simple blue circle on white background"; size = "1024x1024"; n = 1 } | ConvertTo-Json

# Route A: OpenAI v1 unified route (endpoint already ends with /openai/v1)
Invoke-ImageTest -Url "$ep/images/generations" -Body $body -Headers @{ "api-key" = $key; "Content-Type" = "application/json" }

# Route B: same, but Bearer auth
Invoke-ImageTest -Url "$ep/images/generations" -Body $body -Headers @{ "Authorization" = "Bearer $key"; "Content-Type" = "application/json" }

# Route C: classic Azure deployment route (strip /openai/v1, use /openai/deployments/<dep>)
$base = $ep -replace '/openai/v1$', ''
$av = if ($ApiVersion) { $ApiVersion } else { '2025-04-01-preview' }
$bodyNoModel = @{ prompt = "a simple blue circle on white background"; size = "1024x1024"; n = 1 } | ConvertTo-Json
Invoke-ImageTest -Url "$base/openai/deployments/$dep/images/generations?api-version=$av" -Body $bodyNoModel -Headers @{ "api-key" = $key; "Content-Type" = "application/json" }
