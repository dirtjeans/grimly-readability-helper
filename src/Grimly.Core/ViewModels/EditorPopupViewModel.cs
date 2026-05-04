using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grimly.Hosting;
using Grimly.Models;
using Grimly.Services;

namespace Grimly.ViewModels;

public partial class EditorPopupViewModel : ObservableObject
{
    private readonly IFoundryLocalClient _foundryClient;
    private readonly IClipboardService _clipboardService;
    private readonly ITextDiffService _diffService;
    private readonly IFoundryManager _foundryManager;
    private readonly IReadabilityService _readabilityService;
    private readonly IWordReadabilityService? _wordReadabilityService;
    private readonly IGrammarChecker _codeChecker;
    private readonly ISettingsService _settingsService;
    private readonly BrandingOptions _branding;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _liveCheckCts;
    private readonly Stack<string> _undoStack = new();
    private string _preRevisionText = "";
    private readonly HashSet<EditingMode> _appliedModes = new();
    private int _consecutiveTimeouts;

    public event Action? AppliedModesChanged;

    [ObservableProperty]
    private string _workingText = "";

    [ObservableProperty]
    private ObservableCollection<ReviewSegment> _reviewSegments = [];

    [ObservableProperty]
    private EditingMode _selectedMode;

    [ObservableProperty]
    private string _customPrompt = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _isReviewing; // true when showing accept/reject UI

    [ObservableProperty]
    private bool _showRetry;

    [ObservableProperty]
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;

    [ObservableProperty]
    private string _connectionStatusText = "Checking local LLM...";

    [ObservableProperty]
    private double _readabilityScore;

    [ObservableProperty]
    private string _readabilityLabel = "";

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private int _charCount;

    // ===== Match Word state =====
    /// <summary>
    /// True when Microsoft Word is installed on this machine and can be called
    /// via COM to compute the precise Flesch score. Drives visibility of the
    /// "Match Word" button.
    /// </summary>
    [ObservableProperty]
    private bool _isWordMatchAvailable;

    /// <summary>True while the Match Word call is in flight.</summary>
    [ObservableProperty]
    private bool _isMatchingWord;

    /// <summary>
    /// True when the displayed readability score came from Word (precise) rather
    /// than the local heuristic (approximate). Any text change flips this back
    /// to false.
    /// </summary>
    [ObservableProperty]
    private bool _isWordMatched;

    /// <summary>
    /// True when the popup opened as a manual-paste fallback — the host
    /// triggered the popup (hotkey or floating icon), but UIA couldn't read
    /// a selection from the target app (Electron, custom text surfaces,
    /// accessibility denied). The UI shows a specific hint telling the user
    /// to paste their text into the working-text box.
    /// </summary>
    [ObservableProperty]
    private bool _isManualPasteMode;

    // ===== Live grammar-check state =====
    /// <summary>Deterministic violations from the live grammar checker.</summary>
    public ObservableCollection<Violation> Violations { get; }

    /// <summary>Convenience flag for XAML visibility. Tracks <c>Violations.Count &gt; 0</c>.</summary>
    public bool HasViolations => Violations.Count > 0;

    /// <summary>Hint text shown above the violations list.</summary>
    public string QuickFixHint =>
        "Click Quick Fix for the mechanical corrections. Use Fix Grammar for AI-assisted revisions of the rest.";

    public bool IsCustomMode => SelectedMode == EditingMode.CustomPrompt;

    partial void OnSelectedModeChanged(EditingMode value)
    {
        OnPropertyChanged(nameof(IsCustomMode));
    }

    public IReadOnlyList<EditingMode> AvailableModes { get; } = Enum.GetValues<EditingMode>();

    private static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r', '\u00A0' };

    public event Action? RequestClose;
    public event Action? ReviewSegmentsChanged;
    public IntPtr PreviousForegroundWindow { get; set; }

