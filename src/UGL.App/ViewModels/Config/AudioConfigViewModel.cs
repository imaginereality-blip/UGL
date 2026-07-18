using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

public sealed partial class AudioConfigViewModel : ObservableObject
{
    private readonly IAudioPlaylistRepository _audioRepo;
    private readonly IConfigurationService _config;
    private readonly IAudioService _audioService;
    private readonly ILogger<AudioConfigViewModel> _logger;

    // ── Tab selection ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _isMusicTabActive = true;
    [ObservableProperty] private bool _isSoundsTabActive;

    // ── Global toggles ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableBackgroundMusic;
    [ObservableProperty] private bool _enableNavigationSounds;
    [ObservableProperty] private float _masterMusicVolume = 0.5f;
    [ObservableProperty] private float _masterSoundVolume = 1.0f;

    // ── Playlist management ────────────────────────────────────────────────
    public ObservableCollection<AudioPlaylist> Playlists { get; } = [];
    [ObservableProperty] private AudioPlaylist? _selectedPlaylist;
    public ObservableCollection<string> SelectedPlaylistTracks { get; } = [];
    [ObservableProperty] private float _playlistVolume = 0.5f;
    [ObservableProperty] private bool _playlistShuffle = true;

    // ── Category overrides ─────────────────────────────────────────────────
    public ObservableCollection<Category> Categories { get; } = [];
    [ObservableProperty] private Category? _selectedCategory;

    // ── System sound file paths ────────────────────────────────────────────
    [ObservableProperty] private string _soundNavigatePath = string.Empty;
    [ObservableProperty] private string _soundConfirmPath  = string.Empty;
    [ObservableProperty] private string _soundBackPath     = string.Empty;
    [ObservableProperty] private string _soundErrorPath    = string.Empty;

    // ── Video preview audio ────────────────────────────────────────────────
    [ObservableProperty] private bool  _enableVideoPreviewAudio;
    [ObservableProperty] private float _videoPreviewVolume = 0.5f;

    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Music tab field navigation ───────────────────────────────────────
    // Two zones, same convention as the Categories tab: IsMusicListFocused=true means
    // Up/Down browses the Playlists list (original behavior); false means Up/Down
    // cycles the flat field sequence below. Right from the list enters field mode;
    // Up at field position 0 returns to the list.
    [ObservableProperty] private bool _isMusicListFocused = true;
    [ObservableProperty] private int _musicFocusIndex;
    private const int MusicPositionCount = 9;
    // 0 EnableBackgroundMusic, 1 MasterMusicVolume, 2 Category override combo,
    // 3 Use/Create-for-Category button, 4 PlaylistVolume, 5 PlaylistShuffle,
    // 6 Tracks (enters track sub-list), 7 Add Tracks button, 8 Save button.

    // Track sub-list — entered via Confirm at MusicFocusIndex 6, exited via Back.
    [ObservableProperty] private bool _isTrackListFocused;
    [ObservableProperty] private int _selectedTrackIndex;

    public bool IsEnableMusicFocused        => !IsMusicListFocused && MusicFocusIndex == 0;
    public bool IsMasterMusicVolumeFocused  => !IsMusicListFocused && MusicFocusIndex == 1;
    public bool IsCategoryOverrideFocused   => !IsMusicListFocused && MusicFocusIndex == 2;
    public bool IsUseForCategoryFocused     => !IsMusicListFocused && MusicFocusIndex == 3;
    public bool IsPlaylistVolumeFocused     => !IsMusicListFocused && MusicFocusIndex == 4;
    public bool IsPlaylistShuffleFocused    => !IsMusicListFocused && MusicFocusIndex == 5;
    public bool IsTracksFocused             => !IsMusicListFocused && MusicFocusIndex == 6;
    public bool IsAddTracksFocused          => !IsMusicListFocused && MusicFocusIndex == 7;
    public bool IsSaveMusicFocused          => !IsMusicListFocused && MusicFocusIndex == 8;

