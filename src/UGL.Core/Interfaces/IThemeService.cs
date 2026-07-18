using UGL.Core.Models;

namespace UGL.Core.Interfaces;

/// <summary>
/// Loads, validates, and applies UI themes at runtime.
/// Implementations live in UGL.Themes.
/// </summary>
public interface IThemeService
{
    /// <summary>The currently active theme. Never null after initialization.</summary>
    Theme ActiveTheme { get; }

    /// <summary>Fired whenever the active theme changes.</summary>
    event EventHandler<Theme>? ThemeChanged;

    /// <summary>Returns all available themes loaded from themes.json.</summary>
    Task<IReadOnlyList<Theme>> GetAvailableThemesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the active theme and raises <see cref="ThemeChanged"/>.
    /// The implementation must apply changes to the Avalonia resource dictionary
    /// without requiring an application restart.
    /// </summary>
    Task ApplyThemeAsync(string themeId, CancellationToken cancellationToken = default);
}
