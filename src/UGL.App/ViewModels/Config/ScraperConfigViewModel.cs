using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>
/// Backing VM for the Scraper Settings tab — credentials for the three supported
/// metadata/image sources (IGDB, ScreenScraper, TheGamesDB — see UGL.Scraping) plus
/// ComfyUI card-generation config. All fields commit only on Save, same convention
/// as Hooks/Paths.
/// </summary>
public sealed partial class ScraperConfigViewModel : ObservableObject
{
    private readonly IScraperSettingsRepository _settingsRepo;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<ScraperConfigViewModel> _logger;

    [ObservableProperty] private ScraperSourceType _preferredSource = ScraperSourceType.Igdb;

    [ObservableProperty] private string _igdbClientId = string.Empty;
    [ObservableProperty] private string _igdbClientSecret = string.Empty;

    [ObservableProperty] private string _screenScraperUsername = string.Empty;
    [ObservableProperty] private string _screenScraperPassword = string.Empty;
    [ObservableProperty] private string _screenScraperDevId = string.Empty;
    [ObservableProperty] private string _screenScraperDevPassword = string.Empty;

    [ObservableProperty] private string _theGamesDbApiKey = string.Empty;

    [ObservableProperty] private string _comfyUiEndpoint = string.Empty;
    [ObservableProperty] private string _comfyUiWorkflowPath = string.Empty;
    [ObservableProperty] private string _comfyUiCleanupWorkflowPath = string.Empty;
    [ObservableProperty] private string _textDetectionModelPath = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public string PreferredSourceLabel => PreferredSource switch
    {
        ScraperSourceType.Igdb => "IGDB",
        ScraperSourceType.ScreenScraper => "ScreenScraper.fr",
        ScraperSourceType.TheGamesDb => "TheGamesDB",
        _ => PreferredSource.ToString(),
    };

    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    public ScraperConfigViewModel(
        IScraperSettingsRepository settingsRepo,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<ScraperConfigViewModel> logger)
    {
        _settingsRepo = settingsRepo;
        _virtualKeyboard = virtualKeyboard;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var s = await _settingsRepo.GetSettingsAsync();
        PreferredSource = s.PreferredSource;
        IgdbClientId = s.IgdbClientId;
        IgdbClientSecret = s.IgdbClientSecret;
        ScreenScraperUsername = s.ScreenScraperUsername;
        ScreenScraperPassword = s.ScreenScraperPassword;
        ScreenScraperDevId = s.ScreenScraperDevId;
        ScreenScraperDevPassword = s.ScreenScraperDevPassword;
        TheGamesDbApiKey = s.TheGamesDbApiKey;
        ComfyUiEndpoint = s.ComfyUiEndpoint;
        ComfyUiWorkflowPath = s.ComfyUiWorkflowPath;
        ComfyUiCleanupWorkflowPath = s.ComfyUiCleanupWorkflowPath;
        TextDetectionModelPath = s.TextDetectionModelPath;
    }

    partial void OnPreferredSourceChanged(ScraperSourceType value) => OnPropertyChanged(nameof(PreferredSourceLabel));

    // ── Field highlight ───────────────────────────────────────────────────
    [ObservableProperty] private int _focusIndex;
    private const int PositionCount = 13;
    // 0 PreferredSource, 1 IgdbClientId, 2 IgdbClientSecret, 3 ScreenScraperUsername,
    // 4 ScreenScraperPassword, 5 ScreenScraperDevId, 6 ScreenScraperDevPassword,
    // 7 TheGamesDbApiKey, 8 ComfyUiEndpoint, 9 ComfyUiWorkflowPath (Browse),
    // 10 ComfyUiCleanupWorkflowPath (Browse), 11 TextDetectionModelPath (Browse), 12 Save.

    public bool IsPreferredSourceFocused        => FocusIndex == 0;
    public bool IsIgdbClientIdFocused           => FocusIndex == 1;
    public bool IsIgdbClientSecretFocused       => FocusIndex == 2;
    public bool IsScreenScraperUsernameFocused  => FocusIndex == 3;
    public bool IsScreenScraperPasswordFocused  => FocusIndex == 4;
    public bool IsScreenScraperDevIdFocused     => FocusIndex == 5;
    public bool IsScreenScraperDevPasswordFocused => FocusIndex == 6;
    public bool IsTheGamesDbApiKeyFocused       => FocusIndex == 7;
    public bool IsComfyUiEndpointFocused        => FocusIndex == 8;
    public bool IsComfyUiWorkflowPathFocused    => FocusIndex == 9;
    public bool IsComfyUiCleanupWorkflowPathFocused => FocusIndex == 10;
    public bool IsTextDetectionModelPathFocused => FocusIndex == 11;
    public bool IsSaveFocused                   => FocusIndex == 12;