    partial void OnMusicFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEnableMusicFocused));
        OnPropertyChanged(nameof(IsMasterMusicVolumeFocused));
        OnPropertyChanged(nameof(IsCategoryOverrideFocused));
        OnPropertyChanged(nameof(IsUseForCategoryFocused));
        OnPropertyChanged(nameof(IsPlaylistVolumeFocused));
        OnPropertyChanged(nameof(IsPlaylistShuffleFocused));
        OnPropertyChanged(nameof(IsTracksFocused));
        OnPropertyChanged(nameof(IsAddTracksFocused));
        OnPropertyChanged(nameof(IsSaveMusicFocused));
    }

    partial void OnIsMusicListFocusedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEnableMusicFocused));
        OnPropertyChanged(nameof(IsMasterMusicVolumeFocused));
        OnPropertyChanged(nameof(IsCategoryOverrideFocused));
        OnPropertyChanged(nameof(IsUseForCategoryFocused));
        OnPropertyChanged(nameof(IsPlaylistVolumeFocused));
        OnPropertyChanged(nameof(IsPlaylistShuffleFocused));
        OnPropertyChanged(nameof(IsTracksFocused));
        OnPropertyChanged(nameof(IsAddTracksFocused));
        OnPropertyChanged(nameof(IsSaveMusicFocused));
    }

    // ── Sounds tab field highlight ───────────────────────────────────────
    [ObservableProperty] private int _soundsFocusIndex;
    private const int SoundsPositionCount = 13;

    public bool IsEnableNavSoundsFocused    => SoundsFocusIndex == 0;
    public bool IsMasterSoundVolumeFocused  => SoundsFocusIndex == 1;
    public bool IsSoundNavigatePathFocused  => SoundsFocusIndex == 2;
    public bool IsTestNavigateFocused       => SoundsFocusIndex == 3;
    public bool IsSoundConfirmPathFocused   => SoundsFocusIndex == 4;
    public bool IsTestConfirmFocused        => SoundsFocusIndex == 5;
    public bool IsSoundBackPathFocused      => SoundsFocusIndex == 6;
    public bool IsTestBackFocused           => SoundsFocusIndex == 7;
    public bool IsSoundErrorPathFocused     => SoundsFocusIndex == 8;
    public bool IsTestErrorFocused          => SoundsFocusIndex == 9;
    public bool IsEnableVideoPreviewFocused => SoundsFocusIndex == 10;
    public bool IsVideoPreviewVolumeFocused => SoundsFocusIndex == 11;
    public bool IsSaveSoundsFocused         => SoundsFocusIndex == 12;

    partial void OnSoundsFocusIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEnableNavSoundsFocused));
        OnPropertyChanged(nameof(IsMasterSoundVolumeFocused));
        OnPropertyChanged(nameof(IsSoundNavigatePathFocused));
        OnPropertyChanged(nameof(IsTestNavigateFocused));
        OnPropertyChanged(nameof(IsSoundConfirmPathFocused));
        OnPropertyChanged(nameof(IsTestConfirmFocused));
        OnPropertyChanged(nameof(IsSoundBackPathFocused));
        OnPropertyChanged(nameof(IsTestBackFocused));
        OnPropertyChanged(nameof(IsSoundErrorPathFocused));
        OnPropertyChanged(nameof(IsTestErrorFocused));
        OnPropertyChanged(nameof(IsEnableVideoPreviewFocused));
        OnPropertyChanged(nameof(IsVideoPreviewVolumeFocused));
        OnPropertyChanged(nameof(IsSaveSoundsFocused));
    }

    public event Func<string, string[], Task<IReadOnlyList<string>>>? BrowseFilesRequested;
    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    public AudioConfigViewModel(
        IAudioPlaylistRepository audioRepo,
        IConfigurationService config,
        IAudioService audioService,
        ILogger<AudioConfigViewModel> logger)
    {
        _audioRepo = audioRepo;
        _config = config;
        _audioService = audioService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        EnableBackgroundMusic  = _config.Settings.EnableBackgroundMusic;
        EnableNavigationSounds = _config.Settings.EnableNavigationSounds;
        MasterMusicVolume = _config.Settings.MusicVolume;
        MasterSoundVolume = _config.Settings.SoundVolume;

        SoundNavigatePath = _config.Settings.SoundNavigatePath;
        SoundConfirmPath  = _config.Settings.SoundConfirmPath;
        SoundBackPath     = _config.Settings.SoundBackPath;
        SoundErrorPath    = _config.Settings.SoundErrorPath;

        EnableVideoPreviewAudio = _config.Settings.VideoPreviewAudio;
        VideoPreviewVolume      = _config.Settings.VideoPreviewVolume;

        var playlists = await _audioRepo.GetAllAsync();
        Playlists.Clear();
        foreach (var p in playlists) Playlists.Add(p);
        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == "global") ?? Playlists.FirstOrDefault();

        var categories = await _config.GetCategoriesAsync();
        Categories.Clear();
        foreach (var c in categories) Categories.Add(c);
    }

    // ── Tab switching ──────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectMusicTab()
    {
        IsMusicTabActive  = true;
        IsSoundsTabActive = false;
    }

    [RelayCommand]
    private void SelectSoundsTab()
    {
        IsMusicTabActive  = false;
        IsSoundsTabActive = true;
    }

    /// <summary>Dedicated LB/RB sub-tab switching (Music ↔ Sounds), always available
    /// regardless of current field focus — separate from Left/Right, which also
    /// switches tabs from the list but is overloaded to adjust values in field mode.</summary>
    public void SwitchSubTabLeft() => SelectMusicTab();
    public void SwitchSubTabRight() => SelectSoundsTab();

    // ── Playlist ───────────────────────────────────────────────────────────

    partial void OnSelectedPlaylistChanged(AudioPlaylist? value)
    {
        SelectedPlaylistTracks.Clear();
        if (value is null) return;
        foreach (var t in value.Tracks) SelectedPlaylistTracks.Add(t);
        PlaylistVolume  = value.Volume;
        PlaylistShuffle = value.Shuffle;
        SelectedTrackIndex = 0;
    }

    // ── Controller navigation ────────────────────────────────────────────
    // Music tab: two zones (list vs fields), same convention as Categories.
    // Sounds tab: full flat field navigation, since it has no embedded list.
    public void NavigateUp()
    {
        if (IsSoundsTabActive)
        {
            SoundsFocusIndex = (SoundsFocusIndex - 1 + SoundsPositionCount) % SoundsPositionCount;
            return;
        }

        // Music tab
        if (IsMusicListFocused)
        {
            if (Playlists.Count == 0) return;
            int idx = SelectedPlaylist is null ? 0 : Playlists.IndexOf(SelectedPlaylist);
            idx = (idx - 1 + Playlists.Count) % Playlists.Count;
            SelectedPlaylist = Playlists[idx];
            return;
        }

        if (IsTrackListFocused)
        {
            if (SelectedPlaylistTracks.Count == 0) return;
            SelectedTrackIndex = (SelectedTrackIndex - 1 + SelectedPlaylistTracks.Count) % SelectedPlaylistTracks.Count;
            return;
        }

        if (MusicFocusIndex == 0) { IsMusicListFocused = true; return; }
        MusicFocusIndex = (MusicFocusIndex - 1 + MusicPositionCount) % MusicPositionCount;
    }

    public void NavigateDown()
    {
        if (IsSoundsTabActive)
        {
            SoundsFocusIndex = (SoundsFocusIndex + 1) % SoundsPositionCount;
            return;
        }

        if (IsMusicListFocused)
        {
            if (Playlists.Count == 0) return;
            int idx = SelectedPlaylist is null ? 0 : Playlists.IndexOf(SelectedPlaylist);
            idx = (idx + 1) % Playlists.Count;
            SelectedPlaylist = Playlists[idx];
            return;
        }

        if (IsTrackListFocused)
        {
            if (SelectedPlaylistTracks.Count == 0) return;
            SelectedTrackIndex = (SelectedTrackIndex + 1) % SelectedPlaylistTracks.Count;
            return;
        }

        MusicFocusIndex = (MusicFocusIndex + 1) % MusicPositionCount;
    }

    public void NavigateLeft()
    {
        if (IsSoundsTabActive)
        {
            if (SoundsFocusIndex == 1) { MasterSoundVolume = Math.Max(0f, MasterSoundVolume - 0.05f); return; }
            if (SoundsFocusIndex == 11) { VideoPreviewVolume = Math.Max(0f, VideoPreviewVolume - 0.05f); return; }
            SelectMusicTab(); // any other position — Left switches back to Music, not just position 0
            return;
        }

        // Music tab
        if (IsTrackListFocused)
        {
            MoveTrackUp(CurrentTrackOrNull());
            return;
        }

        if (!IsMusicListFocused)
        {
            switch (MusicFocusIndex)
            {
                case 1: MasterMusicVolume = Math.Max(0f, MasterMusicVolume - 0.05f); return;
                case 2: CycleCategoryOverride(-1); return;
                case 4: PlaylistVolume = Math.Max(0f, PlaylistVolume - 0.05f); return;
            }
            return; // no-op elsewhere in field mode
        }

        SelectSoundsTab(); // list mode: Left/Right both switch tabs, matching the Sounds tab's convention
    }

    public void NavigateRight()
    {
        if (IsSoundsTabActive)
        {
            if (SoundsFocusIndex == 1) { MasterSoundVolume = Math.Min(1f, MasterSoundVolume + 0.05f); return; }
            if (SoundsFocusIndex == 11) { VideoPreviewVolume = Math.Min(1f, VideoPreviewVolume + 0.05f); return; }
            return; // no-op elsewhere
        }

        // Music tab
        if (IsTrackListFocused)
        {
            MoveTrackDown(CurrentTrackOrNull());
            return;
        }

        if (IsMusicListFocused)
        {
            IsMusicListFocused = false;
            MusicFocusIndex = 0;
            return;
        }

        switch (MusicFocusIndex)
        {
            case 1: MasterMusicVolume = Math.Min(1f, MasterMusicVolume + 0.05f); return;
            case 2: CycleCategoryOverride(1); return;
            case 4: PlaylistVolume = Math.Min(1f, PlaylistVolume + 0.05f); return;
        }
    }

    private string? CurrentTrackOrNull() =>
        SelectedTrackIndex >= 0 && SelectedTrackIndex < SelectedPlaylistTracks.Count
            ? SelectedPlaylistTracks[SelectedTrackIndex]
            : null;

    private void CycleCategoryOverride(int delta)
    {
        if (Categories.Count == 0) return;
        int idx = SelectedCategory is null ? 0 : Categories.IndexOf(SelectedCategory);
        idx = (idx + delta + Categories.Count) % Categories.Count;
        SelectedCategory = Categories[idx];
    }

    /// <summary>Back while the track sub-list is focused exits just that, back to the
    /// flat field list — same reasoning as GamesConfigViewModel.TryExitCategoryOptions.</summary>
    public bool TryExitTrackList()
    {
        if (!IsTrackListFocused) return false;
        IsTrackListFocused = false;
        return true;
    }

    public async Task ConfirmAsync()
    {
        if (!IsSoundsTabActive)
        {
            // Music tab
            if (IsTrackListFocused)
            {
                var track = CurrentTrackOrNull();
                if (track is not null) RemoveTrack(track);
                return;
            }

            if (IsMusicListFocused) return; // playlist selection already applies live

            switch (MusicFocusIndex)
            {
                case 0: EnableBackgroundMusic = !EnableBackgroundMusic; break;
                case 3: SelectCategoryOverride(); break;
                case 5: PlaylistShuffle = !PlaylistShuffle; break;
                case 6:
                    if (SelectedPlaylistTracks.Count > 0)
                    {
                        IsTrackListFocused = true;
                        SelectedTrackIndex = Math.Clamp(SelectedTrackIndex, 0, SelectedPlaylistTracks.Count - 1);
                    }
                    break;
                case 7: await AddTracksAsync(); break;
                case 8: await SaveAsync(); break;
                // 1, 2, 4 are adjusted via Left/Right, not Confirm.
            }
            return;
        }

        switch (SoundsFocusIndex)
        {
            case 0: EnableNavigationSounds = !EnableNavigationSounds; break;
            case 2: await BrowseNavigateSoundAsync(); break;
            case 3: TestNavigateSound(); break;
            case 4: await BrowseConfirmSoundAsync(); break;
            case 5: TestConfirmSound(); break;
            case 6: await BrowseBackSoundAsync(); break;
            case 7: TestBackSound(); break;
            case 8: await BrowseErrorSoundAsync(); break;
            case 9: TestErrorSound(); break;
            case 10: EnableVideoPreviewAudio = !EnableVideoPreviewAudio; break;
            case 12: await SaveAsync(); break;
            // 1, 11 (volume sliders) are adjusted via Left/Right, not Confirm.
        }
    }

    [RelayCommand]
    private async Task AddTracksAsync()
    {
        if (SelectedPlaylist is null || BrowseFilesRequested is null) return;
        var files = await BrowseFilesRequested.Invoke(
            "Audio Files", ["*.mp3", "*.ogg", "*.wav", "*.flac", "*.m4a"]);
        foreach (var f in files)
        {
            // Stored relative to the app's own folder when possible, so playback
            // keeps working if the whole portable install moves to a different
            // drive letter or machine — LibVlcAudioService.PlayCurrentTrack already
            // resolves either form back to an absolute path before playing it.
            var portable = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(f);
            SelectedPlaylist.Tracks.Add(portable);
            SelectedPlaylistTracks.Add(portable);
        }
    }

    [RelayCommand]
    private void RemoveTrack(string track)
    {
        if (SelectedPlaylist is null) return;
        var idx = SelectedPlaylistTracks.IndexOf(track);
        SelectedPlaylist.Tracks.Remove(track);
        SelectedPlaylistTracks.Remove(track);
        if (SelectedPlaylistTracks.Count > 0)
            SelectedTrackIndex = Math.Clamp(idx, 0, SelectedPlaylistTracks.Count - 1);
        else
            IsTrackListFocused = false; // nothing left to browse — back out to the fields
    }

    [RelayCommand]
    private void MoveTrackUp(string? track)
    {
        if (track is null || SelectedPlaylist is null) return;
        var idx = SelectedPlaylist.Tracks.IndexOf(track);
        if (idx <= 0) return;
        SelectedPlaylist.Tracks.RemoveAt(idx);
        SelectedPlaylist.Tracks.Insert(idx - 1, track);
        SelectedPlaylistTracks.Move(idx, idx - 1);
        SelectedTrackIndex = idx - 1;
    }

    [RelayCommand]
    private void MoveTrackDown(string? track)
    {
        if (track is null || SelectedPlaylist is null) return;
        var idx = SelectedPlaylist.Tracks.IndexOf(track);
        if (idx < 0 || idx >= SelectedPlaylist.Tracks.Count - 1) return;
        SelectedPlaylist.Tracks.RemoveAt(idx);
        SelectedPlaylist.Tracks.Insert(idx + 1, track);
        SelectedPlaylistTracks.Move(idx, idx + 1);
        SelectedTrackIndex = idx + 1;
    }

    [RelayCommand]
    private void SelectCategoryOverride()
    {
        if (SelectedCategory is null) return;
        var existing = Playlists.FirstOrDefault(p => p.Id == SelectedCategory.Id);
        if (existing is not null) { SelectedPlaylist = existing; return; }
        var newPlaylist = new AudioPlaylist
        {
            Id   = SelectedCategory.Id,
            Name = $"{SelectedCategory.Label} Music",
        };
        Playlists.Add(newPlaylist);
        SelectedPlaylist = newPlaylist;
    }

    // ── Sound file browse ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseNavigateSoundAsync()
        => SoundNavigatePath = await BrowseSoundAsync() ?? SoundNavigatePath;

    [RelayCommand]
    private async Task BrowseConfirmSoundAsync()
        => SoundConfirmPath = await BrowseSoundAsync() ?? SoundConfirmPath;

    [RelayCommand]
    private async Task BrowseBackSoundAsync()
        => SoundBackPath = await BrowseSoundAsync() ?? SoundBackPath;

    [RelayCommand]
    private async Task BrowseErrorSoundAsync()
        => SoundErrorPath = await BrowseSoundAsync() ?? SoundErrorPath;

    private async Task<string?> BrowseSoundAsync()
    {
        if (BrowseFileRequested is null) return null;
        var path = await BrowseFileRequested.Invoke(
            "Sound Files", ["*.wav", "*.ogg", "*.mp3"]);
        // Stored relative to the app's own folder when possible — see AddTracksAsync
        // above for the same reasoning; LibVlcAudioService.PlaySoundFromPath already
        // resolves either form back to an absolute path before playing it.
        return path is not null ? UGL.Core.Utilities.PortablePathHelper.ToPortablePath(path) : null;
    }

    // ── Test playback ──────────────────────────────────────────────────────

    [RelayCommand] private void TestNavigateSound() => _audioService.PlayNavigate();
    [RelayCommand] private void TestConfirmSound()  => _audioService.PlayConfirm();
    [RelayCommand] private void TestBackSound()     => _audioService.PlayBack();
    [RelayCommand] private void TestErrorSound()    => _audioService.PlayError();

    // ── Save ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedPlaylist is not null)
        {
            SelectedPlaylist.Volume  = PlaylistVolume;
            SelectedPlaylist.Shuffle = PlaylistShuffle;
        }

        foreach (var p in Playlists)
            await _audioRepo.AddOrUpdateAsync(p);

        await _audioRepo.SaveAsync();

        // Persist sound settings back to AppSettings / settings.json.
        // AppSettings properties are mutable (`set`, not `init`), but every field must
        // still be carried over explicitly here — this reconstruction was previously
        // missing EmulatorsRootPath/AddonsRootPath/LogsRootPath and all four
        // CardHighlight* fields, which meant saving Audio settings silently reset the
        // Paths tab and Card Highlight tab back to their defaults every time.
        var s = _config.Settings;
        var updated = new AppSettings
        {
            MediaRootPath          = s.MediaRootPath,
            RomsRootPath           = s.RomsRootPath,
            EmulatorsRootPath      = s.EmulatorsRootPath,
            AddonsRootPath         = s.AddonsRootPath,
            LogsRootPath           = s.LogsRootPath,
            ActiveThemeId          = s.ActiveThemeId,
            DefaultCategoryId      = s.DefaultCategoryId,
            Language               = s.Language,
            TargetFrameRate        = s.TargetFrameRate,
            EnableBackgroundMusic  = EnableBackgroundMusic,
            EnableNavigationSounds = EnableNavigationSounds,
            MusicVolume            = MasterMusicVolume,
            SoundVolume            = MasterSoundVolume,
            SoundNavigatePath      = SoundNavigatePath.Trim(),
            SoundConfirmPath       = SoundConfirmPath.Trim(),
            SoundBackPath          = SoundBackPath.Trim(),
            SoundErrorPath         = SoundErrorPath.Trim(),
            EnableVideoPreview     = s.EnableVideoPreview,
            VideoPreviewDelayMs    = s.VideoPreviewDelayMs,
            VideoPreviewAudio      = EnableVideoPreviewAudio,
            VideoPreviewVolume     = VideoPreviewVolume,
            CardHighlightColor     = s.CardHighlightColor,
            CardHighlightIntensity = s.CardHighlightIntensity,
            CardHighlightStyle     = s.CardHighlightStyle,
            CardHighlightThickness = s.CardHighlightThickness,
        };

        await _config.UpdateSettingsAsync(updated);

        // Apply volume changes immediately without restart
        _audioService.IsMusicEnabled  = EnableBackgroundMusic;
        _audioService.IsSoundEnabled  = EnableNavigationSounds;
        _audioService.MusicVolume     = MasterMusicVolume;
        _audioService.SoundVolume     = MasterSoundVolume;

        StatusMessage = "Audio settings saved.";
        _logger.LogInformation("Audio config saved.");
    }
}
