"""Maintainer release packaging. Requires openssl; never ships the private key."""
import argparse, hashlib, json, pathlib, subprocess, tempfile, zipfile
p=argparse.ArgumentParser()
for name in ('exe','version','key','output'): p.add_argument('--'+name,required=True)
a=p.parse_args();exe=pathlib.Path(a.exe)
manifest=json.dumps(dict(Version=a.version,File='Trainer Studio.exe',Sha256=hashlib.sha256(exe.read_bytes()).hexdigest(),Size=exe.stat().st_size),separators=(',',':')).encode()
with tempfile.TemporaryDirectory() as folder:
    data=pathlib.Path(folder)/'manifest.json';sig=pathlib.Path(folder)/'manifest.sig';data.write_bytes(manifest)
    subprocess.run(['openssl','dgst','-sha256','-sign',a.key,'-out',str(sig),str(data)],check=True)
    with zipfile.ZipFile(a.output,'x',zipfile.ZIP_DEFLATED) as package:
        package.writestr('manifest.json',manifest);package.write(sig,'manifest.sig');package.write(exe,'Trainer Studio.exe')
print(a.output)
