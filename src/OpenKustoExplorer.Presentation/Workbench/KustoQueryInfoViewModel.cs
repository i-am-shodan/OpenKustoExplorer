using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents detailed information about the most recently attempted query execution.
/// </summary>
public sealed class KustoQueryInfoViewModel : ObservableObject
{
    private string completedText = "Not completed";
    private string durationText = "-";
    private string executedQueryText = string.Empty;
    private string resultShapeText = "No result";
    private string startedText = "Not run";
    private string statusText = "No query has run";
    private string targetText = "No target";
    private string visualizationText = "Table";

    /// <summary>
    /// Gets the executed independent KQL block.
    /// </summary>
    public string ExecutedQueryText
    {
        get => executedQueryText;
        private set => SetProperty(ref executedQueryText, value);
    }

    /// <summary>
    /// Gets the cluster/database target.
    /// </summary>
    public string TargetText
    {
        get => targetText;
        private set => SetProperty(ref targetText, value);
    }

    /// <summary>
    /// Gets the local execution start timestamp.
    /// </summary>
    public string StartedText
    {
        get => startedText;
        private set => SetProperty(ref startedText, value);
    }

    /// <summary>
    /// Gets the local execution completion timestamp.
    /// </summary>
    public string CompletedText
    {
        get => completedText;
        private set => SetProperty(ref completedText, value);
    }

    /// <summary>
    /// Gets the measured authentication and execution duration.
    /// </summary>
    public string DurationText
    {
        get => durationText;
        private set => SetProperty(ref durationText, value);
    }

    /// <summary>
    /// Gets the materialized table, row, and column counts.
    /// </summary>
    public string ResultShapeText
    {
        get => resultShapeText;
        private set => SetProperty(ref resultShapeText, value);
    }

    /// <summary>
    /// Gets the requested visualization.
    /// </summary>
    public string VisualizationText
    {
        get => visualizationText;
        private set => SetProperty(ref visualizationText, value);
    }

    /// <summary>
    /// Gets the final execution state.
    /// </summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>
    /// Copies a complete query-information snapshot from another tab.
    /// </summary>
    /// <param name="source">The source query information.</param>
    internal void CopyFrom(KustoQueryInfoViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ExecutedQueryText = source.ExecutedQueryText;
        TargetText = source.TargetText;
        StartedText = source.StartedText;
        CompletedText = source.CompletedText;
        DurationText = source.DurationText;
        ResultShapeText = source.ResultShapeText;
        VisualizationText = source.VisualizationText;
        StatusText = source.StatusText;
    }

    /// <summary>
    /// Clears previous query information while a new execution is prepared.
    /// </summary>
    internal void ResetForRun()
    {
        ExecutedQueryText = string.Empty;
        TargetText = "Preparing target";
        StartedText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        CompletedText = "Running";
        DurationText = "Running";
        ResultShapeText = "Waiting for result";
        VisualizationText = "Pending";
        StatusText = "Preparing";
    }

    /// <summary>
    /// Records a newly started query execution.
    /// </summary>
    /// <param name="queryText">The independent KQL block.</param>
    /// <param name="clusterUri">The target cluster.</param>
    /// <param name="databaseName">The target database.</param>
    /// <param name="started">The local start timestamp.</param>
    internal void Begin(
        string queryText,
        Uri clusterUri,
        string databaseName,
        DateTimeOffset started)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        ExecutedQueryText = queryText;
        TargetText = $"{clusterUri.Host} / {databaseName}";
        StartedText = started.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        CompletedText = "Running";
        DurationText = "Running";
        ResultShapeText = "Waiting for result";
        VisualizationText = "Pending";
        StatusText = "Running";
    }

    /// <summary>
    /// Records a successful query execution.
    /// </summary>
    /// <param name="result">The completed query result.</param>
    /// <param name="visualization">The effective visualization instructions.</param>
    /// <param name="completed">The local completion timestamp.</param>
    internal void Complete(
        KustoQueryResult result,
        KustoVisualization? visualization,
        DateTimeOffset completed)
    {
        ArgumentNullException.ThrowIfNull(result);

        int rowCount = result.Tables.Sum(table => table.Rows.Count);
        int columnCount = result.Tables.Count > 0 ? result.Tables[0].Columns.Count : 0;
        CompletedText = completed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        DurationText = $"{result.Duration.TotalMilliseconds:N0} ms";
        ResultShapeText = $"{result.Tables.Count:N0} tables · {rowCount:N0} rows · {columnCount:N0} primary columns";
        VisualizationText = visualization?.Kind.ToString() ?? "Table";
        StatusText = "Completed";
    }

    /// <summary>
    /// Records a successful streamed graph query and durable import.
    /// </summary>
    /// <param name="result">The completed graph export and import result.</param>
    /// <param name="completed">The local completion timestamp.</param>
    internal void CompleteGraph(KustoGraphIngestionResult result, DateTimeOffset completed)
    {
        ArgumentNullException.ThrowIfNull(result);

        CompletedText = completed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        DurationText = $"{result.Export.Duration.TotalMilliseconds:N0} ms";
        ResultShapeText = $"{result.Export.NodeCount:N0} nodes · {result.Export.EdgeCount:N0} edges · {result.Import.EvidenceCount:N0} evidence rows";
        VisualizationText = "Graph";
        StatusText = "Completed";
    }

    /// <summary>
    /// Records a canceled or failed query execution.
    /// </summary>
    /// <param name="status">The concise final state.</param>
    /// <param name="completed">The local completion timestamp.</param>
    internal void FinishWithStatus(string status, DateTimeOffset completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        CompletedText = completed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        DurationText = "-";
        ResultShapeText = "No materialized result";
        VisualizationText = "None";
        StatusText = status;
    }
}
