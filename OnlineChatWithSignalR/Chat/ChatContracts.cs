namespace OnlineChatWithSignalR.Chat;

// DTOs shared with the client live in the OnlineChatWithSignalR.Contracts project.

public sealed record CurrentUser(string Id, string Name);

/// <summary>
/// An expected error caused by the client (invalid input, not a member, ...).
/// Its message is safe to show to the user.
/// </summary>
public sealed class ChatException(string message) : Exception(message);
