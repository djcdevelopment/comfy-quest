# Annotation prompt template

Paste this into any capable LLM to draft the description column for a field
dictionary. Feed it the compact field list from a packet (see below), review the
result, then run `assemble_dictionary.py`.

The model's `(?)` self-flags are the review queue — a human editor confirms or
fixes those rows before anything is published. Field names/types are extracted
ground truth; only the prose is drafted.

## Prompt

> You are annotating Valheim modding documentation. Below are the public fields
> of the `<Component>` component (and its base classes) from
> `assembly_valheim.dll`, as "Class.field:type". For EACH field, write one
> plain-English description (max 14 words) of what it controls, for a beginner
> modding guide about custom fields. If you are unsure, still guess but append
> " (?)".
>
> Output STRICT JSON only — a single object mapping "Class.m_fieldName" to the
> description string. No markdown fences, no commentary.
>
> `<paste field list here>`

## Producing the field list from a packet

```powershell
python -c "import json; d=json.load(open('fireplace-packet.json')); print('\n'.join(f\"{x['DeclaredBy']}.{x['Name']}:{x['Type']}\" for x in d['TunableFields']))"
```
