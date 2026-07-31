using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels.Config;

/// <summary>One category checkbox in a playlist's category-assignment grid.</summary>
public sealed partial class PlaylistCategoryCheckItem : ObservableObject
{
    public string Id { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isHighlighted;

    public PlaylistCategoryCheckItem(string id, string label)
    {
        Id = id;
        Label = label;
    }
}

/// <summary>One track entry shown in the Available or Assigned pane.</summary>
public sealed partial class PlaylistTrackItem : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isHighlighted;

    public PlaylistTrackItem(string id, string name)
    {
        Id = id;
        Name = name;
    }
}

public sealed partial class AudioConfigViewModel : ObservableObject
{
    private readonly IAudioPlaylistRepository _playlistRepo;
    private readonly IAudioTrackRepository _trackRepo;
    private readonly IConfigurationService _config;
    private readonly IAudioService _audioService;
    private readonly VirtualKeyboardViewModel _virtualKeyboard;
    private readonly ILogger<AudioConfigViewModel> _logger;

    // ── Tab selection ──────────────────────────────────────────────────────
    // Two tabs: Music (playlists + shared track library, combined) and Sounds.
    [ObservableProperty] private bool _isMusicTabActive = true;
    [ObservableProperty] private bool _isSoundsTabActive;

    // ── Global toggles ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableBackgroundMusic;
    [ObservableProperty] private bool _enableNavigationSounds;
    [ObservableProperty] private float _masterMusicVolume = 0.5f;
    [ObservableProperty] private float _masterSoundVolume = 1.0f;

    // ── Playlists ──────────────────────────────────────────────────────────
    public ObservableCollection<AudioPlaylist> Playlists { get; } = [];
    [ObservableProperty] private AudioPlaylist? _selectedPlaylist;

    // Editable copies of the selected playlist's fields — not written back to the
    // playlist itself until Save, same reasoning as every other editor in Settings.
    [ObservableProperty] private string _editPlaylistName = string.Empty;
    [ObservableProperty] private bool _editPlaylistIsGlobal;
    [ObservableProperty] private float _editPlaylistVolume = 0.5f;
    [ObservableProperty] private bool _editPlaylistShuffle = true;
    public ObservableCollection<PlaylistCategoryCheckItem> EditPlaylistCategories { get; } = [];

    // ── Track library + assignment ──────────────────────────────────────────
    public ObservableCollection<AudioTrack> Library { get; } = [];

    /// <summary>Tracks NOT currently in the selected playlist — drag to Assigned, or
    /// Confirm while this pane is focused, to add.</summary>
    public ObservableCollection<PlaylistTrackItem> AvailableTracks { get; } = [];

    /// <summary>Tracks currently in the selected playlist — drag to Available, or
    /// Confirm while this pane is focused, to remove.</summary>
    public ObservableCollection<PlaylistTrackItem> AssignedTracks { get; } = [];

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
    // Single flat sequence — simpler and lower-risk than true left/right zone
    // navigation, at the cost of a longer Up/Down sequence.
    [ObservableProperty] private int _musicFocusIndex;
    // 0 EnableBackgroundMusic, 1 MasterMusicVolume, 2 Playlist selector,
    // 3 New Playlist, 4 Name, 5 IsGlobal, 6 Categories (sub-mode), 7 Volume,
    // 8 Shuffle, 9 Delete Playlist, 10 Add Track to Library, 11 Available Tracks
    // (sub-mode), 12 Assigned Tracks (sub-mode). Save/Cancel are always-visible
    // buttons reached the same way (14/15) — see below.
    private const int SavePosition = 13;
    private const int CancelPosition = 14;
    private const int MusicPositionCountTotal = 15;

    // Category / track sub-modes — entered via Confirm at MusicFocusIndex 6/11/12.
    [ObservableProperty] private bool _isCategoriesSubModeFocused;
    [ObservableProperty] private int _selectedCategoryCheckIndex;
    [ObservableProperty] private bool _isAvailableTracksSubModeFocused;
    [ObservableProperty] private int _selectedAvailableTrackIndex;
    [ObservableProperty] private bool _isAssignedTracksSubModeFocused;
    [ObservableProperty] private int _selectedAssignedTrackIndex;

