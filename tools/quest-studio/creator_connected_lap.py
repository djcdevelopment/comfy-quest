#!/usr/bin/env python3
"""Operate the connected AM4 lap using the existing released recovery boundary."""
import argparse
import datetime as dt
import json
from pathlib import Path
import subprocess
import shutil
import sys

import architectural_live_probe as base
import creator_dm_live_probe as recovery

parser = argparse.ArgumentParser()
parser.add_argument('operation', choices=('prepare', 'reload', 'checkpoint', 'enter', 'arm', 'fixture_status', 'fixture_prepare', 'capture', 'archive', 'stop', 'restore'))
parser.add_argument('--run-root', type=Path, required=True)
parser.add_argument('--session', required=True)
parser.add_argument('--manifest', type=Path)
parser.add_argument('--baseline', type=Path)
args = parser.parse_args()
game = Path('/home/derek/valheim')
unity = Path('/home/derek/.config/unity3d/IronGate/Valheim')
run = args.run_root.resolve()
assert run.parent == Path('/home/derek/valheim-capture/creator-dm/runs')
session = recovery.Session(game, unity, run, 'am4', args.session)
if args.operation == 'prepare':
    assert args.manifest and args.baseline
    previous = base.read_json(args.baseline / 'creator-session.json')
    assert previous['world_uid'] == recovery.WORLD_UID
    sources = previous['world_backup']
    assert len(sources) == 2
    for record in sources:
        source = Path(record['backup']).resolve()
        assert source.parent == (args.baseline / 'session-world').resolve()
        assert source.name in ('ComfyQuestDemo.db', 'ComfyQuestDemo.fwl')
        assert base.sha256(source) == record['sha256']
    session.prepare(args.manifest)
    try:
        for record in sources:
            source = Path(record['backup'])
            base.atomic_copy(source, unity / 'worlds_local' / source.name)
        result = session.checkpoint()
        result['venue_source'] = str(args.baseline / 'session-world')
        base.atomic_json(run / 'connected-venue.json', result)
    except Exception:
        session.restore()
        raise
elif args.operation == 'reload':
    session.stopped()
    assert session.read()['state'] == 'prepared'
    context = base.read_json(run / 'creator-session.json')
    for record in context['world_backup']:
        source = Path(record['backup']).resolve()
        assert source.parent == run / 'session-world' and source.name in ('ComfyQuestDemo.db', 'ComfyQuestDemo.fwl')
        assert base.sha256(source) == record['sha256']
    for record in context['world_backup']:
        source = Path(record['backup'])
        base.atomic_copy(source, unity / 'worlds_local' / source.name)
    result = {'state': 'checkpoint_reloaded', 'session_id': args.session}
elif args.operation == 'enter':
    session.stopped()
    assert session.read()['state'] == 'prepared'
    now = base.utc_now()
    request = {'schema': base.WORLD_ENTRY_REQUEST_SCHEMA,
        'request_id': 'connected-entry-' + now.strftime('%Y%m%dT%H%M%SZ'),
        'created_utc': base.iso(now), 'expires_utc': base.iso(now + dt.timedelta(minutes=15)),
        'expected_machine': 'am4', 'expected_world_uid': recovery.WORLD_UID,
        'world_name': recovery.WORLD, 'world_display_name': 'Comfy Quest Demo',
        'character_profile': recovery.CHARACTER, 'creator_session_id': args.session}
    runtime = base.owned_paths(game)['runtime']
    base.atomic_json(runtime / 'requests/world-entry.json', request, create=True)
    base.atomic_json(run / 'world-entry-request.json', request)
    launch = argparse.Namespace(valheim_root=game, run_root=run / ('launch-' + now.strftime('%H%M%S')), display=':0')
    launch.run_root.mkdir()
    print(json.dumps(base.launch(launch)), flush=True)
    base.atomic_json(run / 'current-launch.json', {'directory': str(launch.run_root)})
    result = base.wait_world_entry(runtime / 'status/world-entry.json', request['request_id'], 180)
    base.atomic_json(run / 'world-entry-receipt.json', result)
    subprocess.run([sys.executable, str(Path(__file__).with_name('creator_live_input.py')),
        '--run-root', str(run), '--session', args.session, 'console', 'devcommands', 'god', 'ghost'], check=True)
elif args.operation == 'checkpoint':
    result = session.checkpoint()
elif args.operation in ('arm', 'fixture_status', 'fixture_prepare'):
    assert session.read()['state'] == 'prepared'
    driver = base.LiveDriver(argparse.Namespace(valheim_root=game, run_root=run,
        machine='am4', world_uid=recovery.WORLD_UID, session=args.session, request_timeout=30), {})
    result = driver.request('runtime', 'arm') if args.operation == 'arm' else driver.request('lab',
        'signature_hunt_prepare' if args.operation == 'fixture_prepare' else 'signature_hunt_status')
elif args.operation == 'capture':
    assert session.read()['state'] == 'prepared'
    capture = run / ('capture-' + base.utc_now().strftime('%H%M%S'))
    capture.mkdir()
    result = base.capture_screenshot(capture)
elif args.operation == 'archive':
    assert session.read()['state'] == 'prepared'
    destination = run / ('evidence-' + base.utc_now().strftime('%Y%m%dT%H%M%SZ'))
    destination.mkdir()
    for name in ('comfy-quest-runtime', 'comfy-quest-lab', 'comfy-quest-creator'):
        shutil.copytree(game / 'BepInEx/config' / name, destination / name)
    result = {'state': 'evidence_archived', 'session_id': args.session,
        'directory': str(destination), 'files': recovery.tree_hash(destination),
        'boundary': 'Live copied evidence; original recovery backups remain separate.'}
    base.atomic_json(run / 'latest-evidence.json', result)
    print(json.dumps({k: v for k, v in result.items() if k != 'files'}))
    sys.exit(0)
elif args.operation == 'stop':
    assert session.read()['state'] == 'prepared'
    launch_root = Path(base.read_json(run / 'current-launch.json')['directory']) if (run / 'current-launch.json').exists() else run / 'launch'
    assert launch_root.resolve().parent == run
    launch = base.read_json(launch_root / 'launch.json')
    owned = {row['pid'] for row in launch['valheim_processes']}
    assert all(row['pid'] in owned for row in base.process_snapshot()), 'unowned_game_process'
    result = base.stop(argparse.Namespace(valheim_root=game, run_root=launch_root, timeout=45, display=':0'))
else:
    result = session.restore()
print(json.dumps(result, indent=2), flush=True)
