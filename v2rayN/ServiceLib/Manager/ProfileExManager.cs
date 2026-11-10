namespace ServiceLib.Manager;

public class ProfileExManager
{
    private static readonly Lazy<ProfileExManager> _instance = new(() => new());
    public static ProfileExManager Instance => _instance.Value;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, ProfileExItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _dirty = new(StringComparer.Ordinal);
    private long _version;
    private readonly Func<ProfileExItem, Task> _saveItem;

    public ProfileExManager() : this(SaveItemAsync) { }

    // Injectable persistence keeps concurrency and failure tests away from the user database.
    public ProfileExManager(Func<ProfileExItem, Task> saveItem)
    {
        _saveItem = saveItem ?? throw new ArgumentNullException(nameof(saveItem));
    }

    private static async Task SaveItemAsync(ProfileExItem item)
    {
        await SQLiteHelper.Instance.ExecuteAsync(
            "INSERT INTO ProfileExItem (IndexId, Delay, Speed, Sort, Message, IpInfo) VALUES (?, ?, ?, ?, ?, ?) " +
            "ON CONFLICT(IndexId) DO UPDATE SET Delay=excluded.Delay, Speed=excluded.Speed, Sort=excluded.Sort, Message=excluded.Message, IpInfo=excluded.IpInfo",
            item.IndexId, item.Delay, item.Speed, item.Sort, item.Message, item.IpInfo);
    }

    public async Task Init()
    {
        await _saveGate.WaitAsync();
        try
        {
            await SQLiteHelper.Instance.ExecuteAsync("delete from ProfileExItem where indexId not in ( select indexId from ProfileItem )");
            var loaded = await SQLiteHelper.Instance.TableAsync<ProfileExItem>().ToListAsync();
            lock (_sync)
            {
                // Preserve changes that arrived while the database was being read.
                foreach (var item in loaded)
                    if (!_dirty.ContainsKey(item.IndexId))
                        _items[item.IndexId] = Copy(item);
            }
        }
        finally { _saveGate.Release(); }
    }

    private static ProfileExItem Copy(ProfileExItem item) => new()
    {
        IndexId = item.IndexId, Delay = item.Delay, Speed = item.Speed,
        Sort = item.Sort, Message = item.Message, IpInfo = item.IpInfo
    };

    public Task<ConcurrentBag<ProfileExItem>> GetProfileExs()
    {
        lock (_sync)
            return Task.FromResult(new ConcurrentBag<ProfileExItem>(_items.Values.Select(Copy)));
    }

    private void Update(string indexId, Action<ProfileExItem> change)
    {
        if (string.IsNullOrEmpty(indexId)) return;
        lock (_sync)
        {
            if (!_items.TryGetValue(indexId, out var item))
                _items[indexId] = item = new ProfileExItem { IndexId = indexId, Message = string.Empty };
            change(item);
            _dirty[indexId] = ++_version;
        }
    }

    public async Task SaveTo()
    {
        await _saveGate.WaitAsync();
        try
        {
            List<(ProfileExItem Item, long Version)> snapshot;
            lock (_sync)
                snapshot = _dirty.Select(pair => (Copy(_items[pair.Key]), pair.Value)).ToList();

            foreach (var (item, version) in snapshot)
            {
                // Only acknowledge the exact version committed: concurrent edits remain dirty.
                await _saveItem(item);
                lock (_sync)
                    if (_dirty.TryGetValue(item.IndexId, out var current) && current == version)
                        _dirty.Remove(item.IndexId);
            }
        }
        finally { _saveGate.Release(); }
    }

    public async Task ClearAll()
    {
        await _saveGate.WaitAsync();
        try
        {
            await SQLiteHelper.Instance.ExecuteAsync("delete from ProfileExItem");
            lock (_sync) { _items.Clear(); _dirty.Clear(); }
        }
        finally { _saveGate.Release(); }
    }

    public void SetTestDelay(string indexId, int delay) => Update(indexId, item => item.Delay = delay);
    public void SetTestSpeed(string indexId, decimal speed) => Update(indexId, item => item.Speed = speed);
    public void SetTestMessage(string indexId, string message) => Update(indexId, item => item.Message = message);
    public void SetTestIpInfo(string indexId, string ipInfo) => Update(indexId, item => item.IpInfo = ipInfo);
    public void SetSort(string indexId, int sort) => Update(indexId, item => item.Sort = sort);

    public int GetSort(string indexId)
    {
        lock (_sync) return _items.TryGetValue(indexId, out var item) ? item.Sort : 0;
    }

    public int GetMaxSort()
    {
        lock (_sync) return _items.Count == 0 ? 0 : _items.Values.Max(item => item.Sort);
    }
}
