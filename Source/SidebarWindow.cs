using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Aurora;
public sealed class SidebarWindow : Window
{
    public Preferences Prefs { get; } = Preferences.Load();
    public Snapshot? Latest { get; private set; }
    private readonly SensorService service;
    private readonly NativeDock dock;
    private readonly Forms.NotifyIcon tray;
    private readonly Grid root = new();
    private readonly CardViewport scroll = new();
    private Grid cards=>scroll.Cards;
    private readonly TextBlock status = Ui.Text("SENSOREN WERDEN ERKANNT", 9, "Muted");
    private readonly Dictionary<string, IHardwareCard> views = new();
    private HardwareSensorGroup? sensorGroup;
    private string? shape;
    private readonly MetricHistory history=new();
    private SettingsWindow? settings;
    private Button? hideButton;
    private bool placing, exiting;
    public SidebarWindow(SensorService? sensorService=null)
    {
        service=sensorService??new();
        SetResourceReference(FontFamilyProperty,"AppFont");
        Title = "Sidebar Remastered by NemeSIS"; WindowStyle = WindowStyle.None; AllowsTransparency=true;ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty,"Back"); SetResourceReference(ForegroundProperty,"Text");
        ShowInTaskbar = false; Width = Prefs.ColumnWidth; Height = 800;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        dock = new(this);
        root.Margin = new Thickness(2, 0, 2, 0);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(4, 5, 2, 5) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var gear = SidebarActionButton.Create(SidebarActionIcon.Settings,OpenSettings);
        var hide = SidebarActionButton.Create(SidebarActionIcon.Minimize,HideToTray);hide.Margin=new Thickness(6,0,0,0);
        hideButton=hide;
        actions.Children.Add(gear); actions.Children.Add(hide); Grid.SetColumn(actions, 1); header.Children.Add(actions); root.Children.Add(header);
        Grid.SetRow(scroll,1); root.Children.Add(scroll);
        Content = root;
        var menu = new ContextMenu();
        menu.SetResourceReference(FontFamilyProperty,"AppFont");
        var m1 = new MenuItem { Header = "Settings" }; m1.Click += (_,_) => OpenSettings(); menu.Items.Add(m1);
        var m2 = new MenuItem { Header = "Hide" }; m2.Click += (_,_) => HideToTray(); menu.Items.Add(m2);
        var m3 = new MenuItem { Header = "Exit" }; m3.Click += (_,_) => Exit(); menu.Items.Add(m3); ContextMenu = menu;
        tray = new Forms.NotifyIcon { Text = "Sidebar Remastered by NemeSIS", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Visible = true };
        tray.DoubleClick += (_,_) => Dispatcher.BeginInvoke(new Action(ShowSidebar));
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Show sidebar", null, (_,_) => Dispatcher.BeginInvoke(new Action(ShowSidebar)));
        trayMenu.Items.Add("Settings", null, (_,_) => Dispatcher.BeginInvoke(new Action(OpenSettings)));
        trayMenu.Items.Add("Exit", null, (_,_) => Dispatcher.BeginInvoke(new Action(Exit))); tray.ContextMenuStrip = trayMenu;
        SourceInitialized += (_,_) => { dock.Hook(Place); Apply(); };
        Loaded += (_,_) => { Apply(); service.Updated += OnSnapshot; service.Start(); };
        SizeChanged += (_,_) => ResizeCards();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        KeyDown += (_,e) => { if(e.Key == Key.Escape) HideToTray(); };
        Closed += (_,_) => { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; service.Stop(); dock.Release(); tray.Dispose(); settings?.Close(); try { service.Worker?.Wait(2000); } catch { } };
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Place));
    public void Apply()
    {
        Application.Current.Resources["AppFont"]=Ui.Font(Prefs,"Segoe UI");
        ApplyTheme(); Topmost = Prefs.Topmost; Opacity = 1; service.Interval = Prefs.Interval;
        Prefs.SensorSource=SensorSources.Normalize(Prefs.SensorSource);
        if(service.Source!=Prefs.SensorSource){Latest=null;shape=null;cards.Children.Clear();views.Clear();}
        service.Source=Prefs.SensorSource;
        service.CpuTemperatureSources=new Dictionary<string,string>(Prefs.CpuTemperatureSources);
        Place(); shape = null; if (Latest != null) Render(Latest);
        try { Prefs.Save(); } catch(Exception e) { status.Text = "Speichern fehlgeschlagen"; status.ToolTip = e.Message; }
    }
    private void ApplyTheme()
    {
        var d=DesignCatalog.Resolve(Prefs);
        var background=Ui.Brush(d.Back);background.Opacity=0;Application.Current.Resources["Back"]=background;
        var panel=Ui.Brush(d.Panel);panel.Opacity=Prefs.Opacity;Application.Current.Resources["Panel"]=panel;
        Application.Current.Resources["Chrome"]=Ui.Brush(d.Panel);
        Application.Current.Resources["Edge"]=Ui.Brush(d.Edge);
        Application.Current.Resources["Text"]=Ui.Brush(d.Text);
        Application.Current.Resources["Muted"]=Ui.Brush(d.Muted);
        Application.Current.Resources["HardwareName"]=Ui.Brush(DesignCatalog.ColorOr(Prefs.HardwareNameColor,d.Text));
        Application.Current.Resources["Accent"]=Ui.Brush(d.Accent);
    }
    private void Place()
    {
        if (placing || !IsLoaded) return;
        placing = true; try { dock.Place(Prefs); ResizeCards(); } finally { placing = false; }
    }
    private void ResizeCards()
    {
        if(hideButton!=null)hideButton.Visibility=ActualWidth<80?Visibility.Collapsed:Visibility.Visible;
        double availableHeight=Math.Max(100,root.ActualHeight>0?root.ActualHeight-root.RowDefinitions[0].ActualHeight:ActualHeight-28);
        scroll.Configure(Prefs.Fit,Math.Max(1,ActualWidth-4),availableHeight,Prefs.Columns>1,Prefs.ColumnWidth<200?200*Prefs.Columns:0);
    }
    private void OnSnapshot(Snapshot snapshot)
    {
        if (Dispatcher.HasShutdownStarted || exiting) return;
        Dispatcher.BeginInvoke(new Action(() => { if(snapshot.SensorSource.Length>0&&snapshot.SensorSource!=SensorSources.Normalize(Prefs.SensorSource))return; Latest = snapshot; Render(snapshot); settings?.Refresh(snapshot); }));
    }
    private void Render(Snapshot snapshot)
    {
        if(SensorPreferenceMigration.Apply(snapshot,Prefs))try{Prefs.Save();}catch(Exception e){status.ToolTip=e.Message;}
        if (!Prefs.DeviceDefaultsApplied && snapshot.Devices.Count > 0)
        {
            foreach(var d in snapshot.Devices.Where(d=>!d.DefaultVisible)) if (!Prefs.HiddenDevices.Contains(d.Id)) Prefs.HiddenDevices.Add(d.Id);
            Prefs.DeviceDefaultsApplied = true;
            try { Prefs.Save(); } catch { }
        }
        var data = SensorCardComposition.Visible(snapshot,Prefs);
        history.Record(data,snapshot.Time);
        var key = string.Join("|", data.Select(d => d.Id + ":" + d.Name + ":" + string.Join(",",d.Metrics.Select(m=>m.Id+"="+m.Label))));
        if (shape != key)
        {
            shape = key; cards.Children.Clear(); cards.ColumnDefinitions.Clear(); views.Clear();
            sensorGroup=null;
            if(data.Count==0)cards.Children.Add(Ui.Text("No cards enabled.\nSelect devices in Settings ⚙.",13,"Muted"));
            else if(Prefs.CombineSensorCards)
            {
                sensorGroup=new HardwareSensorGroup(data,Prefs,history);cards.Children.Add(sensorGroup);
                foreach(var pair in sensorGroup.Sections)views[pair.Key]=pair.Value;
            }
            else
            {
                Panel layout=Prefs.Columns>1?new HardwareCardGrid{Columns=Prefs.Columns}:new StackPanel();cards.Children.Add(layout);
                foreach(var d in data){var card=CardLayouts.Create(d,Prefs,history);views[d.Id]=card;layout.Children.Add(card.View);}
            }
        }
        if(sensorGroup!=null)sensorGroup.Update(data,Prefs);
        else foreach (var d in data) if (views.TryGetValue(d.Id, out var view)) view.Update(d, Prefs);
        status.Text = snapshot.Time.ToString("HH:mm:ss") + " · " + snapshot.Devices.Count + " GERÄTE";
        status.ToolTip = snapshot.Warnings.Count > 0 ? string.Join("\n", snapshot.Warnings) : "Lokale Live-Abfragen · Keine Cloud-Verbindung";
        if (data.Count == 0 && cards.Children.Count == 0) cards.Children.Add(Ui.Text("No cards enabled.\nSelect devices in Settings ⚙.", 13, "Muted"));
        ResizeCards();
    }
    public void OpenSettingsForDevice(string deviceId){OpenSettings();settings?.SelectDevice(deviceId);}
    public void OpenSettings()
    {
        if (settings == null) { settings = new SettingsWindow(this); settings.Closed += (_,_) => settings = null; }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Show(); settings.Activate();
    }
    private void HideToTray() { dock.Release(); Hide(); }
    private void ShowSidebar() { Show(); Apply(); Activate(); }
    public void Restore() => ShowSidebar();
    public void RevealPinnedValues(string? sensorId=null){Dispatcher.BeginInvoke(new Action(()=>{string target=sensorId!=null&&Latest!=null?PinPlacement.Target(Latest,Prefs,sensorId)?.Id??"custom":"custom";if(views.TryGetValue(target,out var view))view.View.BringIntoView();}));}
    private void Exit() { exiting = true; Close(); Application.Current.Shutdown(); }
}

