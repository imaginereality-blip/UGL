using CommunityToolkit.Mvvm.ComponentModel;

namespace UGL.App.ViewModels;

/// <summary>
/// Represents one selectable option within a filter row (e.g. "SNES", "Fighting", "2 Players").
/// Value is the raw filter string; Label is the display string shown in the UI.
/// </summary>
public sealed partial class FilterOptionViewModel : ObservableObject
{
    /// <summary>Raw value matched against Game fields. Null means "All" (no filter).</summary>
    public string? Value { get; }

    /// <summary>Display label shown on the pill button.</summary>
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFocused;

    public FilterOptionViewModel(string label, string? value = null)
    {
        Label = label;
        Value = value;
    }
}
