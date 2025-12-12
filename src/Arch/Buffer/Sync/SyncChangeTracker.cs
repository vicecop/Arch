using Arch.Core;
using Collections.Pooled;

namespace Arch.Buffer.Sync;

public sealed class SyncChangeTracker : IDisposable
{
    private readonly PooledDictionary<int, TrackedEntityInfo> _entities;
    private readonly SyncStructuralSparseSet _added;
    private readonly SyncStructuralSparseSet _updated;
    private readonly SyncStructuralSparseSet _removed;

    private readonly PooledList<CreateInfo> _creates;
    private readonly PooledList<DestroyInfo> _destroys;

    private int _size;

    public SyncChangeTracker(int initialCapacity = 128)
    {
        _entities = new PooledDictionary<int, TrackedEntityInfo>(initialCapacity);
        _added = new SyncStructuralSparseSet(initialCapacity);
        _updated = new SyncStructuralSparseSet(initialCapacity);
        _removed = new SyncStructuralSparseSet(initialCapacity);
        _creates = new PooledList<CreateInfo>(initialCapacity);
        _destroys = new PooledList<DestroyInfo>(initialCapacity);
    }

    /// <summary>
    /// Gets whether this tracker is empty (has no tracked changes).
    /// </summary>
    public bool IsEmpty
    {
        get => _size == 0 &&
               _creates.Count == 0 &&
               _destroys.Count == 0;
    }

    public void MarkCreated(in Entity entity, ComponentType[] types)
    {
        _creates.Add(new CreateInfo(entity, types));
    }

    public void MarkAdded<T>(in Entity entity)
    {
        if (!_entities.TryGetValue(entity.Id, out var info))
        {
            Register(entity, out info);
        }

        _added.Set<T>(info.AddedIndex);
    }

    public void MarkUpdated<T>(in Entity entity)
    {
        if (!_entities.TryGetValue(entity.Id, out var info))
        {
            Register(entity, out info);
        }

        _updated.Set<T>(info.UpdatedIndex);
    }

    public void MarkRemoved<T>(in Entity entity)
    {
        if (!_entities.TryGetValue(entity.Id, out var info))
        {
            Register(entity, out info);
        }

        _removed.Set<T>(info.RemovedIndex);
    }

    public void MarkDestroyed(in Entity entity, ComponentType[] componentsSnapshot)
    {
        _destroys.Add(new DestroyInfo(entity, componentsSnapshot));
    }

    /// <summary>
    /// Registers an entity in the tracker and creates indices in all sparse sets.
    /// </summary>
    /// <param name="entity">The entity to register.</param>
    /// <param name="info">The tracking info for the entity.</param>
    private void Register(in Entity entity, out TrackedEntityInfo info)
    {
        var addedIndex = _added.Create(in entity);
        var updatedIndex = _updated.Create(in entity);
        var removedIndex = _removed.Create(in entity);

        info = new TrackedEntityInfo(addedIndex, updatedIndex, removedIndex);
        _entities.Add(entity.Id, info);
        _size++;
    }

    public bool IsAdded<T>(in Entity entity)
    {
        if (!_entities.TryGetValue(entity.Id, out var info))
        {
            return false;
        }

        return _added.Contains<T>(info.AddedIndex);
    }

    public bool IsUpdated<T>(in Entity entity)
    {
        if (!_entities.TryGetValue(entity.Id, out var info))
        {
            return false;
        }

        return _updated.Contains<T>(info.UpdatedIndex);
    }

    public bool IsRemoved<T>(in Entity entity)
    {
        if (!_entities.TryGetValue(entity.Id, out var info))
        {
            return false;
        }

        return _removed.Contains<T>(info.RemovedIndex);
    }

    /// <summary>
    /// Gets all created entities that have component T.
    /// </summary>
    /// <typeparam name="T">The component type to filter by.</typeparam>
    /// <returns>A pooled list containing matching entities. Caller must dispose it.</returns>
    public PooledList<Entity> GetCreatedEntities<T>()
    {
        var componentId = Component<T>.ComponentType.Id;

        var result = new PooledList<Entity>();
        foreach (var create in _creates)
        {
            foreach (var type in create.Types)
            {
                if (type.Id == componentId)
                {
                    result.Add(create.Entity);
                    break;
                }
            }
        }

        return result;
    }