    public bool IsEnableMusicFocused    => MusicFocusIndex == 0;
    public bool IsMasterMusicVolumeFocused => MusicFocusIndex == 1;
    public bool IsPlaylistSelectorFocused => MusicFocusIndex == 2;
    public bool IsNewPlaylistFocused    => MusicFocusIndex == 3;
    public bool IsPlaylistNameFocused   => MusicFocusIndex == 4;
    public bool IsPlaylistGlobalFocused => MusicFocusIndex == 5;
    public bool IsPlaylistCategoriesFocused => MusicFocusIndex == 6;
    public bool IsPlaylistVolumeFocused => MusicFocusIndex == 7;
    public bool IsPlaylistShuffleFocused => MusicFocusIndex == 8;
    public bool IsDeletePlaylistFocused  => MusicFocusIndex == 9;
    public bool IsAddTrackFocused       => MusicFocusIndex == 10;
    public bool IsAvailableTracksFocused => MusicFocusIndex == 11;
    public bool IsAssignedTracksFocused => MusicFocusIndex == 12;
    public bool IsSaveMusicFocused      => MusicFocusIndex == SavePosition;
    public bool IsCancelMusicFocused    => MusicFocusIndex == CancelPosition;

    partial void OnMusicFocusIndexChanged(int value) => RaiseMusicFocusChanged();

    private void RaiseMusicFocusChanged()
    {
        OnPropertyChanged(nameof(IsEnableMusicFocused));
        OnPropertyChanged(nameof(IsMasterMusicVolumeFocused));
        OnPropertyChanged(nameof(IsPlaylistSelectorFocused));
        OnPropertyChanged(nameof(IsNewPlaylistFocused));
        OnPropertyChanged(nameof(IsPlaylistNameFocused));
        OnPropertyChanged(nameof(IsPlaylistGlobalFocused));
        OnPropertyChanged(nameof(IsPlaylistCategoriesFocused));
        OnPropertyChanged(nameof(IsPlaylistVolumeFocused));
        OnPropertyChanged(nameof(IsPlaylistShuffleFocused));
        OnPropertyChanged(nameof(IsDeletePlaylistFocused));
        OnPropertyChanged(nameof(IsAddTrackFocused));
        OnPropertyChanged(nameof(IsAvailableTracksFocused));
        OnPropertyChanged(nameof(IsAssignedTracksFocused));
        OnPropertyChanged(nameof(IsSaveMusicFocused));
        OnPropertyChanged(nameof(IsCancelMusicFocused));
    }

    partial void OnIsCategoriesSubModeFocusedChanged(bool value) => RefreshCategoryHighlight();
    partial void OnSelectedCategoryCheckIndexChanged(int value) => RefreshCategoryHighlight();
    partial void OnIsAvailableTracksSubModeFocusedChanged(bool value) => RefreshAvailableTrackHighlight();
    partial void OnSelectedAvailableTrackIndexChanged(int value) => RefreshAvailableTrackHighlight();
    partial void OnIsAssignedTracksSubModeFocusedChanged(bool value) => RefreshAssignedTrackHighlight();
    partial void OnSelectedAssignedTrackIndexChanged(int value) => RefreshAssignedTrackHighlight();

    private void RefreshCategoryHighlight()
    {
        for (int i = 0; i < EditPlaylistCategories.Count; i++)
            EditPlaylistCategories[i].IsHighlighted = IsCategoriesSubModeFocused && i == SelectedCategoryCheckIndex;
    }

    private void RefreshAvailableTrackHighlight()
    {
        for (int i = 0; i < AvailableTracks.Count; i++)
            AvailableTracks[i].IsHighlighted = IsAvailableTracksSubModeFocused && i == SelectedAvailableTrackIndex;
    }

