# CreatorOS Beta 1 content

This directory is the immutable, data-only source for the first public-beta composition:
The Slayers' Field Lodge hosting Air Drop followed by Cold Shot. Native Valheim remains the
network transport. Each installed peer keeps its own Runtime run state; the campaign admits only
locally witnessed kills and message actions.

Regenerate the checked artifacts through the identity-guarded wrapper:

```powershell
.\tools\creatoros\Build-CreatorOsBeta1.ps1
.\tools\creatoros\Build-CreatorOsBeta1.ps1 -Check
```

`campaign.json`, `quest-view.json`, and the `.questpack` are generated and must never be edited
by hand. A public release builder later pins these bytes to one `CreatorOSBeta1` `.db`/`.fwl`
pair, the Runtime/Contracts/Newtonsoft binaries, NetworkSense projection support, and install
receipts. No file here is evidence that an installed player completed either hunt.
