#pragma warning disable CA1416

using System.Text;

namespace TBH_Trainer;

internal sealed class MainForm : Form
{
    private readonly GameMemory _mem = new();
    private AntiCheatPatcher? _patcher;
    private readonly TrainerBridge _bridge = new();
    private int _pid = -1;
    private bool _injected;

    // Controls
    private Label _lblStatus = null!;
    private Button _btnAttach = null!;
    private Button _btnBypass = null!;
    private Label _lblBypassStatus = null!;

    private CheckBox _chkSpeed = null!;
    private Panel _pnlSpeed = null!;
    private TrackBar _trkSpeed = null!;
    private Label _lblSpeedValue = null!;
    private TextBox _txtSpeed = null!;
    private Label _lblSpeedStatus = null!;
    private bool _suppressSlider;

    private Button _btnScanHeroes = null!;
    private HeroScanBridge? _heroScanBridge;
    private TextBox _txtHeroReport = null!;
    private ComboBox _cmbHeroIndex = null!;
    private ComboBox _cmbHeroStat = null!;
    private TextBox _txtHeroValue = null!;
    private Button _btnWriteHeroStat = null!;

    private HighExpPatcher? _exp;
    private TextBox _txtExp = null!;
    private Button _btnApplyExp = null!;
    private Button _btnDisableExp = null!;
    private Label _lblExpStatus = null!;

    private TextBox _txtItemKey = null!;
    private ComboBox _cmbGrade = null!;
    private TextBox _txtSpawnCount = null!;
    private Button _btnSpawnItem = null!;
    private Button _btnScanItems = null!;
    private Button _btnExportCatalog = null!;
    private Label _lblSpawnStatus = null!;
    private ItemScanBridge? _scanBridge;
    private DateTime _lastSpawnClick = DateTime.MinValue;

    private TextBox _txtLog = null!;
    private Panel _header = null!;
    private TabControl _tabs = null!;

    // Static-patch tab state
    private StaticPatcher? _static;
    private GameBuildProfile _buildProfile = GameBuildProfile.V1008;
    private readonly List<StaticPatcher.Feature> _features = CloneStaticFeatures(GameBuildProfile.V1008);
    private readonly Dictionary<StaticPatcher.Feature, (Label name, Label status, Button toggle, Label desc)> _featRows = new();
    private TextBox _txtDllPath = null!;

    // === DENOISER theme palette ===
    private static readonly Color BgForm     = Color.FromArgb(16, 18, 24);
    private static readonly Color BgInput    = Color.FromArgb(34, 37, 46);
    private static readonly Color BtnBg      = Color.FromArgb(30, 33, 42);
    private static readonly Color AccentCyan = Color.FromArgb(60, 220, 225);
    private static readonly Color AccentPurp = Color.FromArgb(155, 110, 255);
    private static readonly Color AccentDim  = Color.FromArgb(40, 70, 80);
    private static readonly Color TextMain   = Color.FromArgb(224, 228, 236);
    private static readonly Color TextDim    = Color.FromArgb(135, 140, 156);

    private static readonly Bitmap? Logo = LoadEmbedded<Bitmap>("logo.png");

    private static T? LoadEmbedded<T>(string endsWith) where T : class
    {
        var asm = typeof(MainForm).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return null;
        if (typeof(T) == typeof(Bitmap)) return new Bitmap(s) as T;
        if (typeof(T) == typeof(Icon)) return new Icon(s) as T;
        return null;
    }

    private static string DllPath => Path.Combine(AppContext.BaseDirectory, "TBHHook.dll");

    public MainForm() => InitializeComponent();

    private void InitializeComponent()
    {
        Text = "TBH Trainer v1.3.1";
        Size = new Size(584, 920);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgForm;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = LoadEmbedded<Icon>("icon.ico"); } catch { }

        _header = new Panel { Location = new Point(0, 0), Size = new Size(584, 76), BackColor = BgForm };
        _header.Paint += PaintHeader;
        Controls.Add(_header);

        // === Tabs ===
        _tabs = new TabControl
        {
            Location = new Point(8, 84), Size = new Size(560, 618),
            DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(178, 30), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _tabs.DrawItem += OnDrawTab;
        var tabRuntime = new TabPage("ACTk bypass")   { BackColor = BgForm, ForeColor = TextMain };
        var tabStatic  = new TabPage("Extra Features") { BackColor = BgForm, ForeColor = TextMain };
        var tabHeroes  = new TabPage("Hero Stats") { BackColor = BgForm, ForeColor = TextMain };
        _tabs.TabPages.Add(tabRuntime);
        _tabs.TabPages.Add(tabStatic);
        _tabs.TabPages.Add(tabHeroes);
        Controls.Add(_tabs);

        // Log must exist before tab builders — BuildStaticTab may call Log() during init.
        var grpLog = new GroupBox
        {
            Text = "Log", Location = new Point(8, 710), Size = new Size(560, 150),
            ForeColor = AccentCyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = BgForm
        };
        _txtLog = new TextBox
        {
            Location = new Point(12, 20), Size = new Size(540, 118),
            Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
            BackColor = Color.FromArgb(12, 13, 18), ForeColor = Color.FromArgb(120, 220, 200),
            Font = new Font("Consolas", 8.5f), BorderStyle = BorderStyle.FixedSingle
        };
        grpLog.Controls.Add(_txtLog);
        Controls.Add(grpLog);

        BuildRuntimeTab(tabRuntime);
        BuildStaticTab(tabStatic);
        BuildHeroStatsTab(tabHeroes);

        Log("Trainer ready.");
        Log("EXTRA FEATURES tab: auto-detects 1.00.08 / 1.00.09 from GameAssembly.dll. CLOSE the game before applying.");
        Log("ACTK BYPASS tab: offsets per build — see MIGRATE_1.00.09.md if a hotfix broke features.");
    }

