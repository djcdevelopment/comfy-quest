#!/usr/bin/env python3
"""Exercise the installed campaign APIs; keep rehearsal distinct from live evidence."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import urllib.error
import urllib.request
from urllib.parse import urlsplit


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('operation', choices=('scene', 'rehearse', 'refusals'))
    p.add_argument('--origin', default='http://127.0.0.1:8085')
    p.add_argument('--guild', required=True)
    p.add_argument('--campaign', required=True)
    p.add_argument('--output', required=True, type=Path)
    p.add_argument('--snapshot', type=int, default=1)
    p.add_argument('--bounds', nargs=4, type=float, default=[370, 490, 150, 290], metavar=('MIN_X', 'MAX_X', 'MIN_Z', 'MAX_Z'))
    a = p.parse_args()
    origin = urlsplit(a.origin)
    assert origin.scheme == 'http' and origin.hostname in ('localhost', '127.0.0.1') and not origin.username
    assert all(v and all(c.isascii() and (c.isalnum() or c in '-_.') for c in v) for v in (a.guild, a.campaign))
    a.output.mkdir(parents=True, exist_ok=True)
    token = json.load(urllib.request.urlopen(a.origin+'/api/v1/workbench/security'))['browser_token']
    receipt = {'schema':'creator-campaign-probe/v1', 'operation':a.operation, 'state':'running', 'checks':[]}
    cp = 'creator-campaigns/'+a.guild+'/'+a.campaign

    def call(method, path, body=None, expected=200, binary=False):
        req = urllib.request.Request(a.origin+'/api/v2/quest-studio/'+path,
            data=None if body is None else json.dumps(body).encode(), method=method,
            headers={'Content-Type':'application/json','X-Workbench-Token':token})
        try: response = urllib.request.urlopen(req, timeout=45)
        except urllib.error.HTTPError as error: response = error
        with response:
            data = response.read()
            result = data if binary else json.loads(data)
            assert response.status == expected, (response.status, result)
            return (result, dict(response.headers)) if binary else result

    def check(name, condition, evidence):
        receipt['checks'].append({'name':name,'passed':bool(condition),'evidence':evidence})
        assert condition, name

    try:
        context = call('GET', cp)
        campaign = context['campaign']
        attempt = campaign['attempt']
        receipt.update(guild_id=a.guild, campaign_id=a.campaign, attempt_id=attempt['attempt_id'])
        if a.operation == 'scene':
            x0,x1,z0,z1 = a.bounds
            data, headers = call('POST', 'creator/scene', {'snapshot_id':a.snapshot,
                'min_x':x0,'max_x':x1,'min_z':z0,'max_z':z1}, binary=True)
            scene_id = next(value for key,value in headers.items() if key.lower() == 'x-steward-scene-id')
            (a.output/'scene.svca').write_bytes(data)
            linked = call('POST',cp+'/scene',{'attempt_id':attempt['attempt_id'],'scene_id':scene_id})
            check('measured_snapshot_linked', linked.get('ok'), linked)
            receipt.update(scene_sha256=hashlib.sha256(data).hexdigest(), proof_level='saved-world geometry; not live actors or quest outcomes')
        elif a.operation == 'rehearse':
            for project in campaign['projects']:
                draft = call('GET','projects/'+project['project_id'])
                routes = [route for node in draft['nodes'] for route in node['routes'] if route.get('event') == 'kill']
                assert len(routes) == 1
                route = routes[0]
                good = {'kind':'event','event_name':route['event'],'target':route['target'],'actor_role':'player','fields':route['where']}
                wrong = copy.deepcopy(good)
                wrong['target'] = '$enemy_greydwarf'
                result = call('POST','projects/'+project['project_id']+'/rehearse',{'mode':'manual','steps':[wrong,good]})
                check(project['title']+' authored route', result['outcome']=='complete' and result['trace'][0].get('outcome')!='complete', result)
            after = call('GET',cp)['campaign']['attempt']
            check('live_attempt_unchanged', after['attempt_id']==attempt['attempt_id'] and after['state']==attempt['state'], {'before':attempt['state'],'after':after['state']})
            receipt['proof_level'] = 'synthetic Studio rehearsal; no live game events or continuation proof'
        else:
            last = next(op for op in reversed(context['operations']) if op['state']=='completed' and op['request']['operation'] in ('campaign_play','campaign_reset'))
            replay = call('POST','creator-operations',last['request'])
            check('same_command_replayed', replay.get('replayed') and replay['operation']['operation_id']==last['operation_id'], replay)
            conflict = copy.deepcopy(last['request']); conflict['expected_campaign_revision'] += 1
            denied = call('POST','creator-operations',conflict,expected=409)
            check('conflicting_command_refused', denied.get('error')=='creator_command_id_conflict', denied)
            import uuid
            stale = copy.deepcopy(last['request']); stale.update(command_id=str(uuid.uuid4()),operation='campaign_reset_preview',attempt_id=attempt['attempt_id'],expected_campaign_revision=campaign['revision']+1,confirm=False,preview_token=None)
            denied = call('POST','creator-operations',stale,expected=409)
            check('stale_revision_refused', denied.get('error')=='campaign_revision_conflict', denied)
            stale.update(command_id=str(uuid.uuid4()),expected_campaign_revision=campaign['revision'],attempt_id='prior-attempt-does-not-qualify')
            denied = call('POST','creator-operations',stale,expected=409)
            check('prior_attempt_refused', denied.get('error')=='campaign_attempt_changed', denied)
            receipt['proof_level'] = 'installed operation journal and refusal paths; no gameplay completion'
        receipt['state'] = 'passed'
    except Exception as error:
        receipt.update(state='failed', error=str(error))
        raise
    finally:
        path = a.output/(a.operation+'.json')
        path.write_text(json.dumps(receipt,indent=2)+'\n')
        print(json.dumps({'receipt':str(path),'state':receipt['state']}))


if __name__ == '__main__': main()
