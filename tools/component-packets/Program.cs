using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Extract a single component "packet" from assembly_valheim.dll:
// inheritance chain, interfaces, tunable fields, lifecycle methods,
// ZDO custom-field reads/writes (via ZDOVars hash map), and instance RPCs.

string assemblyPath = args.Length > 0
    ? args[0]
    : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll";
string targetTypeName = args.Length > 1 ? args[1] : "Fireplace";
string outputPath = args.Length > 2 ? args[2] : $"{targetTypeName.ToLowerInvariant()}-packet.json";

var module = ModuleDefinition.ReadModule(assemblyPath);

// --- 1. ZDOVars hash-key map: static field name -> original string key ---
var zdoVarKeys = new Dictionary<string, string>(); // field name -> string key
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

var target = module.Types.FirstOrDefault(t => t.Name == targetTypeName);
if (target == null)
{
    Console.WriteLine($"Type '{targetTypeName}' not found.");
    return;
}

// --- 2. Inheritance chain ---
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

// --- 3. Tunable fields, flattened across the inheritance chain (derived first),
//        each tagged with the class that declares it ---
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

// --- 4. Walk methods: lifecycle, ZDO access, RPC registrations ---
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

        // ZDO.Get*/Set with a ZDOVars key (or raw string) somewhere in the preceding args
        if (callee.DeclaringType?.Name == "ZDO" &&
            (callee.Name.StartsWith("Get") || callee.Name.StartsWith("Set")))
        {
            string? key = null;
            for (int back = i - 1; back >= Math.Max(0, i - 8) && key == null; back--)
            {
                if (instrs[back].OpCode == OpCodes.Ldsfld &&
                    instrs[back].Operand is FieldReference fr &&
                    fr.DeclaringType.Name == "ZDOVars" &&
                    zdoVarKeys.TryGetValue(fr.Name, out var k))
                    key = k;
                else if (instrs[back].OpCode == OpCodes.Ldstr)
                    key = (string)instrs[back].Operand;
            }
            if (key != null)
            {
                var valueType = callee.Name.StartsWith("Set") && callee.Parameters.Count >= 2
                    ? Short(callee.Parameters[1].ParameterType)
                    : Short(callee.ReturnType);
                zdoAccess.Add(new ZdoAccess(key, callee.Name.StartsWith("Set") ? "write" : "read", valueType, method.Name));
            }
        }

        // ZNetView.Register("Name", ...) => instance RPC
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

var packet = new Packet(
    targetTypeName,
    "assembly_valheim.dll (Steam client install, extracted " + DateTime.Now.ToString("yyyy-MM-dd") + ")",
    chain,
    interfaces,
    lifecycle,
    tunables,
    zdoAccess.DistinctBy(z => (z.Key, z.Access, z.Method)).OrderBy(z => z.Key).ToList(),
    rpcs.DistinctBy(r => r.Name).ToList()
);

var json = JsonSerializer.Serialize(packet, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath, json);
Console.WriteLine($"Wrote {outputPath}: {zdoAccess.Count} ZDO accesses, {rpcs.Count} RPCs, {tunables.Count} tunables");

static string Short(TypeReference t) => t.Name switch
{
    "Single" => "float", "Int32" => "int", "Int64" => "long", "Boolean" => "bool",
    "String" => "string", "Double" => "double", _ => t.Name
};

record FieldInfo(string Name, string Type, string DeclaredBy);
record ZdoAccess(string Key, string Access, string ValueType, string Method);
record RpcInfo(string Name, string RegisteredIn);
record Packet(
    string Component,
    string Source,
    List<string> InheritanceChain,
    List<string> Interfaces,
    List<string> LifecycleMethods,
    List<FieldInfo> TunableFields,
    List<ZdoAccess> ZdoFields,
    List<RpcInfo> InstanceRpcs);
