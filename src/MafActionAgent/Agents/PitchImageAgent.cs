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
    private readonly SemaphoreSlim _generationGate = new(1, 1);

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

    /// <summary>
    /// True when the image agent is enabled and an image generator is configured, i.e. on-demand
    /// generation can actually produce an image.
    /// </summary>
    public bool IsConfigured => IsEnabled(_configuration) && _imageGenerator is not null;

    /// <summary>
    /// Returns the incident-hero image bytes, generating and caching them on demand if needed.
    /// Returns <c>null</c> when the agent is disabled, no generator is configured, or generation
    /// fails. Concurrent callers share a single generation pass.
    /// </summary>
    public async Task<byte[]?> EnsureImageAsync(CancellationToken cancellationToken)
    {
        var cachePath = CachedImagePath;
        if (File.Exists(cachePath))
        {
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }

        if (!IsEnabled(_configuration) || _imageGenerator is null)
        {
            return null;
        }

        await _generationGate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have produced the image while we waited for the gate.
            if (File.Exists(cachePath))
            {
                return await File.ReadAllBytesAsync(cachePath, cancellationToken);
            }

            return await GenerateAndCacheCoreAsync(cachePath, cancellationToken);
        }
        finally
        {
            _generationGate.Release();
        }
    }

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
        _ = Task.Run(() => EnsureImageAsync(_stoppingCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task<byte[]?> GenerateAndCacheCoreAsync(string cachePath, CancellationToken cancellationToken)
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

            return result.ImageBytes;
        }
        catch (Exception ex)
        {
            // Additive feature — never destabilize the core demo.
            _logger.LogWarning(ex, "Pitch image agent failed to generate the incident-hero image; continuing without it.");
            return null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts.Cancel();
        return Task.CompletedTask;
    }
}
