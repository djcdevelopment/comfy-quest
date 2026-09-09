#!/usr/bin/env python3
"""Play one reviewed hunt using real input and read-only observed game facts."""
import argparse
import json
import math
from pathlib import Path
import time
import uuid

import architectural_live_probe as base

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--run-root', type=Path, required=True)
parser.add_argument('--session', required=True)
parser.add_argument('--experience', required=True)
parser.add_argument('--target', choices=('lox', 'drake', 'deathsquito', 'draugr', 'greyling', 'boar'), required=True)
parser.add_argument('--mechanic', choices=('thrown_spear', 'melee'), default='thrown_spear')
parser.add_argument('--expect', choices=('complete', 'finish_rejection'), default='complete')
parser.add_argument('--seconds', type=int, default=180)
parser.add_argument('--content-hash')
parser.add_argument('--binding-instance')
args = parser.parse_args()
assert 10 <= args.seconds <= 600
assert args.mechanic != 'melee' or args.target in ('draugr', 'greyling', 'boar')
run = args.run_root.resolve()
assert run.parent == Path('/home/derek/valheim-capture/creator-dm/runs')
game = Path('/home/derek/valheim')
config = game / 'BepInEx/config'
control = run / 'uinput-control'
assert base.read_json(config / 'creator-dm-install.lock.json') == {
    'session_id': args.session, 'run_root': str(run)}
assert base.read_json(control / 'pointer-configuration.json')['middle_button_scrolling'] is False
status_path = config / 'comfy-quest-lab/status/showcase.json'
runtime = config / 'comfy-quest-runtime'
entries = base.read_json(runtime / 'status/runs.json')['runs']
matches = [r for r in entries if r['experience_id'] == args.experience and not r.get('outcome')]
assert len(matches) == 1, 'one_active_expected_hunt_required'
assert not args.content_hash or matches[0]['content_hash'] == args.content_hash, 'hunt_content_changed'
assert not args.binding_instance or matches[0]['binding_instance_id'] == args.binding_instance, 'hunt_binding_changed'
run_id = matches[0]['run_id']
target_name = '$enemy_' + args.target
stamp = base.utc_now().strftime('%Y%m%dT%H%M%SZ')
out = run / ('hunt-lap-' + stamp)
out.mkdir()
observations = (out / 'observations.jsonl').open('a')
shots = 0
last_throw = 0.0
last_status = 0.0
last_equip = 0.0
last_walk_position = None
stuck_since = None


def read_status():
    for _ in range(5):
        try:
            d = base.read_json(status_path)
            assert time.time() - status_path.stat().st_mtime < 2
            assert d['creator_session_id'] == args.session and d['protected_player']
            assert d['world_uid'] == '-7600395338659582326'
            return d
        except FileNotFoundError:
            time.sleep(.02)
    raise RuntimeError('live_observation_unavailable')


def action(operation, **fields):
    request = {'schema': 'creator-dm-uinput-request/v1', 'request_id': operation + '-' + uuid.uuid4().hex,
               'operation': operation, 'created_unix': time.time(), **fields}
    base.atomic_json(control / 'request.json', request)
    for _ in range(400):
        response = base.read_json(control / 'status.json')
        if response.get('request_id') == request['request_id'] and response['state'] == 'released':
            with (run / 'game-input.jsonl').open('a') as log:
                log.write(json.dumps({'request': request, 'result': response}) + '\n')
            return
        time.sleep(.01)
    raise RuntimeError('native_input_release_timeout')


def aim(d, point, height=1.5, pickup=False):
    p = d['player']
    distance = math.hypot(point['x'] - p['x'], point['z'] - p['z'])
    yaw = math.degrees(math.atan2(point['x'] - p['x'], point['z'] - p['z']))
    error = (yaw - d['camera']['yaw'] + 180) % 360 - 180
    pitch = (d['camera']['pitch'] + 180) % 360 - 180
    if pickup:
        eye = d['camera']['position']
        desired_pitch = -math.degrees(math.atan2(point['y']-eye['y'], math.hypot(point['x']-eye['x'],point['z']-eye['z'])))
    else:
        desired_pitch = -math.degrees(math.atan2(point['y'] - p['y'] - height, distance))
    action('aim', dx=max(-1200, min(1200, round(error / .05))),
           dy=max(-800, min(800, round((desired_pitch - pitch) / .05))))
    return distance, error


result = {'schema': 'creator-hunt-input-lap/v1', 'run_id': run_id, 'experience_id': args.experience,
          'target': args.target, 'mechanic': args.mechanic, 'creator_session_id': args.session,
          'expected_result': args.expect,
          'proof_level': 'actual game inputs; result requires matching Runtime receipt'}
