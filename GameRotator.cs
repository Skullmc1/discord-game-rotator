// Game Rotator - pretends to run games so Discord's game detection picks them up,
// switching to the next game in the list every few seconds.
//
// How it works: Discord publishes its list of detectable games at
// /api/v9/applications/detectable. Each game has one or more executable names
// (e.g. "cs2.exe" or "fortnite/fortniteclient-win64-shipping.exe"). Discord scans
// running processes and matches on the end of the executable path. This app copies
// itself into games\<appid>\<exe path> and launches that copy in "--stub" mode,
// which just shows a tiny window until it is told to close.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Game
{
    public string Id;
    public string Name;
    public string Exe;
    public override string ToString() { return Name + "   (" + Exe + ")"; }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        if (args.Length >= 3 && args[0] == "--stub")
        {
            Application.Run(new StubForm(args[1], int.Parse(args[2])));
            return;
        }
        Application.Run(new MainForm());
    }
}

// The fake game process. Closes itself if the rotator that launched it goes away.
class StubForm : Form
{
    public StubForm(string title, int parentPid)
    {
        Text = title;
        Size = new Size(320, 110);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(20, 20);
        ShowInTaskbar = true;
        Controls.Add(new Label { Text = "Pretending to be: " + title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });

        var watchdog = new Timer { Interval = 1000 };
        watchdog.Tick += delegate
        {
            try { if (Process.GetProcessById(parentPid).HasExited) Close(); }
            catch (ArgumentException) { Close(); }
        };
        watchdog.Start();
    }
}

class MainForm : Form
{
    const string DetectableUrl = "https://discord.com/api/v9/applications/detectable";

    readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    string CachePath { get { return Path.Combine(baseDir, "detectable.json"); } }
    string RotationPath { get { return Path.Combine(baseDir, "rotation.txt"); } }
    string GamesDir { get { return Path.Combine(baseDir, "games"); } }

    List<Game> allGames = new List<Game>();

    TextBox search = new TextBox();
    ListBox results = new ListBox();
    ListBox rotation = new ListBox();
    NumericUpDown interval = new NumericUpDown();
    Button startStop = new Button();
    Label status = new Label();
    Timer timer = new Timer();

    Random rng = new Random();
    Process current;
    int nextIndex;

