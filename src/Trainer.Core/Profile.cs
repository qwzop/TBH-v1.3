using System.Security.Cryptography;
using System.Text.Json;

namespace Trainer.Core;

public sealed record Feature(string Id, string Label, int Offset, string ExpectedHex, string ReplacementHex);
public sealed record GameProfile(int SchemaVersion, string Id, string Game, string Build, string Module, string Sha256, Feature[] Features)
{
    public static GameProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Empty profile.");
        profile.Validate();
        return profile;
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Game) || string.IsNullOrWhiteSpace(Build) || string.IsNullOrWhiteSpace(Module))
            throw new InvalidDataException("Profile schema or identity is invalid.");
        if (Sha256 is null || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("A complete SHA256 is required.");
        if (Features is null || Features.Length == 0 || Features.Any(f => f is null || string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.Label)) || Features.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != Features.Length)
            throw new InvalidDataException("Feature IDs must be present and unique.");
        long previousEnd = 0;
        foreach (var f in Features.OrderBy(f => f.Offset))
        {
            var before = Convert.FromHexString(f.ExpectedHex); var after = Convert.FromHexString(f.ReplacementHex);
            if (f.Offset < previousEnd || before.Length == 0 || before.Length != after.Length || before.AsSpan().SequenceEqual(after))
                throw new InvalidDataException("Feature ranges overlap, are empty, unequal, or have no effect.");
            previousEnd = checked((long)f.Offset + before.Length);
        }
    }
    public bool MatchesFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
