using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Aurora;
public record CardLayout(string Key,string Name,string Description){public override string ToString()=>Name;}
public static class CardLayouts
{
 private static readonly CardLayout[] BaseLayouts=new[]{new CardLayout("LiquidGlass","Liquid Glass","Glaskarte mit Lichtkante und flüssig gefüllten Wertekapseln"),new("FrostGlass","Frost Glass","Milchglas mit heller Frostkante und satten Wertekapseln"),new("AquaGlass","Aqua Glass","Glas in der Farbe der jeweiligen Hardware getönt"),new("PrismGlass","Prism Glass","Glas mit zweifarbig gebrochener Lichtkante"),new("SmokeGlass","Smoke Glass","Dunkles Rauchglas mit leuchtenden Wertekapseln"),new("GlassTubes","Glass Tubes","Schlanke Glasröhren unter jeder Messzeile")};
 public static IReadOnlyList<CardLayout> All {get;}=Array.AsReadOnly(BaseLayouts);
 public static CardLayout Get(string? key){if(key?.EndsWith(".Large",StringComparison.Ordinal)==true)key=key[..^6];return All.FirstOrDefault(l=>l.Key==key)??All[0];}
 public static string BaseLayout(string? key){string value=Get(key).Key;return value.EndsWith(".Large",StringComparison.Ordinal)?value[..^6]:value;}
 public static double ReadingMultiplier(string? key)=>1;
 public static bool ReadingsBelow(Preferences prefs)=>prefs.ExpandMultiColumnReadings;
 public static double SpaceMultiplier(Preferences prefs)=>prefs.ExpandMultiColumnReadings&&prefs.Columns>1?1.2:1;
 public static void PositionInstrument(Grid content,FrameworkElement? instrument,FrameworkElement readings,Preferences prefs)
 {
  if(instrument!=null)System.Windows.Automation.AutomationProperties.SetAutomationId(instrument,"CardInstrument");
  double requested=Preferences.TextFactor(prefs.InstrumentScale);
  if(instrument!=null&&(requested!=1||!ReadingsBelow(prefs))){
   double natural=double.IsFinite(instrument.Width)?instrument.Width:instrument is Panel figurePanel&&figurePanel.Children.OfType<FrameworkElement>().FirstOrDefault() is FrameworkElement graphic&&double.IsFinite(graphic.Width)?graphic.Width+instrument.Margin.Left+instrument.Margin.Right:112;
   double readingRoom=Math.Max(88,78*prefs.FontScale*Preferences.TextFactor(prefs.ValueTextScale));
   double room=Math.Max(40,Math.Max(200,prefs.ColumnWidth)-48-(!ReadingsBelow(prefs)?readingRoom:0));
   double applied=Math.Min(requested,Math.Max(ReadingsBelow(prefs)?1:.35,room/natural));
   instrument.LayoutTransform=new ScaleTransform(applied,applied);
   if(!ReadingsBelow(prefs))content.ColumnDefinitions[0].Width=new GridLength(content.ColumnDefinitions[0].Width.Value*applied);
  }
  if(prefs.Columns>1){readings.VerticalAlignment=VerticalAlignment.Stretch;if(instrument!=null)instrument.VerticalAlignment=VerticalAlignment.Center;}
  if(!ReadingsBelow(prefs))return;
  content.ColumnDefinitions.Clear();content.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
  Grid.SetColumn(readings,0);Grid.SetRow(readings,1);readings.VerticalAlignment=VerticalAlignment.Stretch;
  if(instrument!=null){Grid.SetColumn(instrument,0);Grid.SetRow(instrument,0);instrument.HorizontalAlignment=HorizontalAlignment.Center;instrument.VerticalAlignment=VerticalAlignment.Top;instrument.Margin=new Thickness(0,0,0,8);}
 }
 public static Metric? MainMetric(Device device)=>device.Category=="Network"?device.Metrics.FirstOrDefault(m=>m.Unit!="IP"):device.Metrics.FirstOrDefault(m=>m.Unit=="°C" && m.Label.StartsWith("Temperatur",StringComparison.OrdinalIgnoreCase))??device.Metrics.FirstOrDefault(m=>m.Unit=="°C")??device.Metrics.FirstOrDefault();
 public static IHardwareCard Create(Device d,Preferences p,MetricHistory? history=null){var card=CreateCore(d,p,history);SidebarTextVisibility.Apply(card,d,p);return card;}
 // One registry row per card family: adding a design is one line plus its card class.
 private delegate IHardwareCard CardFactory(Device device,Preferences prefs,MetricHistory history);
 private static readonly (string[] Keys,CardFactory Create)[] Registry={
  (GlassStyle.All.Select(s=>s.Key).ToArray(),(d,p,h)=>new LiquidGlassCard(d,p,h,GlassStyle.Get(BaseLayout(p.Layout)))),
 };
 private static readonly Dictionary<string,CardFactory> Factories=Registry.SelectMany(r=>r.Keys.Select(k=>(k,r.Create))).ToDictionary(x=>x.k,x=>x.Create,StringComparer.Ordinal);
 public static bool HasDedicatedCard(string key)=>Factories.ContainsKey(key);
 // Every layout is a glass style; an unknown or removed name opens Liquid Glass.
 private static IHardwareCard CreateCore(Device d,Preferences p,MetricHistory? history)=>Factories.TryGetValue(BaseLayout(p.Layout),out var factory)?factory(d,p,history??new MetricHistory()):Factories[All[0].Key](d,p,history??new MetricHistory());
}
public interface IHardwareCard {FrameworkElement View {get;} void Update(Device d,Preferences p);}
