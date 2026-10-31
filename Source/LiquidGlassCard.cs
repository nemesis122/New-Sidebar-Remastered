using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Aurora;

// The glass family. Every variant shares one construction: a translucent slab, a rim
// that catches light at the top edge, a sheen across the upper third, and readings held
// in glass. Variants differ in material (frost, tint, smoke, prism) and in form:
//  Capsule – reading text inside a capsule filled like liquid
//  Tubes   – reading row above a slim glass tube
//  Tiles   – two-column glass tiles that fill from the bottom
//  Columns – an equalizer of vertical glass tubes with values beneath
//  Ring    – the main reading as a glass ring, the rest as slim tubes
//  Inline  – one dense line per reading: label, short bar, value
// Throughput (download/upload, drive read/write) always shows a live 60-second trace.
public sealed record GlassStyle(string Key,double Lift=.10,byte Top=235,byte Bottom=200,double Radius=16,byte Sheen=34,double SheenHeight=46,
 byte RimTop=110,byte RimBottom=18,double Tint=0,double Smoke=0,bool Prism=false,bool Tubes=false,byte LiquidA=120,byte LiquidB=150,double TrackLift=0,double PillHeight=24,string Form="Capsule")
{
 public static readonly GlassStyle[] All={
  new("LiquidGlass"),
  new("FrostGlass",Lift:.22,Top:225,Bottom:190,Radius:20,Sheen:52,SheenHeight:62,RimTop:160,RimBottom:44,LiquidA:150,LiquidB:175,TrackLift:.14,PillHeight:26),
  new("AquaGlass",Tint:.17,RimTop:120,RimBottom:24,LiquidA:135,LiquidB:165),
  new("PrismGlass",Radius:18,Sheen:28,Prism:true,RimTop:190,RimBottom:70,LiquidA:130,LiquidB:170),
  // Smoke: a translucent smoky fill with a glowing edge keeps labels readable on the dark glass.
  new("SmokeGlass",Lift:.04,Smoke:.45,Top:242,Bottom:225,Radius:14,Sheen:16,SheenHeight:38,RimTop:70,RimBottom:10,LiquidA:72,LiquidB:105),
  new("GlassTubes",Radius:16,Tubes:true,PillHeight:7,Form:"Tubes"),
  new("GlassTiles",Lift:.14,Radius:18,Sheen:30,RimTop:130,RimBottom:26,LiquidA:66,LiquidB:96,Form:"Tiles"),
  new("GlassColumns",Lift:.08,Radius:16,Sheen:26,RimTop:120,RimBottom:20,LiquidA:140,LiquidB:190,Form:"Columns"),
  new("GlassRing",Lift:.12,Radius:22,Sheen:40,SheenHeight:52,RimTop:140,RimBottom:30,PillHeight:6,Form:"Ring"),
  new("GlassInline",Lift:.06,Radius:12,Sheen:22,SheenHeight:32,RimTop:100,RimBottom:16,LiquidA:150,LiquidB:185,PillHeight:6,Form:"Inline")
 };
 public static GlassStyle Get(string key)=>All.FirstOrDefault(s=>s.Key==key)??All[0];
}

