namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>A Unity-free world position. All Contracts distance math lives here so binding
/// discovery and the Runtime scene walk never gain a radius.</summary>
public readonly struct SpatialPoint {
  public SpatialPoint(double x,double y,double z){X=x;Y=y;Z=z;}
  public double X { get; }
  public double Y { get; }
  public double Z { get; }
  public double DistanceTo(SpatialPoint other){var dx=X-other.X;var dy=Y-other.Y;var dz=Z-other.Z;return Math.Sqrt(dx*dx+dy*dy+dz*dz);}
}

/// <summary>Live spatial facts supplied by the caller at evaluation time: the bound Charm's
/// position and the current positions of this workflow's tracked spawned objects.</summary>
public sealed class SpatialFacts {
  public SpatialPoint? BindingPosition { get; set; }
  public IReadOnlyList<SpatialPoint> SpawnedPositions { get; set; }
}

/// <summary>One spatial predicate's actual-versus-expected outcome, ready for prose and traces.</summary>
public sealed class SpatialObservation {
  public bool Satisfied { get; set; }
  public int Current { get; set; }
  public int Required { get; set; }
  public string Expected { get; set; }
  public string Actual { get; set; }
}

/// <summary>Pure evaluation of the closed spatial predicate registry over observed positions.
/// Missing positions, anchors, or resolutions fail closed; nothing is interpolated beyond the
/// interval between two in-area observations.</summary>
public static class SpatialEvaluator {
  public static bool Satisfied(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context)=>Observe(expression,history,context).Satisfied;

  public static TriggerProgress Progress(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context){var observation=Observe(expression,history,context);return new TriggerProgress{Current=observation.Current,Required=observation.Required};}

  public static SpatialObservation Observe(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context) {
    history??=Array.Empty<RuntimeEvent>();
    var predicate=expression?.Spatial??"";
    var radius=Math.Max(SpatialPredicateCatalog.RadiusMinimum,expression?.Radius??0);
    var label=Label(expression?.Anchor);
    var value=Math.Max(1,expression?.Value??0);
    var counted=string.Equals(predicate,"count_in_area",StringComparison.Ordinal);
    var timed=string.Equals(predicate,"remained",StringComparison.Ordinal);
    var required=counted||timed?value:1;
    var observation=new SpatialObservation{Satisfied=false,Current=0,Required=required,Expected=Expected(predicate,radius,label,value),Actual=null};
    var trigger=history.Count>0?history[history.Count-1]:null;
    if(context==null||!context.TryResolveAnchor(expression?.Anchor,trigger,out var anchor))return observation;
    if(counted){
      if(context.SpawnedPositions==null)return observation;
      var count=context.SpawnedPositions.Count(position=>position.DistanceTo(anchor)<=radius);
      observation.Satisfied=count>=required;
      observation.Current=Math.Min(count,required);
      observation.Actual=count.ToString(CultureInfo.InvariantCulture)+" objects";
      return observation;
    }
    var observed=new List<KeyValuePair<DateTimeOffset,SpatialPoint>>();
    foreach(var item in history)if(TryPosition(item,out var position))observed.Add(new(item.At,position));
    if(string.Equals(predicate,"within_radius",StringComparison.Ordinal)){
      if(!TryPosition(trigger,out var position))return observation;
      var distance=position.DistanceTo(anchor);
      observation.Satisfied=distance<=radius;
      observation.Current=observation.Satisfied?1:0;
      observation.Actual=FormatMeters(distance);
      return observation;
    }
    if(string.Equals(predicate,"entered",StringComparison.Ordinal)||string.Equals(predicate,"left",StringComparison.Ordinal)){
      if(observed.Count<2)return observation;
      var entering=string.Equals(predicate,"entered",StringComparison.Ordinal);
      var transition=false;
      for(var index=1;index<observed.Count&&!transition;index++){
        var wasInside=observed[index-1].Value.DistanceTo(anchor)<=radius;
        var isInside=observed[index].Value.DistanceTo(anchor)<=radius;
        transition=entering?!wasInside&&isInside:wasInside&&!isInside;
      }
      observation.Satisfied=transition;
      observation.Current=transition?1:0;
      observation.Actual=transition?(entering?"entered":"left"):(entering?"not entered":"not left");
      return observation;
    }
    if(timed){
      if(!TryPosition(trigger,out var position))return observation;
      if(position.DistanceTo(anchor)>radius){observation.Actual="outside the area";return observation;}
      var runStart=observed[observed.Count-1].Key;
      for(var index=observed.Count-1;index>=0;index--){
        if(observed[index].Value.DistanceTo(anchor)>radius)break;
        runStart=observed[index].Key;
      }
      var seconds=(trigger.At-runStart).TotalSeconds;
      observation.Satisfied=seconds>=required;
      observation.Current=seconds>=required?required:Math.Max(0,(int)Math.Floor(seconds));
      observation.Actual=FormatSeconds(seconds);
      return observation;
    }
    return observation;
  }