    partial void OnFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPreferredSourceFocused));
        OnPropertyChanged(nameof(IsIgdbClientIdFocused));
        OnPropertyChanged(nameof(IsIgdbClientSecretFocused));
        OnPropertyChanged(nameof(IsScreenScraperUsernameFocused));
        OnPropertyChanged(nameof(IsScreenScraperPasswordFocused));
        OnPropertyChanged(nameof(IsScreenScraperDevIdFocused));
        OnPropertyChanged(nameof(IsScreenScraperDevPasswordFocused));
        OnPropertyChanged(nameof(IsTheGamesDbApiKeyFocused));
        OnPropertyChanged(nameof(IsComfyUiEndpointFocused));
        OnPropertyChanged(nameof(IsComfyUiWorkflowPathFocused));
        OnPropertyChanged(nameof(IsComfyUiCleanupWorkflowPathFocused));
        OnPropertyChanged(nameof(IsTextDetectionModelPathFocused));
        OnPropertyChanged(nameof(IsSaveFocused));
    }

    public void NavigateUp() => FocusIndex = (FocusIndex - 1 + PositionCount) % PositionCount;
    public void NavigateDown() => FocusIndex = (FocusIndex + 1) % PositionCount;

    public void NavigateLeft()
    {
        if (FocusIndex == 0) CyclePreferredSource(-1);
    }

    public void NavigateRight()
    {
        if (FocusIndex == 0) CyclePreferredSource(1);
    }

    private void CyclePreferredSource(int delta)
    {
        var values = Enum.GetValues<ScraperSourceType>();
        int idx = Array.IndexOf(values, PreferredSource);
        idx = (idx + delta + values.Length) % values.Length;
        PreferredSource = values[idx];
    }

    public async Task ConfirmAsync()
    {
        switch (FocusIndex)
        {
            case 1: _virtualKeyboard.Open("IGDB Client ID", IgdbClientId, v => IgdbClientId = v); break;
            case 2: _virtualKeyboard.Open("IGDB Client Secret", IgdbClientSecret, v => IgdbClientSecret = v); break;
            case 3: _virtualKeyboard.Open("ScreenScraper Username", ScreenScraperUsername, v => ScreenScraperUsername = v); break;
            case 4: _virtualKeyboard.Open("ScreenScraper Password", ScreenScraperPassword, v => ScreenScraperPassword = v); break;
            case 5: _virtualKeyboard.Open("ScreenScraper Dev Id", ScreenScraperDevId, v => ScreenScraperDevId = v); break;
            case 6: _virtualKeyboard.Open("ScreenScraper Dev Password", ScreenScraperDevPassword, v => ScreenScraperDevPassword = v); break;
            case 7: _virtualKeyboard.Open("TheGamesDB API Key", TheGamesDbApiKey, v => TheGamesDbApiKey = v); break;
            case 8: _virtualKeyboard.Open("ComfyUI Endpoint", ComfyUiEndpoint, v => ComfyUiEndpoint = v); break;
            case 9: await BrowseWorkflowFileAsync(); break;
            case 10: await BrowseCleanupWorkflowFileAsync(); break;
            case 11: await BrowseTextDetectionModelFileAsync(); break;
            case 12: await SaveAsync(); break;
        }
    }

    [RelayCommand]
    private async Task BrowseWorkflowFileAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("ComfyUI Workflow (API format JSON)", ["*.json"]);
        if (path is not null) ComfyUiWorkflowPath = path;
    }

    [RelayCommand]
    private async Task BrowseCleanupWorkflowFileAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("ComfyUI Cleanup Workflow (API format JSON)", ["*.json"]);
        if (path is not null) ComfyUiCleanupWorkflowPath = path;
    }

    [RelayCommand]
    private async Task BrowseTextDetectionModelFileAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("Text Detection Model (ONNX)", ["*.onnx"]);
        if (path is not null) TextDetectionModelPath = path;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new ScraperSettings
        {
            PreferredSource = PreferredSource,
            IgdbClientId = IgdbClientId.Trim(),
            IgdbClientSecret = IgdbClientSecret.Trim(),
            ScreenScraperUsername = ScreenScraperUsername.Trim(),
            ScreenScraperPassword = ScreenScraperPassword.Trim(),
            ScreenScraperDevId = ScreenScraperDevId.Trim(),
            ScreenScraperDevPassword = ScreenScraperDevPassword.Trim(),
            TheGamesDbApiKey = TheGamesDbApiKey.Trim(),
            ComfyUiEndpoint = ComfyUiEndpoint.Trim(),
            ComfyUiWorkflowPath = ComfyUiWorkflowPath.Trim(),
            ComfyUiCleanupWorkflowPath = ComfyUiCleanupWorkflowPath.Trim(),
            TextDetectionModelPath = TextDetectionModelPath.Trim(),
        };

        await _settingsRepo.SaveSettingsAsync(settings);
        StatusMessage = "Scraper settings saved.";
        _logger.LogInformation("Scraper settings saved (preferred={Source}).", PreferredSource);
    }
}