    private void RefreshAssignedTrackHighlight()
    {
        for (int i = 0; i < AssignedTracks.Count; i++)
            AssignedTracks[i].IsHighlighted = IsAssignedTracksSubModeFocused && i == SelectedAssignedTrackIndex;
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

    public event Func<string, string[], Task<string?>>? BrowseFileRequested;

    public AudioConfigViewModel(
        IAudioPlaylistRepository playlistRepo,
        IAudioTrackRepository trackRepo,
        IConfigurationService config,
        IAudioService audioService,
        VirtualKeyboardViewModel virtualKeyboard,
        ILogger<AudioConfigViewModel> logger)
    {
        _playlistRepo = playlistRepo;
        _trackRepo = trackRepo;
        _config = config;
        _audioService = audioService;
        _virtualKeyboard = virtualKeyboard;
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

        var playlists = await _playlistRepo.GetAllAsync();
        Playlists.Clear();
        foreach (var p in playlists) Playlists.Add(p);

        var tracks = await _trackRepo.GetAllAsync();
        Library.Clear();
        foreach (var t in tracks) Library.Add(t);

        SelectedPlaylist = Playlists.FirstOrDefault(p => p.IsGlobal) ?? Playlists.FirstOrDefault();
        await LoadPlaylistIntoEditorAsync(SelectedPlaylist);
    }

    // ── Tab switching ──────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectMusicTab()
    {
        IsMusicTabActive = true;
        IsSoundsTabActive = false;
    }

    [RelayCommand]
    private void SelectSoundsTab()
    {
        IsMusicTabActive = false;
        IsSoundsTabActive = true;
    }

    public void SwitchSubTabLeft() => SelectMusicTab();
    public void SwitchSubTabRight() => SelectSoundsTab();

    // ── Playlist editing ────────────────────────────────────────────────────

    partial void OnSelectedPlaylistChanged(AudioPlaylist? value) => _ = LoadPlaylistIntoEditorAsync(value);

    /// <summary>Categories are re-fetched every time a playlist is loaded for
    /// editing (not just once when Settings opens), so a category added or renamed
    /// while Audio is already open shows up here without needing to close and
    /// reopen Settings.</summary>
    private async Task LoadPlaylistIntoEditorAsync(AudioPlaylist? playlist)
    {
        EditPlaylistName = playlist?.Name ?? string.Empty;
        EditPlaylistIsGlobal = playlist?.IsGlobal ?? false;
        EditPlaylistVolume = playlist?.Volume ?? 0.5f;
        EditPlaylistShuffle = playlist?.Shuffle ?? true;

        var categories = await _config.GetCategoriesAsync();
        var checkedCategoryIds = new HashSet<string>(playlist?.CategoryIds ?? [], StringComparer.OrdinalIgnoreCase);
        EditPlaylistCategories.Clear();
        foreach (var c in categories)
            EditPlaylistCategories.Add(new PlaylistCategoryCheckItem(c.Id, c.Label) { IsChecked = checkedCategoryIds.Contains(c.Id) });

        var assignedTrackIds = new HashSet<string>(playlist?.TrackIds ?? [], StringComparer.OrdinalIgnoreCase);
        AvailableTracks.Clear();
        AssignedTracks.Clear();
        foreach (var track in Library)
        {
            var item = new PlaylistTrackItem(track.Id, track.Name);
            if (assignedTrackIds.Contains(track.Id)) AssignedTracks.Add(item);
            else AvailableTracks.Add(item);
        }

        IsCategoriesSubModeFocused = false;
        IsAvailableTracksSubModeFocused = false;
        IsAssignedTracksSubModeFocused = false;
        SelectedCategoryCheckIndex = 0;
        SelectedAvailableTrackIndex = 0;
        SelectedAssignedTrackIndex = 0;
    }

    /// <summary>Moves a track from Available to Assigned — used by both the
    /// controller sub-mode (Confirm) and drag-and-drop (dropping onto the Assigned
    /// pane).</summary>
    public void AssignTrack(string trackId)
    {
        var item = AvailableTracks.FirstOrDefault(t => t.Id == trackId);
        if (item is null) return;
        AvailableTracks.Remove(item);
        AssignedTracks.Add(item);
        if (AvailableTracks.Count > 0)
            SelectedAvailableTrackIndex = Math.Clamp(SelectedAvailableTrackIndex, 0, AvailableTracks.Count - 1);
        else
            IsAvailableTracksSubModeFocused = false;
    }

    /// <summary>Moves a track from Assigned back to Available — same dual use as
    /// AssignTrack above.</summary>
    public void UnassignTrack(string trackId)
    {
        var item = AssignedTracks.FirstOrDefault(t => t.Id == trackId);
        if (item is null) return;
        AssignedTracks.Remove(item);
        AvailableTracks.Add(item);
        if (AssignedTracks.Count > 0)
            SelectedAssignedTrackIndex = Math.Clamp(SelectedAssignedTrackIndex, 0, AssignedTracks.Count - 1);
        else
            IsAssignedTracksSubModeFocused = false;
    }

    // ── Controller navigation ────────────────────────────────────────────
    public void NavigateUp()
    {
        if (IsSoundsTabActive)
        {
            SoundsFocusIndex = (SoundsFocusIndex - 1 + SoundsPositionCount) % SoundsPositionCount;
            return;
        }

        if (IsCategoriesSubModeFocused)
        {
            if (EditPlaylistCategories.Count == 0) return;
            SelectedCategoryCheckIndex = (SelectedCategoryCheckIndex - 1 + EditPlaylistCategories.Count) % EditPlaylistCategories.Count;
            return;
        }
        if (IsAvailableTracksSubModeFocused)
        {
            if (AvailableTracks.Count == 0) return;
            SelectedAvailableTrackIndex = (SelectedAvailableTrackIndex - 1 + AvailableTracks.Count) % AvailableTracks.Count;
            return;
        }
        if (IsAssignedTracksSubModeFocused)
        {
            if (AssignedTracks.Count == 0) return;
            SelectedAssignedTrackIndex = (SelectedAssignedTrackIndex - 1 + AssignedTracks.Count) % AssignedTracks.Count;
            return;
        }

        MusicFocusIndex = (MusicFocusIndex - 1 + MusicPositionCountTotal) % MusicPositionCountTotal;
    }

    public void NavigateDown()
    {
        if (IsSoundsTabActive)
        {
            SoundsFocusIndex = (SoundsFocusIndex + 1) % SoundsPositionCount;
            return;
        }

        if (IsCategoriesSubModeFocused)
        {
            if (EditPlaylistCategories.Count == 0) return;
            SelectedCategoryCheckIndex = (SelectedCategoryCheckIndex + 1) % EditPlaylistCategories.Count;
            return;
        }
        if (IsAvailableTracksSubModeFocused)
        {
            if (AvailableTracks.Count == 0) return;
            SelectedAvailableTrackIndex = (SelectedAvailableTrackIndex + 1) % AvailableTracks.Count;
            return;
        }
        if (IsAssignedTracksSubModeFocused)
        {
            if (AssignedTracks.Count == 0) return;
            SelectedAssignedTrackIndex = (SelectedAssignedTrackIndex + 1) % AssignedTracks.Count;
            return;
        }

        MusicFocusIndex = (MusicFocusIndex + 1) % MusicPositionCountTotal;
    }

    public void NavigateLeft()
    {
        if (IsSoundsTabActive)
        {
            if (SoundsFocusIndex == 1) { MasterSoundVolume = Math.Max(0f, MasterSoundVolume - 0.05f); return; }
            if (SoundsFocusIndex == 11) { VideoPreviewVolume = Math.Max(0f, VideoPreviewVolume - 0.05f); return; }
            SwitchSubTabLeft();
            return;
        }

        if (IsCategoriesSubModeFocused || IsAvailableTracksSubModeFocused || IsAssignedTracksSubModeFocused) return;

        switch (MusicFocusIndex)
        {
            case 1: MasterMusicVolume = Math.Max(0f, MasterMusicVolume - 0.05f); return;
            case 2: CyclePlaylistSelector(-1); return;
            case 7: EditPlaylistVolume = Math.Max(0f, EditPlaylistVolume - 0.05f); return;
        }
        SwitchSubTabLeft();
    }

    public void NavigateRight()
    {
        if (IsSoundsTabActive)
        {
            if (SoundsFocusIndex == 1) { MasterSoundVolume = Math.Min(1f, MasterSoundVolume + 0.05f); return; }
            if (SoundsFocusIndex == 11) { VideoPreviewVolume = Math.Min(1f, VideoPreviewVolume + 0.05f); return; }
            SwitchSubTabRight();
            return;
        }

        if (IsCategoriesSubModeFocused || IsAvailableTracksSubModeFocused || IsAssignedTracksSubModeFocused) return;

        switch (MusicFocusIndex)
        {
            case 1: MasterMusicVolume = Math.Min(1f, MasterMusicVolume + 0.05f); return;
            case 2: CyclePlaylistSelector(1); return;
            case 7: EditPlaylistVolume = Math.Min(1f, EditPlaylistVolume + 0.05f); return;
        }
        SwitchSubTabRight();
    }

    private void CyclePlaylistSelector(int delta)
    {
        if (Playlists.Count == 0) return;
        int idx = SelectedPlaylist is null ? 0 : Playlists.IndexOf(SelectedPlaylist);
        idx = (idx + delta + Playlists.Count) % Playlists.Count;
        SelectedPlaylist = Playlists[idx];
    }

    /// <summary>Back while any sub-mode (Categories/Available/Assigned) is focused
    /// exits just that — same convention as every other nested list/grid in
    /// Settings.</summary>
    public bool TryExitSubMode()
    {
        if (IsCategoriesSubModeFocused) { IsCategoriesSubModeFocused = false; return true; }
        if (IsAvailableTracksSubModeFocused) { IsAvailableTracksSubModeFocused = false; return true; }
        if (IsAssignedTracksSubModeFocused) { IsAssignedTracksSubModeFocused = false; return true; }
        return false;
    }

    public async Task ConfirmAsync()
    {
        if (IsSoundsTabActive)
        {
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
            return;
        }

        if (IsCategoriesSubModeFocused)
        {
            if (SelectedCategoryCheckIndex >= 0 && SelectedCategoryCheckIndex < EditPlaylistCategories.Count)
                EditPlaylistCategories[SelectedCategoryCheckIndex].IsChecked = !EditPlaylistCategories[SelectedCategoryCheckIndex].IsChecked;
            return;
        }
        if (IsAvailableTracksSubModeFocused)
        {
            if (SelectedAvailableTrackIndex >= 0 && SelectedAvailableTrackIndex < AvailableTracks.Count)
                AssignTrack(AvailableTracks[SelectedAvailableTrackIndex].Id);
            return;
        }
        if (IsAssignedTracksSubModeFocused)
        {
            if (SelectedAssignedTrackIndex >= 0 && SelectedAssignedTrackIndex < AssignedTracks.Count)
                UnassignTrack(AssignedTracks[SelectedAssignedTrackIndex].Id);
            return;
        }

        switch (MusicFocusIndex)
        {
            case 0: EnableBackgroundMusic = !EnableBackgroundMusic; break;
            case 3: AddNewPlaylist(); break;
            case 4: _virtualKeyboard.Open("Playlist Name", EditPlaylistName, v => EditPlaylistName = v); break;
            case 5: EditPlaylistIsGlobal = !EditPlaylistIsGlobal; break;
            case 6:
                if (EditPlaylistCategories.Count > 0)
                {
                    IsCategoriesSubModeFocused = true;
                    SelectedCategoryCheckIndex = Math.Clamp(SelectedCategoryCheckIndex, 0, EditPlaylistCategories.Count - 1);
                }
                break;
            case 8: EditPlaylistShuffle = !EditPlaylistShuffle; break;
            case 9: await DeleteSelectedPlaylistAsync(); break;
            case 10: await BrowseAddTrackAsync(); break;
            case 11:
                if (AvailableTracks.Count > 0)
                {
                    IsAvailableTracksSubModeFocused = true;
                    SelectedAvailableTrackIndex = Math.Clamp(SelectedAvailableTrackIndex, 0, AvailableTracks.Count - 1);
                }
                break;
            case 12:
                if (AssignedTracks.Count > 0)
                {
                    IsAssignedTracksSubModeFocused = true;
                    SelectedAssignedTrackIndex = Math.Clamp(SelectedAssignedTrackIndex, 0, AssignedTracks.Count - 1);
                }
                break;
            case SavePosition: await SaveAsync(); break;
            case CancelPosition: await CancelPlaylistEditsAsync(); break;
            // 1, 2, 7 (volume/selector) are adjusted via Left/Right, not Confirm.
        }
    }

    [RelayCommand]
    private async Task CancelPlaylistEditsAsync() => await LoadPlaylistIntoEditorAsync(SelectedPlaylist);

    [RelayCommand]
    private void AddNewPlaylist()
    {
        var newPlaylist = new AudioPlaylist
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Playlist",
        };
        Playlists.Add(newPlaylist);
        SelectedPlaylist = newPlaylist;
        MusicFocusIndex = 4; // land on the Name field, ready to rename
    }

