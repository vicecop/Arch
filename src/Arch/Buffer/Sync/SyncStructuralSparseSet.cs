using Arch.Core;
using Arch.Core.Utils;

namespace Arch.Buffer.Sync;

/// <summary>
///     The <see cref="StructuralEntity"/> struct
///     represents an <see cref="Entity"/> with its index in the <see cref="SyncStructuralSparseSet"/>.
/// </summary>
public readonly struct StructuralEntity
{
    internal readonly Entity Entity;
    internal readonly int Index;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StructuralEntity"/> struct.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <param name="index">Its index in its <see cref="SyncStructuralSparseSet"/>.</param>
    public StructuralEntity(Entity entity, int index)
    {
        Entity = entity;
        Index = index;
    }
}

// NOTE: Why not a generic type?
// NOTE: Should this have a more descriptive name? `StructuralSparseArray` sounds too generic for something that's only for `ComponentType`s.
/// <summary>
///     The see <see cref="StructuralSparseArray"/> class
///      stores components of a certain type in a sparse array.
///     It does not store its values however, its more like a registration mechanism.
/// </summary>
internal class SyncStructuralSparseArray
{

    /// <summary>
    ///     Initializes a new instance of the <see cref="SyncStructuralSparseArray"/> class
    ///     with the specified <see cref="ComponentType"/> and an optional initial <paramref name="capacity"/> (default: 64).
    /// </summary>
    /// <param name="type">Its <see cref="ComponentType"/>.</param>
    /// <param name="capacity">Its initial capacity.</param>
    public SyncStructuralSparseArray(ComponentType type, int capacity = 64)
    {
        Type = type;
        Size = 0;
        Entities = new int[capacity];
        Array.Fill(Entities, -1);
    }

    /// <summary>
    ///     Gets the <see cref="ComponentType"/> the <see cref="SyncStructuralSparseArray"/> stores.
    /// </summary>
    public ComponentType Type { get; }

    // NOTE: Should this be `Length` to follow the existing `Array` API?
    /// <summary>
    ///     Gets the total number of elements in the <see cref="SyncStructuralSparseArray"/>.
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    ///     Gets or sets the indices of the stored <see cref="Entity"/> instances.
    /// </summary>
    public int[] Entities;

    /// <summary>
    ///     Adds an item to the array.
    /// </summary>
    /// <param name="index">Its index in the array.</param>

    public void Add(int index)
    {
        lock (this)
        {
            // Resize entities
            if (index >= Entities.Length)
            {
                var lenght = Entities.Length;
                Array.Resize(ref Entities, index + 1);
                Array.Fill(Entities, -1, lenght, index - lenght);
            }

            Entities[index] = Size;
            Size++;
        }
    }

    // NOTE: Should this be `Contains` to follow other existing .NET APIs (ICollection<T>.Contains(T))?
    /// <summary>
    ///     Checks if an component exists at the index.
    /// </summary>
    /// <param name="index">The index in the array.</param>
    /// <returns>True if an component exists there, otherwise false.</returns>

    public bool Contains(int index)
    {
        return index < Entities.Length && Entities[index] != -1;
    }

    /// <summary>
    ///     Clears this <see cref="SyncSparseArray"/> instance and sets its <see cref="Size"/> to 0.
    /// </summary>
    public void Clear()
    {
        Array.Fill(Entities, -1, 0, Entities.Length);
        Size = 0;
    }
}

