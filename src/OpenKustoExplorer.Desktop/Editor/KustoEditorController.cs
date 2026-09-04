using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using OpenKustoExplorer.Application.Language;
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
    private readonly KustoSyntaxColorizer colorizer;
    private readonly TextEditor editor;
    private readonly MainWindowViewModel viewModel;
    private CancellationTokenSource? analysisCancellationSource;
    private CancellationTokenSource? completionCancellationSource;
    private CompletionWindow? completionWindow;
    private IReadOnlyList<KustoClassification> classifications = Array.Empty<KustoClassification>();
    private bool isDisposed;
    private bool isSynchronizingDocument;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoEditorController"/> class.
    /// </summary>
    /// <param name="editor">The native AvaloniaEdit query editor.</param>
    /// <param name="viewModel">The workbench presentation model.</param>
    public KustoEditorController(TextEditor editor, MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(viewModel);

        this.editor = editor;
        this.viewModel = viewModel;
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
        editor.ActualThemeVariantChanged += OnActualThemeVariantChanged;
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
            editor.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            editor.TextArea.TextView.LineTransformers.Remove(colorizer);
            editor.TextArea.TextView.LineTransformers.Remove(executionErrorColorizer);
            completionWindow?.Hide();
            CancelPendingAnalysis();
            CancelPendingCompletion();
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
            completionWindow?.Hide();
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
        bool hasCopyModifier = eventArguments.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta;
        bool requestsCopy = hasCopyModifier && eventArguments.Key == Key.C;

        if (requestsCopy && editor.SelectionLength > 0)
        {
            eventArguments.Handled = true;
            _ = CopySelectionWithFormattingAsync();
        }
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
                () => ApplyAnalysis(documentId, textSnapshot, analysis),
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
            };

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
