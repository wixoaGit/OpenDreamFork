using System.Linq;

namespace OpenDreamRuntime.Objects.Types;

public interface IDreamList {
    public bool IsAssociative { get; }
    public int Length { get; }

    public void SetValue(DreamValue key, DreamValue value, bool allowGrowth = false);
    public void AddValue(DreamValue value);
    public DreamValue GetValue(DreamValue key);
    public bool ContainsKey(DreamValue key);
    public IEnumerable<DreamValue> EnumerateValues();
    public IEnumerable<KeyValuePair<DreamValue, DreamValue>> EnumerateAssocValues();
    public void Cut(int start = 1, int end = 0);

    public DreamValue[] CopyToArray() {
        return EnumerateValues().ToArray();
    }

    public Dictionary<DreamValue, DreamValue> CopyAssocValues() {
        return new(EnumerateAssocValues());
    }
}
