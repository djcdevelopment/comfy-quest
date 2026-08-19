namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>How many objects one authored spawn action staged for this workflow, and how many of
/// them the world authority can still resolve. Total and live come from the same ledger read, so a
/// ledger that cannot be read supplies no tally at all rather than an empty one.</summary>
public sealed class SpawnTally {
  public SpawnTally(int total,int live){Total=Math.Max(0,total);Live=Math.Max(0,Math.Min(live,Math.Max(0,total)));}
  public int Total { get; }
  public int Live { get; }
  public int Cleared => Total-Live;
}

/// <summary>Live encounter facts supplied by the caller at evaluation time, keyed by authored spawn
/// action id. A missing entry fails every spawn-counting measure closed.</summary>
public sealed class EncounterFacts {
  public IReadOnlyDictionary<string,SpawnTally> SpawnsByAction { get; set; }
}

/// <summary>One adaptive measure's actual-versus-expected outcome, ready for prose and traces.</summary>
public sealed class AdaptiveObservation {
  public bool Satisfied { get; set; }
  public int Current { get; set; }
  public int Required { get; set; }
  public string Expected { get; set; }
  public string Actual { get; set; }
}

/// <summary>Pure evaluation of the closed adaptive measure registry. Every measure fails closed when
/// its persisted anchor, counter, or ledger tally is unavailable; nothing is inferred from absence.</summary>
public static class AdaptiveEvaluator {
  public static bool Satisfied(TriggerExpression expression,TriggerEvaluationContext context)=>Observe(expression,context).Satisfied;

  public static TriggerProgress Progress(TriggerExpression expression,TriggerEvaluationContext context){var observation=Observe(expression,context);return new TriggerProgress{Current=observation.Current,Required=observation.Required};}

  public static AdaptiveObservation Observe(TriggerExpression expression,TriggerEvaluationContext context) {
    var required=Math.Max(1,expression?.Value??0);
    AdaptiveMeasureCatalog.TryGet(expression?.Measure,out var definition);
    var observation=new AdaptiveObservation{Satisfied=false,Current=0,Required=required,Expected=Expected(definition,required),Actual=null};
    var authored=expression?.Value>0&&string.Equals(expression?.Comparison,"gte",StringComparison.Ordinal);
    if(!authored||definition==null||context==null||!context.TryMeasure(expression.Measure,expression.ActionId,out var actual)||actual<0)return observation;
    observation.Satisfied=actual>=required;
    observation.Current=actual>=required?required:(int)Math.Floor(actual);
    observation.Actual=Format(actual,definition);
    return observation;
  }

  static string Expected(AdaptiveMeasureDefinition definition,int required)=>
      ">= "+required.ToString(CultureInfo.InvariantCulture)+" "+(definition?.UnitFor(required)??"seconds");

  static string Format(double value,AdaptiveMeasureDefinition definition)=>
      value.ToString(value==Math.Truncate(value)?"0":"0.###",CultureInfo.InvariantCulture)+" "+definition.UnitFor(value);
}
