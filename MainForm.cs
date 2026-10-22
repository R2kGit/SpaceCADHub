// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class ChoiceBox : ComboBox
{
    internal ChoiceBox(){DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=24;}
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();int index=e.Index>=0?e.Index:SelectedIndex;
        if(index>=0 && index<Items.Count)TextRenderer.DrawText(e.Graphics,GetItemText(Items[index]),Font,e.Bounds,e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}


internal sealed partial class MainForm : Form
{
    private readonly HubSettings settings;
    private readonly ControllerHub hub;
    private CadProfile editing;
    private Process adapter;
    private bool closing, loading;
    private int selectedKey;
    private readonly Color navy=Color.FromArgb(19,31,51),blue=Color.FromArgb(37,107,233),muted=Color.FromArgb(105,119,141),paper=Color.FromArgb(242,245,250);
    private Panel page;
    private Label saveStatus, editorTitle, assignmentSummary;
    private DeviceStatusCard deviceCard;
    private ComboBox profiles, keyPicker, actionPicker;
    private TextBox shortcut;
    private ControllerMap map;
    private MotionMonitor monitor;
    private MotionValues motionValues;
    private Button calibrationButton;
    private Label calibrationStatus;
    private bool calibrationArmed, calibrationPending;
    private TextBox displayPreview;
    private bool displayEdited;
    private readonly Dictionary<string,Label> pluginStates=new Dictionary<string,Label>();
    private readonly List<ProgressBar> axes=new List<ProgressBar>();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private NotifyIcon tray;
    private readonly ToolTip helpTips=new ToolTip{InitialDelay=450,ReshowDelay=100,AutoPopDelay=12000,ShowAlways=false};
    private string currentPage="Belegung";
    internal MainForm(HubSettings initialSettings=null)
    {
        settings=initialSettings??HubSettings.Load();editing=settings.Active;hub=new ControllerHub(settings);
        Text="Space Controller Hub · 1.0.11";ClientSize=new Size(1220,800);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
        string icon=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","hub.ico");if(File.Exists(icon))Icon=new Icon(icon);
        AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10);BackColor=paper;StartPosition=FormStartPosition.CenterScreen;
        BuildShell(); ShowPage("Belegung");
        tray=new NotifyIcon {Icon=Icon,Text="Space Controller Hub 1.0.11",Visible=true};
        var menu=new ContextMenuStrip();menu.Items.Add("CAD Hub öffnen",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();});
        menu.Items.Add("Beenden",null,(s,e)=>{closing=true;Close();});tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();};
        timer.Interval=33;timer.Tick+=(s,e)=>RefreshLive();
        Shown+=(s,e)=>{hub.Start();timer.Start();EnsureAdapter();if(settings.StartMinimized)Hide();};
        FormClosing+=(s,e)=>{if(!closing&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();return;}timer.Stop();StopAdapter();hub.Dispose();tray.Dispose();helpTips.Dispose();};
    }
    private Label LabelAt(Control parent,string text,int x,int y,int width=400,int size=10,bool bold=false,Color? color=null)
    {
        var l=new Label{Text=text,UseMnemonic=false,Left=x,Top=y,Width=width,Height=size>=16?46:30,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=color??navy,BackColor=Color.Transparent};parent.Controls.Add(l);return l;
    }
    private Button ButtonAt(Control parent,string text,int x,int y,int width,Action action,bool primary=false)
    {
        var b=new Button{Text=text,UseMnemonic=false,Left=x,Top=y,Width=width,Height=38,FlatStyle=FlatStyle.Flat,BackColor=primary?blue:Color.White,ForeColor=primary?Color.White:navy,Cursor=Cursors.Hand};
        b.FlatAppearance.BorderColor=primary?blue:Color.FromArgb(215,223,235);b.Click+=(s,e)=>Safe(action);parent.Controls.Add(b);return b;
    }
    private void Safe(Action action){try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"CAD Hub",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
    private Panel Card(int x,int y,int w,int h){var p=new Panel{Left=x,Top=y,Width=w,Height=h,BackColor=Color.White};page.Controls.Add(p);return p;}
    private void StyleSidebarButton(Button button)
    {
        button.BackColor=Color.FromArgb(31,48,74);button.ForeColor=Color.White;button.FlatAppearance.BorderSize=0;
    }
    private void BuildShell()
    {
        var sidebar=new Panel{Dock=DockStyle.Left,Width=210,BackColor=navy};Controls.Add(sidebar);
        string logo=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","hub-logo.png");
        if(File.Exists(logo))sidebar.Controls.Add(new PictureBox{Left=35,Top=14,Width=140,Height=140,SizeMode=PictureBoxSizeMode.Zoom,Image=Image.FromFile(logo)});
        LabelAt(sidebar,"Space Controller Hub",18,162,192,12,true,Color.White);
        LabelAt(sidebar,"1.0.11",24,193,170,9,true,Color.FromArgb(76,210,207));
        LabelAt(sidebar,"KONFIGURATION",24,237,180,8,true,Color.FromArgb(133,154,181));
        int y=271;foreach(string name in new[]{"Belegung","Bewegung","Plugins & Profile","Display & Ring"})
        {
            string n=name;StyleSidebarButton(ButtonAt(sidebar,name,16,y,178,()=>ShowPage(n)));y+=49;
        }
        LabelAt(sidebar,"GERÄT",24,520,170,8,true,Color.FromArgb(133,154,181)).Height=18;
        deviceCard=new DeviceStatusCard{Left=16,Top=546,Width=178,Height=98};sidebar.Controls.Add(deviceCard);
        StyleSidebarButton(ButtonAt(sidebar,"Softwareinfo",16,674,178,ShowSoftwareInfo));
        StyleSidebarButton(ButtonAt(sidebar,"Ins Tray",16,727,178,()=>Hide()));
        var header=new Panel{Left=210,Top=0,Height=82,Width=1010,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,BackColor=Color.White};Controls.Add(header);
        LabelAt(header,"Aktives CAD-Profil",30,31,145,9,false,muted);
        profiles=new ChoiceBox{Left=180,Top=24,Width=220,DropDownStyle=ComboBoxStyle.DropDownList};
        profiles.Items.AddRange(settings.Profiles.Select(p=>(object)p.Id).ToArray());profiles.SelectedItem=settings.ActiveProfile;header.Controls.Add(profiles);
        profiles.SelectedIndexChanged+=(s,e)=>{if(profiles.SelectedItem==null)return;Safe(()=>{settings.ActiveProfile=Convert.ToString(profiles.SelectedItem);editing=settings.Active;hub.ApplySettings();EnsureAdapter();ShowPage(currentPage);});};
        ButtonAt(header,"CAD starten",430,20,145,StartCad,true);
        saveStatus=LabelAt(header,"Änderungen werden sofort gespeichert",610,34,370,9,false,muted);
        page=new Panel{Left=210,Top=82,Width=1010,Height=718,Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,AutoScroll=true,BackColor=paper};Controls.Add(page);
    }
    private void ShowPage(string name)
    {
        currentPage=name;calibrationArmed=false;displayPreview=null;displayEdited=false;foreach(Control c in page.Controls.Cast<Control>().ToArray())c.Dispose();page.Controls.Clear();axes.Clear();pluginStates.Clear();map=null;monitor=null;motionValues=null;calibrationButton=null;calibrationStatus=null;pluginTable=null;pluginDetails=null;editing=settings.Active;
        LabelAt(page,name,28,23,600,22,true);
        saveStatus.Text=name=="Display & Ring"?"Display & Ring: mit Übernehmen speichern":"Änderungen werden sofort gespeichert";
        if(name=="Belegung") BuildBindings();else if(name=="Bewegung")BuildMotion();else if(name=="Display & Ring")BuildDisplay();else BuildPluginTable();
    }
    private void BuildBindings()
    {
        LabelAt(page,"Wähle ein Bedienelement und weise ihm eine CAD-Funktion oder ein Tastenkürzel zu.",30,64,950,10,false,muted);
        var left=Card(28,108,557,525);
        map=new ControllerMap{Left=8,Top=12,Width=541,Height=438,Selected=selectedKey,Font=new Font("Segoe UI",8),DescribeKey=DescribeBinding};left.Controls.Add(map);
        left.Controls.Add(new MotionLegend{Left=16,Top=458,Width=525,Height=58});
        map.SelectKey=i=>{selectedKey=i;keyPicker.SelectedIndex=i;};
        var right=Card(607,108,374,525);LabelAt(right,"ZUWEISUNG",22,20,325,9,true,muted);
        editorTitle=LabelAt(right,"Taste 1",22,53,325,21,true);
        keyPicker=new ChoiceBox{Left=22,Top=100,Width=325,DropDownStyle=ComboBoxStyle.DropDownList};right.Controls.Add(keyPicker);
        keyPicker.Items.AddRange(Actions.Keys.Select(k=>(object)("Taste "+k)).Concat(new object[]{"Ring links","Ring rechts"}).ToArray());
        keyPicker.SelectedIndexChanged+=(s,e)=>{selectedKey=keyPicker.SelectedIndex;map.Selected=selectedKey;map.Invalidate();LoadBinding();};
        LabelAt(right,"Funktion",22,151,325,10,true);
        actionPicker=new ChoiceBox{Left=22,Top=184,Width=325,DropDownStyle=ComboBoxStyle.DropDownList};right.Controls.Add(actionPicker);actionPicker.Items.AddRange(Actions.All);actionPicker.Items.Add(new ActionChoice("custom","Eigenes Tastenkürzel …"));
        actionPicker.SelectedIndexChanged+=(s,e)=>{if(loading)return;var a=actionPicker.SelectedItem as ActionChoice;shortcut.Enabled=a!=null&&a.Id=="custom";if(a!=null&&a.Id!="custom")SaveBinding(a.Id);};
        LabelAt(right,"Eigenes Kürzel  ·  z. B. Ctrl+Shift+S",22,236,330,9,false,muted);
        shortcut=new TextBox{Left=22,Top=267,Width=325};right.Controls.Add(shortcut);
        helpTips.SetToolTip(shortcut,"Einzeltaste: Enter, Escape, Tab, Space, Delete.\nKombinationen: Ctrl+S oder Shift+Enter. Kein key:-Präfix eingeben.\nShift/Ctrl/Alt allein werden derzeit nicht als gehaltene Tasten gesendet.\nDie unterstützten Kürzel hängen auch vom CAD-Adapter ab.");
        ButtonAt(right,"Kürzel zuweisen",22,306,325,()=>{string value=shortcut.Text.Trim();if(value.Length==0||value.Length>90||value.Contains("\n"))throw new InvalidDataException("Bitte ein Tastenkürzel wie Ctrl+S eingeben.");SaveBinding("key:"+value);});
        assignmentSummary=LabelAt(right,"",22,373,325,11,true);assignmentSummary.Height=80;
        LabelAt(right,"Gilt für "+editing.Id+". Die Belegung wird\nsofort an den aktiven Adapter übergeben.",22,462,330,9,false,muted).Height=50;
        keyPicker.SelectedIndex=selectedKey;
    }
    private string Binding {get{return selectedKey<17?editing.Bindings[selectedKey]:selectedKey==17?editing.RingLeft:editing.RingRight;}}
    private string DescribeBinding(int index)
    {
        string name=index<17?"Taste "+Actions.Keys[index]:index==17?"Ring links":"Ring rechts";
        string action=index<17?editing.Bindings[index]:index==17?editing.RingLeft:editing.RingRight;
        return name+" · "+Actions.Label(action);
    }
    private void LoadBinding()
    {
        loading=true;editorTitle.Text=selectedKey<17 ? "Taste "+Actions.Keys[selectedKey] : selectedKey==17 ? "Ring links" : "Ring rechts";
        string value=Binding;actionPicker.SelectedItem=actionPicker.Items.Cast<ActionChoice>().FirstOrDefault(a=>a.Id==(value.StartsWith("key:")?"custom":value));
        shortcut.Text=value.StartsWith("key:")?value.Substring(4):"";shortcut.Enabled=value.StartsWith("key:");assignmentSummary.Text="Aktuell:\n"+Actions.Label(value);loading=false;
    }
    private void SaveBinding(string value)
    {
        if(selectedKey<17)editing.Bindings[selectedKey]=value;else if(selectedKey==17)editing.RingLeft=value;else editing.RingRight=value;
        Save();LoadBinding();map.RefreshTooltip();
    }
    private void Save(){hub.ApplySettings();saveStatus.Text="Gespeichert · "+DateTime.Now.ToString("HH:mm:ss");}
    private void AddSlider(Control parent,string title,int y,int val,int max,Action<int> changed)
    {
        var caption=LabelAt(parent,title,22,y,190,10,true);var number=LabelAt(parent,val.ToString(),420,y,60,11,true,blue);
        var t=new TrackBar{Left=210,Top=y-4,Width=200,Minimum=max==100?5:0,Maximum=max,Value=Math.Max(max==100?5:0,Math.Min(max,val)),TickStyle=TickStyle.None,BackColor=Color.White};parent.Controls.Add(t);
        string help=MotionHelp(title);helpTips.SetToolTip(caption,help);helpTips.SetToolTip(number,help);helpTips.SetToolTip(t,help);
        t.ValueChanged+=(s,e)=>{number.Text=t.Value.ToString();changed(t.Value);Save();};
    }
    private void BuildMotion()
    {
        LabelAt(page,"Alle Einstellungen gehören zum ausgewählten CAD-Profil.",30,64,950,10,false,muted);
        var a=Card(28,108,500,360);LabelAt(a,"GEFÜHL & PRÄZISION",22,20,450,9,true,muted);
        AddSlider(a,"Translation",68,editing.Translation,15,v=>editing.Translation=v);AddSlider(a,"Rotation",116,editing.Rotation,15,v=>editing.Rotation=v);
        AddSlider(a,"Totzone",164,editing.Deadzone,15,v=>editing.Deadzone=v);AddSlider(a,"Direktheit",212,editing.Smoothing,100,v=>editing.Smoothing=v);
        calibrationButton=ButtonAt(a,"Nullstellung kalibrieren",22,288,220,ConfirmCalibration);
        calibrationStatus=new Label{Text="",Left=256,Top=284,Width=222,Height=60,Font=new Font("Segoe UI",9),ForeColor=muted,UseMnemonic=false};a.Controls.Add(calibrationStatus);
        var note=Card(550,108,431,360);LabelAt(note,"LIVE-KOPF",22,20,380,9,true,muted);
        monitor=new MotionMonitor{Left=8,Top=46,Width=235,Height=207};note.Controls.Add(monitor);
        motionValues=new MotionValues{Left=252,Top=46,Width=163,Height=207};note.Controls.Add(motionValues);
        note.Controls.Add(new MotionLegend{Left=16,Top=277,Width=399,Height=56});
        string[] names={"X · Links / rechts","Z · Heben / senken","Y · Vor / zurück","A · Kippen","B · Drehen","C · Rollen"};
        var b=Card(28,490,953,200);LabelAt(b,"ACHSEN",22,20,400,9,true,muted);
        for(int i=0;i<6;i++)
        {
            int k=i,x=22+(i%3)*310,y=64+(i/3)*73;LabelAt(b,names[i],x,y,290,10,true);
            var enabled=new CheckBox{Left=x,Top=y+29,Width=115,Text="Aktiv",Checked=editing.AxisEnabled[i]};b.Controls.Add(enabled);
            var inverted=new CheckBox{Left=x+120,Top=y+29,Width=155,Text="Invertieren",Checked=editing.AxisInverted[i]};b.Controls.Add(inverted);
            enabled.CheckedChanged+=(s,e)=>{editing.AxisEnabled[k]=enabled.Checked;Save();};inverted.CheckedChanged+=(s,e)=>{editing.AxisInverted[k]=inverted.Checked;Save();};
        }
    }
    private DataGridView pluginTable;
    private void BuildDisplay()
    {
        LabelAt(page,"Displayvorlagen und Zahlenring gelten für das aktive CAD-Profil. Hier mit »Übernehmen« speichern.",30,64,950,10,false,muted);
        var left=Card(28,108,500,490);LabelAt(left,"DIE SIEBEN LCD-ZEILEN",22,20,456,9,true,muted);
        var inputs=new TextBox[7];
        var right=Card(550,108,431,490);LabelAt(right,"LCD-VORSCHAU",22,20,387,9,true,muted);
        displayPreview=new TextBox{Left=22,Top=58,Width=387,Height=175,Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Horizontal,BackColor=Color.Black,ForeColor=Color.FromArgb(120,235,220),Font=new Font("Consolas",12),BorderStyle=BorderStyle.FixedSingle};right.Controls.Add(displayPreview);
        LabelAt(right,"Textvorschau · kein pixelgenaues LCD-Abbild",22,242,387,9,false,muted);
        var enabled=new CheckBox{Left=22,Top=282,Width=387,Height=30,Text="Zahlenring aktivieren (experimentell)",Checked=editing.NumberRingEnabled};right.Controls.Add(enabled);
        LabelAt(right,"RASTWEITEN · AUSWAHL ODER FREIE EINGABE",22,319,387,9,true,muted);
        var steps=new ComboBox[3];string[] stepLabels={"Körper / übrige Felder*","Skizzenlängen (mm)","Skizzenwinkel (°)"};
        double[] stepValues={editing.NumberRingStep,editing.SketchRingStep,editing.SketchAngleRingStep};
        for(int i=0;i<3;i++)
        {
            LabelAt(right,stepLabels[i],22,349+i*35,230,9,true);
            var combo=new ComboBox{Left=267,Top=344+i*35,Width=142,DropDownStyle=ComboBoxStyle.DropDown};
            combo.Items.AddRange(new object[]{"0,001","0,01","0,05","0,1","0,5","1","5","10"});
            combo.Text=stepValues[i].ToString("0.#########",System.Globalization.CultureInfo.CurrentCulture);right.Controls.Add(combo);steps[i]=combo;
        }
        LabelAt(right,"* In der Feldeinheit. Skizzen: mm bzw. Grad.\nFormeln bleiben unverändert. Ohne Zahlenfeld: Zoom.",22,452,387,9,false,muted).Height=36;
        Action refresh=()=>{var candidate=editing.Copy();for(int i=0;i<7;i++)if(inputs[i]!=null)candidate.DisplayLines[i]=inputs[i].Text;double v;if(TryRingStep(steps[0].Text,out v))candidate.NumberRingStep=v;displayPreview.Text=string.Join(Environment.NewLine,HubDisplay.Build(candidate,hub.ActiveContext,false,null));};
        for(int i=0;i<7;i++)
        {
            LabelAt(left,(i+1).ToString(),22,63+i*40,20,9,false,muted);
            inputs[i]=new TextBox{Left=44,Top=60+i*40,Width=434,Text=editing.DisplayLines[i],MaxLength=100};left.Controls.Add(inputs[i]);
            inputs[i].TextChanged+=(s,e)=>{displayEdited=true;refresh();};
        }
        enabled.CheckedChanged+=(s,e)=>displayEdited=true;foreach(var combo in steps)combo.TextChanged+=(s,e)=>{displayEdited=true;refresh();};
        ButtonAt(left,"Display & Ring übernehmen",22,365,456,()=>{
            double body,sketch,angle;
            if(!TryRingStep(steps[0].Text,out body)||!TryRingStep(steps[1].Text,out sketch)||!TryRingStep(steps[2].Text,out angle)){MessageBox.Show(this,"Bitte Rastweiten zwischen 0,001 und 1000 eingeben (Komma oder Punkt).","Rastweiten",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            var candidate=editing.Copy();candidate.DisplayLines=inputs.Select(t=>t.Text).ToArray();candidate.NumberRingEnabled=enabled.Checked;candidate.NumberRingStep=body;candidate.SketchRingStep=sketch;candidate.SketchAngleRingStep=angle;candidate.Validate();
            editing.DisplayLines=candidate.DisplayLines;editing.NumberRingEnabled=candidate.NumberRingEnabled;editing.NumberRingStep=candidate.NumberRingStep;editing.SketchRingStep=candidate.SketchRingStep;editing.SketchAngleRingStep=candidate.SketchAngleRingStep;Save();displayEdited=false;refresh();
        },true);
        LabelAt(left,"Platzhalter: {cad}, {ring}, {context}, {value}, {step}, {key1} … {key17}\nMaximal 24 Zeichen je LCD-Zeile; lange Texte enden mit ...\nLCD-Updates erfolgen in Eingabepausen, nicht während Bewegung.",22,420,456,9,false,muted).Height=66;
        refresh();
    }
    private static bool TryRingStep(string text,out double value)
    {return double.TryParse((text??"").Trim().Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint|System.Globalization.NumberStyles.AllowLeadingSign,System.Globalization.CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=.001&&value<=1000;}
    private Label pluginDetails;
    private string selectedPlugin;
    private CadProfile SelectedPlugin
    {
        get {return settings.Profiles.FirstOrDefault(p=>p.Id==selectedPlugin) ?? settings.Active;}
    }
    private string AdapterName(CadProfile p)
    {
        return PluginCatalog.Name(p);
    }
    private void BuildPluginTable()
    {
        LabelAt(page,"Adapter, Programme und Profile gemeinsam verwalten.",30,64,950,10,false,muted);
        var card=Card(28,108,953,294);
        pluginTable=new DataGridView{Dock=DockStyle.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,RowHeadersVisible=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EnableHeadersVisualStyles=false};
        pluginTable.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=navy,ForeColor=Color.White,Font=new Font("Segoe UI",9,FontStyle.Bold),Padding=new Padding(6)};
        pluginTable.ColumnHeadersHeight=42;pluginTable.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        pluginTable.DefaultCellStyle=new DataGridViewCellStyle{Font=new Font("Segoe UI",9),Padding=new Padding(6),SelectionBackColor=Color.FromArgb(227,237,254),SelectionForeColor=navy,ForeColor=navy};
        pluginTable.RowTemplate.Height=50;pluginTable.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(246,248,252);
        pluginTable.Columns.Add(new DataGridViewCheckBoxColumn{Name="Enabled",HeaderText="Aktiv",FillWeight=45});
        pluginTable.Columns.Add(new DataGridViewTextBoxColumn{Name="Name",HeaderText="CAD / Profil",ReadOnly=true,FillWeight=105});
        pluginTable.Columns.Add(new DataGridViewTextBoxColumn{Name="Adapter",HeaderText="Plugin",ReadOnly=true,FillWeight=145});
        pluginTable.Columns.Add(new DataGridViewTextBoxColumn{Name="Status",HeaderText="Status",ReadOnly=true,FillWeight=185});
        pluginTable.Columns.Add(new DataGridViewTextBoxColumn{Name="Executable",HeaderText="CAD-Programm",ReadOnly=true,FillWeight=180});
        foreach(var p in settings.Profiles){int row=pluginTable.Rows.Add(p.Enabled,p.Id,AdapterName(p),PluginStatus(p),string.IsNullOrEmpty(p.Executable)?"Nicht ausgewählt":Path.GetFileName(p.Executable));pluginTable.Rows[row].Tag=p.Id;}
        card.Controls.Add(pluginTable);
        var controls=Card(28,423,953,210);
        pluginDetails=LabelAt(controls,"",22,17,910,10,true);pluginDetails.Height=50;
        ButtonAt(controls,"Hinzufügen …",22,77,165,AddPlugin,true);
        ButtonAt(controls,"Programm wählen",202,77,165,()=>ChoosePluginFile(false));
        ButtonAt(controls,"Adapter wählen",382,77,165,()=>ChoosePluginFile(true));
        ButtonAt(controls,"Einrichten",562,77,165,()=>WithSelectedPlugin(()=>{if(editing.Id=="FreeCAD")InstallFreeCad();else if(editing.Id=="Fusion")MessageBox.Show(this,"Fusion-Add-in 1.0: Das eigenständige Paket liegt im Ordner FusionAdapter. Dort Install-FusionAddin.ps1 für eine neue Installation ausführen und das Add-in in Fusion starten. Bestehende Installationen werden nicht überschrieben.\n\nKeine Adapter-EXE auswählen; der Adapter läuft innerhalb von Fusion.","Fusion-Add-in einrichten");else{EnsureAdapter();MessageBox.Show(this,"Adapter verbindet sich über den lokalen Hub. Die Anmeldung muss den CAD-/Profilnamen aus der Tabelle verwenden.","Plugin einrichten");}}));
        ButtonAt(controls,"Entfernen",742,77,185,RemovePlugin);
        ButtonAt(controls,"Belegung bearbeiten",22,130,220,()=>{profiles.SelectedItem=SelectedPlugin.Id;ShowPage("Belegung");});
        ButtonAt(controls,"Programm starten",257,130,205,()=>WithSelectedPlugin(StartCad));
        ButtonAt(controls,"Profil exportieren",477,130,215,()=>WithSelectedPlugin(ExportProfile));
        ButtonAt(controls,"Profil importieren",707,130,220,()=>WithSelectedPlugin(ImportProfile));
        pluginTable.CurrentCellDirtyStateChanged+=(s,e)=>{if(pluginTable.IsCurrentCellDirty)pluginTable.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        pluginTable.CellValueChanged+=(s,e)=>{if(e.RowIndex<0 || e.ColumnIndex!=0)return;Safe(()=>{
            var row=pluginTable.Rows[e.RowIndex];var p=settings.Profiles.First(x=>x.Id==(string)row.Tag);
            p.Enabled=Convert.ToBoolean(row.Cells[0].Value);Save();EnsureAdapter();RefreshPluginRows();
        });};
        pluginTable.SelectionChanged+=(s,e)=>SelectPluginRow();
        int index=settings.Profiles.FindIndex(p=>p.Id==(selectedPlugin??settings.ActiveProfile));
        if(index<0)index=0;pluginTable.CurrentCell=pluginTable.Rows[index].Cells[1];SelectPluginRow();
    }
    private void SelectPluginRow()
    {
        if(pluginTable==null || pluginTable.CurrentRow==null)return;
        selectedPlugin=Convert.ToString(pluginTable.CurrentRow.Tag);
        var p=SelectedPlugin;
        pluginDetails.Text=p.Id+"  ·  "+PluginStatus(p)+"\n"+(string.IsNullOrEmpty(p.Executable)?"Noch keine Programmdatei ausgewählt":p.Executable);
    }
    private void RefreshPluginRows()
    {
        if(pluginTable==null || pluginTable.IsDisposed)return;
        foreach(DataGridViewRow row in pluginTable.Rows)
        {
            var p=settings.Profiles.FirstOrDefault(x=>x.Id==Convert.ToString(row.Tag));if(p==null)continue;
            string status=PluginStatus(p);if(Convert.ToString(row.Cells["Status"].Value)!=status)row.Cells["Status"].Value=status;
            row.Cells["Adapter"].Value=AdapterName(p);row.Cells["Executable"].Value=string.IsNullOrEmpty(p.Executable)?"Nicht ausgewählt":Path.GetFileName(p.Executable);
        }
        SelectPluginRow();
    }
    private void WithSelectedPlugin(Action action)
    {
        editing=SelectedPlugin;try{action();}finally{editing=settings.Active;}RefreshPluginRows();
    }
    private void RefreshProfilePicker()
    {
        var active=settings.ActiveProfile;
        profiles.Items.Clear();profiles.Items.AddRange(settings.Profiles.Select(p=>(object)p.Id).ToArray());profiles.SelectedItem=active;
    }
    private void ChoosePluginFile(bool adapterFile)
    {
        var p=SelectedPlugin;
        using(var dialog=new OpenFileDialog{Filter="Windows-Programme (*.exe)|*.exe",Title=p.Id+(adapterFile?" · Adapter auswählen":" · CAD-Programm auswählen")})
        if(dialog.ShowDialog(this)==DialogResult.OK)
        {
            if(adapterFile)p.AdapterExecutable=dialog.FileName;else p.Executable=dialog.FileName;
            Save();EnsureAdapter();RefreshPluginRows();
        }
    }
    private void AddPlugin()
    {
        using(var dialog=new Form{Text="CAD-Plugin hinzufügen",ClientSize=new Size(520,230),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,Font=Font,BackColor=Color.White})
        {
            LabelAt(dialog,"CAD-/Plugin-Name",24,22,470,11,true);
            var name=new TextBox{Left=24,Top=62,Width=470};dialog.Controls.Add(name);
            LabelAt(dialog,"Der Adapter muss sich mit genau diesem Namen am Hub anmelden.\nProgramm- und Adapterdatei kannst du anschließend auswählen.",24,106,470,9,false,muted).Height=55;
            ButtonAt(dialog,"Hinzufügen",316,173,178,()=>{
                var p=new CadProfile{Id=name.Text.Trim(),Enabled=false};p.Validate();
                if(settings.Profiles.Any(x=>x.Id.Equals(p.Id,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Dieses CAD-Profil existiert bereits.");
                settings.Profiles.Add(p);selectedPlugin=p.Id;Save();dialog.DialogResult=DialogResult.OK;dialog.Close();
            },true);
            if(dialog.ShowDialog(this)==DialogResult.OK){RefreshProfilePicker();ShowPage("Plugins & Profile");}
        }
    }
    private void RemovePlugin()
    {
        var p=SelectedPlugin;
        if(p.Id=="SolidCraft" || p.Id=="FreeCAD" || p.Id=="Fusion")throw new InvalidOperationException("Die mitgelieferten Adapter können über die Spalte »Aktiv« deaktiviert werden.");
        settings.Profiles.Remove(p);
        if(settings.ActiveProfile==p.Id)settings.ActiveProfile="SolidCraft";
        selectedPlugin=settings.ActiveProfile;Save();EnsureAdapter();RefreshProfilePicker();ShowPage("Plugins & Profile");
    }
    private void StartCad()
    {
        if(!File.Exists(editing.Executable))throw new FileNotFoundException("Bitte unter »Profile« zuerst die Programmdatei auswählen.");
        if(editing.Id=="SolidCraft" && Process.GetProcessesByName("SpaceControlSolidCraftBridge").Length>0)throw new InvalidOperationException("Die bisherige SolidCraft-Bridge läuft noch. Bitte zuerst deren Fenster schließen, damit nur ein Adapter die Kamera bewegt.");
        if(editing.Id=="SolidCraft" && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(editing.Executable)).Length>0){EnsureAdapter();return;}
        Process.Start(new ProcessStartInfo(editing.Executable,editing.Id=="SolidCraft"?"--control 7878":""){UseShellExecute=true});EnsureAdapter();
    }
    private string adapterOwner,adapterFile;
    private void EnsureAdapter()
    {
        var p=settings.Active;
        string path=p.AdapterExecutable;
        if(string.IsNullOrEmpty(path) && p.Id=="SolidCraft")path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SolidCraftHubAdapter.exe");
        if(!p.Enabled || string.IsNullOrEmpty(path)){StopAdapter();return;}
        if(p.Id=="SolidCraft"&&Process.GetProcessesByName("SpaceControlSolidCraftBridge").Length>0){StopAdapter();return;}
        if(adapter!=null && (adapterOwner!=p.Id || adapterFile!=path))StopAdapter();
        if(adapter!=null&&!adapter.HasExited)return;
        if(File.Exists(path)){adapter=Process.Start(new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)});adapterOwner=p.Id;adapterFile=path;}
    }
    private void StopAdapter(){if(adapter!=null){try{if(!adapter.HasExited)adapter.Kill();}catch{}adapter.Dispose();adapter=null;}}
    private void ConfirmCalibration()
    {
        ConfirmCalibration(hub.DeviceConnected,hub.RequestCalibration);
    }
    private void ConfirmCalibration(bool connected,Action request)
    {
        if(hub.Calibration.Info.Busy || calibrationPending || !connected)return;
        if(!calibrationArmed){calibrationArmed=true;RefreshCalibration(true);return;}
        calibrationArmed=false;calibrationPending=true;
        try{request();}catch{calibrationPending=false;throw;}
        RefreshCalibration(true);
    }
    private void ShowSoftwareInfo()
    {
        using(var dialog=CreateSoftwareInfo())dialog.ShowDialog(this);
    }
    private Form CreateSoftwareInfo()
    {
        var dialog=new Form{Text="Softwareinfo · Space Controller Hub",ClientSize=new Size(640,620),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=paper,Icon=Icon,AutoScaleMode=AutoScaleMode.Dpi};
        LabelAt(dialog,"Space Controller Hub",28,22,430,22,true);
        LabelAt(dialog,"Version 1.0.11 · SpaceControl-Controller für CAD",30,72,430,9,false,muted);
        string logo=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","hub-logo.png");
        if(File.Exists(logo))
        {
            Image image;using(var source=Image.FromFile(logo))image=new Bitmap(source);
            dialog.Controls.Add(new PictureBox{Left=468,Top=18,Width=145,Height=145,SizeMode=PictureBoxSizeMode.Zoom,Image=image});
            dialog.Disposed+=(s,e)=>image.Dispose();
        }
        LabelAt(dialog,"ENTWICKLUNG",30,120,420,9,true,muted);
        LabelAt(dialog,"Benjamin Preuß\nEntwickelt mit Unterstützung von Codex.",30,150,420,10).Height=50;
        var email=new LinkLabel{Text="R2kGit@Borgcollective.de",Left=30,Top=205,Width=580,Height=28,LinkColor=blue};dialog.Controls.Add(email);
        email.LinkClicked+=(s,e)=>Safe(()=>Process.Start(new ProcessStartInfo("mailto:R2kGit@Borgcollective.de"){UseShellExecute=true}));
        var help=new LinkLabel{Text="Lokale Anleitung und Funktionsbeschreibung",Left=30,Top=245,Width=580,Height=28,LinkColor=blue};dialog.Controls.Add(help);
        help.LinkClicked+=(s,e)=>Safe(()=>Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"README.md")){UseShellExecute=true}));
        var project=new LinkLabel{Text="GitHub · R2kGit/SpaceCADHub",Left=30,Top=277,Width=580,Height=28,LinkColor=blue};dialog.Controls.Add(project);
        project.LinkClicked+=(s,e)=>Safe(()=>Process.Start(new ProcessStartInfo("https://github.com/R2kGit/SpaceCADHub"){UseShellExecute=true}));
        LabelAt(dialog,"HERSTELLER & RECHTE",30,320,580,9,true,muted);
        LabelAt(dialog,"SpaceController ist eine eingetragene Marke der SpaceControl GmbH & Co. KG.\nHerstellerdokumentation: Copyright © 2009–2018 SpaceControl GmbH & Co. KG.\nUnabhängiges Projekt, kein offizielles Herstellerprodukt.",30,350,580,9,false,muted).Height=72;
        var vendor=new LinkLabel{Text="SpaceControl · spacecontrol.de",Left=30,Top=425,Width=580,Height=28,LinkColor=blue};dialog.Controls.Add(vendor);
        vendor.LinkClicked+=(s,e)=>Safe(()=>Process.Start(new ProcessStartInfo("https://spacecontrol.de/"){UseShellExecute=true}));
        LabelAt(dialog,"Copyright © 2026 Benjamin Preuß · GPL-3.0-only\nFreie Software: Weitergabe und Änderung gemäß GPLv3.\nOhne Gewährleistung, soweit gesetzlich zulässig.",30,465,580,9,false,muted).Height=60;
        var license=new LinkLabel{Text="GPLv3-Lizenztext und Hinweise zu Drittkomponenten",Left=30,Top=530,Width=580,Height=28,LinkColor=blue};dialog.Controls.Add(license);
        license.LinkClicked+=(s,e)=>Safe(()=>{
            using(var legal=new Form{Text="Lizenz · GPLv3",ClientSize=new Size(760,600),StartPosition=FormStartPosition.CenterParent,Font=Font,Icon=Icon})
            {
                string root=AppDomain.CurrentDomain.BaseDirectory;
                legal.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",9),Text=File.ReadAllText(Path.Combine(root,"NOTICE.md"))+"\r\n\r\n"+File.ReadAllText(Path.Combine(root,"LICENSE"))});
                legal.ShowDialog(dialog);
            }
        });
        var close=ButtonAt(dialog,"Schließen",438,565,172,()=>dialog.Close(),true);dialog.AcceptButton=close;dialog.CancelButton=close;
        return dialog;
    }
    private void RefreshStatus()
    {
        deviceCard.Connected=hub.DeviceConnected;
        deviceCard.Detail=hub.DeviceConnected?"SpaceControl\nBereit für Eingaben":hub.LastStatus;deviceCard.Invalidate();
        RefreshLiveValues();
        foreach(var pair in pluginStates)pair.Value.Text=PluginStatus(settings.Profiles.First(p=>p.Id==pair.Key));
        RefreshPluginRows();
        EnsureAdapter();
    }
    private long nextStatusTick;
    private void RefreshLiveValues()
    {
        if(displayPreview!=null&&!displayEdited)displayPreview.Text=string.Join(Environment.NewLine,hub.DisplayPreview);
        var calibration=hub.Calibration.Info;
        profiles.Enabled=!calibration.Busy;
        RefreshCalibration(hub.DeviceConnected);
        var values=hub.LiveAxes;
        if(map!=null){map.Values=values;map.Pressed=hub.Feedback.Mask;map.Invalidate();}
        if(monitor!=null){monitor.Values=values;monitor.Invalidate();}
        if(motionValues!=null){motionValues.Values=values;motionValues.Invalidate();}
        for(int i=0;i<axes.Count;i++)axes[i].Value=(int)Math.Min(100,Math.Abs(values[i])/5);
    }
    private void RefreshCalibration(bool connected)
    {
        var calibration=hub.Calibration.Info;
        if(!connected){calibrationArmed=false;calibrationPending=false;}
        if(calibration.Busy)calibrationPending=false;
        if(calibrationButton!=null)
        {
            calibrationButton.Text="Nullstellung kalibrieren";
            calibrationButton.Enabled=!calibration.Busy && !calibrationPending && connected;
            calibrationStatus.Text=calibration.Busy?calibration.Text:calibrationPending?"Kappe vollständig loslassen.\nKalibrierung startet …":calibrationArmed?"Kappe vollständig loslassen.\nZum Start erneut bestätigen.\nDanach 3 Sekunden warten.":calibration.Success||calibration.Failed?calibration.Text:!connected?"Kein Controller verbunden.":"";
            calibrationStatus.ForeColor=calibrationArmed||calibrationPending?muted:calibration.Success?Color.SeaGreen:calibration.Failed?Color.Firebrick:muted;
        }
    }
    private void RefreshLive()
    {
        RefreshLiveValues();
        long now=Stopwatch.GetTimestamp();
        // Slow process/status checks must not hold up the live animation.
        if(now>=nextStatusTick){nextStatusTick=now+Stopwatch.Frequency/2;RefreshStatus();}
    }
    private string PluginStatus(CadProfile p)
    {
        return PluginCatalog.Status(p,settings.ActiveProfile==p.Id,hub.IsConnected(p.Id),
            p.Id=="SolidCraft"&&Process.GetProcessesByName("SpaceControlSolidCraftBridge").Length>0);
    }
    private void ExportProfile()
    {
        using(var d=new SaveFileDialog{Filter="CAD-Hub-Profil (*.json)|*.json",FileName=editing.Id+"-Profil.json"})if(d.ShowDialog(this)==DialogResult.OK)File.WriteAllText(d.FileName,new JavaScriptSerializer().Serialize(editing));
    }
    private void ImportProfile()
    {
        using(var d=new OpenFileDialog{Filter="CAD-Hub-Profil (*.json)|*.json"})if(d.ShowDialog(this)==DialogResult.OK)
        {
            var p=new JavaScriptSerializer().Deserialize<CadProfile>(File.ReadAllText(d.FileName));if(p==null)throw new InvalidDataException("Leeres Profil.");p.Validate();
            if(p.Id!=editing.Id)throw new InvalidDataException("Dieses Profil gehört zu "+p.Id+". Bitte zuerst das passende CAD-Profil auswählen.");
            settings.Profiles[settings.Profiles.IndexOf(editing)]=p;editing=p;Save();EnsureAdapter();ShowPage(currentPage);
        }
    }
    private void InstallFreeCad()
    {
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"FreeCAD"),source=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"FreeCADAddon");
        var roots=new List<string>{root};if(Directory.Exists(root))roots.AddRange(Directory.GetDirectories(root,"v*"));
        foreach(string r in roots){string target=Path.Combine(r,"Mod","SpaceControllerCADHub");Directory.CreateDirectory(target);foreach(string f in Directory.GetFiles(source,"*.py"))File.Copy(f,Path.Combine(target,Path.GetFileName(f)),true);}
        MessageBox.Show(this,"Add-on installiert. Bitte FreeCAD neu starten.","FreeCAD");
    }
}
internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            bool first;using(var mutex=new Mutex(true,"Local\\SpaceControllerCadHub",out first))
            {
                if(!first){MessageBox.Show("Es läuft bereits ein CAD Hub. Bitte die bisherige Version über das Tray beenden.","CAD Hub");return;}
                Application.Run(new MainForm());
            }
        }
        catch(Exception e){MessageBox.Show(e.Message,"CAD Hub konnte nicht starten",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
