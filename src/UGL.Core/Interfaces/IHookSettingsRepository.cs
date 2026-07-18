using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Provides read and write access to config/hooks.json — the single HookSettings
/// object (not a list), since there's exactly one active hook-tool configuration at a
/// time. Implementation lives in UGL.Data and is injected via DI.
/// </summary>
public interface IHookSettingsRepository
{
    Task<HookSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(HookSettings settings, CancellationToken cancellationToken = default);
}
