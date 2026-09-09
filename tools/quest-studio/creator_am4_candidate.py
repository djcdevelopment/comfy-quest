#!/usr/bin/env python3
"""Deploy a verified development candidate into the stopped, leased AM4 seat."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import tarfile
import time

import architectural_live_probe as base

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--run-root', type=Path, required=True)
p.add_argument('--session', required=True)
p.add_argument('--plugins', type=Path)
p.add_argument('--studio-name', required=True)
p.add_argument('--studio-sha', required=True)
a = p.parse_args()
run = a.run_root.resolve()
home = Path('/home/derek/valheim-capture/creator-dm')
game = Path('/home/derek/valheim')
stage = home / 'releases/derek-checklist-bootstrap'
assert socket.gethostname() == 'am4' and run.parent == home / 'runs'
assert base.read_json(game / 'BepInEx/config/creator-dm-install.lock.json') == {
    'session_id': a.session, 'run_root': str(run)}
if a.plugins:
    assert subprocess.run(['pgrep', '-x', 'valheim.x86_64'], stdout=subprocess.DEVNULL).returncode == 1
install = base.read_json(run / 'install.json')
assert install['state'] == 'prepared' and install['session_id'] == a.session
candidate = None
manifest = {'plugins': []}
if a.plugins:
    candidate = a.plugins.resolve()
    assert candidate.parent == stage and re.fullmatch(r'candidate-r\d+', candidate.name)
    manifest = json.loads((candidate / 'manifest.json').read_text(encoding='utf-8-sig'))
    assert manifest['session_id'] == a.session
    assert {r['name'] for r in manifest['plugins']} == {
        'ComfyQuestLab.dll', 'ComfyQuestRuntime.dll', 'ComfyQuestContracts.dll'}
    assert len(manifest['plugins']) == 3
    receipt = run / ('plugin-' + candidate.name + '.json')
    assert not receipt.exists()
    for row in manifest['plugins']:
        assert base.sha256(candidate / row['name']) == row['sha256']
        assert any(r['root'] == 'valheim' and r['relative'] == 'BepInEx/plugins/' + row['name']
                   for r in install['records'])

root = home / 'connected' / run.name
connection = root / 'connection.json'
current = base.read_json(connection)
proc = Path('/proc') / str(current['studio_pid'])
assert (proc / 'exe').resolve() == Path(current['studio_executable'])
environment = dict(item.split('=', 1) for item in (proc / 'environ').read_bytes().decode().split('\0') if '=' in item)
old_cwd = (proc / 'cwd').resolve()
assert re.fullmatch(r'studio-r\d+', a.studio_name)
archive = stage / (a.studio_name + '.tar.gz')
assert base.sha256(archive) == a.studio_sha
destination = root / a.studio_name
destination.mkdir()
with tarfile.open(archive) as tar:
    for member in tar.getmembers():
        assert (destination / member.name).resolve().is_relative_to(destination)
        assert not member.issym() and not member.islnk()
    tar.extractall(destination, filter='data')
executable = destination / 'Comfy.Quest.Studio.Host'
executable.chmod(0o755)
base.atomic_json(root / ('connection-before-' + a.studio_name + '.json'), current, create=True)
for row in manifest['plugins']:
    target = game / 'BepInEx/plugins' / row['name']
    row['before_sha256'] = base.sha256(target)
    base.atomic_copy(candidate / row['name'], target)
    assert base.sha256(target) == row['sha256']
if a.plugins:
    base.atomic_json(receipt, manifest, create=True)
os.kill(current['studio_pid'], signal.SIGTERM)
for _ in range(100):
    if not proc.exists():
        break
    time.sleep(.1)
assert not proc.exists(), 'owned_studio_did_not_stop'
with (root / (a.studio_name + '.log')).open('ab') as log:
    try:
        child = subprocess.Popen([str(executable)], cwd=destination, env=environment,
            stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    except Exception:
        recovered = subprocess.Popen([current['studio_executable']], cwd=old_cwd, env=environment,
            stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        current['studio_pid'] = recovered.pid
        base.atomic_json(connection, current)
        raise
current.update(studio_pid=child.pid, studio_executable=str(executable), studio_archive_sha256=a.studio_sha)
base.atomic_json(connection, current)
print(json.dumps({'candidate': candidate.name if candidate else 'studio-only', 'studio_pid': child.pid,
                  'studio_release': a.studio_name, 'receipt': str(receipt) if candidate else str(connection)}))
