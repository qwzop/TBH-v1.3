using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trainer.Core;
int count=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
void Refuses(Action action,string label){try{action();}catch{Check(true,label);return;}throw new Exception("Did not refuse: "+label);}
byte[] bytes=[10,0,0,0,5,0,0,0];string hash=Convert.ToHexString(SHA256.HashData(bytes));
var p=new GameProfile(1,"demo","Demo","1","buffer",hash,[new("hp","HP",0,"0A000000","64000000"),new("speed","Speed",4,"05000000","06000000")]);
var memory=new FaultTarget(bytes);var session=new PatchSession(p,hash,memory);
session.Enable("hp");session.Enable("speed");Check(memory.Read(0,8).SequenceEqual(new byte[]{100,0,0,0,6,0,0,0}),"independent feature changes");session.RestoreAll();Check(memory.Read(0,8).SequenceEqual(bytes),"all originals restored");
Refuses(()=>new PatchSession(p,new string('0',64),memory),"unknown build rejected");
memory.Write(0,[9]);int writes=memory.Writes;Refuses(()=>session.Enable("hp"),"signature mismatch rejected");Check(memory.Writes==writes,"mismatch made no writes");memory.Write(0,[10]);
memory.FailNext=true;Refuses(()=>session.Enable("hp"),"partial write reported");Check(memory.Read(0,8).SequenceEqual(bytes)&&session.ActiveFeatures.Length==0,"partial write rolled back");
session.Enable("hp");memory.Write(0,[77]);writes=memory.Writes;Refuses(()=>session.Disable("hp"),"foreign change refused");Check(memory.Writes==writes&&session.RecoveryRequired,"foreign bytes preserved and recovery surfaced");
Refuses(()=>(p with{Features=[p.Features[0],new("overlap","Overlap",2,"0000","0101")]}).Validate(),"overlapping features rejected");
using var key=RSA.Create(2048);string dir=Path.Combine(Path.GetTempPath(),"trainer-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
try
{
 byte[] exe=Encoding.UTF8.GetBytes("test fixture, not an executable");
 string Package(string name,bool tamper=false,bool badSignature=false,bool traversal=false)
 {
  var manifest=new ReleaseManifest("0.2.0","Trainer Studio.exe",Convert.ToHexString(SHA256.HashData(exe)),exe.Length);
  byte[] json=JsonSerializer.SerializeToUtf8Bytes(manifest),sig=key.SignData(json,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  if(badSignature)sig[0]^=1;
  string path=Path.Combine(dir,name);using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
  void Entry(string n,byte[] b){using var s=zip.CreateEntry(n).Open();s.Write(b);}
  Entry("manifest.json",json);Entry("manifest.sig",sig);Entry(traversal?"../Trainer Studio.exe":"Trainer Studio.exe",tamper?Enumerable.Repeat((byte)1,exe.Length).ToArray():exe);return path;
 }
 string dest=Path.Combine(dir,"installed"),pub=key.ExportSubjectPublicKeyInfoPem();
 Refuses(()=>UpdatePackage.Install(Package("forged.zip",badSignature:true),dest,pub,new Version(0,1,0)),"untrusted signature refused");Check(!Directory.Exists(dest),"signature rejection happens before installation");
 Refuses(()=>UpdatePackage.Install(Package("path.zip",traversal:true),dest,pub,new Version(0,1,0)),"ZIP path traversal refused");
 Refuses(()=>UpdatePackage.Install(Package("tampered.zip",tamper:true),dest,pub,new Version(0,1,0)),"modified executable rejected");Check(!Directory.EnumerateFileSystemEntries(dest).Any(),"failed staged update cleaned up");
 string valid=Package("valid.zip"),installed=UpdatePackage.Install(valid,dest,pub,new Version(0,1,0));Check(File.ReadAllBytes(installed).SequenceEqual(exe),"signed update installed in versioned folder");
 Refuses(()=>UpdatePackage.Install(valid,dest,pub,new Version(0,1,0)),"existing release not overwritten");Refuses(()=>UpdatePackage.Install(valid,Path.Combine(dir,"old"),pub,new Version(0,2,0)),"same or older release rejected");
}finally{Directory.Delete(dir,true);}
Console.WriteLine($"{count} checks passed.");
sealed class FaultTarget(byte[] initial):IMemoryTarget
{
 readonly byte[] bytes=initial.ToArray();public bool IsAlive=>true;public bool FailNext;public int Writes;
 public byte[] Read(int offset,int count)=>bytes.AsSpan(offset,count).ToArray();
 public void Write(int offset,byte[] value){Writes++;if(FailNext){FailNext=false;bytes[offset]=value[0];throw new IOException("Simulated partial write");}value.CopyTo(bytes.AsSpan(offset,value.Length));}
}