    [RelayCommand]
    private async Task DeleteSelectedPlaylistAsync()
    {
        if (SelectedPlaylist is null) return;
        var removedId = SelectedPlaylist.Id;
        Playlists.Remove(SelectedPlaylist);
        await _playlistRepo.DeleteAsync(removedId);
        await _playlistRepo.SaveAsync();
        SelectedPlaylist = Playlists.FirstOrDefault();
        StatusMessage = "Playlist deleted.";
        _logger.LogInformation("Playlist deleted: {Id}", removedId);
    }

    // ── Track library management ────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseAddTrackAsync()
    {
        if (BrowseFileRequested is null) return;
        var path = await BrowseFileRequested.Invoke("Audio Files", ["*.mp3", "*.ogg", "*.wav", "*.flac", "*.m4a"]);
        if (path is null) return;

        var track = new AudioTrack
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Path = UGL.Core.Utilities.PortablePathHelper.ToPortablePath(path),
        };
        Library.Add(track);
        await _trackRepo.AddOrUpdateAsync(track);
        await _trackRepo.SaveAsync();

        AvailableTracks.Add(new PlaylistTrackItem(track.Id, track.Name));
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
        var path = await BrowseFileRequested.Invoke("Sound Files", ["*.wav", "*.ogg", "*.mp3"]);
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
            SelectedPlaylist.Name = EditPlaylistName.Trim();
            SelectedPlaylist.IsGlobal = EditPlaylistIsGlobal;
            SelectedPlaylist.Volume = EditPlaylistVolume;
            SelectedPlaylist.Shuffle = EditPlaylistShuffle;
            SelectedPlaylist.CategoryIds = EditPlaylistCategories.Where(c => c.IsChecked).Select(c => c.Id).ToList();
            SelectedPlaylist.TrackIds = AssignedTracks.Select(t => t.Id).ToList();