    private void BuildRuntimeTab(TabPage page)
    {
        int y = 8;

        // === 1. Connect ===
        var grp1 = MakeGroup(page, "1. Connect to Game", ref y, 55);
        _btnAttach = MakeBtn("Connect", 15, 22, 120);
        _btnAttach.Click += OnAttach;
        _lblStatus = new Label { Text = "Disconnected", ForeColor = Color.FromArgb(255, 100, 100), Location = new Point(145, 27), AutoSize = true };
        grp1.Controls.AddRange(new Control[] { _btnAttach, _lblStatus });

        // === 2. Anti-Cheat Bypass ===
        var grp2 = MakeGroup(page, "2. Anti-Cheat Bypass (ACTk)", ref y, 55);
        _btnBypass = MakeBtn("Disable ACTk", 15, 22, 150);
        _btnBypass.Click += OnBypass;
        _btnBypass.Enabled = false;
        _lblBypassStatus = new Label { Text = "ACTk active", ForeColor = Color.FromArgb(255, 200, 50), Location = new Point(175, 27), AutoSize = true };
        grp2.Controls.AddRange(new Control[] { _btnBypass, _lblBypassStatus });

        // === 3. Game Speed (Speedhack) ===
        var grp3 = MakeGroup(page, "3. Game Speed (Speedhack)", ref y, 140);
        _chkSpeed = new CheckBox
        {
            Text = "Enable Speedhack", Location = new Point(15, 24), AutoSize = true,
            ForeColor = TextMain
        };
        _chkSpeed.CheckedChanged += OnSpeedToggle;
        grp3.Controls.Add(_chkSpeed);

        // All speed controls live in a panel that is hidden until Speedhack is enabled.
        _pnlSpeed = new Panel
        {
            Location = new Point(3, 48), Size = new Size(536, 88),
            BackColor = BgForm, Visible = false
        };

        _trkSpeed = new TrackBar
        {
            Location = new Point(6, 0), Size = new Size(522, 45),
            Minimum = 0, Maximum = 500, Value = 1, TickFrequency = 50
        };
        _trkSpeed.ValueChanged += OnSpeedSlider;

        _pnlSpeed.Controls.Add(new Label { Text = "Speed:", Location = new Point(12, 55), AutoSize = true });
        _txtSpeed = MakeTxt(67, 52, 80);
        _txtSpeed.Text = "1.0";
        _txtSpeed.KeyDown += OnSpeedKeyDown;
        _txtSpeed.Leave += (_, _) => CommitTypedSpeed();
        _lblSpeedValue = new Label { Text = "Speed  1.00", Location = new Point(160, 55), AutoSize = true, ForeColor = AccentCyan, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        _lblSpeedStatus = new Label { Text = "(0x - 500x  -  type and press Enter, or drag)", Location = new Point(265, 56), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Segoe UI", 8.5f) };

        _pnlSpeed.Controls.AddRange(new Control[] { _trkSpeed, _txtSpeed, _lblSpeedValue, _lblSpeedStatus });
        grp3.Controls.Add(_pnlSpeed);

        // === 4. Hero EXP on Kill (BabyGroot AOB script) ===
        var grp5 = MakeGroup(page, "4. Hero EXP on Kill", ref y, 90);
        grp5.Controls.Add(new Label
        {
            Text = "Each monster kill awards this much EXP. AOB-patches GameAssembly.dll in memory; safe to enable mid-run.",
            Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(150, 150, 150),
            Location = new Point(15, 20), AutoSize = true, MaximumSize = new Size(515, 0)
        });
        grp5.Controls.Add(new Label { Text = "EXP per kill:", Location = new Point(15, 56), AutoSize = true });
        _txtExp = MakeTxt(100, 53, 120); _txtExp.Text = "999999999"; _txtExp.Enabled = false;
        _btnApplyExp = MakeBtn("Enable", 230, 52, 95); _btnApplyExp.Enabled = false;
        _btnApplyExp.Click += OnApplyExp;
        _btnDisableExp = MakeBtn("Disable", 333, 52, 95); _btnDisableExp.Enabled = false;
        _btnDisableExp.Click += OnDisableExp;
        _lblExpStatus = new Label { Text = "OFF", Location = new Point(440, 58), AutoSize = true, ForeColor = TextDim, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        grp5.Controls.AddRange(new Control[] { _txtExp, _btnApplyExp, _btnDisableExp, _lblExpStatus });

        // === 5. Stash Item Spawn (TBHHook + il2cpp) ===
        var grp6 = MakeGroup(page, "5. Stash Item Spawn", ref y, 154);
        grp6.Controls.Add(new Label
        {
            Text = "Each ItemKey has a fixed rarity in the catalog. Grade picks another row (e.g. 304061 Immortal → Cosmic uses 309161). Export catalog to browse IDs.",
            Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(150, 150, 150),
            Location = new Point(15, 20), AutoSize = true, MaximumSize = new Size(515, 0)
        });
        grp6.Controls.Add(new Label { Text = "ItemKey:", Location = new Point(15, 62), AutoSize = true });
        _txtItemKey = MakeTxt(75, 59, 100);
        _txtItemKey.Text = "1001";
        _txtItemKey.Enabled = false;

        grp6.Controls.Add(new Label { Text = "Grade:", Location = new Point(190, 62), AutoSize = true });
        _cmbGrade = new ComboBox
        {
            Location = new Point(235, 58), Width = 145, DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgInput, ForeColor = AccentCyan, FlatStyle = FlatStyle.Flat, Enabled = false
        };
        foreach (TrainerBridge.GradeType g in Enum.GetValues<TrainerBridge.GradeType>())
            _cmbGrade.Items.Add(TrainerBridge.GradeLabel(g));
        _cmbGrade.SelectedIndex = (int)TrainerBridge.GradeType.Legendary;

        grp6.Controls.Add(new Label { Text = "Count:", Location = new Point(395, 62), AutoSize = true });
        _txtSpawnCount = MakeTxt(443, 59, 55);
        _txtSpawnCount.Text = "1";
        _txtSpawnCount.Enabled = false;

        _btnSpawnItem = MakeBtn("Spawn", 15, 94, 105);
        _btnSpawnItem.Enabled = false;
        _btnSpawnItem.Click += OnSpawnItem;

        _btnScanItems = MakeBtn("Scan items", 130, 94, 105);
        _btnScanItems.Enabled = false;
        _btnScanItems.Click += OnScanItems;

        _btnExportCatalog = MakeBtn("Export catalog", 245, 94, 120);
        _btnExportCatalog.Enabled = false;
        _btnExportCatalog.Click += OnExportCatalog;

        _lblSpawnStatus = new Label
        {
            Text = "Hook required", Location = new Point(378, 101), AutoSize = true,
            MaximumSize = new Size(150, 34),
            ForeColor = TextDim, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        grp6.Controls.AddRange(new Control[] { _txtItemKey, _cmbGrade, _txtSpawnCount, _btnSpawnItem, _btnScanItems, _btnExportCatalog, _lblSpawnStatus });
    }

    private void BuildStaticTab(TabPage page)
    {
        int y = 10;
        page.Controls.Add(new Label
        {
            Text = "Edits GameAssembly.dll on disk — CLOSE THE GAME before applying.\n" +
                   "Each patch is byte-verified; a wrong version is refused, not corrupted.\n" +
                   "First apply makes a backup (.denoiser.bak) so you can Restore originals.",
            Location = new Point(10, y), AutoSize = true, ForeColor = TextDim,
            Font = new Font("Segoe UI", 8.5f)
        });
        y += 56;

        page.Controls.Add(new Label { Text = "DLL:", Location = new Point(10, y + 4), AutoSize = true, ForeColor = TextMain });
        _txtDllPath = new TextBox
        {
            Location = new Point(48, y), Width = 386, ReadOnly = true,
            BackColor = BgInput, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f)
        };
        page.Controls.Add(_txtDllPath);
        var btnBrowse = MakeBtn("Browse", 440, y - 2, 100);
        btnBrowse.Click += OnBrowseDll;
        page.Controls.Add(btnBrowse);
        y += 38;

        foreach (var f in _features)
        {
            var name = new Label
            {
                Text = f.Name, Location = new Point(12, y), AutoSize = true,
                ForeColor = TextMain, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            var status = new Label
            {
                Text = "—", Location = new Point(220, y), AutoSize = true,
                ForeColor = TextDim, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            var toggle = MakeBtn("Apply", 436, y - 4, 104);
            toggle.Enabled = false;
            var captured = f;
            toggle.Click += (_, _) => OnToggleFeature(captured);
            var desc = new Label
            {
                Text = f.Description, Location = new Point(12, y + 19), AutoSize = true,
                ForeColor = Color.FromArgb(120, 125, 142), Font = new Font("Segoe UI", 8f)
            };
            page.Controls.AddRange(new Control[] { name, status, toggle, desc });
            _featRows[f] = (name, status, toggle, desc);
            y += 46;
        }

        y += 8;
        var btnRefresh   = MakeBtn("Refresh Status", 12, y, 132);
        btnRefresh.Click += (_, _) => RefreshStatic(log: true);
        var btnApplyAll  = MakeBtn("Apply All", 152, y, 120);
        btnApplyAll.Click += (_, _) => { foreach (var f in _features) if (f.State == StaticPatcher.FeatureState.Original) OnToggleFeature(f); };
        var btnRevertAll = MakeBtn("Revert All", 280, y, 120);
        btnRevertAll.Click += (_, _) => { foreach (var f in _features) if (f.State == StaticPatcher.FeatureState.Patched) OnToggleFeature(f); };
        var btnRestore   = MakeBtn("Restore Backup", 408, y, 132);
        btnRestore.Click += OnRestoreBackup;
        page.Controls.AddRange(new Control[] { btnRefresh, btnApplyAll, btnRevertAll, btnRestore });

        var auto = StaticPatcher.AutoDetectDll();
        if (auto != null)
        {
            _static = new StaticPatcher(auto);
            _txtDllPath.Text = auto;
            var diskBuild = GameBuildProfile.DetectFromDisk(auto);
            if (diskBuild != null)
            {
                ApplyStaticProfile(diskBuild);
                Log($"Extra Features: detected GameAssembly for {diskBuild.VersionLabel}.");
            }
            else
                Log("Extra Features: could not match 1.00.08/1.00.09 on disk — fill GameBuildProfile.V1009.");
            RefreshStatic(log: false);
        }
        else
        {
            _txtDllPath.Text = "(not found — click Browse to locate GameAssembly.dll)";
        }
    }

    private void BuildHeroStatsTab(TabPage page)
    {
        page.Controls.Add(new Label
        {
            Text = "Enter a run with active heroes, connect to the game, then scan or apply a persistent lock.",
            Location = new Point(14, 14), AutoSize = true, ForeColor = TextDim,
            MaximumSize = new Size(515, 0)
        });

        _btnScanHeroes = MakeBtn("Scan active heroes", 14, 45, 160);
        _btnScanHeroes.Enabled = false;
        _btnScanHeroes.Click += OnScanHeroes;
        page.Controls.Add(_btnScanHeroes);

        _cmbHeroIndex = new ComboBox
        {
            Location = new Point(185, 48), Size = new Size(90, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = BgInput, ForeColor = TextMain
        };
        _cmbHeroIndex.Items.AddRange(["Hero 1", "Hero 2", "Hero 3"]);
        _cmbHeroIndex.SelectedIndex = 0;

        _cmbHeroStat = new ComboBox
        {
            Location = new Point(285, 48), Size = new Size(120, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, BackColor = BgInput, ForeColor = TextMain
        };
        _cmbHeroStat.Items.AddRange(["HP Lock", "Attack Speed Lock", "Clear Hero Locks"]);
        _cmbHeroStat.SelectedIndex = 0;

        _txtHeroValue = MakeTxt(415, 48, 70);
        _txtHeroValue.Text = "9999";
        _btnWriteHeroStat = MakeBtn("Apply", 490, 45, 48);
        _btnWriteHeroStat.Enabled = false;
        _btnWriteHeroStat.Click += OnWriteHeroStat;
        page.Controls.AddRange(new Control[] { _cmbHeroIndex, _cmbHeroStat, _txtHeroValue, _btnWriteHeroStat });

        _txtHeroReport = new TextBox
        {
            Location = new Point(14, 82), Size = new Size(514, 490),
            Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, ReadOnly = true,
            BackColor = Color.FromArgb(12, 13, 18), ForeColor = Color.FromArgb(120, 220, 200),
            Font = new Font("Consolas", 8.5f), BorderStyle = BorderStyle.FixedSingle
        };
        page.Controls.Add(_txtHeroReport);
    }

    private void OnDrawTab(object? sender, DrawItemEventArgs e)
    {
        var page = _tabs.TabPages[e.Index];
        bool selected = e.Index == _tabs.SelectedIndex;
        using (var b = new SolidBrush(selected ? AccentDim : BtnBg))
            e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics, page.Text, _tabs.Font, e.Bounds,
            selected ? AccentCyan : TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private GroupBox MakeGroup(Control parent, string text, ref int y, int height)
    {
        var g = new GroupBox
        {
            Text = text, Location = new Point(4, y), Size = new Size(544, height),
            ForeColor = AccentCyan, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            BackColor = BgForm
        };
        parent.Controls.Add(g);
        y += height + 6;
        return g;
    }

    private Button MakeBtn(string text, int x, int y, int w)
    {
        var b = new ThemedButton
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, 30),
            FlatStyle = FlatStyle.Flat, BackColor = BtnBg,
            ForeColor = TextMain, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = AccentCyan;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = AccentDim;
        b.FlatAppearance.MouseDownBackColor = AccentCyan;
        return b;
    }

    private TextBox MakeTxt(int x, int y, int w) => new()
    {
        Location = new Point(x, y), Width = w,
        BackColor = BgInput, ForeColor = AccentCyan,
        BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10, FontStyle.Bold)
    };

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var rect = _header.ClientRectangle;

        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
            rect, Color.FromArgb(18, 20, 30), Color.FromArgb(36, 26, 54), 8f))
            g.FillRectangle(bg, rect);

        int tx = 14;
        if (Logo != null)
        {
            const int sz = 58;
            g.DrawImage(Logo, new Rectangle(12, 9, sz, sz));
            tx = 12 + sz + 10;
        }

        using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
        var titleRect = new Rectangle(tx, 4, 360, 44);
        using (var tb = new System.Drawing.Drawing2D.LinearGradientBrush(
            titleRect, AccentCyan, AccentPurp, 0f))
            g.DrawString("DENOISER", titleFont, tb, tx, 6);

        float dw = g.MeasureString("DENOISER", titleFont).Width;
        using var subFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using (var db = new SolidBrush(TextDim))
            g.DrawString("TRAINER", subFont, db, tx + dw - 10, 20);

        using var tagFont = new Font("Segoe UI", 8.5f);
        using (var tg = new SolidBrush(Color.FromArgb(120, 125, 142)))
            g.DrawString("Taskbar Hero 1.00.08–1.00.09   •   v1.3.1   •   ACTk + Speed + EXP + Stash",
                tagFont, tg, tx, 50);

        using var pen = new Pen(AccentCyan, 2);
        g.DrawLine(pen, 0, rect.Height - 2, rect.Width, rect.Height - 2);
    }

    private void Log(string msg)
    {
        if (_txtLog == null) return;
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n";
        if (_txtLog.InvokeRequired) _txtLog.Invoke(() => _txtLog.AppendText(line));
        else _txtLog.AppendText(line);
    }

    private void OnAttach(object? sender, EventArgs e)
    {
        if (_mem.IsAttached)
        {
            _bridge.SetSpeed(false, 1.0f);
            _bridge.Dispose();
            _scanBridge?.Dispose();
            _scanBridge = null;
            _heroScanBridge?.Dispose();
            _heroScanBridge = null;
            _mem.Detach();
            _patcher = null;
            _pid = -1;
            _injected = false;
            _btnAttach.Text = "Connect";
            _lblStatus.Text = "Disconnected";
            _lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
            _btnBypass.Enabled = false;
            _chkSpeed.Checked = false;
            _pnlSpeed.Visible = false;
            _btnScanHeroes.Enabled = false;
            _btnWriteHeroStat.Enabled = false;
            _exp?.ResetWithoutRestore();
            _exp = null;
            _txtExp.Enabled = _btnApplyExp.Enabled = _btnDisableExp.Enabled = false;
            _lblExpStatus.Text = "OFF"; _lblExpStatus.ForeColor = TextDim;
            SetSpawnControlsEnabled(false);
            Log("Disconnected.");
            return;
        }

        using var picker = new ProcessPickerForm();
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedPid < 0)
        {
            Log("Process selection cancelled.");
            return;
        }

        if (_mem.AttachToPid(picker.SelectedPid))
        {
            _pid = picker.SelectedPid;

            var gaPath = GameBuildProfile.ResolveDllPath(_mem, _txtDllPath.Text);
            var detected = GameBuildProfile.DetectForAttach(_mem, gaPath);
            if (detected != null)
            {
                _buildProfile = detected;
                Log($"Detected game build: {_buildProfile.VersionLabel}.");
            }
            else if (_buildProfile.BuildId == 10009)
            {
                Log("Using 1.00.09 profile from disk (Extra Features tab). ACTk will verify against GameAssembly.dll on disk.");
            }
            else
            {
                _buildProfile = GameBuildProfile.V1008;
                Log("WARNING: Could not auto-detect 1.00.08 or 1.00.09.");
                Log("  Using 1.00.08 offsets as fallback — connect with game on 1.00.09 and rebuild if bypass fails.");
            }

            _patcher = new AntiCheatPatcher(_mem, _buildProfile, gaPath);
            _btnAttach.Text = "Disconnect";
            _lblStatus.Text = $"Connected: {_mem.ProcessName} | GA @ 0x{_mem.GameAssemblyBase.ToInt64():X}";
            _lblStatus.ForeColor = Color.FromArgb(100, 255, 100);
            _btnBypass.Enabled = true;
            _btnScanHeroes.Enabled = true;
            _btnWriteHeroStat.Enabled = true;
            _exp = new HighExpPatcher(_mem);
            _txtExp.Enabled = _btnApplyExp.Enabled = true;
            _btnDisableExp.Enabled = false;
            SetSpawnControlsEnabled(true);
            _lblSpawnStatus.Text = "Connect hook (enable speed or spawn)";
            _lblSpawnStatus.ForeColor = TextDim;
            Log($"Connected! PID {_pid} | GameAssembly.dll base: 0x{_mem.GameAssemblyBase.ToInt64():X}");
        }
        else
        {
            Log("ERROR: could not attach (process has no GameAssembly.dll?).");
            Log("  Make sure the trainer runs as Administrator.");
        }
    }

    private void OnBypass(object? sender, EventArgs e)
    {
        if (_patcher == null) return;

        var report = _patcher.PatchDetectors();
        int ok = report.Count(r => r.ok);
        Log($"--- ACTk Bypass: {ok}/{report.Count} detectors neutralized ---");
        foreach (var (name, success, detail) in report)
            Log($"  [{(success ? "OK" : "SKIP")}] {name} - {detail}");

        if (ok == report.Count)
        {
            _lblBypassStatus.Text = "ACTk DISABLED (verified)";
            _lblBypassStatus.ForeColor = Color.FromArgb(100, 255, 100);
            Log("All detectors neutralized, including SpeedHackDetector.");
        }
        else if (ok > 0)
        {
            _lblBypassStatus.Text = $"ACTk PARTIAL ({ok}/{report.Count})";
            _lblBypassStatus.ForeColor = Color.FromArgb(255, 200, 50);
            Log("WARNING: some patches failed. Run as Administrator.");
        }
        else
        {
            _lblBypassStatus.Text = "ACTk active (failed)";
            _lblBypassStatus.ForeColor = Color.FromArgb(255, 100, 100);
            Log("ERROR: no patch applied.");
        }
    }

    /// <summary>Injects TBHHook.dll (once) and connects the shared-memory bridge.</summary>
    private bool EnsureSpeedReady()
    {
        if (_bridge.IsConnected) return true;
        if (_pid < 0) { Log("Connect to the game first."); return false; }

        if (!_injected)
        {
            Log($"Injecting TBHHook.dll into PID {_pid}...");
            var (ok, msg) = Injector.Inject(_pid, DllPath);
            Log($"  {msg}");
            if (!ok) return false;
            _injected = true;
        }

        if (!_bridge.Connect())
        {
            Log("ERROR: injected DLL did not create the shared channel.");
            return false;
        }

        if (!_bridge.WaitHeartbeat())
        {
            Log("ERROR: DLL is loaded but its worker thread is not responding.");
            return false;
        }

        _bridge.PublishBuildProfile(_buildProfile);

        Log($"Bridge connected. build={_buildProfile.VersionLabel} il2cpp={_bridge.Il2CppReady}, Time={_bridge.TimeResolved}, Stash={_bridge.StashResolved}.");
        if (!_bridge.TimeResolved)
            Log("WARNING: UnityEngine.Time::set_timeScale not resolved; speed may not apply.");
        if (!_bridge.StashResolved)
        {
            Log("WARNING: ud.Stash/jbg not resolved; item spawn will fail until the game is fully loaded.");
            _lblSpawnStatus.Text = "Stash API not ready";
            _lblSpawnStatus.ForeColor = Color.FromArgb(255, 200, 50);
        }
        else
        {
            _lblSpawnStatus.Text = "Ready to spawn";
            _lblSpawnStatus.ForeColor = Color.FromArgb(100, 255, 100);
        }
        return true;
    }

    private void SetSpawnControlsEnabled(bool enabled)
    {
        _txtItemKey.Enabled = enabled;
        _cmbGrade.Enabled = enabled;
        _txtSpawnCount.Enabled = enabled;
        _btnSpawnItem.Enabled = enabled;
        _btnScanItems.Enabled = enabled;
        _btnExportCatalog.Enabled = enabled;
    }

    private void OnExportCatalog(object? sender, EventArgs e)
    {
        if (!EnsureSpeedReady())
        {
            Log("Export aborted: inject TBHHook.dll first.");
            return;
        }

        _scanBridge ??= new ItemScanBridge();
        if (!_scanBridge.TryConnect(3000))
        {
            Log("ERROR: scan channel missing — rebuild TBHHook.dll and reinject.");
            return;
        }

        _btnExportCatalog.Enabled = false;
        try
        {
            Log("Dumping full item catalog from game data (yq)...");
            string? report = _scanBridge.RunCatalogDump(20000);
            if (report == null)
            {
                Log("ERROR: catalog dump failed (timeout). Stay in-game on main menu or stash and retry.");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"TBH_item_catalog_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Save item catalog"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                Log("Export cancelled — showing first lines in log only.");
                LogCatalogPreview(report, 30);
                return;
            }

            File.WriteAllText(dlg.FileName, report, Encoding.UTF8);
            int lineCount = report.Split('\n').Length;
            Log($"Catalog saved: {dlg.FileName} ({lineCount} lines).");
            LogCatalogPreview(report, 15);
        }
        catch (Exception ex)
        {
            Log($"Export error: {ex.Message}");
        }
        finally
        {
            _btnExportCatalog.Enabled = true;
        }
    }

    private void LogCatalogPreview(string report, int maxLines)
    {
        int n = 0;
        foreach (string raw in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("===")) continue;
            if (line.StartsWith("ITEM "))
            {
                Log(line);
                if (++n >= maxLines) break;
            }
        }
        if (n >= maxLines)
            Log("  … (see saved file for full list)");
    }

    private void OnWriteHeroStat(object? sender, EventArgs e)
    {
        if (!float.TryParse(_txtHeroValue.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            Log("Invalid hero stat value. Use dot as decimal separator.");
            return;
        }
        if (!EnsureSpeedReady()) return;

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hero control channel missing. Restart game with the newest hook.");
            return;
        }

        int command = _cmbHeroStat.SelectedIndex switch
        {
            0 => 5,
            1 => 4,
            2 => 6,
            _ => 0
        };
        string? result = _heroScanBridge.WriteValue(command, _cmbHeroIndex.SelectedIndex, value);
        Log(result?.Trim() ?? "ERROR: hero stat write timed out.");
        OnScanHeroes(null, EventArgs.Empty);
    }

    private void OnScanHeroes(object? sender, EventArgs e)
    {
        if (!EnsureSpeedReady())
        {
            Log("Hero scan aborted: inject TBHHook.dll first.");
            return;
        }

        _heroScanBridge ??= new HeroScanBridge();
        if (!_heroScanBridge.TryConnect(3000))
        {
            Log("ERROR: hero scan channel missing. Restart the game after rebuilding TBHHook.dll.");
            return;
        }

        _btnScanHeroes.Enabled = false;
        try
        {
            Log("--- Hero diagnostics scan ---");
            string? report = _heroScanBridge.RunScan(10000);
            if (report == null)
            {
                _txtHeroReport.Text = "ERROR: hero scan timed out.";
                Log("ERROR: hero scan timed out.");
                return;
            }

            _txtHeroReport.Text = report;
            _txtHeroReport.SelectionStart = 0;
            _txtHeroReport.SelectionLength = 0;
            foreach (string raw in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                Log(raw.TrimEnd());
        }
        finally
        {
            _btnScanHeroes.Enabled = _mem.IsAttached;
        }
    }

    private void OnScanItems(object? sender, EventArgs e)
    {
        if (!EnsureSpeedReady())
        {
            Log("Scan aborted: inject TBHHook.dll first.");
            return;
        }

        Log("Waiting for stash API (open in-game stash)...");
        if (!_bridge.WaitForStash(10000))
        {
            Log("ERROR: Stash API not ready — open the stash screen and try Scan again.");
            return;
        }

        _scanBridge ??= new ItemScanBridge();
        if (!_scanBridge.TryConnect(3000))
        {
            Log("ERROR: scan channel missing. Rebuild TBHHook.dll, copy to publish folder, reinject.");
            return;
        }

        _btnScanItems.Enabled = false;
        try
        {
            Log("--- Item scan (stash slots + inventory UIDs) ---");
            string? report = _scanBridge.RunScan(10000);
            if (report == null)
            {
                Log("ERROR: scan failed (timeout or hook exception).");
                return;
            }

            int? firstKey = null;
            TrainerBridge.GradeType? firstGrade = null;
            foreach (string raw in report.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("===") || line.StartsWith("grade =") ||
                    line.StartsWith("src:") || line.StartsWith("Debug:") ||
                    line.StartsWith("--") || line.StartsWith("Summary:"))
                    continue;
                Log(line);

                if (firstKey.HasValue) continue;
                int k = line.IndexOf("itemKey=", StringComparison.Ordinal);
                if (k < 0) continue;
                string tail = line[(k + 8)..];
                int sp = tail.IndexOf(' ');
                string num = sp > 0 ? tail[..sp] : tail;
                if (int.TryParse(num, out int ik) && ik > 0)
                {
                    firstKey = ik;
                    int g = line.IndexOf("grade=", StringComparison.Ordinal);
                    if (g >= 0)
                    {
                        string gt = line[(g + 6)..];
                        int gsp = gt.IndexOf(' ');
                        string gnum = gsp > 0 ? gt[..gsp] : gt;
                        int giEnd = 0;
                        while (giEnd < gnum.Length && char.IsDigit(gnum[giEnd])) giEnd++;
                        if (giEnd > 0 && int.TryParse(gnum[..giEnd], out int gi) &&
                            TrainerBridge.TryGradeFromInt(gi, out var gr))
                            firstGrade = gr;
                    }
                }
            }

            if (firstKey.HasValue)
            {
                _txtItemKey.Text = firstKey.Value.ToString();
                if (firstGrade.HasValue)
                    _cmbGrade.SelectedIndex = (int)firstGrade.Value;
                Log(firstGrade.HasValue
                    ? $"→ Filled spawn: ItemKey={firstKey}, {TrainerBridge.GradeLabel(firstGrade.Value)}"
                    : $"→ Filled ItemKey={firstKey} (grade unchanged)");
            }
            else
                Log("No itemKey found — stash empty or obfuscated names mismatch (rebuild hook).");
        }
        catch (Exception ex)
        {
            Log($"Scan error: {ex.Message}");
        }
        finally
        {
            _btnScanItems.Enabled = true;
        }
    }

    private void OnSpawnItem(object? sender, EventArgs e)
    {
        if (!EnsureSpeedReady())
        {
            Log("Spawn aborted: inject TBHHook.dll first (enable speedhack or retry after connect).");
            return;
        }

        var sinceLast = DateTime.UtcNow - _lastSpawnClick;
        if (_lastSpawnClick != DateTime.MinValue && sinceLast.TotalMilliseconds < 800)
        {
            Log("Wait ~1 second between spawns (inventory sync).");
            return;
        }
        _lastSpawnClick = DateTime.UtcNow;

        Log("Waiting for stash API (open in-game stash if this takes long)...");
        if (!_bridge.WaitForStash(10000))
        {
            Log("ERROR: Stash API still not resolved after 10s.");
            Log("  Open the stash screen in-game, wait 2s, then Spawn again.");
            Log("  If it persists, fill GameBuildProfile.V1009 from Il2CppDumper (see MIGRATE_1.00.09.md).");
            _lblSpawnStatus.Text = "Stash API not ready";
            _lblSpawnStatus.ForeColor = Color.FromArgb(255, 100, 100);
            return;
        }
        _lblSpawnStatus.Text = "Ready to spawn";
        _lblSpawnStatus.ForeColor = Color.FromArgb(100, 255, 100);

        if (!int.TryParse(_txtItemKey.Text.Trim(), out int itemKey) || itemKey <= 0)
        {
            Log("ERROR: invalid ItemKey (positive integer required).");
            return;
        }

        if (!int.TryParse(_txtSpawnCount.Text.Trim(), out int count) || count < 1 || count > 99)
        {
            Log("ERROR: spawn count must be 1..99.");
            return;
        }
        if (count > 15)
            Log("WARNING: high spawn count can lag or crash the game — use 1–5 for testing.");

        TrainerBridge.GradeType grade = TrainerBridge.GradeType.Legendary;
        if (_cmbGrade.SelectedIndex >= 0 && _cmbGrade.SelectedIndex <= (int)TrainerBridge.GradeType.None)
            grade = (TrainerBridge.GradeType)_cmbGrade.SelectedIndex;

        int gradeDigit = (itemKey / 1000) % 10;
        if (itemKey >= 100000 && gradeDigit == (int)grade)
            Log($"ItemKey {itemKey} already matches {TrainerBridge.GradeLabel(grade)} — no grade remap.");
        else if (itemKey >= 100000)
            Log($"Note: for gear IDs use catalog row with digit {(int)grade} (e.g. 31{(int)grade}xxx) or remap from lower grade.");

        _btnSpawnItem.Enabled = false;
        try
        {
            Log($"Spawning {count}x ItemKey={itemKey} {TrainerBridge.GradeLabel(grade)}...");
            int ok = _bridge.SpawnItems(itemKey, grade, count);
            if (ok == count)
            {
                Log($"Spawn OK: {ok}/{count} items.");
                int detail = _bridge.LastSpawnDetail;
                if (detail > 1000)
                    Log($"  Grade remap: used ItemKey {detail} (catalog row for {grade}).");
                _lblSpawnStatus.Text = $"Last: {ok}/{count} OK";
                _lblSpawnStatus.ForeColor = Color.FromArgb(100, 255, 100);
            }
            else
            {
                Log($"Spawn partial/failed: {ok}/{count} succeeded.");
                int err = _bridge.LastSpawnErrorCode;
                int detail = _bridge.LastSpawnDetail;
                if (err < 0)
                    Log($"  {TrainerBridge.DescribeSpawnError(err)}");
                else if (detail < 0)
                    Log($"  {TrainerBridge.DescribeSpawnError(detail)}");
                else if (err == 0)
                    Log("  Timeout — hook did not finish (reinject TBHHook or retry).");
                if (detail > 1000 && detail != itemKey)
                    Log($"  Tried remapped ItemKey {detail} for {grade}.");
                Log("  If AddItemResult code 2+: clear inventory/stash space, wait 1s, retry.");
                _lblSpawnStatus.Text = $"Last: {ok}/{count}";
                _lblSpawnStatus.ForeColor = Color.FromArgb(255, 200, 50);
            }
        }
        catch (Exception ex)
        {
            Log($"Spawn error: {ex.Message}");
            _lblSpawnStatus.Text = "Spawn failed";
            _lblSpawnStatus.ForeColor = Color.FromArgb(255, 100, 100);
        }
        finally
        {
            _btnSpawnItem.Enabled = true;
        }
    }

    private void OnSpeedToggle(object? sender, EventArgs e)
    {
        if (_chkSpeed.Checked)
        {
            if (!EnsureSpeedReady())
            {
                _chkSpeed.Checked = false; // re-enters this handler on the unchecked branch
                return;
            }
            _pnlSpeed.Visible = true;
            float v = ParseSpeed();
            _bridge.SetSpeed(true, v);
            Log($"Speedhack ENABLED at {Fmt(v)}x.");
        }
        else
        {
            _pnlSpeed.Visible = false;
            if (_bridge.IsConnected)
            {
                _bridge.SetSpeed(false, 1.0f);
                Log("Speedhack disabled (speed restored to 1.00x).");
            }
        }
    }

    // Fires for both user drags and programmatic Value changes; _suppressSlider
    // guards the programmatic update we do from Apply to avoid redundant pushes.
    private void OnSpeedSlider(object? sender, EventArgs e)
    {
        float v = _trkSpeed.Value;
        if (!_suppressSlider)
            _txtSpeed.Text = Fmt(v);
        _lblSpeedValue.Text = $"Speed  {Fmt(v)}";
        if (!_suppressSlider && _chkSpeed.Checked && _bridge.IsConnected)
            _bridge.SetSpeed(true, v);
    }

    private void OnSpeedKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true; // no ding
        CommitTypedSpeed();
    }

