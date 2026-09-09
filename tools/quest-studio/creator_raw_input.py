"""Temporary, bounded real keyboard/mouse device for one leased AM4 Creator lap."""
import argparse
import fcntl
import json
import os
import signal
import struct
import time
from pathlib import Path

GAME = Path('/home/derek/valheim')
parser = argparse.ArgumentParser()
parser.add_argument('--run-root', type=Path, required=True)
parser.add_argument('--session', required=True)
args = parser.parse_args()
RUN = args.run_root.resolve()
assert RUN.parent == Path('/home/derek/valheim-capture/creator-dm/runs')
CONTROL = RUN / 'uinput-control'
WORLD_UID = '-7600395338659582326'
SCHEMA = 'creator-dm-uinput-status/v1'

assert os.geteuid() == 0
session = json.loads((RUN / 'creator-session.json').read_text())
assert session['state'] == 'active' and session['session_id'] == args.session
assert session['world_uid'] == WORLD_UID and CONTROL.is_dir()

def iow(type_code, number, size=4):
    return (1 << 30) | (size << 16) | (ord(type_code) << 8) | number

UI_SET_EVBIT = iow('U', 100)
UI_SET_KEYBIT = iow('U', 101)
UI_SET_RELBIT = iow('U', 102)
UI_DEV_CREATE = (ord('U') << 8) | 1
UI_DEV_DESTROY = (ord('U') << 8) | 2
EV_SYN, EV_KEY, EV_REL, SYN_REPORT = 0, 1, 2, 0
REL_X, REL_Y, KEY_LEFTCTRL, BTN_LEFT = 0, 1, 29, 272
KEYS = {'w': 17, 'a': 30, 's': 31, 'd': 32, 'e': 18, 'space': 57,
        '1': 2, '2': 3, '3': 4, '4': 5, '5': 6, '6': 7, '7': 8, '8': 9}
BUTTONS = {'attack': 272, 'block': 273, 'throw': 274}
DEVICE_KEYS = set(KEYS.values()) | set(BUTTONS.values()) | {KEY_LEFTCTRL}
running = True

def atomic_json(path, value):
    temporary = path.with_name(path.name + '.tmp-' + str(os.getpid()))
    temporary.write_text(json.dumps(value, sort_keys=True) + '\n')
    os.replace(temporary, path)

def emit(device, event_type, code, value):
    # Separate devices let the harness configure its temporary pointer without
    # changing the user's physical mouse or keyboard.
    targets = (device, pointer) if event_type == EV_SYN else (
        (pointer,) if event_type == EV_REL or code in BUTTONS.values() else (device,))
    for target in targets:
        target.write(struct.pack('llHHi', 0, 0, event_type, code, value))
        target.flush()

def stop(_signal, _frame):
    global running
    running = False

signal.signal(signal.SIGTERM, stop)
signal.signal(signal.SIGINT, stop)

