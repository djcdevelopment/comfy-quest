using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Extract component "packets" from assembly_valheim.dll: inheritance chain,
// interfaces, tunable fields, lifecycle methods, ZDO custom-field reads/writes
// (via the ZDOVars hash map), and instance RPCs.
//
//   dotnet run -- <dll> <ComponentName> [out.json]   one packet
//   dotnet run -- <dll> --all [atlas.json]           every MonoBehaviour-derived
//                                                    type + global ZDO/RPC indexes

string assemblyPath = args.Length > 0
    ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll";
string targetTypeName = args.Length > 1 ? args[1] : "Fireplace";
bool sweep = targetTypeName == "--all";
string outputPath = args.Length > 2
    ? args[2]
    : sweep ? "valheim-component-atlas.json" : $"{targetTypeName.ToLowerInvariant()}-packet.json";

var module = ModuleDefinition.ReadModule(assemblyPath);

// --- 1. ZDOVars hash-key map: static field name -> original string key ---
var zdoVarKeys = new Dictionary<string, string>();
var zdoVarsType = module.Types.FirstOrDefault(t => t.Name == "ZDOVars");
if (zdoVarsType != null)
{
    var cctor = zdoVarsType.Methods.FirstOrDefault(m => m.Name == ".cctor");
    if (cctor?.HasBody == true)
    {
        string? pendingString = null;
        foreach (var instr in cctor.Body.Instructions)
        {
            if (instr.OpCode == OpCodes.Ldstr)
                pendingString = (string)instr.Operand;
            else if (instr.OpCode == OpCodes.Stsfld && pendingString != null)
            {
                var field = (FieldReference)instr.Operand;
                zdoVarKeys[field.Name] = pendingString;
                pendingString = null;
            }
        }
    }
}
Console.WriteLine($"ZDOVars map: {zdoVarKeys.Count} keys");

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

if (!sweep)
{
    var target = module.Types.FirstOrDefault(t => t.Name == targetTypeName);
    if (target == null)
    {
        Console.WriteLine($"Type '{targetTypeName}' not found.");
        return;
    }
    var packet = AnalyzeType(target);
    File.WriteAllText(outputPath, JsonSerializer.Serialize(packet, jsonOpts));
    Console.WriteLine($"Wrote {outputPath}: {packet.ZdoFields.Count} ZDO accesses, " +
                      $"{packet.InstanceRpcs.Count} RPCs, {packet.TunableFields.Count} tunables");
    return;
}

// --- sweep: every MonoBehaviour-derived type in the module ---
var components = new List<Packet>();
foreach (var type in module.Types)
{
    if (type.IsInterface || type.IsAbstract && type.Name.StartsWith('<')) continue;
    if (!DerivesFromMonoBehaviour(type)) continue;
    components.Add(AnalyzeType(type));
}
components.Sort((a, b) => string.CompareOrdinal(a.Component, b.Component));

// global cross-references
var zdoIndex = new SortedDictionary<string, ZdoKeyEntry>(StringComparer.Ordinal);
var rpcIndex = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
foreach (var c in components)
{
    foreach (var z in c.ZdoFields)
    {
        if (!zdoIndex.TryGetValue(z.Key, out var entry))
            zdoIndex[z.Key] = entry = new ZdoKeyEntry(z.ValueType, new SortedSet<string>(), new SortedSet<string>());
        (z.Access == "write" ? entry.Writers : entry.Readers).Add($"{c.Component}.{z.Method}");
    }
    foreach (var r in c.InstanceRpcs)
    {
        if (!rpcIndex.TryGetValue(r.Name, out var owners)) rpcIndex[r.Name] = owners = new List<string>();
        owners.Add(c.Component);
    }
}

var atlas = new
{
    Source = $"assembly_valheim.dll (Steam client install, extracted {DateTime.Now:yyyy-MM-dd})",
    ComponentCount = components.Count,
    ZdoKeyCount = zdoIndex.Count,
    RpcCount = rpcIndex.Count,
    Components = components,
    ZdoKeyIndex = zdoIndex,
    RpcIndex = rpcIndex,
};
File.WriteAllText(outputPath, JsonSerializer.Serialize(atlas, jsonOpts));
Console.WriteLine($"Wrote {outputPath}: {components.Count} components, " +
                  $"{zdoIndex.Count} ZDO keys indexed, {rpcIndex.Count} RPC names");

bool DerivesFromMonoBehaviour(TypeDefinition type)
{
    TypeDefinition? cursor = type;
    while (cursor != null)
    {
        var bt = cursor.BaseType;
        if (bt == null) return false;
        if (bt.FullName == "UnityEngine.MonoBehaviour") return true;
        cursor = module.Types.FirstOrDefault(t => t.FullName == bt.FullName);
    }
    return false;
}

