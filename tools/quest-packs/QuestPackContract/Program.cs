namespace QuestPackContract;

using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyQuestLab;

internal sealed class Request {
  public string Schema { get; set; }
  public List<RequestFile> Files { get; set; }
}

internal sealed class RequestFile {
  public string Path { get; set; }
  public string Json { get; set; }
}

internal static class Program {
  const string RequestSchema = "comfy-quest-pack-contract-request/v1";
  const string ResultSchema = "comfy-quest-pack-contract-validation/v1";

  static int Main() {
    try {
      string input = Console.In.ReadToEnd();
      Request request = JsonSerializer.Deserialize<Request>(input, JsonOptions());
      if (request == null || request.Schema != RequestSchema || request.Files == null) {
        throw new InvalidOperationException("contract request schema/files are invalid");
      }
      if (request.Files.Count == 0 || request.Files.Count > 512) {
        throw new InvalidOperationException("contract request must contain 1-512 quest files");
      }

      var files = new List<KeyValuePair<string, string>>();
      var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (RequestFile file in request.Files) {
        if (file == null || string.IsNullOrWhiteSpace(file.Path) || file.Json == null) {
          throw new InvalidOperationException("contract request contains a malformed file");
        }
        if (!names.Add(file.Path)) {
          throw new InvalidOperationException("contract request repeats a file path");
        }
        files.Add(new KeyValuePair<string, string>(file.Path, file.Json));
      }

      LabQuestSet set = LabQuestSet.Build(files);
      int manual = 0;
      int unsupported = 0;
      var quests = new List<object>();
      foreach (LabQuest item in set.Quests) {
        bool isManual = item.Armed == LabArmed.NoTrigger
            || item.Armed == LabArmed.AutoChecked || item.Armed == LabArmed.Irl;
        if (isManual) manual++;
        if (!item.IsArmed && !isManual) unsupported++;
        quests.Add(new {
          source = item.SourceFile,
          quest_id = item.QuestId,
          trigger_event = item.Quest?.TriggerEvent,
          armed = item.Armed,
          advisories = item.Advisories.ToArray(),
        });
      }
      var errors = set.Errors.Select(item => new {
        source = item.SourceFile,
        contract_message = item.ContractMessage,
        remedy = item.Remedy,
      }).ToArray();
      bool passed = errors.Length == 0 && unsupported == 0;
      var result = new {
        schema = ResultSchema,
        verdict = passed ? "pass" : "fail",
        files = set.FilesRead.Count,
        parsed_quests = set.Quests.Count,
        armed_quests = set.ArmedCount,
        manual_quests = manual,
        unsupported_quests = unsupported,
        errors,
        quests,
      };
      Console.Write(JsonSerializer.Serialize(result, JsonOptions()));
      return passed ? 0 : 2;
    } catch (Exception exception) {
      Console.Error.WriteLine("quest-pack-contract: " + exception.Message);
      return 1;
    }
  }

  static JsonSerializerOptions JsonOptions() => new() {
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = true,
  };
}