with open('/dev/uinput', 'wb', buffering=0) as device, open('/dev/uinput', 'wb', buffering=0) as pointer:
    for event_type in (EV_SYN, EV_KEY):
        fcntl.ioctl(device, UI_SET_EVBIT, event_type)
    for code in set(KEYS.values()) | {KEY_LEFTCTRL}:
        fcntl.ioctl(device, UI_SET_KEYBIT, code)
    for event_type in (EV_SYN, EV_KEY, EV_REL):
        fcntl.ioctl(pointer, UI_SET_EVBIT, event_type)
    for code in BUTTONS.values():
        fcntl.ioctl(pointer, UI_SET_KEYBIT, code)
    for code in (REL_X, REL_Y):
        fcntl.ioctl(pointer, UI_SET_RELBIT, code)
    descriptor = struct.pack('80sHHHHI', b'creator-connected-keyboard', 0x03, 0x1209, 0x0002, 1, 0)
    descriptor += struct.pack('256i', *([0] * 256))
    device.write(descriptor)
    fcntl.ioctl(device, UI_DEV_CREATE)
    pointer.write(struct.pack('80sHHHHI', b'creator-connected-mouse', 0x03, 0x1209, 0x0003, 1, 0)
                  + struct.pack('256i', *([0] * 256)))
    fcntl.ioctl(pointer, UI_DEV_CREATE)
    try:
        time.sleep(2.0)
        atomic_json(CONTROL / 'status.json', {
            'schema': SCHEMA, 'state': 'ready', 'pid': os.getpid(), 'world_uid': WORLD_UID,
        })
        handled = None
        expires = time.monotonic() + 36000
        while running and time.monotonic() < expires:
            stop_path = CONTROL / 'stop.json'
            if stop_path.exists():
                request = json.loads(stop_path.read_text())
                if request.get('operation') == 'stop' and request.get('pid') == os.getpid():
                    atomic_json(CONTROL / 'status.json', {
                        'schema': SCHEMA, 'state': 'stopping', 'pid': os.getpid(),
                        'world_uid': WORLD_UID,
                    })
                    break
            request_path = CONTROL / 'request.json'
            if request_path.exists():
                request = json.loads(request_path.read_text())
                request_id = request.get('request_id')
                if (request.get('schema') == 'creator-dm-uinput-request/v1'
                        and request.get('operation') in ('hold-left-control', 'move', 'aim', 'attack', 'block', 'throw', 'equip', 'interact')
                        and request_id and request_id != handled):
                    entry = json.loads((GAME / 'BepInEx/config/comfy-quest-runtime/status/world-entry.json').read_text())
                    assert entry['state'] == 'entered' and entry['creator_session_id'] == args.session
                    assert entry['world_uid'] == WORLD_UID
                    lease = json.loads((GAME / 'BepInEx/config/creator-dm-install.lock.json').read_text())
                    assert lease == {'session_id': args.session, 'run_root': str(RUN)}
                    handled = request_id
                    operation = request['operation']
                    if operation != 'hold-left-control':
                        # No commands or arbitrary key codes. Each action releases in finally.
                        assert abs(time.time() - float(request['created_unix'])) < 15
                        if operation == 'aim':
                            dx, dy = int(request['dx']), int(request['dy'])
                            assert -1200 <= dx <= 1200 and -800 <= dy <= 800
                            emit(device, EV_REL, REL_X, dx)
                            emit(device, EV_REL, REL_Y, dy)
                            emit(device, EV_SYN, SYN_REPORT, 0)
                        else:
                            duration = float(request.get('seconds', .15))
                            assert .05 <= duration <= 3.0
                            if operation == 'move':
                                name = request['key']; assert name in ('w', 'a', 's', 'd', 'space')
                                code = KEYS[name]
                            elif operation == 'equip':
                                name = request['key']; assert name in ('1','2','3','4','5','6','7','8')
                                code = KEYS[name]
                            elif operation == 'interact': code = KEYS['e']
                            else: code = BUTTONS[operation]
                            try:
                                emit(device, EV_KEY, code, 1); emit(device, EV_SYN, SYN_REPORT, 0)
                                time.sleep(duration)
                            finally:
                                emit(device, EV_KEY, code, 0); emit(device, EV_SYN, SYN_REPORT, 0)
                        atomic_json(CONTROL / 'status.json', {
                            'schema': SCHEMA, 'state': 'released', 'pid': os.getpid(),
                            'world_uid': WORLD_UID, 'request_id': request_id, 'operation': operation,
                        })
                        continue
                    emit(device, EV_KEY, KEY_LEFTCTRL, 1); emit(device, EV_SYN, SYN_REPORT, 0)
                    atomic_json(CONTROL / 'status.json', {
                        'schema': SCHEMA, 'state': 'holding', 'pid': os.getpid(),
                        'world_uid': WORLD_UID, 'request_id': request_id,
                    })
                    time.sleep(.5)
                    emit(device, EV_KEY, BTN_LEFT, 1); emit(device, EV_SYN, SYN_REPORT, 0)
                    time.sleep(.15)
                    emit(device, EV_KEY, BTN_LEFT, 0); emit(device, EV_SYN, SYN_REPORT, 0)
                    time.sleep(.5)
                    emit(device, EV_KEY, KEY_LEFTCTRL, 0); emit(device, EV_SYN, SYN_REPORT, 0)
                    atomic_json(CONTROL / 'status.json', {
                        'schema': SCHEMA, 'state': 'released', 'pid': os.getpid(),
                        'world_uid': WORLD_UID, 'request_id': request_id,
                    })
            time.sleep(.05)
    finally:
        for code in DEVICE_KEYS: emit(device, EV_KEY, code, 0)
        emit(device, EV_SYN, SYN_REPORT, 0)
        fcntl.ioctl(device, UI_DEV_DESTROY)
        fcntl.ioctl(pointer, UI_DEV_DESTROY)

atomic_json(CONTROL / 'status.json', {
    'schema': SCHEMA, 'state': 'stopped', 'pid': os.getpid(), 'world_uid': WORLD_UID,
})
