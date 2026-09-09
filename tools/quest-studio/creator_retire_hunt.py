#!/usr/bin/env python3
"""Retire one exactly pinned practice run using native Runtime controls."""
import argparse
import datetime as dt
import json
from pathlib import Path
import time
import uuid
import architectural_live_probe as base

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--run-root', type=Path, required=True)
p.add_argument('--session', required=True)
p.add_argument('--run-id', required=True)
p.add_argument('--experience', required=True)
p.add_argument('--content-hash', required=True)
p.add_argument('--binding-instance', required=True)
a = p.parse_args()
run = a.run_root.resolve()
assert run.parent == Path('/home/derek/valheim-capture/creator-dm/runs')
game = Path('/home/derek/valheim')
assert base.read_json(game / 'BepInEx/config/creator-dm-install.lock.json') == {
    'session_id': a.session, 'run_root': str(run)}
root = game / 'BepInEx/config/comfy-quest-runtime'
status = base.read_json(root / 'status/runs.json')
assert status['machine'] == 'am4' and status['world_uid'] == '-7600395338659582326'
assert time.time() - (root / 'status/runs.json').stat().st_mtime < 3
matches = [r for r in status['runs'] if r['run_id'] == a.run_id]
assert len(matches) == 1
current = matches[0]
assert current['experience_id'] == a.experience
assert current['content_hash'] == a.content_hash
assert current['binding_instance_id'] == a.binding_instance
binding = base.read_json(root / 'state/binding-changes' / (a.binding_instance + '.json'))
assert binding['applied']['content_hash'] == a.content_hash
assert binding['applied']['binding_instance_id'] == a.binding_instance
proof = {'proof_level': 'native Runtime retirement and binding recovery', 'operations': []}
output = run / ('retire-hunt-' + base.utc_now().strftime('%Y%m%dT%H%M%SZ') + '.json')

def request(operation, **fields):
    now = base.utc_now()
    rid = 'practice-retire-' + uuid.uuid4().hex
    body = {'schema': 'comfy-quest-runtime-run-control-request/v1', 'request_id': rid,
            'operation': operation, 'created_utc': base.iso(now),
            'expires_utc': base.iso(now + dt.timedelta(minutes=2)),
            'expected_machine': 'am4', 'expected_world_uid': status['world_uid'],
            'creator_session_id': a.session, **fields}
    base.atomic_json(root / 'requests/run-control.json', body, create=True)
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        found = list((root / 'receipts/run-control').rglob(rid + '.json'))
        if found:
            assert len(found) == 1
            receipt = base.read_json(found[0])
            assert receipt['request_id'] == rid and receipt['operation'] == operation
            assert receipt['creator_session_id'] == a.session
            assert receipt['machine'] == 'am4' and receipt['world_uid'] == status['world_uid']
            proof['operations'].append({'request': body, 'receipt': receipt})
            base.atomic_json(output, proof)
            assert receipt['state'] in ('previewed', 'completed'), receipt.get('detail')
            return receipt
        time.sleep(.1)
    raise RuntimeError('native_retirement_receipt_timeout')

preview = request('preview_retire', run_id=a.run_id)
request('apply_retire', run_id=a.run_id,
        preview_token=preview['retire_preview']['preview_token'], confirm_retire=True)
request('restore_binding', binding_zdo=binding['binding_zdo'], binding_change_id=a.binding_instance)
print(json.dumps({'state': 'retired', 'run_id': a.run_id, 'receipt': str(output)}))
