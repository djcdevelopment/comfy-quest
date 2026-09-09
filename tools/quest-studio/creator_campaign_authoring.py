#!/usr/bin/env python3
"""Rehearse the typed Slayers authoring journey through an installed Studio API."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.error
import urllib.request
from urllib.parse import urlsplit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--origin', default='http://127.0.0.1:8085')
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--source-revision', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    origin = urlsplit(args.origin)
    assert origin.scheme == 'http' and origin.hostname in ('127.0.0.1', 'localhost') and not origin.username
    args.output.mkdir(parents=True, exist_ok=True)
    state_path = args.output / 'authoring.json'
    state = json.loads(state_path.read_text()) if state_path.exists() else {'schema':'creator-campaign-authoring/v1','steps':{}}
    source_bytes = args.source.read_bytes()
    source = json.loads(source_bytes)
    quests = [row for row in source['quests'] if row['quest_id'] in ('air_drop', 'cold_shot')]
    assert {row['quest_id'] for row in quests} == {'air_drop', 'cold_shot'}
    source_hash = hashlib.sha256(source_bytes).hexdigest()
    if state.get('source_sha256') not in (None, source_hash): raise RuntimeError('authoring_source_changed')
    state['source_sha256'] = source_hash
    token = json.load(urllib.request.urlopen(args.origin + '/api/v1/workbench/security'))['browser_token']

    def call(method, path, value=None):
        request = urllib.request.Request(args.origin + '/api/v2/quest-studio/' + path,
            data=None if value is None else json.dumps(value).encode(), method=method,
            headers={'Content-Type':'application/json', 'X-Workbench-Token':token})
        try:
            with urllib.request.urlopen(request, timeout=45) as response: result = json.load(response)
        except urllib.error.HTTPError as error:
            result = json.load(error)
            (args.output/'failure.json').write_text(json.dumps({'method':method,'path':path,'status':error.code,'result':result},indent=2))
            raise RuntimeError(str(result.get('error', error.code))) from error
        if result.get('ok') is False: raise RuntimeError(str(result.get('error')))
        return result

    def step(name, action):
        if name not in state['steps']:
            state['steps'][name] = action()
            state_path.write_text(json.dumps(state, indent=2) + '\n')
        return state['steps'][name]

    guild = step('guild', lambda: call('POST','guilds',{'title':'Slayers Connected Campaign','steward':'Derek'}))
    gid = guild['guild_id']
    prefix = 'guilds/' + gid
    provenance = {'schema_version':1,'mode':'frozen-creatoros-source','source':{
        'id':'slayers-beta1','path':'creatoros/beta1/quest-view-source.json',
        'repository':'djcdevelopment/comfy-quest','revision':args.source_revision,'sha256':source_hash},
        'anomalies':[], 'counts':{'quests':2,'anomalies':0}}
    catalog = {'schema_version':1,'guild':'Slayers','era':17,
        'source':{'kind':'frozen-creatoros-source','sha256':source_hash},'quests':quests}
    imported = step('source', lambda: call('POST',prefix+'/sources/import',{
        'expected_revision':guild['revision'],'source_snapshot_id':'slayers-beta1','title':'Frozen Slayers Beta 1 authoring source',
        'catalog_json':json.dumps(catalog),'provenance_json':json.dumps(provenance),
        'anomalies_text':'Frozen authored source; this run does not reimport the original guild spreadsheet.\n'}))
    canonical = step('template', lambda: call('POST','projects',{'template_id':'blank'}))
    canonical['title'] = 'Slayers Signature Hunt'
    node = canonical['nodes'][0]
    node['label'] = 'Finish the named hunt with a thrown spear.'
    route = node['routes'][0]
    route.update(event='kill',target='$enemy_deathsquito',where={'weapon_skill':'Spears','projectile':'true'})
    route['actions'][0]['text'] = 'The signature hunt is complete.'
    saved = step('canonical',lambda:call('PUT','projects/'+canonical['project_id'],{'expected_revision':canonical['revision'],'project':canonical}))
    promoted = step('abstraction',lambda:call('POST',prefix+'/abstractions/promote',{
        'expected_revision':imported['guild']['revision'],'abstraction_id':'slayers-signature-hunt',
        'title':'Slayers Signature Hunt','explanation':'A steward-governed thrown-spear finishing hunt.',
        'source_snapshot_id':'slayers-beta1','source_quest_ids':['air_drop','cold_shot'],
        'project_id':canonical['project_id'],'route_id':route['id'],'completion_action_id':route['actions'][0]['id'],
        'attribution':'Slayers Era 17; frozen CreatorOS Beta 1 source','evidence_policy':'runtime',
        'evidence_explanation':'Local Runtime outcomes; community guild credit remains separate.',
        'target_choices':[{'id':'deathsquito','label':'Deathsquito','runtime_target':'$enemy_deathsquito','source_quest_id':'air_drop'},
                          {'id':'drake','label':'Drake','runtime_target':'$enemy_drake','source_quest_id':'cold_shot'}]}))
    revised = step('stage_owned_revision',lambda:call('POST',prefix+'/abstractions/slayers-signature-hunt/revisions',{
        'expected_revision':promoted['guild']['revision'],'stage_owned_target':True,
        'evidence_policy':'runtime','evidence_explanation':'The active hunt owns its target spawn. Real thrown-spear finishing hits establish completion.'}))
    air = step('air',lambda:call('POST',prefix+'/abstractions/slayers-signature-hunt/instantiate',{
        'expected_guild_revision':revised['guild']['revision'],'abstraction_revision':revised['abstraction']['revision'],'title':'Air Drop',
        'target_choice_id':'deathsquito','instructions':'Bring down the Deathsquito with a thrown spear.',
        'completion_message':'Air Drop answered. Your next hunt awaits.','creator':'Derek'}))
    cold = step('cold',lambda:call('POST',prefix+'/abstractions/slayers-signature-hunt/instantiate',{
        'expected_guild_revision':air['guild']['revision'],'abstraction_revision':revised['abstraction']['revision'],'title':'Cold Shot',
        'target_choice_id':'drake','instructions':'Finish the Drake with a thrown spear.',
        'completion_message':'Cold Shot answered. A clean throw.','creator':'Derek'}))
    campaign = next(c for c in cold['guild']['campaigns'] if c['campaign_id']=='campaign-default')
    cp = prefix + '/campaigns/' + campaign['campaign_id']
    first = step('place_air',lambda:call('POST',cp+'/place',{'expected_revision':campaign['revision'],
        'project_id':air['project']['project_id'],'kind':'quest','container_kind':'questline','questline_id':'main'}))
    second = step('place_cold',lambda:call('POST',cp+'/place',{'expected_revision':first['campaign']['revision'],
        'project_id':cold['project']['project_id'],'kind':'quest','container_kind':'questline','questline_id':'main'}))
    ordered = json.loads(json.dumps(second['campaign']))
    ordered['title'] = 'Slayers Signature Hunt'
    ordered['questlines'][0]['quests'][1]['prerequisite_project_ids'] = [air['project']['project_id']]
    step('continuation',lambda:call('PUT',cp,{'expected_revision':ordered['revision'],'campaign':ordered}))
    step('certify',lambda:call('POST',cp+'/certify',{}))
    state.update(state='authored',guild_id=gid,campaign_id=campaign['campaign_id'],
        project_ids=[air['project']['project_id'],cold['project']['project_id']],
        proof_level='installed-authoring-and-certification; gameplay is separate')
    state_path.write_text(json.dumps(state,indent=2)+'\n')
    print(json.dumps({k:v for k,v in state.items() if k!='steps'}))


if __name__ == '__main__': main()