try:
    deadline = time.monotonic() + (min(45, args.seconds) if args.mechanic == 'melee' else args.seconds)
    while time.monotonic() < deadline:
        d = read_status()
        expected = [r for r in base.read_json(runtime / 'status/runs.json')['runs'] if r['run_id'] == run_id]
        assert len(expected) == 1, 'expected_hunt_no_longer_reported'
        outcomes = [r for r in expected if r.get('outcome')]
        if outcomes:
            result.update(state=outcomes[0]['outcome'], runtime=outcomes[0])
            break
        if args.expect == 'finish_rejection' and shots:
            rejected = []
            for path in (runtime / 'receipts').glob('*.json'):
                receipt = base.read_json(path)
                if receipt.get('run_id') == run_id and any(
                        diagnostic.get('Code') == 'hunt.finish_requirement'
                        for diagnostic in receipt.get('diagnostics', [])):
                    rejected.append({'path': str(path), 'receipt': receipt})
            if rejected:
                assert not expected[0].get('outcome'), 'rejected_hit_has_an_outcome'
                result.update(state='finish_rejection', rejection_receipts=rejected, runtime=expected[0])
                break
        assert expected[0].get('binding_available') is not False, 'hunt_binding_unavailable_replay_required'
        targets = [a for a in d['actors'] if a['name'] == target_name
                   and a.get('spawn_action_key', '').startswith(run_id + '|')
                   and a.get('spawn_content_hash') == matches[0]['content_hash']]
        assert len(targets) <= 1, 'ambiguous_live_target'
        now = time.monotonic()
        if now - last_status > 2:
            row = {'observed_utc': d['observed_utc'], 'player': d['player'], 'health': d['health'],
                   'stamina': d['stamina'], 'weapon': d['equipped_prefab'], 'shots': shots,
                   'targets': targets, 'ground_spears': d['ground_spears']}
            observations.write(json.dumps(row) + '\n'); observations.flush()
            print(json.dumps({'shots': shots, 'health': d['health'], 'weapon': d['equipped_prefab'],
                              'target_health': targets[0]['health'] if targets else None}), flush=True)
            last_status = now
        if d['attacking'] or now - last_throw < 1.25:
            time.sleep(.12); continue
        if args.mechanic == 'melee':
            assert d.get('practice_profile') == 'integration-melee', 'integration_melee_profile_required'
            assert d['equipped_prefab'] == 'SwordCheat', 'prepare_practice_loadout_before_melee_lap'
            if targets:
                distance, error = aim(d, targets[0]['position'])
                if abs(error) < 15:
                    if distance <= 4 and d['stamina'] > 20 and not d['staggering']:
                        action('attack', seconds=.15); shots += 1; last_throw = time.monotonic()
                    elif distance > 4:
                        action('move', key='w', seconds=.4)
            time.sleep(.15)
            continue
        if d['equipped_prefab'] != 'SpearCarapace':
            dropped = sorted(d['ground_spears'], key=lambda p: math.hypot(p['x']-d['player']['x'],p['z']-d['player']['z']))
            if dropped:
                distance, error = aim(d, dropped[0], pickup=True)
                if abs(error) < 12 and distance > 2:
                    position = d['player']
                    if last_walk_position and math.hypot(position['x']-last_walk_position['x'], position['z']-last_walk_position['z']) < .2:
                        stuck_since = stuck_since or now
                    else:
                        stuck_since = None
                    if stuck_since and now-stuck_since > 3:
                        action('move', key='d', seconds=.8)
                        stuck_since = None
                    last_walk_position = position
                    action('move', key='w', seconds=min(.7, max(.08, (distance-.7)/4)))
                elif distance <= 2:
                    action('interact', seconds=.15)
            if now - last_equip > 1:
                action('equip', key='1', seconds=.1); last_equip = now
            time.sleep(.16); continue
        if not targets:
            time.sleep(.2); continue
        distance, error = aim(d, targets[0]['position'])
        if abs(error) > 12:
            time.sleep(.12); continue
        if distance > 16:
            action('move', key='w', seconds=.5)
        elif distance < 2.5:
            action('move', key='s', seconds=.4)
        elif d['stamina'] > 24 and not d['staggering']:
            action('throw', seconds=.15); shots += 1; last_throw = time.monotonic()
        time.sleep(.15)
    else:
        result['state'] = 'timeout'
    receipts = []
    for p in (runtime / 'receipts').glob('*.json'):
        receipt = base.read_json(p)
        if receipt.get('run_id') == run_id and receipt.get('operation') == 'transition':
            receipts.append({'path': str(p), 'receipt': receipt})
    result.update(shots=shots, transition_receipts=receipts)
    if result['state'] == 'complete':
        assert any(row['receipt']['status'] == 'complete' for row in receipts), 'completion_receipt_missing'
except KeyboardInterrupt:
    result.update(state='interrupted', shots=shots)
except Exception as error:
    result.update(state='failed', error=str(error), shots=shots)
finally:
    observations.close()
    base.atomic_json(out / 'result.json', result)
print(json.dumps({'state': result['state'], 'shots': shots, 'receipt': str(out / 'result.json')}), flush=True)
raise SystemExit(0 if result['state'] == args.expect else 1)
