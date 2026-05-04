using CommunityToolkit.Mvvm.ComponentModel;

namespace Grimly.Models;

public enum DiffType
{
    Unchanged,
    Added,
    Removed
}

public sealed class TextDiff
{
    public string Text { get; init; } = "";
    public DiffType Type { get; init; }
}

public enum ChangeState
{
    Pending,
    Accepted,
    Rejected
}

// Represents either unchanged text or a change group (removed+added pair)
public partial class ReviewSegment : ObservableObject
{
    public int Id { get; init; }
    public bool IsChange { get; init; }

    // For unchanged segments
    public string UnchangedText { get; init; } = "";

    // For change segments
    public string RemovedText { get; init; } = "";
    public string AddedText { get; init; } = "";

    [ObservableProperty]
    private ChangeState _state = ChangeState.Pending;

    // Text that drives WorkingText (and therefore the readability score) at
    // the current state. Pending resolves to AddedText so the initial review
    // reflects the "as if all suggestions accepted" revised text; Toggle then
    // flips individual segments to Rejected (score moves down toward original)
    // or Accepted (score moves back up).
    public string ResolvedText => IsChange
        ? State switch
        {
            ChangeState.Accepted => AddedText,
            ChangeState.Rejected => RemovedText,
            _ => AddedText // pending = treated as accepted for scoring and paste
        }
        : UnchangedText;

    // Binary toggle: every click flips between Accepted and Rejected, so each
    // interaction produces a visible text change and a readability update.
    // From the initial Pending state (scored as Accepted), the first click
    // moves to Rejected — matching a user who wants to opt out of a suggestion.
    public void Toggle()
    {
        State = State == ChangeState.Rejected
            ? ChangeState.Accepted
            : ChangeState.Rejected;
    }
}