Packet AnalyzeType(TypeDefinition target)
{
    // inheritance chain
    var chain = new List<string>();
    TypeDefinition? cursor = target;
    while (cursor != null)
    {
        chain.Add(cursor.FullName);
        var bt = cursor.BaseType;
        if (bt == null) break;
        var resolvedBase = module.Types.FirstOrDefault(t => t.FullName == bt.FullName);
        if (resolvedBase == null) { chain.Add(bt.FullName); break; }
        cursor = resolvedBase;
    }

    var interfaces = target.Interfaces.Select(i => i.InterfaceType.Name).ToList();

    // tunable fields, flattened across the chain, tagged with the declaring class
    var tunables = new List<FieldInfo>();
    cursor = target;
    while (cursor != null)
    {
        tunables.AddRange(cursor.Fields
            .Where(f => f.IsPublic && !f.IsStatic)
            .Select(f => new FieldInfo(f.Name, Short(f.FieldType), cursor.Name)));
        cursor = cursor.BaseType == null ? null
            : module.Types.FirstOrDefault(t => t.FullName == cursor.BaseType.FullName);
    }

    var lifecycleNames = new[] { "Awake", "Start", "OnEnable", "OnDisable", "Update", "FixedUpdate", "LateUpdate", "OnDestroy" };
    var lifecycle = new List<string>();
    var zdoAccess = new List<ZdoAccess>();
    var rpcs = new List<RpcInfo>();

    foreach (var method in target.Methods)
    {
        if (lifecycleNames.Contains(method.Name)) lifecycle.Add(method.Name);
        if (!method.HasBody) continue;

        var instrs = method.Body.Instructions;
        for (int i = 0; i < instrs.Count; i++)
        {
            var instr = instrs[i];
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
            if (instr.Operand is not MethodReference callee) continue;

            if (callee.DeclaringType?.Name == "ZDO" &&
                (callee.Name.StartsWith("Get") || callee.Name.StartsWith("Set")))
            {
                // Prefer a ZDOVars field anywhere in the window over a string
                // literal: for Get*(ZDOVars.s_x, "default") the nearest ldstr is
                // the DEFAULT VALUE, not the key. Fall back to the nearest
                // non-empty ldstr only when no ZDOVars field is in play
                // (legacy raw-string keys).
                string? key = null, literal = null;
                for (int back = i - 1; back >= Math.Max(0, i - 8) && key == null; back--)
                {
                    if (instrs[back].OpCode == OpCodes.Ldsfld &&
                        instrs[back].Operand is FieldReference fr &&
                        fr.DeclaringType.Name == "ZDOVars" &&
                        zdoVarKeys.TryGetValue(fr.Name, out var k))
                        key = k;
                    else if (instrs[back].OpCode == OpCodes.Ldstr && literal == null &&
                             (string)instrs[back].Operand is { Length: > 0 } s)
                        literal = s;
                }
                key ??= literal;
                if (key != null)
                {
                    var valueType = callee.Name.StartsWith("Set") && callee.Parameters.Count >= 2
                        ? Short(callee.Parameters[1].ParameterType)
                        : Short(callee.ReturnType);
                    zdoAccess.Add(new ZdoAccess(key, callee.Name.StartsWith("Set") ? "write" : "read", valueType, method.Name));
                }
            }

            if (callee.DeclaringType?.Name == "ZNetView" && callee.Name == "Register")
            {
                for (int back = i - 1; back >= Math.Max(0, i - 6); back--)
                {
                    if (instrs[back].OpCode == OpCodes.Ldstr)
                    {
                        rpcs.Add(new RpcInfo((string)instrs[back].Operand, method.Name));
                        break;
                    }
                }
            }
        }
    }

    return new Packet(
        target.Name,
        $"assembly_valheim.dll (Steam client install, extracted {DateTime.Now:yyyy-MM-dd})",
        chain,
        interfaces,
        lifecycle,
        tunables,
        zdoAccess.DistinctBy(z => (z.Key, z.Access, z.Method)).OrderBy(z => z.Key).ToList(),
        rpcs.DistinctBy(r => r.Name).ToList()
    );
}

static string Short(TypeReference t) => t.Name switch
{
    "Single" => "float", "Int32" => "int", "Int64" => "long", "Boolean" => "bool",
    "String" => "string", "Double" => "double", _ => t.Name
};

record FieldInfo(string Name, string Type, string DeclaredBy);
record ZdoAccess(string Key, string Access, string ValueType, string Method);
record RpcInfo(string Name, string RegisteredIn);
record ZdoKeyEntry(string ValueType, SortedSet<string> Readers, SortedSet<string> Writers);
record Packet(
    string Component,
    string Source,
    List<string> InheritanceChain,
    List<string> Interfaces,
    List<string> LifecycleMethods,
    List<FieldInfo> TunableFields,
    List<ZdoAccess> ZdoFields,
    List<RpcInfo> InstanceRpcs);
