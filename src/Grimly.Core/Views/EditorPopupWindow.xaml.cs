using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Grimly.Models;
using Grimly.ViewModels;

namespace Grimly.Views;

public partial class EditorPopupWindow : Window
{
    // Colors for review states. Pulled from the active theme dictionary so the
    // diff palette switches with the rest of the app. Fallbacks preserve the
    // old hardcoded values if a theme forgets a key.
    private readonly SolidColorBrush UnchangedFg;
    private readonly SolidColorBrush PendingRemovedFg;
    private readonly SolidColorBrush PendingAddedFg;
    private readonly SolidColorBrush PendingAddedBg;
    private readonly SolidColorBrush AcceptedFg;
    private readonly SolidColorBrush AcceptedBg;
    private readonly SolidColorBrush RejectedFg;
    private readonly SolidColorBrush RejectedBg;
    private readonly SolidColorBrush HoverBg;

    public EditorPopupWindow()
    {
        InitializeComponent();

        UnchangedFg      = ResolveBrush("DiffUnchangedForeground",      Color.FromRgb(180, 180, 180));
        PendingRemovedFg = ResolveBrush("DiffPendingRemovedForeground", Color.FromRgb(220, 100, 100));
        PendingAddedFg   = ResolveBrush("DiffPendingAddedForeground",   Color.FromRgb(80, 220, 80));
        PendingAddedBg   = ResolveBrush("DiffPendingAddedBackground",   Color.FromArgb(40, 0, 180, 0));
        AcceptedFg       = ResolveBrush("DiffAcceptedForeground",       Color.FromRgb(120, 200, 120));
        AcceptedBg       = ResolveBrush("DiffAcceptedBackground",       Color.FromArgb(30, 0, 160, 0));
        RejectedFg       = ResolveBrush("DiffRejectedForeground",       Color.FromRgb(120, 120, 120));
        RejectedBg       = ResolveBrush("DiffRejectedBackground",       Color.FromArgb(20, 255, 0, 0));
        HoverBg          = ResolveBrush("DiffHoverBackground",          Color.FromArgb(40, 100, 150, 255));

        DataContextChanged += OnDataContextChanged;
        BuildModeButtons();
    }

