using ElBruno.Text2Image;

namespace MafActionAgent.Agents;

/// <summary>
/// Optional, additive MAF "pitch" agent that pre-renders a single incident-hero image used as the
/// demo cold-open. It is gated behind <c>ENABLE_IMAGE_AGENT</c> (default <c>false</c>) and is fully
/// isolated from the grounded-action RAG path, so an image hiccup never affects the core demo.
///
/// Reliability design:
/// - Runs once at startup (never on a live request path) and caches the result to disk.
/// - Idempotent: if a cached image already exists it is reused (no re-generation on restart).
/// - Fault-tolerant: any failure (missing credentials, network, model) degrades to a no-op.
/// </summary>
internal sealed class PitchImageAgent : IHostedService
{
    internal const string IncidentHeroPrompt =
        "A clean, modern site-reliability operations dashboard illustration titled " +
        "'Payments Service — Incident Response', showing a rising error-rate line chart crossing a 5% " +
        "threshold, an alert badge, and a calm on-call engineer reviewing a runbook. Flat vector style, " +
        "blue and teal palette, high contrast, professional, no text artifacts.";

    private readonly IConfiguration _configuration;
    private readonly ILogger<PitchImageAgent> _logger;
    private readonly IImageGenerator? _imageGenerator;
    private readonly CancellationTokenSource _stoppingCts = new();

    public PitchImageAgent(
        IConfiguration configuration,
        ILogger<PitchImageAgent> logger,
        IImageGenerator? imageGenerator = null)
    {
        _configuration = configuration;
        _logger = logger;
        _imageGenerator = imageGenerator;
    }

    /// <summary>Absolute path of the cached incident-hero image.</summary>
    public static string CachedImagePath =>
        Path.Combine(AppContext.BaseDirectory, "pitch", "incident-hero.png");

    public static bool IsEnabled(IConfiguration configuration) =>
        string.Equals(configuration["ENABLE_IMAGE_AGENT"], "true", StringComparison.OrdinalIgnoreCase);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled(_configuration))
        {
            return Task.CompletedTask;
        }

        var cachePath = CachedImagePath;

        if (File.Exists(cachePath))
        {
            _logger.LogInformation("Pitch image agent: using cached incident-hero image at {Path}.", cachePath);
            return Task.CompletedTask;
        }

        if (_imageGenerator is null)
        {
            _logger.LogWarning(
                "Pitch image agent is enabled but no image generator is configured " +
                "(set FOUNDRY_IMAGE_ENDPOINT / FOUNDRY_IMAGE_API_KEY). Skipping image generation.");
            return Task.CompletedTask;
        }

        // GPT-Image-2 can take several minutes. Run generation in the background so it never blocks
        // application/Aspire startup (the cold-open image simply appears once it is ready; the Web UI
        // polls /api/pitch/hero-image). Fully fire-and-forget and fault-tolerant.
        _ = Task.Run(() => GenerateAndCacheAsync(cachePath, _stoppingCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task GenerateAndCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Pitch image agent: generating incident-hero image (one-time, cached)...");

            var result = await _imageGenerator!.GenerateAsync(IncidentHeroPrompt, options: null, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(cachePath, result.ImageBytes, cancellationToken);

            _logger.LogInformation(
                "Pitch image agent: incident-hero image generated in {Ms}ms and cached at {Path}.",
                result.InferenceTimeMs,
                cachePath);
        }
        catch (Exception ex)
        {
            // Additive feature — never destabilize the core demo.
            _logger.LogWarning(ex, "Pitch image agent failed to generate the incident-hero image; continuing without it.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts.Cancel();
        return Task.CompletedTask;
    }
}