    public MainForm()
    {
        Text = "Discord Game Rotator";
        Size = new Size(900, 560);
        MinimumSize = new Size(700, 400);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: search + refresh / rotation header
        var searchRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        searchRow.Controls.Add(new Label { Text = "Search games:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        search.Width = 220;
        searchRow.Controls.Add(search);
        var refresh = new Button { Text = "Refresh list", AutoSize = true };
        searchRow.Controls.Add(refresh);
        root.Controls.Add(searchRow, 0, 0);
        root.Controls.Add(new Label { Text = "Rotation (top to bottom, loops):", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 2, 0);

        // Row 1: lists + add/remove buttons
        results.Dock = DockStyle.Fill;
        results.HorizontalScrollbar = true;
        rotation.Dock = DockStyle.Fill;
        rotation.HorizontalScrollbar = true;
        root.Controls.Add(results, 0, 1);
        root.Controls.Add(rotation, 2, 1);

        var mid = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Anchor = AnchorStyles.None };
        var add = new Button { Text = "Add  >", Width = 90 };
        var remove = new Button { Text = "<  Remove", Width = 90 };
        var up = new Button { Text = "Move up", Width = 90 };
        var down = new Button { Text = "Move down", Width = 90 };
        var random = new Button { Text = "Add 100 random", Width = 90, Height = 40, Margin = new Padding(3, 15, 3, 3) };
        var clear = new Button { Text = "Clear rotation", Width = 90 };
        mid.Controls.AddRange(new Control[] { add, remove, up, down, random, clear });

        // Click and drag (or Shift/Ctrl+click) to select several games at once
        results.SelectionMode = SelectionMode.MultiExtended;
        rotation.SelectionMode = SelectionMode.MultiExtended;
        root.Controls.Add(mid, 1, 1);

        // Row 2: interval, start/stop, status
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        bottom.Controls.Add(new Label { Text = "Swap every (seconds):", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        interval.Minimum = 1;
        interval.Maximum = 3600;
        interval.Value = 5;
        interval.Width = 70;
        bottom.Controls.Add(interval);
        startStop.Text = "Start";
        startStop.Width = 100;
        bottom.Controls.Add(startStop);
        status.AutoSize = true;
        status.Margin = new Padding(8, 8, 0, 0);
        bottom.Controls.Add(status);
        root.Controls.Add(bottom, 0, 2);
        root.SetColumnSpan(bottom, 3);

        Controls.Add(root);

        search.TextChanged += delegate { Filter(); };
        refresh.Click += delegate { LoadGames(true); };
        add.Click += delegate { AddSelected(); };
        results.DoubleClick += delegate { AddSelected(); };
        remove.Click += delegate { RemoveSelected(); };
        rotation.DoubleClick += delegate { RemoveSelected(); };
        up.Click += delegate { MoveItem(-1); };
        down.Click += delegate { MoveItem(1); };
        random.Click += delegate { AddRandom(100); };
        clear.Click += delegate { rotation.Items.Clear(); SaveRotation(); };
        startStop.Click += delegate { if (timer.Enabled) StopRotation(); else StartRotation(); };
        timer.Tick += delegate { LaunchNext(); };
        interval.ValueChanged += delegate { timer.Interval = (int)interval.Value * 1000; };
        FormClosing += delegate { StopRotation(); SaveRotation(); };
        Shown += delegate { LoadGames(false); };
    }

    void SetStatus(string text) { status.Text = text; status.Refresh(); }

    void LoadGames(bool forceDownload)
    {
        try
        {
            if (forceDownload || !File.Exists(CachePath))
            {
                SetStatus("Downloading Discord's detectable games list...");
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "GameRotator/1.0";
                    wc.Encoding = Encoding.UTF8;
                    File.WriteAllText(CachePath, wc.DownloadString(DetectableUrl), Encoding.UTF8);
                }
            }
            allGames = ParseGames(File.ReadAllText(CachePath, Encoding.UTF8));
            SetStatus("Loaded " + allGames.Count + " detectable Windows games.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not load games list: " + ex.Message);
        }
        Filter();
        LoadRotation();
    }

    static List<Game> ParseGames(string json)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var apps = (object[])serializer.DeserializeObject(json);
        var games = new List<Game>();
        foreach (Dictionary<string, object> app in apps)
        {
            object exes;
            if (!app.TryGetValue("executables", out exes) || exes == null) continue;
            foreach (Dictionary<string, object> exe in (object[])exes)
            {
                object os, launcher, name;
                exe.TryGetValue("os", out os);
                exe.TryGetValue("is_launcher", out launcher);
                exe.TryGetValue("name", out name);
                if ((os as string) != "win32" || (launcher is bool && (bool)launcher) || name == null) continue;
                string exeName = ((string)name).Replace('\\', '/').TrimStart('/', '>');
                if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                games.Add(new Game { Id = app["id"].ToString(), Name = app["name"].ToString(), Exe = exeName });
                break; // one executable per game is enough
            }
        }
        return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    IEnumerable<Game> SearchMatches()
    {
        string q = search.Text.Trim();
        return allGames.Where(g => q.Length == 0 || g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    void Filter()
    {
        var matches = SearchMatches().Take(1000).ToArray();
        results.BeginUpdate();
        results.Items.Clear();
        results.Items.AddRange(matches);
        results.EndUpdate();
    }

    void AddGames(IEnumerable<Game> games)
    {
        var have = new HashSet<string>(rotation.Items.Cast<Game>().Select(r => r.Id));
        rotation.BeginUpdate();
        foreach (var g in games)
            if (have.Add(g.Id)) rotation.Items.Add(g);
        rotation.EndUpdate();
        SaveRotation();
    }

    void AddSelected()
    {
        AddGames(results.SelectedItems.Cast<Game>().ToList());
    }

    // Picks from whatever matches the search box (or every game if it's empty).
    void AddRandom(int count)
    {
        var have = new HashSet<string>(rotation.Items.Cast<Game>().Select(r => r.Id));
        var pool = SearchMatches().Where(g => !have.Contains(g.Id)).ToList();
        var picked = pool.OrderBy(g => rng.Next()).Take(count).ToList();
        AddGames(picked);
        SetStatus("Added " + picked.Count + " random games (" + rotation.Items.Count + " in rotation).");
    }

    void RemoveSelected()
    {
        var indices = rotation.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        rotation.BeginUpdate();
        foreach (int i in indices) rotation.Items.RemoveAt(i);
        rotation.EndUpdate();
        SaveRotation();
    }

    void MoveItem(int delta)
    {
        int i = rotation.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= rotation.Items.Count) return;
        var item = rotation.Items[i];
        rotation.Items.RemoveAt(i);
        rotation.Items.Insert(j, item);
        rotation.ClearSelected();
        rotation.SelectedIndex = j;
        SaveRotation();
    }

    void SaveRotation()
    {
        try { File.WriteAllLines(RotationPath, rotation.Items.Cast<Game>().Select(g => g.Id)); } catch { }
    }

    void LoadRotation()
    {
        if (rotation.Items.Count > 0 || !File.Exists(RotationPath)) return;
        var byId = new Dictionary<string, Game>();
        foreach (var g in allGames) byId[g.Id] = g;
        foreach (var id in File.ReadAllLines(RotationPath))
        {
            Game g;
            if (byId.TryGetValue(id.Trim(), out g)) rotation.Items.Add(g);
        }
    }

    void StartRotation()
    {
        if (rotation.Items.Count == 0)
        {
            MessageBox.Show(this, "Add at least one game to the rotation first.", Text);
            return;
        }
        nextIndex = 0;
        timer.Interval = (int)interval.Value * 1000;
        LaunchNext();
        timer.Start();
        startStop.Text = "Stop";
    }

    void StopRotation()
    {
        timer.Stop();
        KillCurrent();
        startStop.Text = "Start";
        SetStatus("Stopped.");
    }

    void KillCurrent()
    {
        if (current == null) return;
        try { if (!current.HasExited) { current.Kill(); current.WaitForExit(3000); } } catch { }
        current.Dispose();
        current = null;
    }

    void LaunchNext()
    {
        if (rotation.Items.Count == 0) { StopRotation(); return; }
        if (nextIndex >= rotation.Items.Count) nextIndex = 0;
        var game = (Game)rotation.Items[nextIndex];
        rotation.ClearSelected();
        rotation.SelectedIndex = nextIndex;
        nextIndex++;

        KillCurrent();
        try
        {
            string target = Path.Combine(GamesDir, game.Id, game.Exe.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var self = new FileInfo(Application.ExecutablePath);
            var existing = new FileInfo(target);
            if (!existing.Exists || existing.Length != self.Length || existing.LastWriteTimeUtc < self.LastWriteTimeUtc)
                File.Copy(self.FullName, target, true);

            var psi = new ProcessStartInfo(target)
            {
                Arguments = "--stub \"" + game.Name.Replace("\"", "'") + "\" " + Process.GetCurrentProcess().Id,
                WorkingDirectory = Path.GetDirectoryName(target),
                UseShellExecute = false,
            };
            current = Process.Start(psi);
            SetStatus("Now playing: " + game.Name + "  [" + game.Exe + "]");
        }
        catch (Exception ex)
        {
            SetStatus("Failed to launch " + game.Name + ": " + ex.Message);
        }
    }
}