// NOTE: Why not a generic type?
// NOTE: Should this have a more descriptive name? `StructuralSparseSet` sounds too generic for something that's only for `Entity`s.
/// <summary>
///     The <see cref="StructuralSparseSet"/> class
///     stores a series of <see cref="SyncStructuralSparseArray"/>'s and their associated components.
/// </summary>
internal class SyncStructuralSparseSet
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SyncStructuralSparseSet"/> class
    ///     with an optional initial <paramref name="capacity"/> (default: 64).
    /// </summary>
    /// <param name="capacity">Its initial capacity.</param>
    public SyncStructuralSparseSet(int capacity = 64)
    {
        Capacity = capacity;
        Entities = new List<StructuralEntity>(capacity);
        Used = Array.Empty<int>();
        Components = Array.Empty<SyncStructuralSparseArray>();
    }

    /// <summary>
    ///     Gets the total number of elements the <see cref="SyncStructuralSparseSet"/> initially can hold.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    ///     Gets the total number of elements in the <see cref="SyncStructuralSparseSet"/>.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    ///     Gets a <see cref="List{T}"/> of all <see cref="StructuralEntity"/> instances in the <see cref="SyncStructuralSparseSet"/>.
    /// </summary>
    public List<StructuralEntity> Entities { get; private set; }

    /// <summary>
    ///     Gets the total number of <see cref="SyncStructuralSparseArray"/> instances in the <see cref="SyncStructuralSparseSet"/>.
    /// </summary>
    public int UsedSize { get; private set; }

    /// <summary>
    ///     Gets or sets an array containing used <see cref="SyncStructuralSparseArray"/> indices.
    /// </summary>
    public int[] Used;

    /// <summary>
    ///     Gets or sets an array containing <see cref="SyncStructuralSparseArray"/> instances.
    /// </summary>
    public SyncStructuralSparseArray[] Components; // The components as a `SparseSet` so we can easily access them via component IDs.

    /// <summary>
    ///     Ensures the capacity for registered components types.
    ///     Resizes the existing <see cref="Components"/> array properly to fit the id in.
    ///     <remarks>Does not ensure the capacity in terms of how many operations or components are recorded.</remarks>
    /// </summary>
    /// <param name="capacity">The new capacity, the id of the component which will be ensured to fit into the arrays.</param>

    private void EnsureTypeCapacity(int capacity)
    {
        // Resize arrays
        if (capacity < Components.Length)
        {
            return;
        }

        Array.Resize(ref Components, capacity + 1);
    }
    /// <summary>
    ///     Ensures the capacity for the <see cref="Used"/> array.
    /// </summary>
    /// <param name="capacity">The new capacity.</param>

    private void EnsureUsedCapacity(int capacity)
    {
        // Resize UsedSize array.
        if (capacity < UsedSize)
        {
            return;
        }

        Array.Resize(ref Used, UsedSize + 1);
    }

    /// <summary>
    ///     Adds an <see cref="SyncStructuralSparseArray"/> to the <see cref="Components"/> list and updates the <see cref="Used"/> properly.
    /// </summary>
    /// <param name="type">The <see cref="ComponentType"/> of the <see cref="SyncStructuralSparseArray"/>.</param>

    private void AddStructuralSparseArray(ComponentType type)
    {
        Components[type.Id] = new SyncStructuralSparseArray(type, Capacity);

        Used[UsedSize] = type.Id;
        UsedSize++;
    }

    /// <summary>
    ///     Checks whether a <see cref="SyncStructuralSparseArray"/> for a certain <see cref="ComponentType"/> exists in the <see cref="Components"/>.
    /// </summary>
    /// <param name="type">The <see cref="ComponentType"/> to check.</param>
    /// <returns>True if it does, false if not.</returns>

    private bool HasStructuralSparseArray(ComponentType type)
    {
        return Components[type.Id] != null;
    }

    /// <summary>
    ///     Returns the existing <see cref="SyncStructuralSparseArray"/> for the registered <see cref="ComponentType"/>.
    /// </summary>
    /// <param name="type">The <see cref="ComponentType"/>.</param>
    /// <returns>The existing <see cref="SyncStructuralSparseArray"/> instance.</returns>

    private SyncStructuralSparseArray GetStructuralSparseArray(ComponentType type)
    {
        return Components[type.Id];
    }

    /// <summary>
    ///     Adds an <see cref="Entity"/> to the <see cref="SyncStructuralSparseSet"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Entity"/>.</param>
    /// <returns>Its index in this <see cref="SyncStructuralSparseSet"/>.</returns>

    public int Create(in Entity entity)
    {
        var id = Count;
        Entities.Add(new StructuralEntity(entity, id));

        Count++;
        return id;
    }

    // NOTE: If `StructuralSparseSet` were generic, this could perhaps be an indexer (T this[int index]).
    /// <summary>
    ///     Sets a component at the index.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="index">The index.</param>

    public void Set<T>(int index)
    {
        var componentType = Component<T>.ComponentType;
        Set(index, componentType);
    }

    /// <summary>
    ///     Sets a component at the index using a ComponentType.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="componentType">The component type.</param>
    public void Set(int index, ComponentType componentType)
    {
        // Ensure that enough space for the additional component type array exists and add it if it does not exist yet.
        EnsureTypeCapacity(componentType.Id);
        if (!HasStructuralSparseArray(componentType))
        {
            EnsureUsedCapacity(UsedSize + 1);
            AddStructuralSparseArray(componentType);
        }

        // Add to array.
        var array = GetStructuralSparseArray(componentType);
        if (!array.Contains(index))
        {
            array.Add(index);
        }
    }

    // NOTE: Should this be `Contains` to follow other existing .NET APIs (ICollection<T>.Contains(T))?
    /// <summary>
    ///     Checks if an component exists at the index.
    /// </summary>
    /// <param name="index">The index in the array.</param>
    /// <returns>True if an component exists there, otherwise false.</returns>

    public bool Contains<T>(int index)
    {
        var id = Component<T>.ComponentType.Id;
        var array = Components[id];

        return array.Contains(index);
    }

    /// <summary>
    ///     Gets the <see cref="Entity"/> at the specified index.
    /// </summary>
    /// <param name="index">The index in the <see cref="Entities"/> list.</param>
    /// <returns>The <see cref="Entity"/> at the specified index.</returns>
    public Entity GetEntity(int index)
    {
        return Entities[index].Entity;
    }

    /// <summary>
    ///     Merges component types from source sparse set at sourceIndex into this sparse set at targetIndex.
    /// </summary>
    /// <param name="source">The source <see cref="SyncStructuralSparseSet"/>.</param>
    /// <param name="sourceIndex">The index in the source sparse set.</param>
    /// <param name="targetIndex">The index in this sparse set.</param>
    public void MergeFrom(SyncStructuralSparseSet source, int sourceIndex, int targetIndex)
    {
        // Iterate through all used component types in source
        for (var i = 0; i < source.UsedSize; i++)
        {
            var componentTypeId = source.Used[i];
            var sourceArray = source.Components[componentTypeId];

            // Check if source has this component at sourceIndex
            if (sourceArray.Contains(sourceIndex))
            {
                var componentType = sourceArray.Type;

                // Ensure target has capacity for this component type
                EnsureTypeCapacity(componentTypeId);
                if (!HasStructuralSparseArray(componentType))
                {
                    EnsureUsedCapacity(UsedSize + 1);
                    AddStructuralSparseArray(componentType);
                }

                // Add component to target
                var targetArray = GetStructuralSparseArray(componentType);
                if (!targetArray.Contains(targetIndex))
                {
                    targetArray.Add(targetIndex);
                }
            }
        }
    }

    /// <summary>
    ///     Clears the <see cref="SyncStructuralSparseSet"/>.
    /// </summary>

    public void Clear()
    {
        Count = 0;
        Entities.Clear();

        foreach (var sparset in Components)
        {
            sparset?.Clear();
        }
    }
}