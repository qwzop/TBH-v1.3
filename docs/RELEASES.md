# Releases and installation

Initial delivery: extract the bootstrap ZIP and double-click Trainer Studio.exe.
No terminal or source build is needed on the user's computer.

Later releases use **Install Update**. A package contains exactly:

* `manifest.json`: Version, File, Sha256, Size.
* `manifest.sig`: RSA PKCS#1 SHA256 signature of the exact manifest bytes.
* `Trainer Studio.exe`: a self-contained Windows x64 app.

The app pins the public key embedded at build time. Keep the private key outside
source control and off client machines. The workspace signing key is separately
stored and is not included in source/binary packages. Maintain that key for future
updates; generating a different key without a rotation plan breaks update trust.

Version must be newer than the running app. Installation uses a staging directory,
checks expanded length and the executable hash, then moves to a new version folder.
No existing version is overwritten or automatically executed. Signing checks
publisher possession of the key, not code quality or Windows Authenticode status.

Release helper (maintainer only):

```text
python scripts/package_release.py --exe "path/to/Trainer Studio.exe" --version 0.2.0 --key /secure/private-key.pem --output update-0.2.0.zip
```

No update feed or hosting service is configured yet. The package picker and
installer are implemented; automatic downloads and remote status are future work.