public sealed class LiquidGlassCard:ModernCardBase
{
 private readonly GlassStyle style;
 private readonly MetricHistory history;
 private readonly TextBlock? number,unit;
 private readonly string? heroId;
 private readonly string second;
 private readonly bool light;
 private readonly Color rimTop,rimBottom;
 private readonly List<(string Id,Capsule Pill,bool Level)> pills=new();
 private readonly HashSet<string> links=new();
 private readonly List<(string Id,TraceCapsule Trace)> traces=new();
 private readonly GlassRing? ring;
 public LiquidGlassCard(Device device,Preferences p,MetricHistory history):this(device,p,history,GlassStyle.All[0]){}
 public LiquidGlassCard(Device device,Preferences p,MetricHistory history,GlassStyle style):base(device,p)
 {
  this.style=style;this.history=history;
  // Prism splits light into the accent and a neighbouring hue unless the user chose a secondary colour.
  second=style.Prism&&Secondary==Accent?HueShift(Accent,42):Secondary;
  var panel=C(D.Panel);var accent=C(Accent);double alpha=Math.Clamp(p.Opacity,0,1);
  // Light themes (Light, Arctic): a white rim vanishes on white glass and dark smoke would
  // hide dark text, so the rim takes the theme edge and smoke becomes a light grey tint.
  light=Luminance(panel)>.55;
  if(style.Tint>0)panel=Mix(panel,accent,style.Tint);if(style.Smoke>0)panel=Mix(panel,Colors.Black,light?.10:style.Smoke);
  var edge=C(Edge);var tintRim=style.Tint>0||style.Prism?accent:light?edge:Colors.White;
  rimTop=Color.FromArgb(light?(byte)230:style.RimTop,tintRim.R,tintRim.G,tintRim.B);rimBottom=Color.FromArgb(light?(byte)120:style.RimBottom,(light?edge:tintRim).R,(light?edge:tintRim).G,(light?edge:tintRim).B);
  var top=Mix(panel,Colors.White,style.Lift);
  CornerRadius=new CornerRadius(style.Radius);Margin=new Thickness(3,0,3,8);BorderThickness=new Thickness(style.Prism?1.4:1.2);
  Background=new LinearGradientBrush(Color.FromArgb((byte)(style.Top*alpha),top.R,top.G,top.B),Color.FromArgb((byte)(style.Bottom*alpha),panel.R,panel.G,panel.B),new Point(0,0),new Point(0,1));
  var root=new Grid();Child=root;
  // The sheen belongs to the glass surface, so it fades with "Widget-Hintergrund". Its type name
  // lets the shared HW Sensors frame lift one sheen onto its perimeter instead of one per section.
  byte sheen=(byte)(style.Sheen*alpha);
  root.Children.Add(new FrameDecoration{CornerRadius=new CornerRadius(Math.Max(0,style.Radius-1),Math.Max(0,style.Radius-1),40,40),VerticalAlignment=VerticalAlignment.Top,Height=style.SheenHeight,IsHitTestVisible=false,
   Background=new LinearGradientBrush(Color.FromArgb(sheen,255,255,255),Color.FromArgb(0,255,255,255),new Point(0,0),style.Prism?new Point(.7,1):new Point(0,1))});
  var body=new ExpandedReadingsPanel(p,1){Margin=new Thickness(14,12,14,13)};root.Children.Add(body);
  body.Children.Add(Header(device,p));
  var content=new GrowPanel();body.Children.Add(content);
  bool network=device.Category=="Network";
  var hero=network?null:HeroMetric(device);
  // Grafikgröße scales capsule and trace size; Linien-/Balkenstärke scales bar thickness and
  // line widths. "Text unter dem Graphen" moves each reading row beneath its capsule.
  double scale=S*Preferences.TextFactor(p.InstrumentScale),stroke=DesignCatalog.GraphSize(p);
  if(hero!=null&&Plottable(hero))
  {
   heroId=hero.Id;(number,unit)=HeroNumber(p,style.Form=="Inline"?24:30);
   var line=new StackPanel{Orientation=Orientation.Horizontal};line.Children.Add(number);line.Children.Add(unit);
   if(style.Form=="Ring")
   {
    // The main reading sits inside a glass ring that fills to its level.
    double size=118*scale;ring=new GlassRing(D.Track,Accent,second,style,stroke*scale,light){Width=size,Height=size};
    var face=new Grid{Width=size,Height=size};face.Children.Add(ring);
    line.HorizontalAlignment=HorizontalAlignment.Center;line.VerticalAlignment=VerticalAlignment.Center;
    face.Children.Add(new Viewbox{Child=line,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,MaxWidth=size*.62,MaxHeight=size*.42,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
    content.Children.Add(new Viewbox{Child=face,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)});
   }
   // The big number leads; its own capsule below names it and shows its level.
   else content.Children.Add(new Viewbox{Child=line,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,style.Form=="Inline"?6:9)});
  }
  bool below=CardLayouts.ReadingsBelow(p);
  double textRow=Math.Max(10.5*F(p.LabelTextScale),12*F(p.ValueTextScale))*S*1.45;
  // Tiles and columns collect their bars into a grid; with fewer than three bars a column
  // equalizer would be a lone block, so those cards use slim tubes instead.
  var levelMetrics=device.Metrics.Where(m=>Plottable(m)&&m.Unit is not ("MB/s" or "B/s")).ToList();
  string form=style.Form=="Columns"&&levelMetrics.Count<3?"Tubes":style.Form;
  System.Windows.Controls.Primitives.UniformGrid? grid=null;
  if(form is "Tiles" or "Columns"&&levelMetrics.Count>0)
  {
   int columns=form=="Tiles"?Math.Min(2,levelMetrics.Count):Math.Clamp((int)Math.Floor((Math.Max(120,p.ColumnWidth)-40)/62),3,5);
   if(form=="Columns")columns=Math.Min(columns,levelMetrics.Count);
   grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=columns,Margin=new Thickness(0,0,form=="Tiles"?-6:0,form=="Tiles"?0:6)};
  }
  if(form=="Inline")Grid.SetIsSharedSizeScope(content,true);
  bool gridPlaced=false;
  foreach(var m in device.Metrics)
  {
   // Throughput (network DL/UL, drive read/write) has no fixed maximum: it gets a live trace.
   bool traced=m.Unit is "MB/s" or "B/s";
   // Totals, capacities and link speed sit in the same glass but without a level:
   // a fill there would always be full. Only text values such as IP addresses stay plain rows.
   bool level=!(network&&!traced)&&m.Label is not ("Gesamt" or "Kapazität");
   // Link speed: the glass shows how much of the link download + upload use right now.
   if(network&&m.Unit is "Gbit/s" or "Mbit/s")links.Add(m.Id);
   if(!Plottable(m)){content.Children.Add(new AddressReadoutPanel(Label(m.Label,p),Value(m,p,11.5)){Margin=new Thickness(4,3,4,3)});continue;}
   Capsule NewCapsule(){var pill=new Capsule(D.Track,Accent,second,style,stroke,light);pills.Add((m.Id,pill,level));return pill;}
   TraceCapsule NewTrace(bool full){var trace=new TraceCapsule(D.Track,Accent,second,style,stroke,full,light);traces.Add((m.Id,trace));return trace;}
   if(grid!=null&&!traced)
   {
    if(!gridPlaced){content.Children.Add(grid);gridPlaced=true;}
    var pill=NewCapsule();
    if(form=="Tiles")
    {
     // A glass tile fills from the bottom; label and value stay in its upper left corner.
     pill.Vertical=true;pill.Corner=10;pill.Exact=true;pill.BarThickness=2*stroke;
     var tile=new Grid{MinHeight=Math.Max(textRow*2.2,46*scale),Margin=new Thickness(0,0,6,6)};tile.Children.Add(pill);
     var text=new StackPanel{Margin=new Thickness(10,7,10,8),VerticalAlignment=VerticalAlignment.Top};var label=Label(m.Label,p,10);text.Children.Add(label);var value=Value(m,p,15);text.Children.Add(new Viewbox{Child=value,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,1,0,0)});
     if(!light)text.Effect=TextHalo;tile.Children.Add(text);grid.Children.Add(tile);
    }
    else
    {
     // Equalizer: a vertical tube per reading, value and label centred beneath it.
     pill.Vertical=true;pill.BarThickness=14*S*stroke;pill.Width=pill.BarThickness;pill.Height=58*scale;pill.HorizontalAlignment=HorizontalAlignment.Center;
     var cell=new StackPanel{Margin=new Thickness(2,0,2,8)};cell.Children.Add(pill);
     var value=Value(m,p,11.5);var label=Label(m.Label,p,9.5);
     cell.Children.Add(new Viewbox{Child=value,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,5,0,0)});
     cell.Children.Add(new Viewbox{Child=label,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center});
     grid.Children.Add(cell);
    }
    continue;
   }
   if(form=="Inline")
   {
    // One dense line: label | short bar or trace | value, with shared column edges.
    var line=new Grid{Margin=new Thickness(0,traced?2:1,0,traced?2:1)};
    line.ColumnDefinitions.Add(new(){Width=GridLength.Auto,SharedSizeGroup="InlineLabel"});line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=16});line.ColumnDefinitions.Add(new(){Width=GridLength.Auto,SharedSizeGroup="InlineValue"});
    var label=Label(m.Label,p,10.5);label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,0,8,0);label.MaxWidth=Math.Max(44,p.ColumnWidth*.30);line.Children.Add(label);
    FrameworkElement glass=traced?NewTrace(true):NewCapsule();
    if(glass is Capsule bar){bar.BarThickness=style.PillHeight*S*stroke;bar.Height=bar.BarThickness;}else glass.Height=Math.Max(textRow,16*scale);
    glass.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(glass,1);line.Children.Add(glass);
    var value=Value(m,p,12);value.VerticalAlignment=VerticalAlignment.Center;value.TextAlignment=TextAlignment.Right;value.MinWidth=50*S;value.Margin=new Thickness(6,0,0,0);Grid.SetColumn(value,2);line.Children.Add(value);
    content.Children.Add(line);continue;
   }
   bool separate=form is "Tubes" or "Ring" or "Tiles" or "Columns"||below;
   FrameworkElement piece=traced?NewTrace(separate):NewCapsule();
   if(separate)
   {
    // Reading row and glass on separate lines: tubes put the row above, the text-below option beneath.
    // Slim tubes are 7 px at 100 %; capsule styles under "text below" use half their capsule height.
    double thickness=(form=="Capsule"?style.PillHeight*.5:style.PillHeight<=10?Math.Max(style.PillHeight,6):7)*S*stroke;
    if(piece is Capsule tube)tube.BarThickness=thickness;
    piece.MinHeight=traced?Math.Max(10,22*scale):Math.Max(3,thickness);
    // Bars keep exactly the set thickness in every card; only traces take spare height.
    var cell=traced?GrowPanel.Grow(new Grid{Margin=new Thickness(0,1,0,7)},1,2.2):new Grid{Margin=new Thickness(0,1,0,7)};
    var auto=new RowDefinition{Height=GridLength.Auto};var star=new RowDefinition{Height=new GridLength(1,GridUnitType.Star)};
    var row=new ReadingRow(Label(m.Label,p,10.5),Value(m,p,12)){Margin=new Thickness(2,below?4:0,2,below?0:4)};
    if(below){cell.RowDefinitions.Add(star);cell.RowDefinitions.Add(auto);Grid.SetRow(row,1);}else{cell.RowDefinitions.Add(auto);cell.RowDefinitions.Add(star);Grid.SetRow(piece,1);}
    cell.Children.Add(row);cell.Children.Add(piece);content.Children.Add(cell);
   }
   else
   {
    // Text sits inside the capsule, so it never gets thinner than one line of text.
    double thickness=style.PillHeight*scale*stroke;if(piece is Capsule pill)pill.BarThickness=thickness;
    piece.MinHeight=traced?Math.Max(textRow*1.9,style.PillHeight*1.6*scale):Math.Max(textRow,thickness);
    var cell=traced?GrowPanel.Grow(new Grid{Margin=new Thickness(0,0,0,5)},1,2.4):new Grid{Margin=new Thickness(0,0,0,5)};cell.Children.Add(piece);
    var row=new ReadingRow(Label(m.Label,p,10.5),Value(m,p,12)){Margin=new Thickness(11,0,11,0),VerticalAlignment=traced?VerticalAlignment.Top:VerticalAlignment.Center};
    if(traced)row.Margin=new Thickness(12,5,12,0);
    // Text sits on the liquid: on dark glass a soft dark halo keeps light labels readable over bright fills.
    if(!light)row.Effect=TextHalo;
    cell.Children.Add(row);content.Children.Add(cell);
   }
  }
  Update(device,p);
 }
 // Share of the link in use: (download + upload) against the link speed, both in MB/s.
 private static double? LinkUse(Device device,Metric? link)
 {
  if(link?.Value is not double speed||!double.IsFinite(speed)||speed<=0)return null;
  double capacity=speed*(link.Unit=="Gbit/s"?125:.125);
  double used=device.Metrics.Where(m=>m.Unit=="MB/s"&&m.Value is double v&&double.IsFinite(v)).Sum(m=>Math.Max(0,m.Value!.Value))
   +device.Metrics.Where(m=>m.Unit=="B/s"&&m.Value is double v&&double.IsFinite(v)).Sum(m=>Math.Max(0,m.Value!.Value)/1048576d);
  return Math.Clamp(used/capacity,0,1);
 }
 private static readonly System.Windows.Media.Effects.DropShadowEffect TextHalo=CreateHalo();
 private static System.Windows.Media.Effects.DropShadowEffect CreateHalo(){var halo=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,ShadowDepth=0,BlurRadius=4,Opacity=.85};halo.Freeze();return halo;}
 private static double Luminance(Color c)=>(.2126*c.R+.7152*c.G+.0722*c.B)/255;
 public sealed class FrameDecoration:Border{}
 private static string HueShift(string hex,double degrees)
 {
  var c=C(hex);double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),l=(max+min)/2,s=0,h=0,d=max-min;
  if(d>0){s=l>.5?d/(2-max-min):d/(max+min);h=max==r?(g-b)/d+(g<b?6:0):max==g?(b-r)/d+2:(r-g)/d+4;h*=60;}
  h=(h+degrees)%360;double q=l<.5?l*(1+s):l+s-l*s,pp=2*l-q;
  double Ch(double t){t=(t%1+1)%1;return t<1/6d?pp+(q-pp)*6*t:t<.5?q:t<2/3d?pp+(q-pp)*(2/3d-t)*6:pp;}
  byte B(double v)=>(byte)Math.Round(Math.Clamp(v,0,1)*255);
  return s==0?hex:$"#{B(Ch(h/360+1/3d)):X2}{B(Ch(h/360)):X2}{B(Ch(h/360-1/3d)):X2}";
 }
 public override void Update(Device device,Preferences p)
 {
  UpdateValues(device,p);
  bool hot=device.Metrics.Any(m=>Warn(m,p));
  var a=C(Accent);var b=C(second);
  var white=light?C(Edge):Colors.White;
  BorderBrush=hot?Ui.Brush(WarningColor):style.Prism
   ?new LinearGradientBrush(new GradientStopCollection{new GradientStop(Color.FromArgb(style.RimTop,a.R,a.G,a.B),0),new GradientStop(Color.FromArgb(style.RimTop,white.R,white.G,white.B),.35),new GradientStop(Color.FromArgb(style.RimBottom,b.R,b.G,b.B),1)},new Point(0,0),new Point(1,1))
   :new LinearGradientBrush(rimTop,rimBottom,new Point(0,0),new Point(0,1));
  var heroMetric=device.Metrics.FirstOrDefault(m=>m.Id==heroId);SetHero(number,unit,heroMetric,p);
  ring?.Set(Ratio(heroMetric),heroMetric!=null&&Warn(heroMetric,p));
  foreach(var (id,pill,level) in pills){var m=device.Metrics.FirstOrDefault(x=>x.Id==id);if(links.Contains(id))pill.Exact=true;pill.Set(links.Contains(id)?LinkUse(device,m):level?Ratio(m):null,m!=null&&Warn(m,p));}
  foreach(var (id,trace) in traces)trace.Set(history.Read(DeviceId,id),history.LatestTime,TimeSpan.FromMilliseconds(Math.Max(2500,p.Interval*2.5)));
 }
 private static void DrawGlassTrack(DrawingContext dc,Rect shape,double r,Color track,GlassStyle style,bool light)
 {
  var t=style.TrackLift>0?Mix(track,Colors.White,style.TrackLift):track;
  var hairline=light?Color.FromArgb(40,0,0,0):Color.FromArgb(28,255,255,255);
  dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(style.TrackLift>0?170:150),t.R,t.G,t.B)),r>=3?new Pen(new SolidColorBrush(hairline),1):null,shape,r,r);
 }
 // Liquid fill to the reading's level with a bright meniscus at its leading edge.
 public sealed class Capsule:FrameworkElement
 {
  private readonly Color track,start,end,warn=C(WarningColor);private readonly GlassStyle style;private readonly double stroke;private readonly bool light;private double? ratio;private bool hot;
  public Color AccentColor=>start;public Color EndColor=>end;public Color TrackColor=>track;
  // Vertical: fills from the bottom (tiles, columns). Corner: fixed radius instead of a full pill.
  public bool Vertical {get;set;}
  public double? Corner {get;set;}
  // The thickness the card chose from "Linien- / Balkenstärke"; drawn by the card's layout.
  public double BarThickness {get;set;}
  public Capsule(string track,string accent,string secondary,GlassStyle style,double stroke,bool light){this.track=C(track);start=C(accent);end=C(secondary);this.style=style;this.stroke=stroke;this.light=light;IsHitTestVisible=false;}
  // Exact: the fill shows the true share without a minimum stub (link use is often near zero).
  public bool Exact {get;set;}
  public void Set(double? r,bool w){ratio=r;hot=w;InvalidateVisual();}
  protected override void OnRender(DrawingContext dc)
  {
   double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;double r=Corner??Math.Min(w,h)/2;var shape=new Rect(0,0,w,h);
   DrawGlassTrack(dc,shape,r,track,style,light);
   if(ratio is not double v||v<=0)return;
   var clip=new RectangleGeometry(shape,r,r);clip.Freeze();dc.PushClip(clip);
   var a=hot?warn:start;var b=hot?warn:end;
   if(Vertical)
   {
    double fillHeight=Exact?h*v:Math.Max(Math.Min(w,h)*.6,h*v);
    var rising=new LinearGradientBrush(Color.FromArgb(style.LiquidB,b.R,b.G,b.B),Color.FromArgb(style.LiquidA,a.R,a.G,a.B),new Point(0,0),new Point(0,1));
    dc.DrawRectangle(rising,null,new Rect(0,h-fillHeight,w,fillHeight));
    // Meniscus at the liquid level; in tiles it fades out on the left where label and value sit.
    double level=Math.Max(1.5,BarThickness>0&&Corner.HasValue?BarThickness:2*stroke);
    if(fillHeight>=level){var edge=Color.FromArgb(210,a.R,a.G,a.B);var clear=Color.FromArgb(0,a.R,a.G,a.B);
     Brush top=Corner.HasValue?new LinearGradientBrush(new GradientStopCollection{new GradientStop(clear,0),new GradientStop(clear,.55),new GradientStop(edge,.8),new GradientStop(edge,1)},new Point(0,0),new Point(1,0)):new SolidColorBrush(edge);top.Freeze();
     dc.DrawRectangle(top,null,new Rect(0,h-fillHeight,w,Math.Min(level,fillHeight)));}
    dc.Pop();return;
   }
   var liquid=new LinearGradientBrush(Color.FromArgb(style.LiquidA,a.R,a.G,a.B),Color.FromArgb(style.LiquidB,b.R,b.G,b.B),new Point(0,0),new Point(1,0));
   double fillWidth=Exact?w*v:Math.Max(h*.6,w*v);dc.DrawRectangle(liquid,null,new Rect(0,0,fillWidth,h));
   // Meniscus: a bright edge at the liquid level that fades out across the middle, where the
   // reading text sits, so it never cuts through digits.
   double meniscus=2*stroke;
   if(h>=10&&fillWidth>=meniscus){var edge=Color.FromArgb(210,a.R,a.G,a.B);var clear=Color.FromArgb(0,a.R,a.G,a.B);
    var fade=new LinearGradientBrush(new GradientStopCollection{new GradientStop(edge,0),new GradientStop(edge,.22),new GradientStop(clear,.36),new GradientStop(clear,.64),new GradientStop(edge,.78),new GradientStop(edge,1)},new Point(0,0),new Point(0,1));fade.Freeze();
    dc.DrawRectangle(fade,null,new Rect(Math.Max(0,fillWidth-meniscus),0,meniscus,h));}
   // Smoke glass: a glowing hairline along the top of the smoky liquid.
   if(style.Smoke>0&&!light)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150,a.R,a.G,a.B)),null,new Rect(0,0,fillWidth,Math.Max(1,stroke)));
   dc.Pop();
  }
 }
 // The main reading as a 270-degree glass ring filled like liquid to its level.
 public sealed class GlassRing:FrameworkElement
 {
  private readonly Color track,start,end,warn=C(WarningColor);private readonly GlassStyle style;private readonly double thickness;private readonly bool light;private double? ratio;private bool hot;
  public GlassRing(string track,string accent,string secondary,GlassStyle style,double thickness,bool light){this.track=C(track);start=C(accent);end=C(secondary);this.style=style;this.thickness=thickness;this.light=light;IsHitTestVisible=false;}
  public void Set(double? r,bool w){ratio=r;hot=w;InvalidateVisual();}
  protected override void OnRender(DrawingContext dc)
  {
   double w=ActualWidth,h=ActualHeight;if(w<20||h<20)return;double band=Math.Clamp(11*thickness,4,22),radius=Math.Min(w,h)/2-band/2-1;var center=new Point(w/2,h/2);
   Point At(double degrees)=>new(center.X+radius*Math.Cos(degrees*Math.PI/180),center.Y+radius*Math.Sin(degrees*Math.PI/180));
   Geometry Arc(double from,double sweep){var figure=new PathFigure{StartPoint=At(from)};figure.Segments.Add(new ArcSegment(At(from+sweep),new Size(radius,radius),0,sweep>180,SweepDirection.Clockwise,true));var g=new PathGeometry(new[]{figure});g.Freeze();return g;}
   var t=style.TrackLift>0?Mix(track,Colors.White,style.TrackLift):track;
   var trackPen=new Pen(new SolidColorBrush(Color.FromArgb(160,t.R,t.G,t.B)),band){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};trackPen.Freeze();
   dc.DrawGeometry(null,trackPen,Arc(135,270));
   // Glass highlight along the outer edge of the track.
   var shine=new Pen(new SolidColorBrush(light?Color.FromArgb(40,0,0,0):Color.FromArgb(34,255,255,255)),1){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};shine.Freeze();
   var outer=radius+band/2-.5;var f2=new PathFigure{StartPoint=new Point(center.X+outer*Math.Cos(135*Math.PI/180),center.Y+outer*Math.Sin(135*Math.PI/180))};f2.Segments.Add(new ArcSegment(new Point(center.X+outer*Math.Cos(405*Math.PI/180),center.Y+outer*Math.Sin(405*Math.PI/180)),new Size(outer,outer),0,true,SweepDirection.Clockwise,true));dc.DrawGeometry(null,shine,new PathGeometry(new[]{f2}));
   if(ratio is not double v||v<=0)return;
   var a=hot?warn:start;var b=hot?warn:end;
   var liquid=new LinearGradientBrush(Color.FromArgb(style.LiquidB,a.R,a.G,a.B),Color.FromArgb(Math.Max(style.LiquidB,(byte)200),b.R,b.G,b.B),new Point(0,1),new Point(1,0));liquid.Freeze();
   var fill=new Pen(liquid,band){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};fill.Freeze();
   dc.DrawGeometry(null,fill,Arc(135,Math.Max(.5,270*Math.Clamp(v,0,1))));
  }
 }
 // A live 60-second throughput trace held inside a glass capsule, scaled to its own peak.
 public sealed class TraceCapsule:FrameworkElement
 {
  private readonly Color track,start,end;private readonly GlassStyle style;private readonly double stroke;private readonly bool full,light;
  private IReadOnlyList<HistorySample> samples=Array.Empty<HistorySample>();private DateTime now;private TimeSpan gap;
  // full: the reading row sits outside, so the trace may use the whole capsule height.
  public TraceCapsule(string track,string accent,string secondary,GlassStyle style,double stroke,bool full,bool light){this.track=C(track);start=C(accent);end=C(secondary);this.style=style;this.stroke=stroke;this.full=full;this.light=light;IsHitTestVisible=false;}
  public int SampleCount=>samples.Count;
  public void Set(IReadOnlyList<HistorySample> s,DateTime now,TimeSpan gap){samples=s;this.now=now;this.gap=gap;InvalidateVisual();}
  protected override void OnRender(DrawingContext dc)
  {
   double w=ActualWidth,h=ActualHeight;if(w<=4||h<=4)return;double r=Math.Min(h/2,style.Tubes?h/2:12);var shape=new Rect(0,0,w,h);
   DrawGlassTrack(dc,shape,r,track,style,light);
   var clip=new RectangleGeometry(shape,r,r);clip.Freeze();dc.PushClip(clip);
   // A small floor keeps idle noise from filling the capsule.
   double peak=Math.Max(.05,samples.Where(s=>s.Value is double v&&double.IsFinite(v)).Select(s=>s.Value!.Value).DefaultIfEmpty(0).Max()*1.15);
   double top=full?1+stroke:h*.42,bottom=h;// with text inside, the upper part stays clear for it
   var segments=new List<List<Point>>();var points=new List<Point>();DateTime? previous=null;
   foreach(var s in samples)
   {
    double age=(now-s.Time).TotalSeconds;if(age<0||age>60)continue;
    if(s.Value is not double v||!double.IsFinite(v)){if(points.Count>0)segments.Add(points);points=new();previous=null;continue;}
    if(previous.HasValue&&s.Time-previous.Value>gap&&points.Count>0){segments.Add(points);points=new();}
    points.Add(new Point(w*(1-age/60),bottom-(bottom-top)*Math.Clamp(Math.Max(0,v)/peak,0,1)));previous=s.Time;
   }
   if(points.Count>0)segments.Add(points);
   var fill=new LinearGradientBrush(Color.FromArgb(style.LiquidB,start.R,start.G,start.B),Color.FromArgb((byte)(style.LiquidA/4),end.R,end.G,end.B),new Point(0,0),new Point(0,1));fill.Freeze();
   var pen=new Pen(new SolidColorBrush(Color.FromArgb(230,start.R,start.G,start.B)),1.5*stroke){LineJoin=PenLineJoin.Round};pen.Freeze();
   foreach(var seg in segments)
   {
    if(seg.Count<2)continue;
    var area=new StreamGeometry();using(var c=area.Open()){c.BeginFigure(new Point(seg[0].X,h),true,true);foreach(var pt in seg)c.LineTo(pt,true,true);c.LineTo(new Point(seg[^1].X,h),true,false);}area.Freeze();dc.DrawGeometry(fill,null,area);
    var line=new StreamGeometry();using(var c=line.Open()){c.BeginFigure(seg[0],false,false);for(int i=1;i<seg.Count;i++)c.LineTo(seg[i],true,true);}line.Freeze();dc.DrawGeometry(null,pen,line);
   }
   dc.Pop();
  }
 }
}
