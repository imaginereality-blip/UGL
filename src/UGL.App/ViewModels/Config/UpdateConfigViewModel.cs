using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

public sealed partial class UpdateConfigViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;
    private readonly ILogger<UpdateConfigViewModel> _logger;

    [ObservableProperty] private string _currentVersion = string.Empty;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private UpdateCheckResult? _lastCheckResult;

    /// <summary>True once a check has found an update and it's awaiting the user's
    /// confirmation before actually downloading/applying — separates "found one" from
    /// "committed to installing it", since applying restarts the app.</summary>
    [ObservableProperty] private bool _isAwaitingConfirmation;

    public UpdateConfigViewModel(IUpdateService updateService, ILogger<UpdateConfigViewModel> logger)
    {
        _updateService = updateService;
        _logger = logger;
        CurrentVersion = _updateService.CurrentVersion;
    }

    // ── Controller navigation ────────────────────────────────────────────
    [ObservableProperty] private int _focusIndex;

    // Position count varies with state: while awaiting confirmation there are two
    // extra buttons (Install / Not Now) in place of the single Check button.
    private int PositionCount => IsAwaitingConfirmation ? 2 : 1;

    public bool IsCheckFocused    => !IsAwaitingConfirmation && FocusIndex == 0;
    public bool IsInstallFocused  => IsAwaitingConfirmation && FocusIndex == 0;
    public bool IsNotNowFocused   => IsAwaitingConfirmation && FocusIndex == 1;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCheckFocused));
        OnPropertyChanged(nameof(IsInstallFocused));
        OnPropertyChanged(nameof(IsNotNowFocused));
    }

    partial void OnIsAwaitingConfirmationChanged(bool value)
    {
        FocusIndex = 0;
        OnPropertyChanged(nameof(IsCheckFocused));
        OnPropertyChanged(nameof(IsInstallFocused));
        OnPropertyChanged(nameof(IsNotNowFocused));
    }

    public void NavigateUp()   => FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    public void NavigateDown() => FocusIndex = (FocusIndex + 1) % PositionCount;

    public async Task ConfirmAsync()
    {
        if (IsAwaitingConfirmation)
        {
            if (FocusIndex == 0) await ApplyUpdateAsync();
            else DismissUpdate();
            return;
        }

        await CheckForUpdatesAsync();
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsChecking || IsApplying) return;

        IsChecking = true;
        StatusMessage = "Checking for updates…";
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            LastCheckResult = result;

            if (result.IsUpdateAvailable)
            {
                StatusMessage = $"Update available: {result.LatestVersion}";
                IsAwaitingConfirmation = true;
            }
            else
            {
                StatusMessage = $"You're up to date ({CurrentVersion}).";
                IsAwaitingConfirmation = false;
            }
        }
        catch (Exception ex)
        {
            // CheckForUpdateAsync itself shouldn't throw (see IUpdateService), but
            // guard here too — a failed manual check should show a message, not crash
            // the Settings screen.
            _logger.LogWarning(ex, "Manual update check failed.");
            StatusMessage = "Couldn't check for updates — check your internet connection.";
        }
        finally { IsChecking = false; }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (LastCheckResult is not { IsUpdateAvailable: true } update || IsApplying) return;

        IsApplying = true;
        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            // ApplyUpdateAsync exits the process itself once the update is staged and
            // the relaunch script is running — control does not normally return here.
            await _updateService.ApplyUpdateAsync(update, progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update.");
            StatusMessage = "Update failed — you're still on the current version.";
            IsApplying = false;
        }
    }

    /// <summary>User declined an available update — dismiss the confirmation for
    /// this session without applying it. It'll be offered again on the next check.</summary>
    [RelayCommand]
    private void DismissUpdate()
    {
        IsAwaitingConfirmation = false;
        StatusMessage = $"Update available ({LastCheckResult?.LatestVersion}) — not installed.";
    }
}
