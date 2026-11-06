namespace Trainer.Core;

// Adapters own process/module identity, address translation and instruction-cache
// flushing. Offsets are relative to the adapter's selected memory region.
public interface IMemoryTarget
{
    bool IsAlive { get; }
    byte[] Read(int offset, int count);
    void Write(int offset, byte[] bytes);
}

public sealed class PatchSession
{
    private sealed record Patch(int Offset, byte[] Original, byte[] Replacement);
    private readonly IMemoryTarget target;
    private readonly Dictionary<string, Patch> definitions;
    private readonly HashSet<string> active = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private bool faulted;
    public bool RecoveryRequired { get { lock (gate) return faulted; } }
    public string[] ActiveFeatures { get { lock (gate) return active.Order().ToArray(); } }
    public PatchSession(GameProfile profile, string observedModuleSha256, IMemoryTarget target)
    {
        profile.Validate();
        if (!profile.Sha256.Equals(observedModuleSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unknown module build; session refused.");
        this.target = target;
        // Snapshot definitions so caller mutation cannot alter an active session.
        definitions = profile.Features.ToDictionary(f => f.Id, f => new Patch(f.Offset, Convert.FromHexString(f.ExpectedHex), Convert.FromHexString(f.ReplacementHex)), StringComparer.Ordinal);
    }
    private bool Same(int offset, byte[] bytes) => target.Read(offset, bytes.Length).AsSpan().SequenceEqual(bytes);
    public void Enable(string id)
    {
        lock (gate)
        {
            if (faulted) throw new InvalidOperationException("Recovery required; new changes are blocked.");
            if (!target.IsAlive) throw new InvalidOperationException("Target exited.");
            var patch = definitions[id];
            if (active.Contains(id))
            {
                if (!Same(patch.Offset, patch.Replacement)) { faulted = true; throw new InvalidOperationException("Active patch changed externally."); }
                return;
            }
            if (!Same(patch.Offset, patch.Original)) throw new InvalidOperationException("Expected bytes do not match; nothing written.");
            active.Add(id); // Track ownership before a possibly partial write.
            try
            {
                target.Write(patch.Offset, patch.Replacement);
                if (!Same(patch.Offset, patch.Replacement)) throw new IOException("Patch read-back failed.");
            }
            catch
            {
                try
                {
                    target.Write(patch.Offset, patch.Original);
                    if (!Same(patch.Offset, patch.Original)) throw new IOException("Rollback read-back failed.");
                    active.Remove(id);
                }
                catch { faulted = true; }
                throw;
            }
        }
    }
    public void Disable(string id)
    {
        lock (gate)
        {
            if (!active.Contains(id)) return;
            if (!target.IsAlive) { active.Clear(); faulted = false; return; }
            var patch = definitions[id];
            if (Same(patch.Offset, patch.Original)) { active.Remove(id); return; }
            if (!Same(patch.Offset, patch.Replacement)) { faulted = true; throw new InvalidOperationException("Ownership changed; refusing to overwrite foreign bytes."); }
            try
            {
                target.Write(patch.Offset, patch.Original);
                if (!Same(patch.Offset, patch.Original)) throw new IOException("Restoration read-back failed.");
                active.Remove(id);
            }
            catch { faulted = true; throw; }
        }
    }
    public void RestoreAll()
    {
        lock (gate)
        {
            List<Exception> failures = [];
            foreach (var id in active.ToArray()) { try { Disable(id); } catch (Exception e) { failures.Add(e); } }
            if (failures.Count != 0) throw new AggregateException("Some changes need recovery.", failures);
            faulted = false;
        }
    }
}

public sealed class BufferTarget(byte[] initial) : IMemoryTarget
{
    private readonly byte[] bytes = initial.ToArray();
    public bool IsAlive => true;
    public byte[] Read(int offset, int count) => bytes.AsSpan(offset, count).ToArray();
    public void Write(int offset, byte[] value) => value.CopyTo(bytes.AsSpan(offset, value.Length));
}
