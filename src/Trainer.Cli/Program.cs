using System.Security.Cryptography;
using Trainer.Core;

try
{
    if (args.Length == 3 && args[0] == "verify")
    {
        var profile = GameProfile.Load(args[1]);
        bool matches = profile.MatchesFile(args[2]);
        Console.WriteLine($"{profile.Game} / {profile.Build}: {(matches ? "module hash matches" : "UNKNOWN BUILD — refused")}");
        return matches ? 0 : 2;
    }
    if (args.Length == 1 && args[0] == "demo")
    {
        byte[] image = [10, 0, 0, 0, 99, 0, 0, 0];
        string hash = Convert.ToHexString(SHA256.HashData(image));
        var profile = new GameProfile(1, "demo-v1", "Simulated game", "1", "memory-buffer", hash,
            [new Feature("health", "Demo health", 0, "0A000000", "64000000")]);
        var target = new BufferTarget(image);
        var session = new PatchSession(profile, hash, target);
        Console.WriteLine($"Before: {BitConverter.ToInt32(target.Read(0, 4))}");
        try { session.Enable("health"); Console.WriteLine($"Enabled: {BitConverter.ToInt32(target.Read(0, 4))}"); }
        finally { session.RestoreAll(); }
        Console.WriteLine($"Restored: {BitConverter.ToInt32(target.Read(0, 4))}");
        Console.WriteLine("Demo only. No game process or file was modified.");
        return 0;
    }
    Console.WriteLine("Trainer framework v0.1\n  demo\n  verify <profile.json> <module-file>\nLive process adapters and a Windows UI are not implemented yet.");
    return args.Length == 0 ? 0 : 1;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
