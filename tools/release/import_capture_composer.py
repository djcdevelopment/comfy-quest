#!/usr/bin/env python3
"""Import a hash-verified Steward UI release artifact; never read another checkout."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT=Path(__file__).resolve().parents[2]
NAMES={'capture-composer.js','capture-composer.css','camera-model.js','creator-scene.js','camera-fixtures.json'}


def install(artifact,pin):
    data=artifact.read_bytes()
    if len(data)!=pin['bytes'] or hashlib.sha256(data).hexdigest()!=pin['sha256']:raise ValueError('Composer artifact integrity mismatch')
    with zipfile.ZipFile(artifact) as z:
        if len(z.namelist())!=len(NAMES)+1 or set(z.namelist())!=NAMES|{'manifest.json'}:raise ValueError('Unexpected composer artifact files')
        manifest=json.loads(z.read('manifest.json'))
        if manifest.get('schema')!='comfy-steward-capture-composer-artifact/v1':raise ValueError('Unsupported composer artifact')
        if not isinstance(manifest.get('sourceRevision'),str) or len(manifest['sourceRevision'])!=40:raise ValueError('Missing source revision')
        contents={}
        for row in manifest['files']:
            if row['path'] not in NAMES or row['path'] in contents:raise ValueError('Invalid composer file list')
            value=z.read(row['path'])
            if len(value)!=row['bytes'] or hashlib.sha256(value).hexdigest()!=row['sha256']:raise ValueError('Composer file integrity mismatch')
            contents[row['path']]=value
        if set(contents)!=NAMES:raise ValueError('Incomplete composer manifest')
    shell=shutil.which('pwsh') or shutil.which('powershell')
    if shell is None:raise ValueError('PowerShell is required for the repository identity guard')
    subprocess.run([shell,'-NoProfile','-File',str(ROOT/'tools/Assert-RepoIdentity.ps1')],check=True)
    dest=ROOT/'src/Quest.Studio/CaptureComposer';dest.mkdir(parents=True,exist_ok=True)
    for name,value in contents.items():(dest/name).write_bytes(value)
    (dest/'manifest.json').write_text(json.dumps({**manifest,'artifact':pin},indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--artifact',type=Path,required=True);p.add_argument('--pin',type=Path,required=True);args=p.parse_args();install(args.artifact,json.loads(args.pin.read_text(encoding='utf-8')))