            // IsGlobal is exclusive in practice — only one playlist should be THE
            // global one at a time — enforced here, not by the model itself.
            if (SelectedPlaylist.IsGlobal)
                foreach (var other in Playlists.Where(p => p.Id != SelectedPlaylist.Id))
                    other.IsGlobal = false;
        }

        foreach (var p in Playlists)
            await _playlistRepo.AddOrUpdateAsync(p);
        await _playlistRepo.SaveAsync();

        // Persist sound settings back to AppSettings / settings.json.
        // AppSettings properties are mutable (`set`, not `init`), but every field must
        // still be carried over explicitly here — every field, since a reconstruction
        // like this silently resets anything left out.
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
            HidHideEnabled         = s.HidHideEnabled,
            HidHideCliPath         = s.HidHideCliPath,
        };

        await _config.UpdateSettingsAsync(updated);

        // Apply volume/enable changes immediately without restart
        _audioService.IsMusicEnabled  = EnableBackgroundMusic;
        _audioService.IsSoundEnabled  = EnableNavigationSounds;
        _audioService.MusicVolume     = MasterMusicVolume;
        _audioService.SoundVolume     = MasterSoundVolume;

        StatusMessage = "Audio settings saved.";
        _logger.LogInformation("Audio config saved.");
    }
}
