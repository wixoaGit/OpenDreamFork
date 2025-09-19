namespace OpenDreamRuntime.Objects.Types;

// TODO: An arglist given to New() can be used to initialize an alist with values
public sealed class DreamAssocList : DreamObject, IDreamList {
    public bool IsAssociative => true;
    public int Length => _values.Count;

    bool IDreamList.IsIndexableByNumber => false;

    private readonly Dictionary<DreamValue, DreamValue> _values;

    public DreamAssocList(DreamObjectDefinition aListDef, int size) : base(aListDef) {
        _values = new(size);
    }

    private DreamAssocList(DreamObjectDefinition aListDef, Dictionary<DreamValue, DreamValue> values) : base(aListDef) {
        _values = new(values);
    }

    public void SetValue(DreamValue key, DreamValue value, bool allowGrowth = false) {
        _values[key] = value;
    }

    public void AddValue(DreamValue value) {
        _values.TryAdd(value, DreamValue.Null);
    }

    public void RemoveValue(DreamValue value) {
        _values.Remove(value);
    }

    public DreamValue GetValue(DreamValue key) {
        if (!_values.TryGetValue(key, out var value))
            throw new Exception($"No value with the key {key}");

        return value;
    }

    public bool ContainsValue(DreamValue value) {
        return _values.ContainsKey(value);
    }

    public bool HasAssociatedValue(DreamValue key) {
        return ContainsValue(key); // Every value has an associated value
    }

    public IEnumerable<DreamValue> EnumerateValues() {
        return _values.Keys; // The keys, counter-intuitively
    }

    public IEnumerable<KeyValuePair<DreamValue, DreamValue>> EnumerateAssocValues() {
        return _values;
    }

    public void Cut(int start = 1, int end = 0) {
        if (start != 1 && end != 0)
            throw new ArgumentException("Assoc lists cannot be cut by index");

        _values.Clear();
    }

    public IDreamList CreateCopy(int start = 1, int end = 0) {
        if (start != 1 && end != 0)
            throw new ArgumentException("Assoc lists cannot be copied by index");

        return new DreamAssocList(ObjectDefinition, _values);
    }

    public DreamValue[] CopyToArray() {
        var array = new DreamValue[_values.Count];

        _values.Keys.CopyTo(array, 0);
        return array;
    }

    public Dictionary<DreamValue, DreamValue> CopyAssocValues() {
        return new(_values);
    }
}
