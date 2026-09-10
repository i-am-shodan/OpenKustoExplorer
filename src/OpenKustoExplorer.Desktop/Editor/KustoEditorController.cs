using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop.Appearance;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Desktop.Editor;

/// <summary>
/// Coordinates debounced KQL analysis, syntax coloring, diagnostics, and context-aware completion requests.
/// </summary>
internal sealed class KustoEditorController : IDisposable
{
    private static readonly TimeSpan AnalysisDelay = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan CompletionDelay = TimeSpan.FromMilliseconds(120);
    private static readonly DataFormat<string> HtmlFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> WindowsHtmlFormat = DataFormat.CreateStringPlatformFormat("HTML Format");
    private readonly KustoExecutionErrorColorizer executionErrorColorizer;
    private readonly AppearanceSettings appearanceSettings;
    private readonly KustoSyntaxColorizer colorizer;
    private readonly TextEditor editor;
    private readonly MainWindowViewModel viewModel;
    private CancellationTokenSource? analysisCancellationSource;
    private CancellationTokenSource? completionCancellationSource;
    private CancellationTokenSource? explicitHelpCancellationSource;
    private CancellationTokenSource? hoverHelpCancellationSource;
    private CompletionWindow? completionWindow;
    private KustoSyntaxHelp? cachedHelp;
    private KustoSyntaxHelp? displayedExplicitHelp;
    private KustoSyntaxHelp? displayedHoverHelp;
    private KustoSyntaxHelpWindow? helpWindow;
    private IReadOnlyList<KustoClassification> classifications = Array.Empty<KustoClassification>();
    private Guid cachedHelpDocumentId;
    private int? explicitHelpRequestPosition;
    private int? pendingHoverPosition;
    private string? cachedHelpText;
    private string? displayedExplicitHelpText;
    private bool isDisposed;
    private bool isSynchronizingDocument;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoEditorController"/> class.
    /// </summary>
    /// <param name="editor">The native AvaloniaEdit query editor.</param>
    /// <param name="viewModel">The workbench presentation model.</param>
    /// <param name="appearanceSettings">The persisted desktop appearance and editor settings.</param>
    public KustoEditorController(
        TextEditor editor,
        MainWindowViewModel viewModel,
        AppearanceSettings appearanceSettings)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(appearanceSettings);