    public EditorPopupViewModel(
        IFoundryLocalClient foundryClient,
        IClipboardService clipboardService,
        ITextDiffService diffService,
        IFoundryManager foundryManager,
        IReadabilityService readabilityService,
        IGrammarChecker codeChecker,
        ISettingsService settingsService,
        BrandingOptions branding,
        IWordReadabilityService? wordReadabilityService = null)
    {
        _foundryClient = foundryClient;
        _clipboardService = clipboardService;
        _diffService = diffService;
        _foundryManager = foundryManager;
        _readabilityService = readabilityService;
        _wordReadabilityService = wordReadabilityService;
        _codeChecker = codeChecker;
        _settingsService = settingsService;
        _branding = branding;

        IsWordMatchAvailable = wordReadabilityService?.IsAvailable ?? false;

        Violations = [];
        Violations.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasViolations));
            OnPropertyChanged(nameof(HasAutoFixableViolations));
            ApplyQuickFixesCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnWorkingTextChanged(string value)
    {
        UpdateReadability(value);
        // Once the user has pasted anything non-empty into the internal
        // buffer, we're no longer in the "can't read selection" state —
        // revert to the normal hint wording.
        if (!string.IsNullOrEmpty(value) && IsManualPasteMode)
            IsManualPasteMode = false;

        ScheduleLiveCheck(value);
    }

    /// <summary>
    /// Debounced live deterministic style/grammar/spelling check. Runs the
    /// in-process <see cref="IGrammarChecker"/> ~400 ms after the user
    /// stops typing so the violations panel reflects the current text without
    /// requiring a button click.
    /// </summary>
    private void ScheduleLiveCheck(string snapshot)
    {
        _liveCheckCts?.Cancel();
        _liveCheckCts = new CancellationTokenSource();
        var ct = _liveCheckCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct);
                if (ct.IsCancellationRequested) return;

                // Empty input = no violations, no work to do.
                IReadOnlyList<Violation> result = string.IsNullOrWhiteSpace(snapshot)
                    ? Array.Empty<Violation>()
                    : _codeChecker.Check(snapshot);

                if (ct.IsCancellationRequested) return;

                // Marshal back to the UI thread — Violations is bound to the
                // view, and ObservableCollection mutations must happen there.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                await dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    Violations.Clear();
                    foreach (var v in result) Violations.Add(v);
                });
            }
            catch (OperationCanceledException) { }
            catch { /* live check failures are non-fatal */ }
        }, ct);
    }

    public async Task CheckConnectionAsync()
    {
        ConnectionStatus = ConnectionStatus.Unknown;
        ConnectionStatusText = "Checking...";

        var status = await _foundryManager.CheckConnectionAsync();
        ConnectionStatus = status;

        // Pull the active model name so the status line shows which LLM is
        // actually serving requests (helpful when the user has multiple models
        // or when the app falls back to a backup).
        var modelName = _settingsService.Load().ModelName;

        ConnectionStatusText = status switch
        {
            ConnectionStatus.Connected => $"Connected · {modelName}",
            ConnectionStatus.ServiceNotRunning => "Not connected to local LLM (service not running)",
            ConnectionStatus.ModelNotLoaded => "Not connected to local LLM (model not loaded)",
            ConnectionStatus.NotInstalled => "Not connected to local LLM (Foundry not installed)",
            ConnectionStatus.Error => "Local LLM not responding",
            _ => "Checking local LLM..."
        };

        // If the check confirms we're connected but an earlier request had set
        // a "cannot connect" error, reconcile the two — the connection is
        // actually fine, so downgrade the banner to a transient retry prompt
        // instead of showing a contradictory "Cannot connect" message.
        if (status == ConnectionStatus.Connected &&
            ErrorMessage == "Cannot connect to local LLM.")
        {
            ErrorMessage = "That request failed. Try again.";
        }
    }

    [RelayCommand]
    private async Task RetryConnectionAsync() => await CheckConnectionAsync();

    [RelayCommand]
    private async Task RetryAsync()
    {
        ShowRetry = false;
        ErrorMessage = null;
        await ProcessAsync();
    }

    private async Task FallbackToCpuModelAndRetryAsync()
    {
        ConnectionStatusText = "Switching to backup model...";
        ConnectionStatus = ConnectionStatus.Unknown;

        try
        {
            // Stop the service, restart, and load phi-4-mini (CPU, always reliable)
            var success = await _foundryManager.FallbackToCpuModelAsync();
            if (success)
            {
                _consecutiveTimeouts = 0;
                ConnectionStatus = ConnectionStatus.Connected;
                // Settings were just rewritten to the fallback model name.
                var backupName = _settingsService.Load().ModelName;
                ConnectionStatusText = $"Connected · {backupName} (backup)";
                ErrorMessage = null;
                await ProcessAsync();
            }
            else
            {
                ErrorMessage = "Could not switch models. Try restarting Foundry Local manually.";
                ShowRetry = true;
                _ = CheckConnectionAsync();
            }
        }
        catch
        {
            ErrorMessage = "Model switch failed.";
            ShowRetry = true;
        }
    }

    private async Task AutoReconnectAndRetryAsync()
    {
        ConnectionStatusText = "Restarting Foundry Local...";
        ConnectionStatus = ConnectionStatus.Unknown;

        var success = await _foundryManager.ForceReconnectAsync();
        if (success)
        {
            ConnectionStatus = ConnectionStatus.Connected;
            ConnectionStatusText = $"Connected · {_settingsService.Load().ModelName}";
            ErrorMessage = null;
            // Auto-retry
            await ProcessAsync();
        }
        else
        {
            ErrorMessage = "Auto-reconnect failed.";
            ShowRetry = true;
            _ = CheckConnectionAsync();
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        ShowRetry = false;
        ErrorMessage = null;
        IsLoading = true;
        ConnectionStatusText = "Restarting Foundry Local...";
        ConnectionStatus = ConnectionStatus.Unknown;

        try
        {
            var success = await _foundryManager.ForceReconnectAsync();
            if (success)
            {
                _consecutiveTimeouts = 0;
                ConnectionStatus = ConnectionStatus.Connected;
                ConnectionStatusText = $"Connected · {_settingsService.Load().ModelName}";
                ErrorMessage = null;
                IsLoading = false;
                // Auto-retry the last command
                await ProcessAsync();
            }
            else
            {
                ErrorMessage = "Could not reconnect. Try restarting Foundry Local manually.";
                ShowRetry = true;
                _ = CheckConnectionAsync();
                IsLoading = false;
            }
        }
        catch
        {
            ErrorMessage = "Reconnection failed.";
            ShowRetry = true;
            IsLoading = false;
        }
    }

    private void UpdateReadability(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ReadabilityScore = 0;
            ReadabilityLabel = "";
            WordCount = 0;
            CharCount = 0;
            IsWordMatched = false;
            return;
        }

        // Word count: whitespace-delimited tokens, ignoring empty entries from
        // consecutive spaces. Char count: raw length including whitespace
        // (matches the Word / Google Docs default — users can mentally
        // subtract spaces if they want the no-spaces version).
        WordCount = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
        CharCount = text.Length;

        ReadabilityScore = _readabilityService.CalculateFleschReadingEase(text);

        // Any text change invalidates a prior Word match — drop back to
        // the local estimate and re-render the label accordingly.
        IsWordMatched = false;
        RenderReadabilityLabel(fromWord: false);
        MatchWordCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Rebuilds <see cref="ReadabilityLabel"/> from the current stats. The
    /// score is prefixed with "~" when it's the local estimate AND Word is
    /// available (signaling "click Match Word for the precise value"). When
    /// the score came from Word, it's shown plainly with a "(Word)" suffix.
    /// </summary>
    private void RenderReadabilityLabel(bool fromWord)
    {
        string scoreText;
        if (fromWord)
        {
            scoreText = $"Readability {ReadabilityScore:F1} (Word)";
        }
        else
        {
            // Only mark as estimate when Word is a realistic alternative.
            // On machines without Word there's nothing to compare against,
            // so the tilde would just be noise.
            var prefix = IsWordMatchAvailable ? "~" : "";
            scoreText = $"Readability {prefix}{ReadabilityScore:F1}";
        }

        ReadabilityLabel = $"{WordCount:N0} {(WordCount == 1 ? "word" : "words")} · " +
                           $"{CharCount:N0} {(CharCount == 1 ? "char" : "chars")} · " +
                           scoreText;
    }

    [RelayCommand(CanExecute = nameof(CanMatchWord))]
    private async Task MatchWordAsync()
    {
        if (_wordReadabilityService == null || !IsWordMatchAvailable) return;
        if (string.IsNullOrWhiteSpace(WorkingText)) return;

        IsMatchingWord = true;
        MatchWordCommand.NotifyCanExecuteChanged();
        try
        {
            var score = await _wordReadabilityService.CalculateFleschAsync(WorkingText);
            if (score.HasValue)
            {
                ReadabilityScore = score.Value;
                IsWordMatched = true;
                RenderReadabilityLabel(fromWord: true);
            }
            // null result = Word failed; leave local estimate visible. The
            // user can click again, or edit to retry. No error surfaced —
            // this is an enhancement, not a critical path.
        }
        finally
        {
            IsMatchingWord = false;
            MatchWordCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanMatchWord() =>
        IsWordMatchAvailable && !IsMatchingWord && !IsWordMatched &&
        !string.IsNullOrWhiteSpace(WorkingText);

    public bool IsModeApplied(EditingMode mode) => _appliedModes.Contains(mode);

    public void SetCapturedText(string text)
    {
        WorkingText = text;
    }

    [RelayCommand]
    private async Task ProcessAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsLoading = true;
        ErrorMessage = null;
        ShowRetry = false;
        IsReviewing = false;

        try
        {
            _preRevisionText = WorkingText;

            var result = await _foundryClient.GetEditedTextAsync(
                _preRevisionText, SelectedMode,
                SelectedMode == EditingMode.CustomPrompt ? CustomPrompt : null,
                ct);

            if (ct.IsCancellationRequested) return;

            // Compute diff and group into review segments
            var diffs = _diffService.ComputeDiff(_preRevisionText, result);
            var segments = _diffService.GroupIntoSegments(diffs);

            ReviewSegments = new ObservableCollection<ReviewSegment>(segments);

            // Check if there are any actual changes
            // Always mark the mode as applied
            _appliedModes.Add(SelectedMode);
            AppliedModesChanged?.Invoke();

            bool hasChanges = segments.Any(s => s.IsChange);
            if (hasChanges)
            {
                _undoStack.Push(_preRevisionText);
                _consecutiveTimeouts = 0; // success — reset timeout counter
                CanUndo = true;
                IsReviewing = true;
                HasResult = true;

                RebuildWorkingText();
                ReviewSegmentsChanged?.Invoke();
            }
            else
            {
                ErrorMessage = "No changes suggested.";
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException ex)
        {
            var timedOut = ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
            if (timedOut)
            {
                _consecutiveTimeouts++;
                if (_consecutiveTimeouts >= 2)
                {
                    // Second timeout — fall back to a CPU model
                    ErrorMessage = "Model not responding. Switching to a backup model...";
                    _ = FallbackToCpuModelAndRetryAsync();
                }
                else
                {
                    // First timeout — try reconnecting with the same model
                    ErrorMessage = "Request timed out. Reconnecting...";
                    _ = AutoReconnectAndRetryAsync();
                }
            }
            else
            {
                // Reset the status so the stale "Connected" dot doesn't linger
                // while CheckConnectionAsync re-evaluates in the background —
                // avoids showing "Connected" and "Cannot connect" at the same time.
                ConnectionStatus = ConnectionStatus.Unknown;
                ConnectionStatusText = "Checking...";
                ErrorMessage = "Cannot connect to local LLM.";
                ShowRetry = true;
                _ = CheckConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            ShowRetry = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Quick Fix — apply every deterministic fix the live checker found, as
    /// a single reviewable diff. Mirrors the accept/reject UX used by Fix
    /// Grammar so the user reviews before committing, rather than seeing
    /// changes applied silently.
    ///
    /// Violations without a deterministic fix (ALL CAPS, absolute claims,
    /// spelling, etc.) stay in the panel — the hint text directs the user
    /// to the Fix Grammar button for those.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyQuickFixes))]
    private void ApplyQuickFixes()
    {
        var fixed1 = _codeChecker.ApplyAutoFixes(WorkingText);
        if (fixed1 == WorkingText) return; // nothing to do

        _preRevisionText = WorkingText;
        var diffs = _diffService.ComputeDiff(_preRevisionText, fixed1);
        var segments = _diffService.GroupIntoSegments(diffs);
        ReviewSegments = new ObservableCollection<ReviewSegment>(segments);

        if (segments.Any(s => s.IsChange))
        {
            _undoStack.Push(_preRevisionText);
            CanUndo = true;
            IsReviewing = true;
            HasResult = true;
            RebuildWorkingText();
            ReviewSegmentsChanged?.Invoke();
        }
    }

    /// <summary>
    /// True when at least one auto-fixable violation is currently in the
    /// panel. Drives Quick Fix button enable state so users don't see a
    /// grayed-out button on text with only judgment-required violations.
    /// </summary>
    public bool HasAutoFixableViolations => Violations.Any(v => v.CanAutoFix);

    private bool CanApplyQuickFixes() => HasAutoFixableViolations && !IsLoading;

    public void ToggleChange(int segmentId)
    {
        var segment = ReviewSegments.FirstOrDefault(s => s.Id == segmentId && s.IsChange);
        if (segment == null) return;

        segment.Toggle();
        RebuildWorkingText();
        ReviewSegmentsChanged?.Invoke();
    }

    public void SetChangeStates(IEnumerable<int> segmentIds, ChangeState state)
    {
        foreach (var id in segmentIds)
        {
            var seg = ReviewSegments.FirstOrDefault(s => s.Id == id && s.IsChange);
            if (seg != null) seg.State = state;
        }
        RebuildWorkingText();
        ReviewSegmentsChanged?.Invoke();
    }

    [RelayCommand]
    private void AcceptAllChanges()
    {
        foreach (var seg in ReviewSegments.Where(s => s.IsChange))
            seg.State = ChangeState.Accepted;
        RebuildWorkingText();
        ReviewSegmentsChanged?.Invoke();
    }

    [RelayCommand]
    private void RejectAllChanges()
    {
        foreach (var seg in ReviewSegments.Where(s => s.IsChange))
            seg.State = ChangeState.Rejected;
        RebuildWorkingText();
        ReviewSegmentsChanged?.Invoke();
    }

    [RelayCommand]
    private void ApplyReview()
    {
        // Pending segments resolve to AddedText, so WorkingText already reflects
        // the fully-revised output. Just tear down the review UI.
        IsReviewing = false;
        ReviewSegments = [];
    }

    private void RebuildWorkingText()
    {
        WorkingText = string.Concat(ReviewSegments.Select(s => s.ResolvedText));
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count > 0)
        {
            WorkingText = _undoStack.Pop();
            CanUndo = _undoStack.Count > 0;
            IsReviewing = false;
            HasResult = _undoStack.Count > 0;
            ReviewSegments = [];
        }
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        // If still reviewing, finalize first
        if (IsReviewing)
            ApplyReview();

        var textToPaste = WorkingText;
        RequestClose?.Invoke();
        await Task.Delay(100);
        await _clipboardService.PasteTextAsync(textToPaste, PreviousForegroundWindow);
    }

    [RelayCommand]
    private void Dismiss()
    {
        _cts?.Cancel();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void CopyResult()
    {
        if (!string.IsNullOrEmpty(WorkingText))
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try { System.Windows.Clipboard.SetText(WorkingText); } catch { }
            });
        }
    }
}
