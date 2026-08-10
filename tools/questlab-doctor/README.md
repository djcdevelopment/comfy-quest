# Quest Lab compatibility doctor

The doctor answers “what is installed, what actually ran, and what evidence is still missing?”
without changing Valheim or the repository.

```powershell
python tools\questlab-doctor\questlab_doctor.py `
  --output captures\questlab\doctor.json `
  --support-bundle captures\questlab\questlab-support.zip
```

It checks:

- the 91-row / 90-signature / 34-event catalog contract;
- source, package-manifest, built-DLL, installed-DLL, and last-live release identity;
- structural health of local schema-1 quest files;
- the expected/read record counts of every tree-recovery ledger;
- exact-release creator-event and all-school suite receipts.

The support capsule contains only the generated report and a privacy note. It deliberately
excludes raw logs, quest contents, filenames, player names, and absolute paths. Receipt and DLL
hashes provide correlation without exporting private gameplay data.

The doctor is read-only. A failing tree ledger reports the block and remedy; it never attempts a
clear, restore, rebuild, deploy, or repair.
