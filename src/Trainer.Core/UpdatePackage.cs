using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trainer.Core;

public sealed record ReleaseManifest(string Version, string File, string Sha256, long Size);
public static class UpdatePackage
{
    const string Exe="Trainer Studio.exe";
    const long MaxExe=200L*1024*1024;
    public static string Install(string zipPath,string destination,string publicKeyPem,Version current)
    {
        using var archive=ZipFile.OpenRead(zipPath);
        var expected=new HashSet<string>(["manifest.json","manifest.sig",Exe],StringComparer.Ordinal);
        if(archive.Entries.Count!=3 || archive.Entries.Any(e=>!expected.Remove(e.FullName)) || expected.Count!=0)
            throw new InvalidDataException("Update package must contain exactly the manifest, signature and app executable.");
        static byte[] Read(ZipArchiveEntry entry,int limit)
        {
            if(entry.Length>limit)throw new InvalidDataException("Update metadata exceeds size limit.");
            using var source=entry.Open();using var memory=new MemoryStream();byte[] buffer=new byte[4096];int n;
            while((n=source.Read(buffer))!=0){if(memory.Length+n>limit)throw new InvalidDataException("Update metadata exceeds size limit.");memory.Write(buffer,0,n);}
            return memory.ToArray();
        }
        byte[] manifest=Read(archive.GetEntry("manifest.json")!,16384),signature=Read(archive.GetEntry("manifest.sig")!,1024);
        using var rsa=RSA.Create();rsa.ImportFromPem(publicKeyPem);
        if(!rsa.VerifyData(manifest,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("Update signature is not trusted.");
        var release=JsonSerializer.Deserialize<ReleaseManifest>(manifest)??throw new InvalidDataException("Empty update manifest.");
        if(!Version.TryParse(release.Version,out var version)||version<=current||release.File!=Exe||release.Size<=0||release.Size>MaxExe||release.Sha256 is null||release.Sha256.Length!=64||!release.Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid release or version is not newer.");
        var entry=archive.GetEntry(Exe)!;
        if(entry.Length!=release.Size)throw new InvalidDataException("Update executable length mismatch.");
        // All paths come from fixed names and a parsed version, never ZIP paths.
        Directory.CreateDirectory(destination);
        string final=Path.Combine(destination,"TrainerStudio-"+version);
        if(Directory.Exists(final))throw new IOException("This version folder already exists. Nothing overwritten.");
        string staging=Path.Combine(destination,".trainer-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            string exe=Path.Combine(staging,Exe);
            using(var source=entry.Open())using(var output=new FileStream(exe,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                byte[] buffer=new byte[81920];long total=0;int n;
                while((n=source.Read(buffer))!=0){total+=n;if(total>release.Size)throw new InvalidDataException("Expanded update exceeds declared length.");output.Write(buffer,0,n);}
                if(total!=release.Size)throw new InvalidDataException("Incomplete update executable.");output.Flush(true);
            }
            using(var input=File.OpenRead(exe))if(!Convert.ToHexString(SHA256.HashData(input)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Executable hash mismatch. Update rejected.");
            Directory.Move(staging,final);
            return Path.Combine(final,Exe);
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
}
