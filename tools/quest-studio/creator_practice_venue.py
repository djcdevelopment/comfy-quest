#!/usr/bin/env python3
"""Inspect or rebuild the fixed Field Lodge in the leased AM4 practice world."""
import argparse
from pathlib import Path
import json
import socket

import architectural_live_probe as base

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('operation', choices=('inspect', 'rebuild'))
p.add_argument('--run-root', type=Path, required=True)
p.add_argument('--session', required=True)
a = p.parse_args()
run = a.run_root.resolve()
game = Path('/home/derek/valheim')
assert socket.gethostname() == 'am4'
assert run.parent == Path('/home/derek/valheim-capture/creator-dm/runs')
assert base.read_json(game / 'BepInEx/config/creator-dm-install.lock.json') == {
    'session_id': a.session, 'run_root': str(run)}
blueprint = 'tn0304-6ue2ukrad7ntjfoa7cvdmvwkcvsccusli5wrfvhq2rnkhtm2ehuq'
driver = base.LiveDriver(argparse.Namespace(valheim_root=game, run_root=run,
    machine='am4', world_uid='-7600395338659582326', session=a.session,
    request_timeout=45, blueprint=blueprint, placement_mode='at',
    x=425, y=77, z=233, yaw=0), {})
result = {'schema': 'creator-practice-venue/v1', 'session_id': a.session,
          'operation': a.operation, 'started_utc': base.iso(base.utc_now())}
try:
    check = driver.request('lab', 'blueprint_check', driver.lab_fields('blueprint_check'))
    result['check'] = check
    assert '40 buildable piece(s)' in check['detail'], 'reviewed_lodge_piece_count_changed'
    count = driver.count(driver.request('lab', 'blueprint_count', driver.lab_fields('blueprint_count')))
    result['before_count'] = count
    if a.operation == 'rebuild':
        assert count == 0, 'inspect_existing_lodge_before_rebuilding'
        authority = driver.request('runtime', 'build_on')
        assert authority.get('creator_build_enabled') is True
        try:
            result['build'] = driver.request('lab', 'blueprint_build', driver.lab_fields('blueprint_build', placed=True))
        finally:
            off = driver.request('runtime', 'build_off')
            assert off.get('creator_build_enabled') is False
        result['after_count'] = driver.count(driver.request('lab', 'blueprint_count', driver.lab_fields('blueprint_count')))
        assert result['after_count'] == 40
        diff = driver.request('lab', 'blueprint_diff', driver.lab_fields('blueprint_diff', placed=True))
        result['diff'] = diff
        assert f'capture diff {blueprint}: MATCH' in diff['detail'], 'lodge_geometry_mismatch'
    result['state'] = 'completed'
except Exception as error:
    result.update(state='failed', error=str(error))
finally:
    output = run / ('practice-venue-' + base.utc_now().strftime('%Y%m%dT%H%M%SZ') + '.json')
    base.atomic_json(output, result, create=True)
print(json.dumps({'state': result['state'], 'error': result.get('error'), 'receipt': str(output)}))
raise SystemExit(0 if result['state'] == 'completed' else 1)