  public static bool TryPosition(RuntimeEvent value,out SpatialPoint point){point=default;if(value?.PosX==null||value.PosY==null||value.PosZ==null)return false;point=new SpatialPoint(value.PosX.Value,value.PosY.Value,value.PosZ.Value);return true;}

  public static IReadOnlyDictionary<string,SpatialPoint> AnchorMap(ExperienceDocument document){if(document?.Anchors==null||document.Anchors.Count==0)return null;var map=new Dictionary<string,SpatialPoint>(StringComparer.Ordinal);foreach(var anchor in document.Anchors)if(!string.IsNullOrWhiteSpace(anchor?.Id))map[anchor.Id]=new SpatialPoint(anchor.X,anchor.Y,anchor.Z);return map;}

  public static string Label(AreaAnchor anchor){
    var kind=anchor?.Kind??"";
    if(string.Equals(kind,"authored",StringComparison.Ordinal))return anchor.AnchorId??"the authored anchor";
    if(string.Equals(kind,"binding",StringComparison.Ordinal))return "the bound Charm";
    if(string.Equals(kind,"player",StringComparison.Ordinal))return "the player";
    if(string.Equals(kind,"coordinates",StringComparison.Ordinal))return "("+FormatCoordinate(anchor.X)+", "+FormatCoordinate(anchor.Y)+", "+FormatCoordinate(anchor.Z)+")";
    return "the anchor";
  }

  static string Expected(string predicate,int radius,string label,int value){
    var meters=radius.ToString(CultureInfo.InvariantCulture);
    if(string.Equals(predicate,"within_radius",StringComparison.Ordinal))return "within "+meters+" m of "+label;
    if(string.Equals(predicate,"entered",StringComparison.Ordinal))return "entered the area "+meters+" m around "+label;
    if(string.Equals(predicate,"left",StringComparison.Ordinal))return "left the area "+meters+" m around "+label;
    if(string.Equals(predicate,"remained",StringComparison.Ordinal))return ">= "+value.ToString(CultureInfo.InvariantCulture)+" seconds in the area "+meters+" m around "+label;
    if(string.Equals(predicate,"count_in_area",StringComparison.Ordinal))return ">= "+value.ToString(CultureInfo.InvariantCulture)+" objects in the area "+meters+" m around "+label;
    return "spatial predicate";
  }

  static string FormatMeters(double value)=>value.ToString(value==Math.Truncate(value)?"0":"0.#",CultureInfo.InvariantCulture)+" m";
  static string FormatSeconds(double value)=>value.ToString(value==Math.Truncate(value)?"0":"0.###",CultureInfo.InvariantCulture)+" seconds";
  static string FormatCoordinate(double? value)=>(value??0).ToString("0.#",CultureInfo.InvariantCulture);
}
