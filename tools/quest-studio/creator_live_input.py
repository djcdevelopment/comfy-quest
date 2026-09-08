#!/usr/bin/env python3
"""Bounded real X11 input on the leased AM4 game; never injects Runtime events."""
import argparse
import ctypes as C
import json
from pathlib import Path
import time
import subprocess
import sys
import uuid

import architectural_live_probe as base
import creator_dm_live_probe as recovery

parser = argparse.ArgumentParser()
parser.add_argument('--run-root', type=Path, required=True)
parser.add_argument('--session', required=True)
parser.add_argument('operation', choices=('console', 'key', 'click', 'capture', 'raw-start', 'raw-drop', 'raw-stop'))
parser.add_argument('values', nargs='*')
args = parser.parse_args()
game = Path('/home/derek/valheim')
unity = Path('/home/derek/.config/unity3d/IronGate/Valheim')
assert recovery.Session(game, unity, args.run_root, 'am4', args.session).read()['state'] == 'prepared'
status = base.read_json(game / 'BepInEx/config/comfy-quest-runtime/status/world-entry.json')
assert status['state'] == 'entered' and status['world_uid'] == recovery.WORLD_UID
assert status['creator_session_id'] == args.session
window = base.discover_valheim_window(':0')
x, xt = C.CDLL('libX11.so.6'), C.CDLL('libXtst.so.6')
x.XOpenDisplay.argtypes = [C.c_char_p]; x.XOpenDisplay.restype = C.c_void_p
x.XStringToKeysym.argtypes = [C.c_char_p]; x.XStringToKeysym.restype = C.c_ulong
x.XKeysymToKeycode.argtypes = [C.c_void_p, C.c_ulong]; x.XKeysymToKeycode.restype = C.c_uint
x.XSetInputFocus.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_ulong]
x.XRaiseWindow.argtypes = [C.c_void_p, C.c_ulong]; x.XFlush.argtypes = [C.c_void_p]
xt.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
xt.XTestFakeMotionEvent.argtypes = [C.c_void_p, C.c_int, C.c_int, C.c_int, C.c_ulong]
xt.XTestFakeButtonEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
display = x.XOpenDisplay(b':0'); assert display
x.XRaiseWindow(display, window['id']); x.XSetInputFocus(display, window['id'], 2, 0); x.XFlush(display)


def key(name, down):
    code = x.XKeysymToKeycode(display, x.XStringToKeysym(name.encode())); assert code
    xt.XTestFakeKeyEvent(display, code, int(down), 0); x.XFlush(display)


def tap(name):
    key(name, True); time.sleep(.08); key(name, False); time.sleep(.08)


if args.operation == 'console':
    allowed = {'devcommands', 'god', 'ghost', 'pos', 'goto 419 235', 'goto 421 235', 'goto 425 231', 'goto 429 235',
               'spawn Resin 1', 'spawn Resin 1 1 1 p', 'save'}
    assert args.values and all(command in allowed for command in args.values)
    tap('F5'); time.sleep(.4)
    for command in args.values:
        for char in command:
            if char.isupper():
                key('Shift_L', True); time.sleep(.15)
            tap('space' if char == ' ' else char.lower())
            if char.isupper():
                time.sleep(.15); key('Shift_L', False)
        tap('Return'); time.sleep(.8)
    tap('F5')
elif args.operation == 'key':
    assert args.values and all(name in {'Tab', 'Escape', 'F5', 'w', 's', 'a', 'd', 'e', 'Return'} for name in args.values)
    for name in args.values: tap(name)
elif args.operation in ('click', 'raw-drop'):
    assert len(args.values) == 3
    px, py, scale = map(float, args.values)
    assert 0 <= px < window['width'] and 0 <= py < window['height'] and .4 <= scale <= 2
    xt.XTestFakeMotionEvent(display, 0, window['x'] + round(px/scale), window['y'] + round(py/scale), 0)
    x.XFlush(display); time.sleep(.3)
    if args.operation == 'click':
        xt.XTestFakeButtonEvent(display, 1, 1, 0); x.XFlush(display); time.sleep(.12)
        xt.XTestFakeButtonEvent(display, 1, 0, 0); x.XFlush(display); time.sleep(.4)
    else:
        control = args.run_root / 'uinput-control'
        assert base.read_json(control / 'status.json')['state'] in ('ready', 'released')
        request_id = 'drop-' + uuid.uuid4().hex
        base.atomic_json(control / 'request.json', {'schema': 'creator-dm-uinput-request/v1',
            'request_id': request_id, 'operation': 'hold-left-control'})
        for _ in range(200):
            result = base.read_json(control / 'status.json')
            if result.get('request_id') == request_id and result['state'] == 'released': break
            time.sleep(.05)
        else: raise RuntimeError('raw_input_release_timeout')
        print(json.dumps(result))
elif args.operation in ('raw-start', 'raw-stop'):
    assert not args.values
    control = args.run_root / 'uinput-control'
    if args.operation == 'raw-start':
        control.mkdir(mode=0o700)
        with (control / 'daemon.log').open('wb') as log:
            subprocess.Popen(['sudo', '-n', sys.executable, str(Path(__file__).with_name('creator_raw_input.py')),
                '--run-root', str(args.run_root), '--session', args.session],
                stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        for _ in range(100):
            if (control / 'status.json').exists(): break
            time.sleep(.05)
        assert base.read_json(control / 'status.json')['state'] == 'ready'
    else:
        result = base.read_json(control / 'status.json')
        base.atomic_json(control / 'stop.json', {'operation': 'stop', 'pid': result['pid']})
        for _ in range(100):
            if base.read_json(control / 'status.json')['state'] == 'stopped': break
            time.sleep(.05)
        assert base.read_json(control / 'status.json')['state'] == 'stopped'
    print(json.dumps(base.read_json(control / 'status.json')))
else:
    assert not args.values
    target = args.run_root / ('capture-' + base.utc_now().strftime('%H%M%S'))
    target.mkdir()
    result = base.capture_screenshot(target)
    result['directory'] = str(target)
    print(json.dumps(result))
print(json.dumps({'operation': args.operation, 'window': window, 'input_source': 'real_x11'}))
