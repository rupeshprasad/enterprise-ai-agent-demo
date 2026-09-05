using System.Collections.Concurrent;
using System.Text.Json;
using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Services;

public sealed class ChatHistoryService(
    IWebHostEnvironment environment,
    ILogger<ChatHistoryService> logger)
{
    private static readonly IReadOnlyDictionary<string, string> UserFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["demo-user"] = "demo-user.json",
            ["restricted-user"] = "restricted-user.json"
        };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> userLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string historyDirectory = Path.GetFullPath(
        Path.Combine(environment.ContentRootPath, "..", "..", "data", "chat-history"));

    public bool IsSupportedUser(string userId) => UserFiles.ContainsKey(userId);

    public async Task<IReadOnlyCollection<ChatHistoryEntry>> GetAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var filePath = GetFilePath(userId);
        var userLock = GetLock(userId);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadFileAsync(filePath, cancellationToken);
            logger.LogInformation(
                "Chat history loaded. UserId={UserId}, MessageCount={MessageCount}.",
                userId,
                entries.Count);
            return entries;
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task AppendAsync(
        string userId,
        ChatHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        var filePath = GetFilePath(userId);
        var userLock = GetLock(userId);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadFileAsync(filePath, cancellationToken);
            entries.Add(entry);
            await WriteFileAsync(filePath, entries, cancellationToken);
            logger.LogInformation(
                "Chat history updated. UserId={UserId}, MessageCount={MessageCount}, Role={Role}.",
                userId,
                entries.Count,
                entry.Role);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        var filePath = GetFilePath(userId);
        var userLock = GetLock(userId);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            await WriteFileAsync(filePath, [], cancellationToken);
            logger.LogInformation("Chat history cleared. UserId={UserId}.", userId);
        }
        finally
        {
            userLock.Release();
        }
    }

    private string GetFilePath(string userId)
    {
        if (!UserFiles.TryGetValue(userId, out var fileName))
        {
            throw new ArgumentException("Unknown demo user.", nameof(userId));
        }

        return Path.Combine(historyDirectory, fileName);
    }

    private SemaphoreSlim GetLock(string userId) =>
        userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    private static async Task<List<ChatHistoryEntry>> ReadFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<ChatHistoryEntry>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];
    }

    private async Task WriteFileAsync(
        string filePath,
        IReadOnlyCollection<ChatHistoryEntry> entries,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(historyDirectory);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
