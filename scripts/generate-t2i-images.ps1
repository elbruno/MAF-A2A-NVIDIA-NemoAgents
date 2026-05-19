# Generate promotional images using ElBruno.Text2Image CLI
# Install ElBruno.Text2Image CLI first: dotnet tool install -g elbruno.text2image

param(
    [string]$OutputDir = "docs\promotional\images",
    [string]$Provider = "openai",  # openai, azure, or local
    [string]$Style = "modern"      # modern, minimalist, professional
)

function New-Image {
    param(
        [string]$Name,
        [string]$Prompt,
        [int]$Width = 1920,
        [int]$Height = 1080
    )
    
    Write-Host "Generating: $Name" -ForegroundColor Yellow
    
    $outputPath = Join-Path $OutputDir "$Name.png"
    
    # Call ElBruno.Text2Image CLI
    t2i generate `
        --prompt "$Prompt" `
        --output "$outputPath" `
        --width $Width `
        --height $Height `
        --provider $Provider `
        --style $Style `
        --quality high
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Generated: $Name" -ForegroundColor Green
    }
    else {
        Write-Host "✗ Failed to generate: $Name" -ForegroundColor Red
    }
}

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Generating Promotional Images" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Image 1: System Architecture
New-Image `
    -Name "system-architecture" `
    -Prompt @"
System architecture diagram showing three connected cloud services:
- Left: Purple box labeled "NeMo Data Analysis Agent" with trend chart and anomaly detection icons
- Center: Blue box labeled "Web Chat Interface" with chat bubbles and dashboard 
- Right: Green box labeled "MAF Action Agent" with rocket/action icons
- Arrows show JSON-RPC A2A communication between services (labeled A2A Protocol)
- Top: "Azure Aspire Orchestration" cloud with connections to all services
- Bottom: "OpenTelemetry" showing distributed tracing
Modern tech illustration, clean minimalist style, professional blue/purple/green color scheme
High contrast, suitable for blog posts and presentations
"@

# Image 2: Data Flow Visualization
New-Image `
    -Name "data-flow" `
    -Prompt @"
Horizontal data flow pipeline showing 6 connected stages:
1. Left: User icon with speech bubble "Analyze Data"
2. Arrow right to: Chart/data icon "Receive Request"
3. Arrow right to: Python icon "NeMo Analysis" with trend lines and anomaly points
4. Arrow right to: Lightning bolt "Generate Insights" 
5. Arrow right to: Rocket icon "MAF Execute Action"
6. Right: Dashboard icon "Display Results"
Below each stage show timing: User (0s) → Request (0.1s) → Analysis (0.5s) → Insights (0.3s) → Action (0.2s) → Results (0.1s)
Flowing left to right with smooth connections
Modern flat design, blue gradient background
"@

# Image 3: Feature Highlights
New-Image `
    -Name "feature-highlights" `
    -Prompt @"
Infographic showing 6 key features in 2x3 grid layout:

Top row (left to right):
1. Icon: Bar chart with trend arrow up, Text: "Real-Time Analysis", Subtitle: "Trend detection & anomalies"
2. Icon: Rocket launching, Text: "Autonomous Actions", Subtitle: "Execute without handoffs"
3. Icon: Connected nodes network, Text: "Agent Discovery", Subtitle: "Service-to-service awareness"

Bottom row (left to right):
4. Icon: Connected lines/nodes, Text: "Distributed Tracing", Subtitle: "Full OTEL visibility"
5. Icon: Checkmark in circle, Text: "Production Ready", Subtitle: "Tests, docs, configs"
6. Icon: GitHub logo, Text: "100% Open Source", Subtitle: "MIT license"

Each feature box has a distinct icon and clean typography
Modern flat design with rounded corners
Color palette: Blue, purple, green, white background
"@

# Image 4: Technology Stack
New-Image `
    -Name "tech-stack" `
    -Prompt @"
Layered technology stack visualization:

Bottom layer (Data): Python, Pandas, NumPy, Scikit-Learn icons and logos
Middle layer (Analysis): NVIDIA NeMo Toolkit logo with agent icons
Next layer (Orchestration): .NET 10 logo, Microsoft Agent Framework logo, ASP.NET Core logo
Next layer (Monitoring): OpenTelemetry logo, OTEL icons
Top layer (Cloud): Azure Aspire logo, Docker logo, Kubernetes icon
All layers connected with arrows showing data flow

Left side: "Platform" with Python and C# logos
Right side: "Observability" with chart and metric icons
Bottom: "Enterprise Ready" with security and compliance icons

Clean, professional tech illustration style
White background with colored section backgrounds
"@

# Image 5: Multi-Agent Collaboration
New-Image `
    -Name "agent-collaboration" `
    -Prompt @"
Venn diagram style illustration showing two agents collaborating:

Left circle (Purple): "NeMo Agent"
- Analysis capability
- Data processing
- Insight generation
- Machine learning

Right circle (Green): "MAF Agent"
- Action execution
- Alert triggering
- Report generation
- Event routing

Center overlap: "A2A Communication"
- JSON-RPC Protocol
- Service Discovery
- Real-time Coordination
- Event-driven Architecture

Above the circles: "User Interface" layer with chat and dashboard
Below circles: "Aspire Orchestration" with service management

Modern illustration with clear visual hierarchy
Bold colors with white overlap area for clarity
"@

# Image 6: Use Case Scenario
New-Image `
    -Name "use-case-q4-sales" `
    -Prompt @"
Timeline infographic for Q4 Sales Analysis scenario:

Left to right progression:

Stage 1: User icon with message "Analyze Q4 Sales"
Stage 2: Data points and time series chart
Stage 3: Trend arrow pointing up "+12% growth"
Stage 4: Anomaly visualization with red highlight "3 anomalies detected"
Stage 5: Light bulb icon "Insight: Promo correlation"
Stage 6: Rocket icon "Action: Send alert"
Stage 7: Checkmark "Operations notified"

Timeline bars show duration: Seconds for each stage
Results box shows: Trend, Anomalies, Insights, Actions taken

Icons and arrows showing progression
Color gradient from blue (analysis) to green (action) to blue (results)
Professional infographic style with clear labels
"@

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "✓ Image Generation Complete" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Cyan
Write-Host "Generated images in: $OutputDir" -ForegroundColor Yellow
Write-Host "`nUsage:" -ForegroundColor Yellow
Write-Host "1. Add images to blog post" -ForegroundColor Gray
Write-Host "2. Use in LinkedIn/Twitter posts" -ForegroundColor Gray
Write-Host "3. Reference in README and docs" -ForegroundColor Gray
Write-Host "`nNote: Adjust --style parameter for different visual styles:" -ForegroundColor Gray
Write-Host "  --style modern        (trendy, contemporary)" -ForegroundColor Gray
Write-Host "  --style minimalist    (clean, simple)" -ForegroundColor Gray
Write-Host "  --style professional (formal, corporate)" -ForegroundColor Gray
