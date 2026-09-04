namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one user or GitHub Copilot conversation message.
/// </summary>
public sealed class KustoCopilotMessageViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotMessageViewModel"/> class.
    /// </summary>
    /// <param name="author">The concise message author.</param>
    /// <param name="content">The complete message text.</param>
    /// <param name="isUser">Whether the message came from the user.</param>
    public KustoCopilotMessageViewModel(string author, string content, bool isUser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        Author = author;
        Content = content.Trim();
        IsUser = isUser;
    }

    /// <summary>
    /// Gets the concise message author.
    /// </summary>
    public string Author { get; }

    /// <summary>
    /// Gets the complete message text.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets a value indicating whether the message came from the user.
    /// </summary>
    public bool IsUser { get; }

    /// <summary>
    /// Gets the message surface color.
    /// </summary>
    public string BackgroundHex => IsUser ? "#1F087A72" : "#14000000";
}