    // Applies a value typed in the box (supports fractions like 2.5 that the
    // integer slider can't reach). Pushes the exact typed value when enabled.
    private void CommitTypedSpeed()
    {
        float v = ParseSpeed();
        _txtSpeed.Text = Fmt(v);
        _lblSpeedValue.Text = $"Speed  {Fmt(v)}";

        _suppressSlider = true;
        _trkSpeed.Value = (int)Math.Clamp(Math.Round(v), 0, 500);
        _suppressSlider = false;

        if (_chkSpeed.Checked && _bridge.IsConnected)
        {
            _bridge.SetSpeed(true, v);
            Log($"Applied speed {Fmt(v)}x.");
        }
    }

    // Parse tolerant of both '.' and ',' decimal separators (pt-BR types comma).
    private float ParseSpeed()
    {
        string s = _txtSpeed.Text.Trim().Replace(',', '.');
        if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v))
            v = 1.0f;
        return Math.Clamp(v, 0f, 500f);
    }

    private static string Fmt(float v) =>
        v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    // Parse tolerant of both '.' and ',' decimal separators, no clamp.
    private static bool TryParseStat(string text, out float value)
    {
        string s = text.Trim().Replace(',', '.');
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

#if false
    private void OnApplyHp(object? sender, EventArgs e)
    {
        if (_hero == null) { Log("Connect to the game first."); return; }
        if (!TryParseStat(_txtHp.Text, out float v)) { Log("Invalid HP value."); return; }

        float? before = _hero.ReadHp();
        if (_hero.SetHp(v))
        {
            float? after = _hero.ReadHp();
            Log($"HP set to {Fmt(v)} (was {(before.HasValue ? Fmt(before.Value) : "?")}, now {(after.HasValue ? Fmt(after.Value) : "?")}).");
        }
        else if (_buildProfile.HpStatic == 0)
        {
            Log("HP not configured for 1.00.09 — GameBuildProfile.V1009.HpStatic is 0.");
            Log("  Use Cheat Engine pointer scan (see MONO_CE_GUIDE_1.00.09.md).");
        }
        else
        {
            Log("Could not write HP. Enter a run/battle first so the hero exists.");
        }
    }

    private void OnApplyAtk(object? sender, EventArgs e)
    {
        if (_hero == null) { Log("Connect to the game first."); return; }
        if (!TryParseStat(_txtAtk.Text, out float v)) { Log("Invalid ATK value."); return; }

        float? before = _hero.ReadAtk();
        if (_hero.SetAtk(v))
        {
            float? after = _hero.ReadAtk();
            Log($"ATK set to {Fmt(v)} (was {(before.HasValue ? Fmt(before.Value) : "?")}, now {(after.HasValue ? Fmt(after.Value) : "?")}).");
        }
        else if (_buildProfile.AtkStatic == 0)
        {
            Log("ATK not configured for 1.00.09 — GameBuildProfile.V1009.AtkStatic is 0.");
            Log("  Use Cheat Engine pointer scan (see MONO_CE_GUIDE_1.00.09.md).");
        }
        else
        {
            Log("Could not write ATK. Enter a run/battle first so the hero exists.");
        }
    }

#endif

    // === Hero EXP on Kill handlers ===

    private void OnApplyExp(object? sender, EventArgs e)
    {
        if (_exp == null) { Log("Connect to the game first."); return; }
        if (!TryParseStat(_txtExp.Text, out float v) || v <= 0)
        {
            Log("Invalid EXP value (must be a positive number).");
            return;
        }
        // Cap at a sensible upper bound for a float (~ 2^24 = 16,777,216 keeps integer precision).
        // Going above is fine, but log a hint.
        if (v > 1_000_000_000f) Log($"Note: {Fmt(v)} is large; float precision rounds it off.");

        var (ok, msg) = _exp.Enable(v);
        Log($"[High EXP] {msg}");
        if (ok)
        {
            _lblExpStatus.Text = $"ON  ({v:0})";
            _lblExpStatus.ForeColor = Color.FromArgb(120, 255, 160);
            _btnApplyExp.Text = "Update";
            _btnDisableExp.Enabled = true;
        }
    }

    private void OnDisableExp(object? sender, EventArgs e)
    {
        if (_exp == null) return;
        var (ok, msg) = _exp.Disable();
        Log($"[High EXP] {msg}");
        if (ok)
        {
            _lblExpStatus.Text = "OFF";
            _lblExpStatus.ForeColor = TextDim;
            _btnApplyExp.Text = "Enable";
            _btnDisableExp.Enabled = false;
        }
    }

    // === Extra Features (static patches) handlers ===

    private void OnBrowseDll(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Select GameAssembly.dll",
            Filter = "GameAssembly.dll|GameAssembly.dll|DLL files (*.dll)|*.dll",
            FileName = "GameAssembly.dll"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _static = new StaticPatcher(ofd.FileName);
        _txtDllPath.Text = ofd.FileName;
        var diskBuild = GameBuildProfile.DetectFromDisk(ofd.FileName);
        if (diskBuild != null)
        {
            ApplyStaticProfile(diskBuild);
            Log($"Extra Features: detected {diskBuild.VersionLabel} on disk.");
        }
        RefreshStatic(log: true);
    }

    private void OnToggleFeature(StaticPatcher.Feature f)
    {
        if (_static == null) { Log("Select GameAssembly.dll first (Browse)."); return; }
        var (ok, msg) = f.State == StaticPatcher.FeatureState.Patched
            ? _static.Revert(f)
            : _static.Apply(f);
        Log($"[{f.Name}] {msg}");
        RefreshStatic(log: false);
    }

    private void OnRestoreBackup(object? sender, EventArgs e)
    {
        if (_static == null) { Log("Select GameAssembly.dll first (Browse)."); return; }
        var (ok, msg) = _static.RestoreBackup();
        Log(msg);
        if (ok) RefreshStatic(log: false);
    }

    /// <summary>Re-reads the DLL and updates every feature row's status + button.</summary>
    private void RefreshStatic(bool log)
    {
        if (_static == null) { if (log) Log("No DLL selected."); return; }
        if (!_static.DllExists) { if (log) Log("GameAssembly.dll not found at the selected path."); return; }

        try { _static.Refresh(_features); }
        catch (Exception ex) { if (log) Log($"Could not read DLL: {ex.Message}"); return; }

        foreach (var f in _features)
        {
            var (_, status, toggle, _) = _featRows[f];
            switch (f.State)
            {
                case StaticPatcher.FeatureState.Original:
                    status.Text = "OFF"; status.ForeColor = TextDim;
                    toggle.Text = "Apply"; toggle.Enabled = true;
                    break;
                case StaticPatcher.FeatureState.Patched:
                    status.Text = "ON"; status.ForeColor = Color.FromArgb(120, 255, 160);
                    toggle.Text = "Revert"; toggle.Enabled = true;
                    break;
                default:
                    status.Text = "MISMATCH"; status.ForeColor = Color.FromArgb(255, 170, 60);
                    toggle.Text = "Apply"; toggle.Enabled = false;
                    break;
            }
        }

        if (log)
        {
            int on = _features.Count(f => f.State == StaticPatcher.FeatureState.Patched);
            int mis = _features.Count(f => f.State == StaticPatcher.FeatureState.Mismatch);
            Log($"Status refreshed: {on} ON, {mis} mismatch. Backup {(_static.BackupExists ? "present" : "not created yet")}.");
            if (mis > 0) Log("MISMATCH = offset doesn't match this game version (refused, safe).");
        }
    }

    private static List<StaticPatcher.Feature> CloneStaticFeatures(GameBuildProfile profile) =>
        profile.StaticFeatures.Select(f => new StaticPatcher.Feature
        {
            Name = f.Name,
            Description = f.Description,
            Offset = f.Offset,
            Original = f.Original.ToArray(),
            Patched = f.Patched.ToArray(),
        }).ToList();

    private void ApplyStaticProfile(GameBuildProfile profile)
    {
        if (profile.StaticFeatures.Count == 0) return;
        _buildProfile = profile;
        for (int i = 0; i < _features.Count; i++)
        {
            var match = profile.StaticFeatures.FirstOrDefault(f => f.Name == _features[i].Name);
            if (match == null) continue;
            // Mutate existing Feature in-place so _featRows keys remain valid.
            _features[i].Description = match.Description;
            _features[i].Offset      = match.Offset;
            _features[i].Original    = match.Original.ToArray();
            _features[i].Patched     = match.Patched.ToArray();
            _features[i].State       = StaticPatcher.FeatureState.Unknown;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { if (_bridge.IsConnected) _bridge.SetSpeed(false, 1.0f); } catch { }
        try { if (_exp is { IsEnabled: true }) _exp.Disable(); } catch { }
        _heroScanBridge?.Dispose();
        _bridge.Dispose();
        _mem.Dispose();
        base.OnFormClosing(e);
    }
}
