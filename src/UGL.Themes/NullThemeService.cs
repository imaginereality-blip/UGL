using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.Themes;

/// <summary>
/// No-op IThemeService for Milestone 1.
/// Replaced by AvaloniaThemeService in Milestone 6.
/// </summary>
internal sealed class NullThemeService : IThemeService
{
    public Theme ActiveTheme { get; } = new Theme { Id = "default", Name = "Default" };

#pragma warning disable CS0067
    public event EventHandler<Theme>? ThemeChanged;
#pragma warning restore CS0067

    public Task<IReadOnlyList<Theme>> GetAvailableThemesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Theme>>(new[] { ActiveTheme });

    public Task ApplyThemeAsync(string themeId, CancellationToken ct = default)
        => Task.CompletedTask;
}
