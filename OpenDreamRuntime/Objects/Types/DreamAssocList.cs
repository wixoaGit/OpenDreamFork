namespace OpenDreamRuntime.Objects.Types;

// TODO: An arglist given to New() can be used to initialize an alist with values
public sealed class DreamAssocList(DreamObjectDefinition aListDef, int size) : DreamObject(aListDef), IDreamList {
    public bool IsAssociative => true;
    public int Length => _values.Count;

    private readonly Dictionary<DreamValue, DreamValue> _values = new(size);

    public void SetValue(DreamValue key, DreamValue value, bool allowGrowth = false) {
        _values[key] = value;
    }

    public void AddValue(DreamValue value) {
        _values.TryAdd(value, DreamValue.Null);
    }

    public DreamValue GetValue(DreamValue key) {
        if (!_values.TryGetValue(key, out var value))
            throw new Exception($"No value with the key {key}");

        return value;
    }

    public bool ContainsKey(DreamValue key) {
        return _values.ContainsKey(key);
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

    public DreamValue[] CopyToArray() {
        var array = new DreamValue[_values.Count];

        _values.Keys.CopyTo(array, 0);
        return array;
    }

    public Dictionary<DreamValue, DreamValue> CopyAssocValues() {
        return new(_values);
    }
}
