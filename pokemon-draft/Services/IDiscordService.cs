public interface IDiscordService
{
    /// <summary>
    /// Sends a message to the Discord channel.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
}