    private static SolidColorBrush ResolveBrush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush themed) return themed;
        return new SolidColorBrush(fallback);
    }

    private void BuildModeButtons()
    {
        // Use the curated UI order from EditingModeExtensions rather than the
        // enum declaration order. Keeps the escape-hatch "Custom" button last
        // and lets us reshuffle task-specific modes without breaking the
        // integer-serialized DefaultMode setting.
        foreach (var mode in EditingModeExtensions.UiOrder)
        {
            var btn = new Button
            {
                Content = mode.GetDisplayName(),
                Tag = mode,
                Style = (Style)FindResource("PillButton"),
                Margin = new Thickness(3)
            };

            btn.Click += (s, ev) =>
            {
                if (s is Button clicked && clicked.Tag is EditingMode m && DataContext is EditorPopupViewModel vm)
                {
                    vm.SelectedMode = m;
                    // Custom doesn't have a canned prompt — it needs the user
                    // to type an instruction and confirm with Enter. Clicking
                    // the Custom pill just reveals the input and focuses it;
                    // the CustomPromptTextBox_KeyDown handler runs the command
                    // when the user presses Enter.
                    if (m == EditingMode.CustomPrompt)
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            CustomPromptTextBox.Focus();
                            CustomPromptTextBox.SelectAll();
                        }, System.Windows.Threading.DispatcherPriority.Input);
                        return;
                    }
                    vm.ProcessCommand.ExecuteAsync(null);
                }
            };

            ModePanel.Children.Add(btn);
        }
    }

    private void UpdateModeButtonStyles()
    {
        if (DataContext is not EditorPopupViewModel vm) return;

        foreach (Button btn in ModePanel.Children)
        {
            if (btn.Tag is EditingMode mode && vm.IsModeApplied(mode))
            {
                btn.Style = (Style)FindResource("PillButtonApplied");
            }
            else
            {
                btn.Style = (Style)FindResource("PillButton");
            }
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EditorPopupViewModel oldVm)
        {
            oldVm.RequestClose -= Close;
            oldVm.ReviewSegmentsChanged -= RenderReviewSegments;
            oldVm.AppliedModesChanged -= UpdateModeButtonStyles;
        }

        if (e.NewValue is EditorPopupViewModel newVm)
        {
            newVm.RequestClose += Close;
            newVm.ReviewSegmentsChanged += RenderReviewSegments;
            newVm.AppliedModesChanged += UpdateModeButtonStyles;
        }
    }

    private void RenderReviewSegments()
    {
        if (DataContext is not EditorPopupViewModel vm) return;

        ReviewParagraph.Inlines.Clear();

        foreach (var seg in vm.ReviewSegments)
        {
            if (!seg.IsChange)
            {
                // Unchanged text — plain
                var run = new Run(seg.UnchangedText) { Foreground = UnchangedFg };
                ReviewParagraph.Inlines.Add(run);
            }
            else
            {
                // Change group — clickable container
                var container = CreateChangeInline(seg, vm);
                ReviewParagraph.Inlines.Add(container);
            }
        }
    }

    private Span CreateChangeInline(ReviewSegment seg, EditorPopupViewModel vm)
    {
        var span = new Span();
        span.Cursor = Cursors.Hand;
        span.Tag = seg.Id;

        // Mouse click handler
        span.MouseLeftButtonDown += (s, e) =>
        {
            vm.ToggleChange(seg.Id);
            e.Handled = true;
        };

        // Hover effect
        span.MouseEnter += (s, e) =>
        {
            if (s is Span sp) sp.Background = HoverBg;
        };
        span.MouseLeave += (s, e) =>
        {
            if (s is Span sp) sp.Background = Brushes.Transparent;
        };

        // Render based on state
        ApplyStateToSpan(span, seg);
        return span;
    }

    private void ApplyStateToSpan(Span span, ReviewSegment seg)
    {
        span.Inlines.Clear();

        switch (seg.State)
        {
            case ChangeState.Pending:
                // Show removed (red strikethrough) then added (green)
                if (!string.IsNullOrEmpty(seg.RemovedText))
                {
                    span.Inlines.Add(new Run(seg.RemovedText)
                    {
                        Foreground = PendingRemovedFg,
                        TextDecorations = TextDecorations.Strikethrough
                    });
                }
                if (!string.IsNullOrEmpty(seg.AddedText))
                {
                    span.Inlines.Add(new Run(seg.AddedText)
                    {
                        Foreground = PendingAddedFg,
                        Background = PendingAddedBg
                    });
                }
                break;

            case ChangeState.Accepted:
                // Show only the added text, subtle green
                var acceptedText = !string.IsNullOrEmpty(seg.AddedText) ? seg.AddedText : "";
                span.Inlines.Add(new Run("\u2713 ") // checkmark
                {
                    Foreground = AcceptedFg,
                    FontSize = 10
                });
                span.Inlines.Add(new Run(acceptedText)
                {
                    Foreground = AcceptedFg,
                    Background = AcceptedBg
                });
                break;

            case ChangeState.Rejected:
                // Show only the original text, dimmed
                var rejectedText = !string.IsNullOrEmpty(seg.RemovedText) ? seg.RemovedText : "";
                span.Inlines.Add(new Run("\u2717 ") // X mark
                {
                    Foreground = RejectedFg,
                    FontSize = 10
                });
                span.Inlines.Add(new Run(rejectedText)
                {
                    Foreground = RejectedFg,
                    Background = RejectedBg
                });
                break;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Native.NativeMethods.GetCursorPos(out var pt))
        {
            var workArea = SystemParameters.WorkArea;
            double x = pt.X + 16;
            double y = pt.Y + 16;

            if (x + ActualWidth > workArea.Right)
                x = workArea.Right - ActualWidth - 16;
            if (y + ActualHeight > workArea.Bottom)
                y = pt.Y - ActualHeight - 16;
            if (x < workArea.Left) x = workArea.Left + 16;
            if (y < workArea.Top) y = workArea.Top + 16;

            Left = x;
            Top = y;
        }
    }

    /// <summary>
    /// Called by the host when no text was captured via UIA — activates the
    /// popup and focuses the working-text box so the user can paste their
    /// content (Ctrl+V) directly into the internal buffer without needing
    /// to click anything first. Used as the graceful fallback for Electron
    /// apps, custom text surfaces, and other UIA-hostile targets.
    /// </summary>
    public void FocusWorkingTextInput()
    {
        // Dispatcher hop so we run after the window's layout pass and after
        // its HWND is active; otherwise Focus() can be dropped on the floor.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Activate();
            WorkingTextInput.Focus();
            System.Windows.Input.Keyboard.Focus(WorkingTextInput);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is EditorPopupViewModel vm)
        {
            vm.DismissCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch { }
    }

    /// <summary>
    /// Runs the custom-prompt mode when the user presses Enter inside the
    /// custom-instruction TextBox. Ignores Enter with modifiers (Shift+Enter,
    /// Ctrl+Enter) in case we later want to support multi-line instructions.
    /// Runs only when there's non-empty text — prevents triggering with an
    /// empty prompt.
    /// </summary>
    private void CustomPromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        if (DataContext is not EditorPopupViewModel vm) return;
        if (string.IsNullOrWhiteSpace(vm.CustomPrompt)) return;

        e.Handled = true;
        vm.ProcessCommand.ExecuteAsync(null);
    }

    private void StatusLed_Click(object sender, MouseButtonEventArgs e)
    {
        // Clicking the LED re-checks the connection
        if (DataContext is EditorPopupViewModel vm)
        {
            _ = vm.CheckConnectionAsync();
        }
        e.Handled = true;
    }

    private List<int> GetSelectedChangeIds()
    {
        var ids = new List<int>();
        var selection = ReviewDisplay.Selection;
        if (selection == null || selection.IsEmpty) return ids;

        // Walk all Spans in the paragraph and check if they overlap with selection
        foreach (var inline in ReviewParagraph.Inlines)
        {
            if (inline is Span span && span.Tag is int segId)
            {
                var spanStart = span.ContentStart;
                var spanEnd = span.ContentEnd;
                var selStart = selection.Start;
                var selEnd = selection.End;

                // Check if the span overlaps with the selection
                if (spanStart.CompareTo(selEnd) < 0 && spanEnd.CompareTo(selStart) > 0)
                {
                    ids.Add(segId);
                }
            }
        }
        return ids;
    }

    private void ReviewDisplay_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedChangeIds();
        if (ids.Count > 0)
        {
            SelectionLabel.Text = $"{ids.Count} change{(ids.Count == 1 ? "" : "s")} selected:";
            SelectionToolbar.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionToolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void AcceptSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPopupViewModel vm) return;
        vm.SetChangeStates(GetSelectedChangeIds(), ChangeState.Accepted);
    }

    private void RejectSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPopupViewModel vm) return;
        vm.SetChangeStates(GetSelectedChangeIds(), ChangeState.Rejected);
    }
}
