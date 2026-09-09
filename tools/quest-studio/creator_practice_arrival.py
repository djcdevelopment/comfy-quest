#!/usr/bin/env python3
"""Walk the actual sparse fixture approach and open its supply chest with native input."""
import argparse
import json
import math
from pathlib import Path
import subprocess
import time
import uuid
import architectural_live_probe as base

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--run-root', type=Path, required=True)
p.add_argument('--session', required=True)
a = p.parse_args()
run = a.run_root.resolve()
game = Path('/home/derek/valheim')
assert run.parent == Path('/home/derek/valheim-capture/creator-dm/runs')
assert base.read_json(game / 'BepInEx/config/creator-dm-install.lock.json') == {
    'session_id': a.session, 'run_root': str(run)}
out = run / ('arrival-lap-' + base.utc_now().strftime('%Y%m%dT%H%M%SZ'))
out.mkdir()
status = game / 'BepInEx/config/comfy-quest-lab/status/showcase.json'
control = run / 'uinput-control'

def observe():
    d = base.read_json(status)
    assert time.time() - status.stat().st_mtime < 2
    assert d['creator_session_id'] == a.session and d['world_uid'] == '-7600395338659582326'
    assert d['protected_player'] and d['daylight'] and d['arrival_graves'] == 0
    assert d['lodge_pieces'] == 40
    assert len(d['signs']) == 1, 'sparse_fixture_requires_one_sign'
    return d

def action(operation, **fields):
    request = {'schema': 'creator-dm-uinput-request/v1', 'request_id': uuid.uuid4().hex,
               'created_unix': time.time(), 'operation': operation, **fields}
    base.atomic_json(control / 'request.json', request)
    for _ in range(400):
        result = base.read_json(control / 'status.json')
        if result.get('request_id') == request['request_id'] and result['state'] == 'released':
            with (out / 'inputs.jsonl').open('a') as stream:
                stream.write(json.dumps({'request': request, 'result': result}) + '\n')
            return
        time.sleep(.01)
    raise RuntimeError('input_release_timeout')

result = {'state': 'failed', 'receipt': str(out),
          'proof_boundary': 'Native walk and interact; inventory visibility and screenshot verify resupply.'}
try:
    initial = observe()
    assert not initial.get('inventory_visible'), 'close_inventory_before_lap'
    assert math.hypot(initial['player']['x']-415, initial['player']['z']-232) < 10, 'prepare_arrival_before_lap'
    base.atomic_json(out / 'before.json', initial)
    sign = initial['signs'][0]
    point = {'x': sign['x'] - 1, 'y': sign['y'] - .6, 'z': sign['z'] + 1}
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        d = observe()
        player, camera = d['player'], d['camera']
        distance = math.hypot(point['x']-player['x'], point['z']-player['z'])
        yaw = math.degrees(math.atan2(point['x']-player['x'], point['z']-player['z']))
        error = (yaw-camera['yaw']+180)%360-180
        eye = camera['position']
        pitch = -math.degrees(math.atan2(point['y']-eye['y'], math.hypot(point['x']-eye['x'],point['z']-eye['z'])))
        current_pitch = (camera['pitch']+180)%360-180
        action('aim', dx=max(-1200,min(1200,round(error/.05))), dy=max(-800,min(800,round((pitch-current_pitch)/.05))))
        time.sleep(.18)  # Wait for a fresh game observation before applying another correction.
        if abs(error) > 10:
            continue
        if distance <= 3:
            action('interact', seconds=.15)
            for _ in range(20):
                time.sleep(.1)
                if observe().get('inventory_visible'):
                    break
            else:
                raise RuntimeError('supply_inventory_did_not_open')
            break
        action('move', key='w', seconds=min(.4, max(.08,(distance-1.6)/4)))
    else:
        raise RuntimeError('supply_approach_timeout')
    result['state'] = 'supplies_open'
except Exception as error:
    result['error'] = str(error)
finally:
    base.atomic_json(out / 'after.json', base.read_json(status))
    subprocess.run(['ffmpeg','-v','error','-nostdin','-f','x11grab','-video_size','3440x1245',
        '-i',':0+0,195','-frames:v','1','-q:v','3',str(out/'supplies.jpg')],check=True)
    base.atomic_json(out / 'result.json', result)
    print(json.dumps(result))
if result['state'] != 'supplies_open':
    raise SystemExit(1)