public static class Ui
{
    public static IReadOnlyList<string> FontNames { get; } = Fonts.SystemFontFamilies.Select(f=>f.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).ToArray();
    public static FontFamily Font(Preferences prefs,string fallback) => new(FontNames.FirstOrDefault(n=>n.Equals(prefs.TextFontFamily,StringComparison.OrdinalIgnoreCase)) ?? fallback);
    public static string SidebarLabel(string label)=>label switch{"Interne IP" or "Internal IP"=>"Int IP","Externe IP" or "External IP"=>"Ext IP","Download"=>"DL","Upload"=>"UL","Adapter-Link" or "Adapter link" or "Link speed" or "Verbindung"=>"Link","Temperatur" or "Temperature" or "Temperatur (Package)" or "Temperature (Package)" or "Temp (Package)"=>"Temp","Auslastung"=>"Load","Takt Ø" or "Average clock" or "Average Clock"=>"Clock","Takt"=>"Clock","Leistung"=>"Power","Spannung"=>"Voltage","Lüfter"=>"Fan","Belegt"=>"Used","Verfügbar"=>"Available","Frei"=>"Free","Gesamt"=>"Total","Kapazität"=>"Capacity","Lesen"=>"Read","Schreiben"=>"Write","VRAM belegt"=>"VRAM used","VRAM Last"=>"VRAM load","Verbleibende Lebensdauer"=>"Remaining life","Mainboard"=>"Motherboard","Uhrzeit"=>"Time","Tagesfortschritt"=>"Day","Kalenderwoche"=>"Week","Betriebszeit"=>"Uptime","Prozesse"=>"Processes","Ladung"=>"Charge","Restzeit"=>"Remaining","Custom"=>"Custom sensors","Controller"=>"Controllers",_=>label.Replace("Temperatur (","Temp (").Replace("Temperature (","Temp (").Replace("Nicht verfügbar","Unavailable")};
    public static string SidebarFullLabel(string label)=>label switch{"Interne IP" or "Internal IP" or "Int IP"=>"Internal IP address","Externe IP" or "External IP" or "Ext IP"=>"External IP address (default internet route)","Temperatur" or "Temperature" or "Temp"=>"Temperature","Takt Ø" or "Average clock" or "Average Clock"=>"Average clock","Download" or "DL"=>"Download","Upload" or "UL"=>"Upload","Adapter-Link" or "Adapter link" or "Link speed" or "Link" or "Verbindung"=>"Adapter link speed",_=>SidebarLabel(label)};
    public static string HardwareName(string name)=>name.Replace("Physischer Arbeitsspeicher","Physical memory").Replace("Eigene Werte ohne Widget-Zuordnung","Unassigned custom readings").Replace("Deine angehefteten Sensoren","Pinned sensors");
    public static TextBlock SidebarText(string text,double size=13,string resource="Text"){var block=Text(SidebarLabel(text),size,resource);SidebarTextVisibility.SetRole(block,new[]{"CPU","GPU","RAM","Drives","Network","Mainboard","Controller","Custom"}.Contains(text,StringComparer.OrdinalIgnoreCase)?SidebarTextRole.WidgetTitles:SidebarTextRole.Labels);block.ToolTip=SidebarFullLabel(text);return block;}
    public static TextBlock HardwareTitle(string name,double size,bool literal=false){var text=Text(literal?name:HardwareName(name),size,"HardwareName");text.FontWeight=FontWeights.SemiBold;text.ToolTip=text.Text;System.Windows.Automation.AutomationProperties.SetAutomationId(text,"HardwareName");return text;}
    public static SolidColorBrush ValueBrush(Preferences prefs,string fallback)=>Brush(DesignCatalog.ColorOr(prefs.ValueColor,fallback));
    public static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    public static TextBlock Text(string text, double size = 13, string resource = "Text") { var t = new SidebarTextBlock { Text = text, FontSize = size }; t.SetResourceReference(TextBlock.ForegroundProperty, resource); if(text=="—")SidebarTextVisibility.SetRole(t,SidebarTextRole.Readings); return t; }
    public static TextBlock RoleText(SidebarTextRole role,string text,double size=13,string resource="Text"){var block=Text(text,size,resource);SidebarTextVisibility.SetRole(block,role);return block;}
    public static Button Button(string text, Action click) { var b = new Button { Content = text }; b.Click += (_,_) => click(); return b; }
    public static string Color(string category) => category switch { "CPU" => "#66DEC5", "GPU" => "#AC9BFA", "RAM" => "#78B9FC", "Drives" => "#F6BC70", "Network" => "#7DD9EC", _ => "#ADBBD0" };
    public static string Icon(string category) => category switch { "CPU" => "▦", "GPU" => "▣", "RAM" => "▥", "Drives" => "▰", "Network" => "⇅", "Custom" => "✧", "Clock" => "◷", "System" => "▤", "Battery" => "▭", _ => "◇" };
    public static FrameworkElement HardwareIcon(string category,double size)
    {
        string path=category switch {
            "CPU"=>"M5,5 L15,5 15,15 5,15 Z M8,8 L12,8 12,12 8,12 Z M7,1 L7,5 M10,1 L10,5 M13,1 L13,5 M7,15 L7,19 M10,15 L10,19 M13,15 L13,19 M1,7 L5,7 M1,10 L5,10 M1,13 L5,13 M15,7 L19,7 M15,10 L19,10 M15,13 L19,13",
            "RAM"=>"M2,5 L18,5 18,14 2,14 Z M5,7 L5,12 M8,7 L8,12 M11,7 L11,12 M14,7 L14,12 M4,14 L4,17 M7,14 L7,17 M10,14 L10,17 M13,14 L13,17 M16,14 L16,17",
            "GPU"=>"M2,2 L18,2 18,14 2,14 Z M10,14 L10,18 M6,18 L14,18",
            "Drives"=>"M3,6 L17,6 19,8 19,13 17,15 3,15 1,13 1,8 Z M4,11 L6,11 M15,11 L16,11",
            "Network"=>"M5,15 C0,15 0,8 5,8 C5,1 15,1 15,8 C21,8 21,15 16,15 M10,7 L10,18 M7,15 L10,18 13,15",
            "Clock"=>"M10,1 A9,9 0 1 1 9.99,1 Z M10,5 L10,10 14,12",
            "System"=>"M2,3 L18,3 18,16 2,16 Z M2,6 L18,6 M5,13 L5,11 M8,13 L8,9 M11,13 L11,10 M14,13 L14,8",
            "Battery"=>"M2,6 L16,6 16,14 2,14 Z M16,8.5 L18.5,8.5 18.5,11.5 16,11.5 M4,8 L9,8 9,12 4,12 Z",
            _=>"M10,1 L19,10 10,19 1,10 Z M10,6 L10,14 M6,10 L14,10" };
        var icon=new System.Windows.Shapes.Path { Data=Geometry.Parse(path),Width=size,Height=size,StrokeThickness=1.15,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(icon,"HardwareIcon");
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Text"); return icon;
    }
}
