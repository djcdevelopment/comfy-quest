#!/usr/bin/env python3
"""Linux campaign prerequisites within an already prepared, recoverable Creator session."""
import argparse
import json
from pathlib import Path
import socket
import subprocess
import sys

import architectural_live_probe as base
import creator_dm_live_probe as recovery


def context(game):
    lease = base.read_json(game / recovery.LOCK)
    run = Path(lease['run_root']).resolve()
    state = base.read_json(run / 'install.json')
    session = recovery.Session(game, Path(state['unity']), run, socket.gethostname(), lease['session_id'])
    state = session.read()
    if state['state'] != 'prepared':
        raise RuntimeError('campaign_recovery_session_not_prepared')
    for record in state['records']:
        backup = recovery.contained(run / 'backup' / record['root'], record['relative'])
        if recovery.tree_hash(backup) != record['before']:
            raise RuntimeError('recovery_backup_hash_mismatch:' + record['relative'])
    creator = base.read_json(game / recovery.SESSION)
    if (creator.get('state') != 'active' or creator.get('session_id') != session.session
            or creator.get('world_uid') != recovery.WORLD_UID
            or creator.get('character_profile') != recovery.CHARACTER):
        raise RuntimeError('campaign_creator_session_mismatch')
    return session


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--operation-id', required=True)
    parser.add_argument('--valheim-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--clear-preparation')
    args = parser.parse_args()
    base.require_safe_token(args.operation_id, 'operation_id')
    game = args.valheim_root.resolve()
    session = context(game)
    output = args.output.resolve()
    if output.is_relative_to(game) or output.is_relative_to(session.unity) or output.is_relative_to(session.run / 'backup'):
        raise RuntimeError('campaign_output_overlaps_owned_data')
    output.mkdir(parents=True, exist_ok=True)
    resumed = bool(base.process_snapshot())
    if not resumed:
        subprocess.run([sys.executable, str(Path(__file__).with_name('creator_connected_lap.py')),
            'enter', '--run-root', str(session.run), '--session', session.session],
            check=True, stdout=sys.stderr)
    entry = base.read_json(game / 'BepInEx/config/comfy-quest-runtime/status/world-entry.json')
    if any(entry.get(k) != v for k, v in {'state':'entered', 'machine':session.machine,
            'world_uid':recovery.WORLD_UID, 'creator_session_id':session.session}.items()):
        raise RuntimeError('campaign_world_entry_mismatch')
    driver = base.LiveDriver(argparse.Namespace(valheim_root=game, run_root=output,
        machine=session.machine, world_uid=recovery.WORLD_UID, session=session.session, request_timeout=40), {})
    driver.request('runtime', 'arm')
    if args.clear_preparation:
        status = driver.request('lab', 'signature_hunt_status')
        fixture = read_fixture(game, status)
        if fixture.get('preparation_id') != args.clear_preparation:
            raise RuntimeError('campaign_fixture_ownership_changed')
        driver.request('lab', 'signature_hunt_clear')
        result = {'state':'cleared', 'preparation_id':args.clear_preparation}
    else:
        driver.request('lab', 'showcase_prepare')
        receipt = driver.request('lab', 'signature_hunt_prepare')
        fixture = read_fixture(game, receipt)
        fixture_path = output / 'fixture.json'
        base.atomic_json(fixture_path, fixture)
        result = {'schema':'comfy-quest-studio-campaign-play-prerequisites/v1',
            'operation_id':args.operation_id, 'state':'ready',
            'creator_session_id':session.session, 'machine':session.machine,
            'world_uid':recovery.WORLD_UID, 'resumed_running_session':resumed,
            'fixture_request_id':receipt['request_id'], 'fixture_receipt_path':str(fixture_path),
            'fixture_receipt_sha256':base.sha256(fixture_path), 'fixture':fixture}
    base.atomic_json(output / 'prerequisites.json', result)
    print(json.dumps(result))


def read_fixture(game, receipt):
    # Lab reports the durable preparation sidecar, which has its own preparation ID.
    path = Path(receipt.get('evidence_path', '')).resolve()
    parent = (game / 'BepInEx/config/comfy-quest-lab/receipts/fixtures').resolve()
    if path.parent != parent or not path.is_file() or path.stat().st_size > 1024 * 1024:
        raise RuntimeError('campaign_fixture_receipt_unavailable')
    fixture = base.read_json(path)
    if (fixture.get('schema') not in ('comfy-questlab-signature-hunt-fixture/v1', 'comfy-questlab-signature-hunt-fixture/v2')
            or fixture.get('world_uid') != receipt['world_uid']
            or fixture.get('machine') != receipt['machine']
            or (receipt['operation'] == 'signature_hunt_prepare' and fixture.get('request_id') != receipt['request_id'])):
        raise RuntimeError('campaign_fixture_receipt_identity_mismatch')
    return fixture


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
