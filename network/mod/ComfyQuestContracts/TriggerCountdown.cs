namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>An authored deadline that is currently running, ready for player-facing tension.
/// A contract-only deadline is not tension: this is the fact every surface renders from.</summary>
public sealed class TriggerDeadline {
  public bool Running { get; set; }
  public int RemainingSeconds { get; set; }
  public int TotalSeconds { get; set; }
  public int Current { get; set; }
  public int Required { get; set; }
  /// <summary>Player-facing phrase, e.g. "1/2, 6 seconds remaining".</summary>
  public string Label { get; set; }
}

/// <summary>Pure reading of the one authored window a player is currently racing.
///
/// A repeat window is a sliding window anchored on the most recent contributing event, so the
/// deadline a player actually faces is the moment their earliest still-counted attempt leaves it.
/// The window is reported as running only once an attempt has started it and while it is not yet
/// satisfied, because before the first attempt there is nothing to race.</summary>
public static class TriggerCountdown {
  public static TriggerDeadline Read(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,DateTimeOffset at) {
    var deadline=new TriggerDeadline{Running=false,Label=null};
    history??=Array.Empty<RuntimeEvent>();
    var node=Windowed(expression);
    if(node==null||!node.WithinSeconds.HasValue)return deadline;
    var window=node.WithinSeconds.Value;
    // A compiled repeat keeps its window on the composite root and its count on the inner COUNT.
    var counted=FirstCount(node);
    var leaves=Leaves(counted??node).ToArray();
    if(leaves.Length==0)return deadline;
    var contributing=history.Where(value=>leaves.Any(leaf=>Matches(leaf,value)))
        .Where(value=>value.At>at.AddSeconds(-window)).OrderBy(value=>value.At).ToArray();
    if(contributing.Length==0)return deadline;
    var required=counted!=null?Math.Max(1,counted.Count.GetValueOrDefault()):Math.Max(1,leaves.Length);
    var current=counted!=null
        ?Math.Min(contributing.Length,required)
        :leaves.Count(leaf=>contributing.Any(value=>Matches(leaf,value)));
    if(current>=required)return deadline;
    var remaining=(contributing[0].At.AddSeconds(window)-at).TotalSeconds;
    if(remaining<0)return deadline;
    deadline.Running=true;
    deadline.RemainingSeconds=(int)Math.Ceiling(remaining);
    deadline.TotalSeconds=window;
    deadline.Current=current;
    deadline.Required=required;
    deadline.Label=current.ToString(CultureInfo.InvariantCulture)+"/"+required.ToString(CultureInfo.InvariantCulture)
        +", "+Seconds(deadline.RemainingSeconds)+" remaining";
    return deadline;
  }

  /// <summary>Whole seconds with the unit a player reads, singular at one — and a clock once more than
  /// a minute is left. Phase 3 exit session 2 ran a real ten-minute deadline that rendered
  /// "594 seconds remaining" once a second for its whole length and was never read as a countdown:
  /// past a minute, a number of seconds is a log line, and only a clock is tension.</summary>
  public static string Seconds(int value)=>
      value<60?value.ToString(CultureInfo.InvariantCulture)+(value==1?" second":" seconds")
      :Clock(value);

  /// <summary>m:ss, and h:mm:ss for the deadlines long enough to need it.</summary>
  static string Clock(int value) {
    var hours=value/3600;var minutes=value%3600/60;
    return (hours>0
            ?hours.ToString(CultureInfo.InvariantCulture)+":"+minutes.ToString("00",CultureInfo.InvariantCulture)
            :minutes.ToString(CultureInfo.InvariantCulture))
        +":"+(value%60).ToString("00",CultureInfo.InvariantCulture);
  }

  static TriggerExpression Windowed(TriggerExpression expression) {
    if(expression==null)return null;
    if(expression.WithinSeconds.HasValue)return expression;
    foreach(var child in expression.Children??new List<TriggerExpression>()) {
      var found=Windowed(child);
      if(found!=null)return found;
    }
    return null;
  }

  static TriggerExpression FirstCount(TriggerExpression expression) {
    if(expression==null)return null;
    if(string.Equals(expression.Op,"COUNT",StringComparison.OrdinalIgnoreCase))return expression;
    foreach(var child in expression.Children??new List<TriggerExpression>()) {
      var found=FirstCount(child);
      if(found!=null)return found;
    }
    return null;
  }

  static IEnumerable<TriggerExpression> Leaves(TriggerExpression expression) {
    if(expression==null)yield break;
    if(string.Equals(expression.Op,"EVENT",StringComparison.OrdinalIgnoreCase)){yield return expression;yield break;}
    foreach(var child in expression.Children??new List<TriggerExpression>())
      foreach(var found in Leaves(child))yield return found;
  }

  static bool Matches(TriggerExpression leaf,RuntimeEvent value) {
    if(leaf==null||value==null||!string.Equals(leaf.Event,value.Name,StringComparison.OrdinalIgnoreCase))return false;
    if(!string.IsNullOrWhiteSpace(leaf.Target)&&!string.Equals(leaf.Target,value.Target,StringComparison.OrdinalIgnoreCase))return false;
    foreach(var pair in leaf.Where??new Dictionary<string,string>())
      if(value.Fields==null||!value.Fields.TryGetValue(pair.Key,out var field)
          ||!string.Equals(field,pair.Value,StringComparison.OrdinalIgnoreCase))return false;
    return true;
  }
}