    public bool IsDestroyed<T>(in Entity entity)
    {
        var componentId = Component<T>.ComponentType.Id;

        foreach (var destroy in _destroys)
        {
            if (destroy.Entity.Id != entity.Id)
            {
                continue;
            }

            foreach (var type in destroy.ComponentsSnapshot)
            {
                if (type.Id == componentId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Merge(SyncChangeTracker other)
    {
        _creates.AddRange(other._creates);

        _destroys.AddRange(other._destroys);

        MergeSparseSet(_added, other._added, other._entities);
        MergeSparseSet(_updated, other._updated, other._entities);
        MergeSparseSet(_removed, other._removed, other._entities);
    }

    /// <summary>
    /// Merges changes from a SyncCommandBuffer into this tracker.
    /// </summary>
    /// <param name="buffer">The command buffer to merge from.</param>
    public void Merge(SyncCommandBuffer buffer)
    {
        // Merge Creates
        foreach (var createCmd in buffer.Creates)
        {
            var entity = buffer.Entities[createCmd.Index];
            _creates.Add(new CreateInfo(entity, createCmd.Types));
        }

        // Merge Destroys
        foreach (var destroyIndex in buffer.Destroys)
        {
            var entity = buffer.Entities[destroyIndex];

            // Получаем текущие компоненты сущности для snapshot
            // Используем пустой массив, т.к. SyncCommandBuffer не хранит snapshot компонентов
            _destroys.Add(new DestroyInfo(entity, []));
        }

        // Merge Adds, Sets (Updates), Removes
        foreach (var (entityId, bufferInfo) in buffer.BufferedEntityInfo)
        {
            var entity = buffer.Entities[bufferInfo.Index];

            // Регистрируем сущность в трекере, если её ещё нет
            if (!_entities.TryGetValue(entityId, out var trackerInfo))
            {
                Register(entity, out trackerInfo);
            }

            // Merge Adds
            var addedComponents = buffer.Adds.Components;
            for (var i = 0; i < buffer.Adds.UsedSize; i++)
            {
                var componentTypeId = buffer.Adds.Used[i];
                var sparseArray = addedComponents[componentTypeId];

                if (sparseArray.Contains(bufferInfo.AddIndex))
                {
                    var componentType = sparseArray.Type;
                    _added.Set(trackerInfo.AddedIndex, componentType);
                }
            }

            // Merge Sets (Updates)
            var setComponents = buffer.Sets.Components;
            for (var i = 0; i < buffer.Sets.UsedSize; i++)
            {
                var componentTypeId = buffer.Sets.Used[i];
                var sparseArray = setComponents[componentTypeId];

                if (sparseArray.Contains(bufferInfo.SetIndex))
                {
                    var componentType = sparseArray.Type;
                    _updated.Set(trackerInfo.UpdatedIndex, componentType);
                }
            }

            // Merge Removes
            var removedComponents = buffer.Removes.Components;
            for (var i = 0; i < buffer.Removes.UsedSize; i++)
            {
                var componentTypeId = buffer.Removes.Used[i];
                var sparseArray = removedComponents[componentTypeId];

                if (sparseArray.Contains(bufferInfo.RemoveIndex))
                {
                    var componentType = sparseArray.Type;
                    _removed.Set(trackerInfo.RemovedIndex, componentType);
                }
            }
        }
    }

    private void MergeSparseSet(
        SyncStructuralSparseSet target,
        SyncStructuralSparseSet source,
        PooledDictionary<int, TrackedEntityInfo> sourceEntities)
    {
        foreach (var (entityId, sourceInfo) in sourceEntities)
        {
            if (!_entities.TryGetValue(entityId, out var targetInfo))
            {
                var entity = source.GetEntity(sourceInfo.AddedIndex);
                Register(entity, out targetInfo);
            }

            target.MergeFrom(source, sourceInfo.AddedIndex, targetInfo.AddedIndex);
        }
    }

    public void Clear()
    {
        _creates.Clear();
        _destroys.Clear();
        _added.Clear();
        _updated.Clear();
        _removed.Clear();
        _entities.Clear();
        _size = 0;
    }

    public void Dispose()
    {
        _entities.Dispose();
        _creates.Dispose();
        _destroys.Dispose();
        _added.Clear();
        _updated.Clear();
        _removed.Clear();
        GC.SuppressFinalize(this);
    }
}

internal readonly record struct CreateInfo(Entity Entity, ComponentType[] Types);
internal readonly record struct DestroyInfo(Entity Entity, ComponentType[] ComponentsSnapshot);
internal readonly record struct TrackedEntityInfo(int AddedIndex, int UpdatedIndex, int RemovedIndex);
