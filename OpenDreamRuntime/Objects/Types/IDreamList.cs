using System.Linq;

namespace OpenDreamRuntime.Objects.Types;

public interface IDreamList {
    public bool IsAssociative { get; }
    public int Length { get; }

    protected bool IsIndexableByNumber => true;

    public void SetValue(DreamValue key, DreamValue value, bool allowGrowth = false);
    public void AddValue(DreamValue value);
    public void RemoveValue(DreamValue value);
    public DreamValue GetValue(DreamValue key);
    public bool HasAssociatedValue(DreamValue key);
    public IEnumerable<DreamValue> EnumerateValues();
    public IEnumerable<KeyValuePair<DreamValue, DreamValue>> EnumerateAssocValues();
    public void Cut(int start = 1, int end = 0);
    public IDreamList CreateCopy(int start = 1, int end = 0);

    // Below methods have default implementations for convenience, though they might be slow
    // It's best to override them if your list allows for a more optimized approach

    public int FindValue(DreamValue value, int start = 1, int end = 0) {
        if (!IsIndexableByNumber && (start != 1 || end != 0))
            throw new ArgumentException($"List {this} cannot be indexed by number");
        if (end == 0 || end > Length)
            end = Length;

        int i = start;
        foreach (var v in EnumerateValues().Skip(start - 1)) {
            if (v.Equals(value))
                return i;

            i++;
            if (i > end)
                break;
        }

        return 0;
    }

    public bool ContainsValue(DreamValue value) {
        return FindValue(value) != 0;
    }

    public DreamValue[] CopyToArray() {
        return EnumerateValues().ToArray();
    }

    public Dictionary<DreamValue, DreamValue> CopyAssocValues() {
        return new(EnumerateAssocValues());
    }
}
