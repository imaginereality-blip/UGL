using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UGL.App.ViewModels;

/// <summary>One key on the virtual keyboard. IsHighlighted drives its visual state directly
/// (same Classes-binding trick used for the settings sidebar and Peripheral Hooks' Rescan
/// button) — no Avalonia focus system involved.</summary>
public sealed partial class VirtualKeyViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private bool _isHighlighted;

    public VirtualKeyViewModel(string label) => _label = label;
}

/// <summary>
/// A reusable on-screen QWERTY keyboard for editing text fields with a controller.
/// Driven the same way every other controller-navigable list in this app is: bound
/// properties manipulated directly in C#, no Avalonia focus system involved.
///
/// When IsOpen, MainWindowViewModel routes ALL controller input here first — above even
/// Settings — until Done or Cancel closes it. Callers open it via Open(label, currentValue,
/// onCommit); onCommit only fires on Done, never on Cancel, so cancelling discards edits.
///
/// Scope note: only plain text entry is supported (letters, digits, space, shift/case,
/// backspace). No symbols/punctuation keys yet — add rows here if a field needs them.
/// </summary>
public sealed partial class VirtualKeyboardViewModel : ObservableObject
{
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _bufferText = string.Empty;
    [ObservableProperty] private string _fieldLabel = string.Empty;
    [ObservableProperty] private bool _isShifted;

    /// <summary>Index into BufferText where the next typed/deleted character applies —
    /// same meaning as a text caret. Moved by the dedicated ◀/▶ keys.</summary>
    [ObservableProperty] private int _cursorPosition;

    public string TextBeforeCursor => BufferText[..Math.Clamp(CursorPosition, 0, BufferText.Length)];
    public string TextAfterCursor  => BufferText[Math.Clamp(CursorPosition, 0, BufferText.Length)..];

    partial void OnBufferTextChanged(string value)
    {
        OnPropertyChanged(nameof(TextBeforeCursor));
        OnPropertyChanged(nameof(TextAfterCursor));
    }

    partial void OnCursorPositionChanged(int value)
    {
        OnPropertyChanged(nameof(TextBeforeCursor));
        OnPropertyChanged(nameof(TextAfterCursor));
    }

    [ObservableProperty] private int _rowIndex;
    [ObservableProperty] private int _keyIndex;

    public ObservableCollection<VirtualKeyViewModel> Row0Keys { get; } = MakeRow("1234567890");
    public ObservableCollection<VirtualKeyViewModel> Row1Keys { get; } = MakeRow("qwertyuiop");
    public ObservableCollection<VirtualKeyViewModel> Row2Keys { get; } = MakeRow("asdfghjkl");

    public ObservableCollection<VirtualKeyViewModel> Row3Keys { get; } =
    [
        new("Shift"), new("z"), new("x"), new("c"), new("v"), new("b"), new("n"), new("m"), new("Back"),
    ];

    public ObservableCollection<VirtualKeyViewModel> Row4Keys { get; } =
    [
        new("◀"), new("Space"), new("▶"), new("Done"), new("Cancel"),
    ];

    private Action<string>? _onCommit;

    private static ObservableCollection<VirtualKeyViewModel> MakeRow(string chars)
    {
        var result = new ObservableCollection<VirtualKeyViewModel>();
        foreach (var c in chars) result.Add(new VirtualKeyViewModel(c.ToString()));
        return result;
    }

    private IEnumerable<ObservableCollection<VirtualKeyViewModel>> AllRows()
    {
        yield return Row0Keys;
        yield return Row1Keys;
        yield return Row2Keys;
        yield return Row3Keys;
        yield return Row4Keys;
    }

    private ObservableCollection<VirtualKeyViewModel> GetRow(int index) => index switch
    {
        0 => Row0Keys,
        1 => Row1Keys,
        2 => Row2Keys,
        3 => Row3Keys,
        _ => Row4Keys,
    };

    partial void OnIsShiftedChanged(bool value)
    {
        foreach (var row in new[] { Row1Keys, Row2Keys, Row3Keys })
        {
            foreach (var key in row)
            {
                if (key.Label.Length == 1 && char.IsLetter(key.Label[0]))
                    key.Label = value ? key.Label.ToUpperInvariant() : key.Label.ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// Opens the keyboard for editing a text field. onCommit is called with the final
    /// text only if the user presses Done — Cancel discards changes entirely.
    /// </summary>
    public void Open(string fieldLabel, string initialValue, Action<string> onCommit)
    {
        FieldLabel = fieldLabel;
        BufferText = initialValue;
        CursorPosition = initialValue.Length; // start at the end, matching prior append-only behavior
        _onCommit = onCommit;
        RowIndex = 0;
        KeyIndex = 0;
        IsOpen = true;
        RefreshHighlight();
    }

    public void NavigateUp()
    {
        RowIndex = (RowIndex - 1 + 5) % 5;
        KeyIndex = Math.Min(KeyIndex, GetRow(RowIndex).Count - 1);
        RefreshHighlight();
    }

    public void NavigateDown()
    {
        RowIndex = (RowIndex + 1) % 5;
        KeyIndex = Math.Min(KeyIndex, GetRow(RowIndex).Count - 1);
        RefreshHighlight();
    }

    public void NavigateLeft()
    {
        var row = GetRow(RowIndex);
        KeyIndex = (KeyIndex - 1 + row.Count) % row.Count;
        RefreshHighlight();
    }

    public void NavigateRight()
    {
        var row = GetRow(RowIndex);
        KeyIndex = (KeyIndex + 1) % row.Count;
        RefreshHighlight();
    }

    public void Confirm()
    {
        var key = GetRow(RowIndex)[KeyIndex];
        switch (key.Label)
        {
            case "Shift":
                IsShifted = !IsShifted;
                break;
            case "Back":
                if (CursorPosition > 0)
                {
                    BufferText = BufferText.Remove(CursorPosition - 1, 1);
                    CursorPosition--;
                }
                break;
            case "Space":
                BufferText = BufferText.Insert(CursorPosition, " ");
                CursorPosition++;
                break;
            case "◀":
                if (CursorPosition > 0) CursorPosition--;
                break;
            case "▶":
                if (CursorPosition < BufferText.Length) CursorPosition++;
                break;
            case "Done":
                _onCommit?.Invoke(BufferText);
                IsOpen = false;
                break;
            case "Cancel":
                IsOpen = false;
                break;
            default:
                BufferText = BufferText.Insert(CursorPosition, key.Label);
                CursorPosition += key.Label.Length;
                break;
        }
    }

    public void Cancel() => IsOpen = false;

    private void RefreshHighlight()
    {
        foreach (var row in AllRows())
            foreach (var key in row)
                key.IsHighlighted = false;

        var current = GetRow(RowIndex);
        if (KeyIndex < current.Count)
            current[KeyIndex].IsHighlighted = true;
    }
}
