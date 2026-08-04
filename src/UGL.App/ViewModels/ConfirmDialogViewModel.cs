using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UGL.App.ViewModels;

/// <summary>
/// A reusable Yes/No confirmation overlay for destructive actions (deleting a game,
/// system, emulator, category, playlist, ...). Same architecture as
/// VirtualKeyboardViewModel: when IsOpen, MainWindowViewModel routes all controller
/// input here first, above Settings itself, until Yes or No/Back closes it.
///
/// Defaults to "No" highlighted, not "Yes" — an accidental extra Select press right
/// after triggering a delete (e.g. a held button repeating) lands on Cancel, not on
/// confirming the delete. Callers open it via Open(message, onConfirm); onConfirm only
/// fires when Yes is explicitly selected and confirmed.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>Which option is highlighted — toggled via Left/Right. Starts false
    /// (No highlighted) every time the dialog opens; see class remarks.</summary>
    [ObservableProperty] private bool _isYesSelected;

    private Action? _onConfirm;

    public void Open(string message, Action onConfirm)
    {
        Message = message;
        _onConfirm = onConfirm;
        IsYesSelected = false;
        IsOpen = true;
    }

    public void NavigateLeft() => IsYesSelected = true;
    public void NavigateRight() => IsYesSelected = false;

    public void Confirm()
    {
        IsOpen = false;
        if (IsYesSelected) _onConfirm?.Invoke();
    }

    public void Cancel() => IsOpen = false;

    /// <summary>Direct mouse-click equivalents — bypass IsYesSelected entirely so a
    /// click always does exactly what the button it landed on says.</summary>
    [RelayCommand]
    private void ClickYes()
    {
        IsOpen = false;
        _onConfirm?.Invoke();
    }

    [RelayCommand]
    private void ClickNo() => IsOpen = false;
}
