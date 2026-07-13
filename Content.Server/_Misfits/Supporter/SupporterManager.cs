using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Misfits.Supporter;
using Robust.Shared.Network;

namespace Content.Server._Misfits.Supporter;

public interface ISupporterManager
{
    void Initialize();
    bool TryGetSupporter(NetUserId userId, [NotNullWhen(true)] out SupporterEntry? data);
    Task SetSupporterAsync(Guid userId, string username, string? title, string? nameColor);
    Task RemoveSupporterAsync(Guid userId);
    Task WaitLoadedAsync();
    IReadOnlyList<SupporterEntry> GetAll();
}

public sealed class SupporterManager : ISupporterManager
{
    [Dependency] private readonly IServerDbManager _db = default!;

    private readonly Dictionary<Guid, SupporterEntry> _cache = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private ISawmill _sawmill = default!;
    private Task _loadTask = Task.CompletedTask;

    public void Initialize()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = Logger.GetSawmill("supporter");
        _loadTask = Task.Run(LoadAsync);
    }

    private async Task LoadAsync()
    {
        try
        {
            var rows = await _db.GetAllSupportersAsync();
            lock (_cache)
            {
                foreach (var row in rows)
                    _cache[row.UserId] = new SupporterEntry(row.UserId, row.Username, row.Title, row.NameColor);
            }
            _sawmill.Info($"Loaded {_cache.Count} supporter(s) from database.");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to load supporters from database: {ex}");
        }
    }

    public bool TryGetSupporter(NetUserId userId, [NotNullWhen(true)] out SupporterEntry? data)
    {
        lock (_cache)
            return _cache.TryGetValue(userId.UserId, out data);
    }

    public async Task SetSupporterAsync(Guid userId, string username, string? title, string? nameColor)
    {
        await _writeSemaphore.WaitAsync();
        try
        {
            await _db.UpsertSupporterAsync(userId, username, title, nameColor);
            lock (_cache)
                _cache[userId] = new SupporterEntry(userId, username, title, nameColor);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public async Task RemoveSupporterAsync(Guid userId)
    {
        await _writeSemaphore.WaitAsync();
        try
        {
            await _db.RemoveSupporterAsync(userId);
            lock (_cache)
                _cache.Remove(userId);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public Task WaitLoadedAsync()
    {
        return _loadTask;
    }

    public IReadOnlyList<SupporterEntry> GetAll()
    {
        lock (_cache)
            return _cache.Values.ToList();
    }
}
