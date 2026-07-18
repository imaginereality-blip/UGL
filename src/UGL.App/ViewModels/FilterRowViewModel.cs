using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UGL.App.ViewModels;

/// <summary>
/// Represents one horizontal row in the filter overlay (e.g. "System", "Genre").
/// Options is an ObservableCollection so the ItemsControl updates automatically
/// when OpenAsync rebuilds the pill list on subsequent opens.
/// </summary>
public sealed partial class FilterRowViewModel : ObservableObject
{
    public string RowLabel { get; }

    /// <summary>
    /// ObservableCollection — Avalonia's ItemsControl subscribes to CollectionChanged,
    /// so Clear() + Add() on re-open correctly rebuilds the pill list in the UI.
    /// </summary>
    public ObservableCollection<FilterOptionViewModel> Options { get; } = new();

    [ObservableProperty]
    private bool _isFocusedRow;

    private int _focusedOptionIndex;

    public FilterRowViewModel(string rowLabel)
    {
        RowLabel = rowLabel;
    }

    public FilterOptionViewModel? FocusedOption =>
        Options.Count > 0 ? Options[_focusedOptionIndex] : null;

    public FilterOptionViewModel? SelectedOption =>
        Options.FirstOrDefault(o => o.IsSelected);

    public string? SelectedValue => SelectedOption?.Value;

    // ── Navigation ─────────────────────────────────────────────────────────

    public void MoveFocusLeft()
    {
        if (Options.Count == 0) return;
        SetFocusedIndex(Math.Max(0, _focusedOptionIndex - 1));
    }

    public void MoveFocusRight()
    {
        if (Options.Count == 0) return;
        SetFocusedIndex(Math.Min(Options.Count - 1, _focusedOptionIndex + 1));
    }

    public void ConfirmFocused()
    {
        foreach (var opt in Options)
            opt.IsSelected = false;

        if (FocusedOption is not null)
            FocusedOption.IsSelected = true;
    }

    public void RestoreFocusToSelected()
    {
        var selected = Options.FirstOrDefault(o => o.IsSelected) ?? Options.FirstOrDefault();
        var idx = selected is not null ? Options.IndexOf(selected) : 0;
        SetFocusedIndex(Math.Max(0, idx));
    }

    public void Reset()
    {
        foreach (var opt in Options)
            opt.IsSelected = false;

        if (Options.Count > 0)
        {
            Options[0].IsSelected = true;
            SetFocusedIndex(0);
        }
    }

    /// <summary>
    /// Clears all options and resets the focused index to 0.
    /// Call this before re-populating Options on subsequent OpenAsync calls
    /// so the index never points past the end of the new list.
    /// </summary>
    public void ResetOptions()
    {
        _focusedOptionIndex = 0;
        Options.Clear();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetFocusedIndex(int index)
    {
        if (_focusedOptionIndex < Options.Count)
            Options[_focusedOptionIndex].IsFocused = false;

        _focusedOptionIndex = index;

        if (_focusedOptionIndex < Options.Count)
            Options[_focusedOptionIndex].IsFocused = true;
    }
}