        this.editor = editor;
        this.viewModel = viewModel;
        this.appearanceSettings = appearanceSettings;
        editor.TextArea.TextView.Margin = new Thickness(5, 0, 0, 0);
        colorizer = new KustoSyntaxColorizer(ResolveBrush);
        executionErrorColorizer = new KustoExecutionErrorColorizer(ResolveBrush);
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        editor.TextArea.TextView.LineTransformers.Add(executionErrorColorizer);
        editor.TextChanged += OnTextChanged;
        editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        editor.TextArea.KeyDown += OnKeyDown;
        editor.TextArea.KeyUp += OnKeyUp;
        editor.TextArea.TextView.ScrollOffsetChanged += OnTextViewScrollOffsetChanged;
        editor.TextArea.TextView.PointerExited += OnPointerExited;
        editor.TextArea.TextView.PointerMoved += OnPointerMoved;
        editor.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        appearanceSettings.PropertyChanged += OnAppearanceSettingsPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SynchronizeEditorFromViewModel();
        QueueAnalysis(TimeSpan.Zero);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            editor.TextChanged -= OnTextChanged;
            editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            editor.TextArea.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKeyDown);
            editor.TextArea.KeyDown -= OnKeyDown;
            editor.TextArea.KeyUp -= OnKeyUp;
            editor.TextArea.TextView.ScrollOffsetChanged -= OnTextViewScrollOffsetChanged;
            editor.TextArea.TextView.PointerExited -= OnPointerExited;
            editor.TextArea.TextView.PointerMoved -= OnPointerMoved;
            editor.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
            appearanceSettings.PropertyChanged -= OnAppearanceSettingsPropertyChanged;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            editor.TextArea.TextView.LineTransformers.Remove(colorizer);
            editor.TextArea.TextView.LineTransformers.Remove(executionErrorColorizer);
            completionWindow?.Hide();
            DismissHoverHelp();
            if (helpWindow is not null)
            {
                helpWindow.RefreshRequested -= OnHelpWindowRefreshRequested;
                helpWindow.ClosePermanently();
            }

            CancelPendingAnalysis();
            CancelPendingCompletion();
            CancelPendingExplicitHelp();
            CancelPendingHoverHelp();
        }
    }

    /// <summary>
    /// Copies the active selection as plain text and syntax-colored HTML.
    /// </summary>
    /// <returns>A task that completes after the clipboard is updated.</returns>
    internal async Task CopySelectionWithFormattingAsync()
    {
        if (editor.SelectionLength <= 0)
        {
            return;
        }

        string documentText = editor.Text ?? string.Empty;
        int selectionStart = editor.SelectionStart;
        int selectionLength = editor.SelectionLength;
        KustoRichClipboardContent content = KustoRichClipboardFormatter.Create(
            documentText,
            selectionStart,
            selectionLength,
            classifications,
            ResolveClassificationCssColor,
            ResolveCssColor("TextPrimaryBrush", "#18242D"),
            ResolveCssColor("SurfaceBrush", "#F7F9FA"));
        IClipboard? clipboard = TopLevel.GetTopLevel(editor)?.Clipboard;

        if (clipboard is null)
        {
            return;
        }

        try
        {
            DataTransferItem item = new();
            item.SetText(content.PlainText);
            item.Set(HtmlFormat, content.Html);

            if (OperatingSystem.IsWindows())
            {
                item.Set(WindowsHtmlFormat, content.WindowsHtml);
            }

            DataTransfer transfer = new();
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        {
            DataTransfer fallback = new();
            fallback.Add(DataTransferItem.CreateText(content.PlainText));
            await clipboard.SetDataAsync(fallback);
            await clipboard.FlushAsync();
        }
    }

    private void OnTextChanged(object? sender, EventArgs eventArguments)
    {
        if (!isSynchronizingDocument)
        {
            CancelPendingCompletion();
            bool wasResolvingExplicitHelp = explicitHelpCancellationSource is not null
                && displayedExplicitHelp is null;
            CancelPendingExplicitHelp();
            if (wasResolvingExplicitHelp)
            {
                DismissExplicitHelp();
            }

            CancelPendingHoverHelp();
            DismissHoverHelp();
            InvalidateHelpCache();
            viewModel.QueryText = editor.Text ?? string.Empty;
            QueueAnalysis(AnalysisDelay);
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs eventArguments)
    {
        if (!isSynchronizingDocument)
        {
            viewModel.CaretPosition = editor.TextArea.Caret.Offset;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs eventArguments)
    {
        editor.TextArea.TextView.Redraw();
    }

    private void OnAppearanceSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArguments)
    {
        _ = sender;
        if (eventArguments.PropertyName == nameof(AppearanceSettings.ShowKqlHoverHelp)
            && !appearanceSettings.ShowKqlHoverHelp)
        {
            CancelPendingHoverHelp();
            DismissHoverHelp();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArguments)
    {
        bool queryTextChanged = eventArguments.PropertyName == nameof(MainWindowViewModel.QueryText);
        bool caretPositionChanged = eventArguments.PropertyName == nameof(MainWindowViewModel.CaretPosition);
        bool activeSchemaChanged = eventArguments.PropertyName == nameof(MainWindowViewModel.ActiveSchemaRevision);
        bool queryErrorHighlightChanged = eventArguments.PropertyName == nameof(MainWindowViewModel.QueryErrorHighlight);
        bool selectedDocumentChanged = eventArguments.PropertyName == nameof(MainWindowViewModel.SelectedDocument);
        bool editorNeedsUpdate = !string.Equals(editor.Text, viewModel.QueryText, StringComparison.Ordinal);

        if (selectedDocumentChanged)
        {
            CancelPendingCompletion();
            CancelPendingHoverHelp();
            completionWindow?.Hide();
            DismissHoverHelp();
            DismissExplicitHelp();
            InvalidateHelpCache();
            classifications = Array.Empty<KustoClassification>();
            colorizer.Update(Array.Empty<KustoClassification>());
            executionErrorColorizer.Update(viewModel.QueryErrorHighlight);
            SynchronizeEditorFromViewModel();
            editor.TextArea.TextView.Redraw();
            QueueAnalysis(TimeSpan.Zero);
        }
        else if (queryTextChanged && editorNeedsUpdate)
        {
            ApplyUndoableEditorTextFromViewModel();
            QueueAnalysis(TimeSpan.Zero);
        }
        else if (caretPositionChanged)
        {
            SynchronizeCaretFromViewModel();
        }

        if (queryErrorHighlightChanged)
        {
            executionErrorColorizer.Update(viewModel.QueryErrorHighlight);
            editor.TextArea.TextView.Redraw();
        }

        if (activeSchemaChanged)
        {
            CancelPendingHoverHelp();
            DismissHoverHelp();
            DismissExplicitHelp();
            InvalidateHelpCache();
            QueueAnalysis(TimeSpan.Zero);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        bool handled = TryHandleExecutionShortcut(eventArguments)
            || TryInsertPipe(eventArguments);
        bool requestsCompletion = !handled
            && eventArguments.Key == Key.Space
            && eventArguments.KeyModifiers == KeyModifiers.Control;

        if (requestsCompletion)
        {
            QueueCompletion(TimeSpan.Zero);
            handled = true;
        }

        eventArguments.Handled = handled;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        CancelPendingHoverHelp();
        DismissHoverHelp();

        if (TryHandleSyntaxHelpShortcut(eventArguments))
        {
            eventArguments.Handled = true;
            return;
        }

        bool hasCopyModifier = eventArguments.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta;
        bool requestsCopy = hasCopyModifier && eventArguments.Key == Key.C;

        if (requestsCopy && editor.SelectionLength > 0)
        {
            eventArguments.Handled = true;
            _ = CopySelectionWithFormattingAsync();
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        CancelPendingHoverHelp();
        DismissHoverHelp();
    }

    private void OnTextViewScrollOffsetChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        CancelPendingHoverHelp();
        DismissHoverHelp();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArguments)
    {
        _ = sender;
        if (!appearanceSettings.ShowKqlHoverHelp)
        {
            return;
        }

        PointerPointProperties properties = eventArguments
            .GetCurrentPoint(editor.TextArea.TextView)
            .Properties;
        if (properties.IsLeftButtonPressed
            || properties.IsMiddleButtonPressed
            || properties.IsRightButtonPressed)
        {
            CancelPendingHoverHelp();
            DismissHoverHelp();
            return;
        }

        TextViewPosition? textPosition = editor.TextArea.TextView.GetPosition(
            eventArguments.GetPosition(editor.TextArea.TextView));
        if (textPosition is null)
        {
            CancelPendingHoverHelp();
            DismissHoverHelp();
            return;
        }

        int position = editor.Document.GetOffset(textPosition.Value.Location);
        if (displayedHoverHelp is not null
            && KustoSyntaxHelpInteraction.ContainsPosition(displayedHoverHelp, position, includeEnd: false))
        {
            return;
        }

        if (pendingHoverPosition == position)
        {
            return;
        }

        DismissHoverHelp();
        QueueHoverHelp(position);
    }

    private bool TryHandleExecutionShortcut(KeyEventArgs eventArguments)
    {
        bool handled = false;
        bool requestsRun = eventArguments.Key == Key.F5
            && eventArguments.KeyModifiers == KeyModifiers.None;
        requestsRun |= eventArguments.Key == Key.Enter
            && eventArguments.KeyModifiers == KeyModifiers.Shift;
        bool requestsCancellation = eventArguments.Key == Key.F5
            && eventArguments.KeyModifiers == KeyModifiers.Shift;
        requestsCancellation |= eventArguments.Key == Key.Escape
            && eventArguments.KeyModifiers == KeyModifiers.None
            && viewModel.IsRunningQuery;

        if (requestsRun)
        {
            viewModel.RunQueryCommand.Execute(null);
            handled = true;
        }
        else if (requestsCancellation)
        {
            viewModel.CancelQueryCommand.Execute(null);
            handled = true;
        }

        return handled;
    }

    private bool TryInsertPipe(KeyEventArgs eventArguments)
    {
        bool handled = eventArguments.Key == Key.Enter
            && eventArguments.KeyModifiers == KeyModifiers.Control;

        if (handled)
        {
            int offset = editor.TextArea.Caret.Offset;
            string newLine = TextUtilities.GetNewLineFromDocument(editor.Document, offset);
            string insertion = $"{newLine}| ";
            editor.Document.Insert(offset, insertion);
            editor.TextArea.Caret.Offset = offset + insertion.Length;
            editor.TextArea.ClearSelection();
        }

        return handled;
    }

    private void OnKeyUp(object? sender, KeyEventArgs eventArguments)
    {
        if (completionWindow is null && ShouldRequestAutomaticCompletion(eventArguments))
        {
            QueueCompletion(CompletionDelay);
        }
    }

    private void QueueAnalysis(TimeSpan delay)
    {
        CancelPendingAnalysis();
        analysisCancellationSource = new CancellationTokenSource();
        Guid documentId = viewModel.SelectedDocument?.Id ?? Guid.Empty;
        string textSnapshot = editor.Text ?? string.Empty;
        int caretPosition = Math.Min(editor.TextArea.Caret.Offset, textSnapshot.Length);
        _ = AnalyzeAfterDelayAsync(
            delay,
            documentId,
            textSnapshot,
            caretPosition,
            analysisCancellationSource.Token);
    }

    private void QueueCompletion(TimeSpan delay)
    {
        CancelPendingCompletion();
        completionCancellationSource = new CancellationTokenSource();
        Guid documentId = viewModel.SelectedDocument?.Id ?? Guid.Empty;
        string textSnapshot = editor.Text ?? string.Empty;
        int caretPosition = Math.Min(editor.TextArea.Caret.Offset, textSnapshot.Length);
        _ = ShowCompletionAfterDelayAsync(
            delay,
            documentId,
            textSnapshot,
            caretPosition,
            completionCancellationSource.Token);
    }

    private void QueueHoverHelp(int position)
    {
        CancelPendingHoverHelp();
        hoverHelpCancellationSource = new CancellationTokenSource();
        pendingHoverPosition = position;
        Guid documentId = viewModel.SelectedDocument?.Id ?? Guid.Empty;
        string textSnapshot = editor.Text ?? string.Empty;
        _ = ShowHoverHelpAfterDelayAsync(
            documentId,
            textSnapshot,
            position,
            hoverHelpCancellationSource.Token);
    }

    private void SynchronizeCaretFromViewModel()
    {
        int caretPosition = Math.Min(viewModel.CaretPosition, editor.Document.TextLength);
        if (editor.TextArea.Caret.Offset != caretPosition)
        {
            isSynchronizingDocument = true;

            try
            {
                editor.TextArea.Caret.Offset = caretPosition;
            }
            finally
            {
                isSynchronizingDocument = false;
            }
        }
    }

    private void SynchronizeEditorFromViewModel()
    {
        isSynchronizingDocument = true;

        try
        {
            editor.Text = viewModel.QueryText;
            editor.TextArea.Caret.Offset = Math.Min(viewModel.CaretPosition, editor.Document.TextLength);
        }
        finally
        {
            isSynchronizingDocument = false;
        }
    }

    private void ApplyUndoableEditorTextFromViewModel()
    {
        isSynchronizingDocument = true;

        try
        {
            KustoEditorDocumentSynchronizer.ApplyUndoableText(editor.Document, viewModel.QueryText);
            editor.TextArea.Caret.Offset = Math.Min(viewModel.CaretPosition, editor.Document.TextLength);
        }
        finally
        {
            isSynchronizingDocument = false;
        }
    }

    private void CancelPendingAnalysis()
    {
        analysisCancellationSource?.Cancel();
        analysisCancellationSource?.Dispose();
        analysisCancellationSource = null;
    }

    private void CancelPendingCompletion()
    {
        completionCancellationSource?.Cancel();
        completionCancellationSource?.Dispose();
        completionCancellationSource = null;
    }

    private void CancelPendingExplicitHelp()
    {
        explicitHelpCancellationSource?.Cancel();
        explicitHelpCancellationSource?.Dispose();
        explicitHelpCancellationSource = null;
    }

    private void CancelPendingHoverHelp()
    {
        hoverHelpCancellationSource?.Cancel();
        hoverHelpCancellationSource?.Dispose();
        hoverHelpCancellationSource = null;
        pendingHoverPosition = null;
    }

    private async Task AnalyzeAfterDelayAsync(
        TimeSpan delay,
        Guid documentId,
        string textSnapshot,
        int caretPosition,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            KustoLanguageAnalysis analysis = await Task.Run(
                () => viewModel.Analyze(textSnapshot, caretPosition, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(
                () => ApplyAnalysis(documentId, textSnapshot, caretPosition, analysis),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer document snapshot superseded the pending analysis.
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => viewModel.ReportAnalysisFailure(exception.Message),
                DispatcherPriority.Background,
                CancellationToken.None);
        }
    }

    private void ApplyAnalysis(
        Guid documentId,
        string textSnapshot,
        int caretPosition,
        KustoLanguageAnalysis analysis)
    {
        bool isCurrentDocument = string.Equals(editor.Text, textSnapshot, StringComparison.Ordinal);
        isCurrentDocument &= viewModel.SelectedDocument?.Id == documentId;

        if (isCurrentDocument && !isDisposed)
        {
            classifications = analysis.Classifications;
            colorizer.Update(analysis.Classifications);
            editor.TextArea.TextView.Redraw();
            viewModel.ApplyAnalysis(analysis);

            if (analysis.SyntaxHelp is not null
                && KustoSyntaxHelpInteraction.ContainsPosition(
                    analysis.SyntaxHelp,
                    caretPosition,
                    includeEnd: true))
            {
                CacheHelp(documentId, textSnapshot, analysis.SyntaxHelp);
            }
        }
    }

    private async Task ShowCompletionAfterDelayAsync(
        TimeSpan delay,
        Guid documentId,
        string textSnapshot,
        int caretPosition,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            KustoLanguageAnalysis analysis = await Task.Run(
                () => viewModel.Analyze(textSnapshot, caretPosition, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(
                () => ShowCompletionWindow(documentId, textSnapshot, analysis),
                DispatcherPriority.Input,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke or document superseded the pending completion request.
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => viewModel.ReportAnalysisFailure(exception.Message),
                DispatcherPriority.Background,
                CancellationToken.None);
        }
    }

    private async Task ShowHoverHelpAfterDelayAsync(
        Guid documentId,
        string textSnapshot,
        int position,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(KustoSyntaxHelpInteraction.HoverDelay, cancellationToken).ConfigureAwait(false);
            KustoSyntaxHelp? help = await Task.Run(
                () => viewModel.GetSyntaxHelp(textSnapshot, position, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(
                () => ShowHoverHelp(documentId, textSnapshot, position, help),
                DispatcherPriority.Input,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Pointer movement or a newer document superseded the pending help request.
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => viewModel.ReportAnalysisFailure(exception.Message),
                DispatcherPriority.Background,
                CancellationToken.None);
        }
    }

    private async Task ShowExplicitHelpAsync(
        Guid documentId,
        string textSnapshot,
        int position,
        CancellationTokenSource requestSource)
    {
        CancellationToken cancellationToken = requestSource.Token;
        try
        {
            KustoSyntaxHelp? help = await Task.Run(
                () => viewModel.GetSyntaxHelp(textSnapshot, position, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(
                () => CompleteExplicitHelp(documentId, textSnapshot, position, help, requestSource),
                DispatcherPriority.Input,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A repeated request or a newer document superseded this lookup.
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    CompleteExplicitHelp(documentId, textSnapshot, position, null, requestSource);
                    viewModel.ReportAnalysisFailure(exception.Message);
                },
                DispatcherPriority.Background,
                CancellationToken.None);
        }
    }

    private void ShowHoverHelp(
        Guid documentId,
        string textSnapshot,
        int requestedPosition,
        KustoSyntaxHelp? help)
    {
        pendingHoverPosition = null;
        bool canShow = !isDisposed
            && appearanceSettings.ShowKqlHoverHelp
            && help is not null
            && KustoSyntaxHelpInteraction.ContainsPosition(help, requestedPosition, includeEnd: true)
            && IsCurrentDocument(documentId, textSnapshot);
        if (!canShow)
        {
            return;
        }

        displayedHoverHelp = help;
        CacheHelp(documentId, textSnapshot, help!);
        ToolTip.SetTip(editor, new KustoSyntaxHelpContent(help!, isCompact: true));
        ToolTip.SetPlacement(editor, PlacementMode.Pointer);
        ToolTip.SetIsOpen(editor, true);
    }

    private void CompleteExplicitHelp(
        Guid documentId,
        string textSnapshot,
        int requestedPosition,
        KustoSyntaxHelp? help,
        CancellationTokenSource requestSource)
    {
        if (!ReferenceEquals(explicitHelpCancellationSource, requestSource))
        {
            return;
        }

        requestSource.Dispose();
        explicitHelpCancellationSource = null;
        if (isDisposed || !IsCurrentDocument(documentId, textSnapshot))
        {
            return;
        }

        if (help is not null)
        {
            CacheHelp(documentId, textSnapshot, help);
        }

        bool canUpdateWindow = helpWindow?.IsVisible == true
            && explicitHelpRequestPosition == requestedPosition
            && string.Equals(displayedExplicitHelpText, textSnapshot, StringComparison.Ordinal);
        if (!canUpdateWindow || TopLevel.GetTopLevel(editor) is not Window owner)
        {
            return;
        }

        if (help is null)
        {
            helpWindow!.ShowUnavailable(owner);
        }
        else
        {
            helpWindow!.ShowHelp(help, owner);
        }

        displayedExplicitHelp = help;
    }

    private void DismissHoverHelp()
    {
        displayedHoverHelp = null;
        ToolTip.SetIsOpen(editor, false);
        ToolTip.SetTip(editor, null);
    }

    private bool TryHandleSyntaxHelpShortcut(KeyEventArgs eventArguments)
    {
        if (KustoSyntaxHelpInteraction.IsDismissKey(
            eventArguments.Key,
            eventArguments.KeyModifiers)
            && (helpWindow?.IsVisible == true || explicitHelpCancellationSource is not null))
        {
            DismissExplicitHelp();
            return true;
        }

        if (!KustoSyntaxHelpInteraction.IsRequestKey(
            eventArguments.Key,
            eventArguments.KeyModifiers))
        {
            return false;
        }

        completionWindow?.Hide();
        CancelPendingHoverHelp();
        DismissHoverHelp();
        RequestExplicitHelp();
        return true;
    }

    private void RequestExplicitHelp()
    {
        CancelPendingExplicitHelp();
        Guid documentId = viewModel.SelectedDocument?.Id ?? Guid.Empty;
        string textSnapshot = editor.Text ?? string.Empty;
        int position = Math.Min(editor.TextArea.Caret.Offset, textSnapshot.Length);

        if (cachedHelp is not null
            && cachedHelpDocumentId == documentId
            && string.Equals(cachedHelpText, textSnapshot, StringComparison.Ordinal)
            && KustoSyntaxHelpInteraction.ContainsPosition(cachedHelp, position, includeEnd: true))
        {
            ShowCachedExplicitHelp(documentId, textSnapshot, position, cachedHelp);
            return;
        }

        if (TopLevel.GetTopLevel(editor) is not Window owner)
        {
            return;
        }

        helpWindow ??= CreateHelpWindow();
        helpWindow.ShowLoading(owner);
        displayedExplicitHelp = null;
        displayedExplicitHelpText = textSnapshot;
        explicitHelpRequestPosition = position;

        explicitHelpCancellationSource = new CancellationTokenSource();
        _ = ShowExplicitHelpAsync(
            documentId,
            textSnapshot,
            position,
            explicitHelpCancellationSource);
    }

    private void ShowCachedExplicitHelp(
        Guid documentId,
        string textSnapshot,
        int position,
        KustoSyntaxHelp help)
    {
        if (TopLevel.GetTopLevel(editor) is not Window owner)
        {
            return;
        }

        helpWindow ??= CreateHelpWindow();
        helpWindow.ShowHelp(help, owner, reveal: true);
        displayedExplicitHelp = help;
        displayedExplicitHelpText = textSnapshot;
        explicitHelpRequestPosition = position;
        CacheHelp(documentId, textSnapshot, help);
    }

    private void DismissExplicitHelp()
    {
        CancelPendingExplicitHelp();
        helpWindow?.Dismiss();
        displayedExplicitHelp = null;
        displayedExplicitHelpText = null;
        explicitHelpRequestPosition = null;
    }

    private KustoSyntaxHelpWindow CreateHelpWindow()
    {
        KustoSyntaxHelpWindow window = new();
        window.RefreshRequested += OnHelpWindowRefreshRequested;
        return window;
    }

    private void OnHelpWindowRefreshRequested(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        RequestExplicitHelp();
    }

    private bool IsCurrentDocument(Guid documentId, string textSnapshot)
    {
        return string.Equals(editor.Text, textSnapshot, StringComparison.Ordinal)
            && viewModel.SelectedDocument?.Id == documentId;
    }

    private void CacheHelp(Guid documentId, string textSnapshot, KustoSyntaxHelp help)
    {
        cachedHelp = help;
        cachedHelpDocumentId = documentId;
        cachedHelpText = textSnapshot;
    }

    private void InvalidateHelpCache()
    {
        cachedHelp = null;
        cachedHelpDocumentId = Guid.Empty;
        cachedHelpText = null;
    }

    private void ShowCompletionWindow(
        Guid documentId,
        string textSnapshot,
        KustoLanguageAnalysis analysis)
    {
        bool canShow = !isDisposed
            && analysis.Completions.Count > 0
            && string.Equals(editor.Text, textSnapshot, StringComparison.Ordinal)
            && viewModel.SelectedDocument?.Id == documentId;

        if (canShow)
        {
            completionWindow?.Hide();
            CompletionWindow newCompletionWindow = new(editor.TextArea)
            {
                StartOffset = analysis.CompletionEditStart,
                EndOffset = analysis.CompletionEditStart + analysis.CompletionEditLength,
                CloseWhenCaretAtBeginning = false,
                Width = 480,
                MaxWidth = 560,
                MaxHeight = 390,
                WindowManagerAddShadowHint = true,
            };
            newCompletionWindow.Classes.Add("kustoCompletionWindow");
            newCompletionWindow.CompletionList.Background = ResolveBrush("SurfaceBrush");
            newCompletionWindow.CompletionList.BorderBrush = ResolveBrush("ControlBoundaryBrush");
            newCompletionWindow.CompletionList.BorderThickness = new Thickness(1);
            newCompletionWindow.CompletionList.CornerRadius = new CornerRadius(6);
            newCompletionWindow.CompletionList.Padding = new Thickness(3);
            newCompletionWindow.CompletionList.MaxHeight = 390;
            newCompletionWindow.CompletionList.ListBox.Classes.Add("kustoCompletionList");
            newCompletionWindow.CompletionList.ListBox.MaxHeight = 380;
            ScrollViewer.SetHorizontalScrollBarVisibility(
                newCompletionWindow.CompletionList.ListBox,
                ScrollBarVisibility.Disabled);

            foreach (KustoCompletion completion in analysis.Completions)
            {
                newCompletionWindow.CompletionList.CompletionData.Add(new KustoCompletionData(completion));
            }

            newCompletionWindow.Closed += OnCompletionWindowClosed;
            completionWindow = newCompletionWindow;
            newCompletionWindow.Show();
        }
    }

    private void OnCompletionWindowClosed(object? sender, EventArgs eventArguments)
    {
        completionWindow = null;
    }

    private bool ShouldRequestAutomaticCompletion(KeyEventArgs eventArguments)
    {
        bool hasBlockedModifier = eventArguments.KeyModifiers.HasFlag(KeyModifiers.Control)
            || eventArguments.KeyModifiers.HasFlag(KeyModifiers.Alt)
            || eventArguments.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool isContextDelimiter = eventArguments.Key is Key.Space or Key.OemPeriod or Key.OemPipe or Key.OemComma;
        bool isIdentifierKey = eventArguments.Key is >= Key.A and <= Key.Z;
        bool hasIdentifierPrefix = isIdentifierKey && GetIdentifierPrefixLength() >= 2;

        return !hasBlockedModifier && (isContextDelimiter || hasIdentifierPrefix);
    }

    private int GetIdentifierPrefixLength()
    {
        string text = editor.Text ?? string.Empty;
        int position = Math.Min(editor.TextArea.Caret.Offset, text.Length);
        int start = position;

        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return position - start;
    }

    private IBrush? ResolveBrush(string resourceKey)
    {
        Avalonia.Application? application = Avalonia.Application.Current;
        IBrush? brush = null;

        if (editor.TryGetResource(resourceKey, editor.ActualThemeVariant, out object? editorResource))
        {
            brush = editorResource as IBrush;
        }
        else if (application is not null
            && application.TryGetResource(resourceKey, editor.ActualThemeVariant, out object? resource))
        {
            brush = resource as IBrush;
        }

        return brush;
    }

    private string? ResolveClassificationCssColor(string classificationKind)
    {
        string? resourceKey = KustoSyntaxColorizer.GetBrushResourceKey(classificationKind);
        return resourceKey is null ? null : ResolveCssColor(resourceKey, string.Empty);
    }

    private string ResolveCssColor(string resourceKey, string fallback)
    {
        return ResolveBrush(resourceKey) is SolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : fallback;
    }
}
