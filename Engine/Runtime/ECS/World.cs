using System.Collections;

namespace TACTIX.Engine.Runtime.ECS;

public sealed class World
{
    private int _nextEntityId = 1;
    private readonly Dictionary<Type, IDictionary> _stores = new();
    private readonly HashSet<int> _entities = new();

    public event Action? Changed;

    public Entity CreateEntity()
    {
        var e = new Entity(_nextEntityId++);
        _entities.Add(e.Id);
        Changed?.Invoke();
        return e;
    }

    public Entity CreateEntityWithId(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        _entities.Add(id);
        _nextEntityId = Math.Max(_nextEntityId, id + 1);
        Changed?.Invoke();
        return new Entity(id);
    }

    public IReadOnlyList<Entity> Entities => _entities.OrderBy(id => id).Select(id => new Entity(id)).ToArray();
    public bool Exists(Entity e) => _entities.Contains(e.Id);

    public void DestroyEntity(Entity e)
    {
        if (!_entities.Remove(e.Id)) return;
        foreach (var store in _stores.Values) store.Remove(e.Id);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _entities.Clear();
        _stores.Clear();
        _nextEntityId = 1;
        Changed?.Invoke();
    }

    public void Add<T>(Entity e, T component) where T : struct
    {
        var store = GetStore<T>(createIfMissing: true)!;
        store[e.Id] = component;
        Changed?.Invoke();
    }

    public bool Has<T>(Entity e) where T : struct
    {
        var store = GetStore<T>(createIfMissing: false);
        return store != null && store.ContainsKey(e.Id);
    }

    public T Get<T>(Entity e) where T : struct
    {
        var store = GetStore<T>(createIfMissing: false);
        if (store == null || !store.TryGetValue(e.Id, out var boxed))
            throw new KeyNotFoundException($"{e} does not have component {typeof(T).Name}.");
        return (T)boxed;
    }

    public void Set<T>(Entity e, T component) where T : struct
    {
        if (!Exists(e)) throw new InvalidOperationException($"Cannot set component on missing {e}.");
        var store = GetStore<T>(createIfMissing: true)!;
        store[e.Id] = component;
        Changed?.Invoke();
    }

    public bool Remove<T>(Entity e) where T : struct
    {
        var store = GetStore<T>(createIfMissing: false);
        var removed = store != null && store.Remove(e.Id);
        if (removed) Changed?.Invoke();
        return removed;
    }

    public IEnumerable<(Entity entity, T component)> Query<T>() where T : struct
    {
        var store = GetStore<T>(createIfMissing: false);
        if (store == null) yield break;
        foreach (var kv in store) yield return (new Entity(kv.Key), (T)kv.Value);
    }

    private Dictionary<int, object>? GetStore<T>(bool createIfMissing) where T : struct
    {
        var t = typeof(T);
        if (_stores.TryGetValue(t, out var existing)) return (Dictionary<int, object>)existing;
        if (!createIfMissing) return null;
        var store = new Dictionary<int, object>();
        _stores[t] = store;
        return store;
    }
}
