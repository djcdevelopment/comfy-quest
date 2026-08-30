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
  public string AreaId { get; set; }
  public string AnchorSha256 { get; set; }
  public SpatialPoint? ResolvedCenter { get; set; }
  public SpatialPoint? ObservedPosition { get; set; }
  public double RadiusMeters { get; set; }
  public double? DistanceMeters { get; set; }
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
    var area=context?.FindArea(expression?.AreaId);
    var radius=Math.Max(SpatialPredicateCatalog.RadiusMinimum,area?.RadiusMeters??0);
    var label=Label(area);
    var value=Math.Max(1,expression?.Value??0);
    var counted=string.Equals(predicate,"count_in_area",StringComparison.Ordinal);
    var timed=string.Equals(predicate,"remained",StringComparison.Ordinal);
    var required=counted||timed?value:1;
    var observation=new SpatialObservation{Satisfied=false,Current=0,Required=required,Expected=Expected(predicate,radius,label,value),Actual=null,AreaId=expression?.AreaId,AnchorSha256=area?.SourceAnchor?.ContentSha256,RadiusMeters=radius};
    var trigger=history.Count>0?history[history.Count-1]:null;
    if(context==null||!context.TryResolveArea(expression?.AreaId,trigger,out var anchor))return observation;
    observation.ResolvedCenter=anchor;
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
      observation.ObservedPosition=position;observation.DistanceMeters=distance;
      observation.Satisfied=distance<=radius;
      observation.Current=observation.Satisfied?1:0;
      observation.Actual=FormatMeters(distance);
      return observation;
    }
    if(string.Equals(predicate,"entered",StringComparison.Ordinal)||string.Equals(predicate,"left",StringComparison.Ordinal)){
      if(observed.Count<2)return observation;
      var entering=string.Equals(predicate,"entered",StringComparison.Ordinal);
      var transition=false;
      SpatialPoint? transitionPosition=null;
      for(var index=1;index<observed.Count&&!transition;index++){
        var wasInside=observed[index-1].Value.DistanceTo(anchor)<=radius;
        var isInside=observed[index].Value.DistanceTo(anchor)<=radius;
        transition=entering?!wasInside&&isInside:wasInside&&!isInside;
        if(transition)transitionPosition=observed[index].Value;
      }
      observation.Satisfied=transition;
      observation.Current=transition?1:0;
      observation.Actual=transition?(entering?"entered":"left"):(entering?"not entered":"not left");
      var evidencePosition=transitionPosition??observed[observed.Count-1].Value;
      observation.ObservedPosition=evidencePosition;observation.DistanceMeters=evidencePosition.DistanceTo(anchor);
      return observation;
    }
    if(timed){
      if(!TryPosition(trigger,out var position))return observation;
      observation.ObservedPosition=position;observation.DistanceMeters=position.DistanceTo(anchor);
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

  public static IReadOnlyDictionary<string,SpatialArea> AreaMap(ExperienceDocument document){if(document?.SpatialAreas==null||document.SpatialAreas.Count==0)return null;var map=new Dictionary<string,SpatialArea>(StringComparer.Ordinal);foreach(var area in document.SpatialAreas)if(!string.IsNullOrWhiteSpace(area?.Id))map[area.Id]=area;return map;}

  public static string Label(SpatialArea area){
    var frame=area?.Frame??"";
    if(string.Equals(frame,"binding",StringComparison.Ordinal))return "the bound Charm";
    if(string.Equals(frame,"player",StringComparison.Ordinal))return "the player";
    if(string.Equals(frame,"world",StringComparison.Ordinal)&&area?.Center!=null)return "("+FormatCoordinate(area.Center.X)+", "+FormatCoordinate(area.Center.Y)+", "+FormatCoordinate(area.Center.Z)+")";
    return area?.Id??"the area";
  }

  static string Expected(string predicate,double radius,string label,int value){
    var meters=radius.ToString(radius==Math.Truncate(radius)?"0":"0.###",CultureInfo.InvariantCulture);
    if(string.Equals(predicate,"within_radius",StringComparison.Ordinal))return "within "+meters+" m of "+label;
    if(string.Equals(predicate,"entered",StringComparison.Ordinal))return "entered the area "+meters+" m around "+label;
    if(string.Equals(predicate,"left",StringComparison.Ordinal))return "left the area "+meters+" m around "+label;
    if(string.Equals(predicate,"remained",StringComparison.Ordinal))return ">= "+value.ToString(CultureInfo.InvariantCulture)+" seconds in the area "+meters+" m around "+label;
    if(string.Equals(predicate,"count_in_area",StringComparison.Ordinal))return ">= "+value.ToString(CultureInfo.InvariantCulture)+" objects in the area "+meters+" m around "+label;
    return "spatial predicate";
  }

  static string FormatMeters(double value)=>value.ToString(value==Math.Truncate(value)?"0":"0.#",CultureInfo.InvariantCulture)+" m";
  static string FormatSeconds(double value)=>value.ToString(value==Math.Truncate(value)?"0":"0.###",CultureInfo.InvariantCulture)+" seconds";
  static string FormatCoordinate(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
}
