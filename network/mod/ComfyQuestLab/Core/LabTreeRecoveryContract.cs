namespace ComfyQuestLab;

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

/// <summary>Unity-free, explicitly named JSON contract for recoverable natural trees.
///
/// Valheim's Unity JsonUtility silently discarded the custom record collection in two
/// different DTO shapes while still reading every scalar beside it. Data-contract JSON is
/// already exercised by the shipping network mod and gives every field an explicit wire name.
/// Keeping this file Unity-free lets the shared test project execute the real serializer.</summary>
[DataContract]
public sealed class LabTreeRecoveryRecord {
  [DataMember(Name = "Prefab", Order = 1)] public string Prefab;
  [DataMember(Name = "PrefabHash", Order = 2)] public int PrefabHash;
  [DataMember(Name = "X", Order = 3)] public float X;
  [DataMember(Name = "Y", Order = 4)] public float Y;
  [DataMember(Name = "Z", Order = 5)] public float Z;
  [DataMember(Name = "Qx", Order = 6)] public float Qx;
  [DataMember(Name = "Qy", Order = 7)] public float Qy;
  [DataMember(Name = "Qz", Order = 8)] public float Qz;
  [DataMember(Name = "Qw", Order = 9)] public float Qw;
  [DataMember(Name = "HasEuler", Order = 10)] public bool HasEuler;
  [DataMember(Name = "Rx", Order = 11)] public float Rx;
  [DataMember(Name = "Ry", Order = 12)] public float Ry;
  [DataMember(Name = "Rz", Order = 13)] public float Rz;
  [DataMember(Name = "Sx", Order = 14)] public float Sx;
  [DataMember(Name = "Sy", Order = 15)] public float Sy;
  [DataMember(Name = "Sz", Order = 16)] public float Sz;
  [DataMember(Name = "HasHealth", Order = 17)] public bool HasHealth;
  [DataMember(Name = "Health", Order = 18)] public float Health;
}

[DataContract]
public sealed class LabTreeRecoveryLedger {
  [DataMember(Name = "Schema", Order = 1)] public string Schema;
  [DataMember(Name = "PluginRelease", Order = 2)] public string PluginRelease;
  [DataMember(Name = "ProfileId", Order = 3)] public string ProfileId;
  [DataMember(Name = "BuildId", Order = 4)] public string BuildId;
  [DataMember(Name = "CreatedUtc", Order = 5)] public string CreatedUtc;
  [DataMember(Name = "RestoredUtc", Order = 6)] public string RestoredUtc;
  [DataMember(Name = "Restored", Order = 7)] public bool Restored;
  [DataMember(Name = "RecordCount", Order = 8)] public int RecordCount;
  [DataMember(Name = "RecordsSha256", Order = 9)] public string RecordsSha256;
  [DataMember(Name = "RemovedCount", Order = 10)] public int RemovedCount;
  [DataMember(Name = "RestoredCount", Order = 11)] public int RestoredCount;
  [DataMember(Name = "Trees", Order = 12)]
  public LabTreeRecoveryRecord[] Trees = new LabTreeRecoveryRecord[0];
}

public static class LabTreeRecoveryContract {
  static readonly DataContractJsonSerializer Serializer =
      new DataContractJsonSerializer(typeof(LabTreeRecoveryLedger));

  public static LabTreeRecoveryLedger Deserialize(string json) {
    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty))) {
      return Serializer.ReadObject(stream) as LabTreeRecoveryLedger;
    }
  }

  public static string Serialize(LabTreeRecoveryLedger ledger) {
    if (ledger == null) {
      throw new ArgumentNullException(nameof(ledger));
    }
    using (var stream = new MemoryStream()) {
      Serializer.WriteObject(stream, ledger);
      return Encoding.UTF8.GetString(stream.ToArray());
    }
  }
}
