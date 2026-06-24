using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

#pragma warning disable CS0162

namespace ForecastDesk;

public partial class Form1 : Form
{
    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private readonly DataStore _dataStore = new();
    private readonly MarketDataService _marketData = new();
    private readonly TelegramService _telegram = new();
    private readonly System.Windows.Forms.Timer _autoCheckTimer = new();
    private readonly System.Windows.Forms.Timer _chartSyncTimer = new();
    private readonly CultureInfo _ruCulture = CultureInfo.GetCultureInfo("ru-RU");

    private AppData _data = new();
    private WebView2 _webView = null!;
    private CoreWebView2Environment? _webViewEnvironment;
    private TextBox _exchangeBox = null!;
    private TextBox _symbolBox = null!;
    private TextBox _timeframeBox = null!;
    private ComboBox _directionBox = null!;
    private DirectionSwitch _directionSwitch = null!;
    private ComboBox _checkModeBox = null!;
    private ModernNumberBox _barsBox = null!;
    private ModernDateBox _checkDatePicker = null!;
    private ModernTimeBox _checkTimePicker = null!;
    private ComboBox _timeZoneBox = null!;
    private TextBox _levelBox = null!;
    private TextBox _targetBox = null!;
    private Label _targetLabel = null!;
    private TextBox _stopBox = null!;
    private CheckBox _autoExtendBox = null!;
    private ModernNumberBox _maxExtensionsBox = null!;
    private ModernNumberBox _extendBarsBox = null!;
    private TextBox _commentBox = null!;
    private TextBox _telegramTokenBox = null!;
    private TextBox _telegramChatBox = null!;
    private TextBox _telegramTopicBox = null!;
    private CheckBox _sendInitialBox = null!;
    private CheckBox _sendResultBox = null!;
    private ComboBox _languageBox = null!;
    private DataGridView _forecastGrid = null!;
    private Label _statusLabel = null!;
    private Label _toolbarStatusLabel = null!;
    private Label _forecastStatusLabel = null!;
    private Button _createForecastButton = null!;
    private Button _checkSelectedButton = null!;
    private Button _sendTelegramButton = null!;
    private Button _previewButton = null!;
    private Button _themeToggleButton = null!;
    private bool _processingDueForecasts;
    private bool _syncingFieldsFromChart;
    private string _lastSyncedChartState = "";

    public Form1()
    {
        InitializeComponent();
        _data = _dataStore.Load();
        _data.Settings.Theme = "Dark";
        BuildUi();
        LoadSettingsIntoControls();

        Shown += async (_, _) => await InitializeWebViewAsync();
        FormClosing += (_, _) => SaveSettingsFromControls();

        _autoCheckTimer.Interval = 60_000;
        _autoCheckTimer.Tick += async (_, _) => await ProcessDueForecastsAsync();
        _autoCheckTimer.Start();

        _chartSyncTimer.Interval = 3_000;
        _chartSyncTimer.Tick += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: false);
        _chartSyncTimer.Start();
    }

    private void BuildUi(bool preserveWindow = false)
    {
        var previousWindowState = WindowState;
        var previousBounds = Bounds;
        var shouldRestoreWindow = preserveWindow && IsHandleCreated;

        SuspendLayout();
        ThemeManager.Use(_data.Settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light
            : AppTheme.Dark);
        var theme = ThemeManager.Palette;
        Text = "Forecast Desk";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1240, 780);
        if (!preserveWindow)
        {
            ClientSize = new Size(1240, 780);
        }

        Font = UiFonts.Body;
        BackColor = theme.Background;
        Controls.Clear();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = theme.Background,
            Padding = new Padding(6, 6, 6, 0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        Controls.Add(root);

        BuildTopServiceBar(root);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = theme.Background
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 354));
        root.Controls.Add(content, 0, 1);

        var chartHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 8, 0),
            BackColor = theme.Background
        };
        var workPanelHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            BackColor = theme.Background
        };
        content.Controls.Add(chartHost, 0, 0);
        content.Controls.Add(workPanelHost, 1, 0);

        BuildChartPanel(chartHost);
        BuildRightPanel(workPanelHost);

        var journalHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = theme.Background
        };
        root.Controls.Add(journalHost, 0, 2);
        BuildJournalTab(journalHost);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Готово. Автопроверка активна, пока программа запущена.",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            BackColor = theme.Background,
            ForeColor = theme.MutedText,
            Font = UiFonts.Small
        };
        root.Controls.Add(_statusLabel, 0, 3);

        if (shouldRestoreWindow)
        {
            if (previousWindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                WindowState = previousWindowState;
                if (previousWindowState == FormWindowState.Normal)
                {
                    Bounds = previousBounds;
                }
            }
        }

        ResumeLayout(true);
    }

    private void BuildTopServiceBar(TableLayoutPanel root)
    {
        var theme = ThemeManager.Palette;
        var bar = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 1,
            DrawBorder = false,
            BackColor = theme.Background,
            Padding = new Padding(8, 3, 6, 3),
            Margin = Padding.Empty
        };
        root.Controls.Add(bar, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = theme.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 620));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.Controls.Add(layout);

        var titleLabel = new Label
        {
            Text = "  Forecast Desk",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiFonts.Title,
            ForeColor = theme.Text,
            BackColor = theme.Background
        };
        layout.Controls.Add(titleLabel, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = theme.Background
        };
        layout.Controls.Add(actions, 1, 0);

        var closeButton = new WindowControlButton
        {
            Kind = WindowControlButtonKind.Close
        };
        closeButton.Click += (_, _) => Close();
        actions.Controls.Add(closeButton);

        var maximizeButton = new WindowControlButton
        {
            Kind = WindowControlButtonKind.Maximize
        };
        maximizeButton.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        actions.Controls.Add(maximizeButton);

        var minimizeButton = new WindowControlButton
        {
            Kind = WindowControlButtonKind.Minimize
        };
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        actions.Controls.Add(minimizeButton);

        actions.Controls.Add(new Panel
        {
            Width = 22,
            Height = 28,
            BackColor = theme.Background,
            Margin = new Padding(10, 1, 0, 0)
        });

        var screenshotButton = new ModernButton
        {
            Text = "▣  Screenshot",
            Width = 110,
            Height = 28,
            Margin = new Padding(8, 1, 0, 0)
        };
        screenshotButton.Click += async (_, _) => await SaveManualScreenshotAsync();
        actions.Controls.Add(screenshotButton);

        var screenshotsButton = new ModernButton
        {
            Text = "▱  Screenshots",
            Width = 116,
            Height = 28,
            Margin = new Padding(8, 1, 0, 0)
        };
        screenshotsButton.Click += (_, _) => OpenScreenshotsFolder();
        actions.Controls.Add(screenshotsButton);

        var settingsButton = new ModernButton
        {
            Text = "⚙  Настройки",
            Width = 124,
            Height = 28,
            Margin = new Padding(8, 1, 0, 0)
        };
        settingsButton.Click += (_, _) => ShowSignalSettingsDialog();
        actions.Controls.Add(settingsButton);

        _themeToggleButton = new ModernButton
        {
            Text = ThemeManager.CurrentTheme == AppTheme.Dark ? "☾  Dark  ●" : "☼  Light ●",
            Width = 88,
            Height = 28,
            Primary = false,
            Margin = new Padding(8, 1, 0, 0)
        };
        _themeToggleButton.Click += (_, _) => ToggleTheme();
        actions.Controls.Add(_themeToggleButton);

        WireWindowDrag(bar);
        WireWindowDrag(layout);
        WireWindowDrag(titleLabel);
    }

    private void BuildChartPanel(Control parent)
    {
        var theme = ThemeManager.Palette;
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = theme.Background
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parent.Controls.Add(panel);

        var top = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = theme.Panel,
            Padding = new Padding(10, 8, 10, 8),
            Radius = 8
        };
        panel.Controls.Add(top, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = theme.Panel
        };
        top.Controls.Add(toolbar);

        var title = new Label
        {
            Text = "Forecast Desk",
            AutoSize = true,
            Font = UiFonts.Title,
            ForeColor = theme.Text,
            Margin = new Padding(2, 6, 18, 0)
        };
        toolbar.Controls.Add(title);

        var openButton = new ModernButton
        {
            Text = "↗ Открыть",
            Width = 118,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        openButton.Click += (_, _) => NavigateToCurrentChart();
        toolbar.Controls.Add(openButton);

        var captureButton = new ModernButton
        {
            Text = "▣ Скриншот",
            Width = 126,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        captureButton.Click += async (_, _) => await SaveManualScreenshotAsync();
        toolbar.Controls.Add(captureButton);

        var syncButton = new ModernButton
        {
            Text = "↻ Sync",
            Width = 92,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        syncButton.Click += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: true);
        toolbar.Controls.Add(syncButton);

        _sendTelegramButton = new ModernButton
        {
            Text = "✈ Telegram",
            Width = 118,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        _sendTelegramButton.Click += async (_, _) => await SendCurrentSignalToTelegramAsync();
        toolbar.Controls.Add(_sendTelegramButton);

        var settingsButton = new ModernButton
        {
            Text = "⚙ Настройки",
            Width = 118,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        settingsButton.Click += (_, _) => ShowSignalSettingsDialog();
        toolbar.Controls.Add(settingsButton);

        var tradeButton = new ModernButton
        {
            Text = "Trade",
            Width = 74,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        toolbar.Controls.Add(tradeButton);

        var publishButton = new ModernButton
        {
            Text = "Publish",
            Width = 86,
            Height = 28,
            Primary = true,
            Margin = new Padding(0, 0, 14, 0)
        };
        toolbar.Controls.Add(publishButton);

        _toolbarStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = theme.MutedText,
            Font = UiFonts.Small,
            Text = "TradingView подключен  •  Telegram активен  •  Последняя синхронизация: --",
            Margin = new Padding(4, 7, 0, 0)
        };
        toolbar.Controls.Add(_toolbarStatusLabel);

        var chartFrame = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = theme.Card,
            Padding = new Padding(1),
            Radius = 8,
            Margin = Padding.Empty
        };
        panel.Controls.Add(chartFrame, 0, 1);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = theme.Background
        };
        chartFrame.Controls.Add(_webView);
    }

    private void WireWindowDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        };
    }

    private void BuildRightPanel(Control parent)
    {
        parent.Padding = new Padding(0);
        InitializeTelegramControls();
        BuildForecastTab(parent);
    }

    private void InitializeTelegramControls()
    {
        _telegramTokenBox = new TextBox
        {
            UseSystemPasswordChar = true,
            PlaceholderText = "token от BotFather"
        };
        _telegramChatBox = new TextBox
        {
            PlaceholderText = "chat_id"
        };
        _telegramTopicBox = new TextBox
        {
            PlaceholderText = "message_thread_id"
        };
        _sendInitialBox = new CheckBox { Text = "Отправлять прогноз при фиксации", AutoSize = true };
        _sendResultBox = new CheckBox { Text = "Отправлять результат проверки", AutoSize = true };
    }

    private void BuildForecastTab(Control parent)
    {
        var theme = ThemeManager.Palette;
        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            BackColor = theme.Background
        };
        parent.Controls.Add(scrollHost);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = theme.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scrollHost.Controls.Add(layout);

        _exchangeBox = CreateReadOnlyTextBox("KUCOIN");
        _symbolBox = CreateReadOnlyTextBox("BTCUSDT");
        _timeframeBox = CreateReadOnlyTextBox("1H");
        _directionBox = CreateComboBox("UP", "DOWN");
        _directionBox.Visible = false;
        _directionSwitch = new DirectionSwitch { Dock = DockStyle.Fill };
        _directionSwitch.ValueChanged += (_, _) => _directionBox.Text = _directionSwitch.Value;
        _checkModeBox = CreateComboBox(ForecastCheckMode.TimeOnly, ForecastCheckMode.Price, ForecastCheckMode.PriceAndTime);
        _checkModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _checkModeBox.SelectedIndexChanged += (_, _) => UpdateCheckModeUi();
        _barsBox = CreateNumber(90, 1, 10000);
        _checkDatePicker = new ModernDateBox();
        _checkTimePicker = new ModernTimeBox();
        _timeZoneBox = CreateTimeZoneComboBox();
        _levelBox = new TextBox { Dock = DockStyle.Fill };
        _targetBox = new TextBox { Dock = DockStyle.Fill };
        _stopBox = new TextBox { Dock = DockStyle.Fill };
        _autoExtendBox = new CheckBox { Text = "Автопродление при неисполнении", AutoSize = true };
        _maxExtensionsBox = CreateNumber(3, 0, 100);
        _extendBarsBox = CreateNumber(24, 1, 10000);
        _commentBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 88,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Опиши идею прогноза: зона, сценарий, почему ждешь движение, что важно увидеть на графике."
        };

        StyleForecastInputs();

        BuildModernForecastInspector(layout);
        SetDefaultCheckDateTime();
        UpdateCheckModeUi();
        return;

        var detailsSection = CreateSection("Детали прогноза");
        AddRow(detailsSection, 1, "Биржа", _exchangeBox);
        AddRow(detailsSection, 2, "Актив", _symbolBox);
        AddRow(detailsSection, 3, "Таймфрейм", _timeframeBox);
        AddRow(detailsSection, 4, "Направление", _directionSwitch);
        AddRow(detailsSection, 5, "Режим реакции", _checkModeBox);
        AddRow(detailsSection, 6, "Время линии", BuildSplitPanel(_checkDatePicker, _checkTimePicker, 60, 40));
        AddRow(detailsSection, 8, "Описание", _commentBox);
        layout.Controls.Add(detailsSection, 0, 0);

        var checkSection = CreateSection("Проверка");
        AddFullRow(checkSection, 1, CreateStatusStrip());
        AddRow(checkSection, 2, "Окно реакции", BuildInlinePanel(_barsBox, new Label
        {
            Text = "баров",
            AutoSize = true,
            Margin = new Padding(8, 7, 0, 0),
            ForeColor = theme.MutedText
        }));
        _targetLabel = AddRow(checkSection, 3, "Цель", _targetBox);
        AddRow(checkSection, 4, "Стоп", _stopBox);
        AddFullRow(checkSection, 5, _autoExtendBox);
        AddRow(checkSection, 6, "Макс. продлений", _maxExtensionsBox);
        AddRow(checkSection, 7, "Продлевать на", BuildInlinePanel(_extendBarsBox, new Label
        {
            Text = "баров",
            AutoSize = true,
            Margin = new Padding(8, 7, 0, 0),
            ForeColor = theme.MutedText
        }));

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        _createForecastButton = new ModernButton
        {
            Text = "Зафиксировать прогноз",
            Width = 190,
            Height = 34,
            Primary = true,
            Margin = new Padding(0, 8, 8, 0)
        };
        _createForecastButton.Click += async (_, _) => await CreateForecastAsync();
        buttons.Controls.Add(_createForecastButton);

        var syncButton = new ModernButton
        {
            Text = "Взять с графика",
            Width = 136,
            Height = 34,
            Margin = new Padding(0, 8, 0, 0)
        };
        syncButton.Click += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: true);
        buttons.Controls.Add(syncButton);
        AddFullRow(checkSection, 8, buttons);

        var note = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = theme.MutedText,
            Font = UiFonts.Small,
            Text = "Для сигнала по времени нарисуй вертикальную линию в TradingView, укажи ее точное время справа и направление UP/DOWN. TP % считается от свечи линии, окно реакции идет после нее."
        };
        AddFullRow(checkSection, 9, note);
        layout.Controls.Add(checkSection, 0, 1);

        SetDefaultCheckDateTime();
        UpdateCheckModeUi();
    }

    private void BuildModernForecastInspector(TableLayoutPanel layout)
    {
        var theme = ThemeManager.Palette;
        layout.Controls.Clear();
        layout.RowStyles.Clear();
        layout.RowCount = 2;
        layout.ColumnCount = 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var commandBar = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 8,
            BackColor = theme.Panel,
            Padding = new Padding(10, 8, 10, 8),
            Margin = new Padding(0, 0, 0, 10)
        };
        layout.Controls.Add(commandBar, 0, 0);

        var commandLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = theme.Panel
        };
        commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        commandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        commandBar.Controls.Add(commandLayout);

        _languageBox = CreateComboBox("RU", "EN", "DE");
        _languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageBox.Margin = new Padding(0, 0, 6, 0);
        _languageBox.SelectedIndexChanged += (_, _) =>
        {
            _data.Settings.Language = GetSelectedLanguage();
            _dataStore.Save(_data);
        };
        commandLayout.Controls.Add(_languageBox, 0, 0);

        _sendTelegramButton = new ModernButton
        {
            Text = "Send",
            Dock = DockStyle.Fill,
            Success = true,
            Icon = ModernButtonIcon.PaperPlane,
            Radius = 6,
            Margin = new Padding(0, 0, 6, 0)
        };
        _sendTelegramButton.Click += async (_, _) => await SendCurrentSignalToTelegramAsync();
        commandLayout.Controls.Add(_sendTelegramButton, 1, 0);

        _previewButton = new ModernButton
        {
            Text = "Preview",
            Dock = DockStyle.Fill,
            Icon = ModernButtonIcon.Eye,
            Radius = 6,
            Margin = Padding.Empty
        };
        _previewButton.Click += (_, _) => ShowTelegramPreviewDialog();
        commandLayout.Controls.Add(_previewButton, 2, 0);

        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 8,
            BackColor = theme.Card,
            Padding = new Padding(12, 10, 12, 10),
            Margin = Padding.Empty
        };
        layout.Controls.Add(card, 0, 1);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 9,
            BackColor = theme.Card
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(body);

        BuildInspectorContent(card);
        return;

        AddInspectorTitle(body, "ДЕТАЛИ ПРОГНОЗА", 0);
        AddInspectorRow(body, 1,
            CreateCompactField("Биржа", _exchangeBox),
            CreateCompactField("Актив", _symbolBox),
            CreateCompactField("Таймфрейм", _timeframeBox));

        AddInspectorTitle(body, "НАПРАВЛЕНИЕ", 2);
        AddFullInspectorRow(body, 3, _directionSwitch);

        AddInspectorTitle(body, "ПАРАМЕТРЫ ВРЕМЕНИ", 4);
        AddInspectorRow(body, 5,
            CreateCompactField("Режим", _checkModeBox),
            CreateCompactField("Дата", _checkDatePicker),
            CreateCompactField("Время", _checkTimePicker));

        AddInspectorTitle(body, "ЦЕЛИ", 6);
        AddInspectorRow(body, 7,
            CreateCompactField("Level", _levelBox),
            CreateCompactField("SL", _stopBox),
            CreateCompactField("TP", _targetBox));

        var checkBlock = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = theme.Card,
            Padding = new Padding(0, 4, 0, 0)
        };
        body.Controls.Add(checkBlock, 0, 8);

        AddInspectorTitle(checkBlock, "ПРОВЕРКА", 0);
        AddInspectorRow(checkBlock, 1,
            CreateCompactField("Окно реакции", BuildInlinePanel(_barsBox, CreateInlineSuffix("баров"))),
            CreateCompactField("", _autoExtendBox));
        AddInspectorRow(checkBlock, 2,
            CreateCompactField("Макс. продлений", _maxExtensionsBox),
            CreateCompactField("Продлевать на", BuildInlinePanel(_extendBarsBox, CreateInlineSuffix("баров"))));

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = 42,
            BackColor = theme.Card,
            Margin = new Padding(0, 8, 0, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        checkBlock.Controls.Add(bottom, 0, 3);

        _createForecastButton = new ModernButton
        {
            Text = "▣  Зафиксировать прогноз",
            Dock = DockStyle.Fill,
            Primary = true,
            Margin = new Padding(0, 4, 8, 4)
        };
        _createForecastButton.Click += async (_, _) => await CreateForecastAsync();
        bottom.Controls.Add(_createForecastButton, 0, 0);

        var syncButton = new ModernButton
        {
            Text = "⌁  Взять с графика",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4)
        };
        syncButton.Click += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: true);
        bottom.Controls.Add(syncButton, 1, 0);

        _forecastStatusLabel = new Label
        {
            Text = "Статус:  ● Ожидает",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = theme.Warning,
            Font = UiFonts.Strong,
            BackColor = theme.Card
        };
        bottom.Controls.Add(_forecastStatusLabel, 2, 0);
    }

    private void BuildInspectorContent(Control card)
    {
        var theme = ThemeManager.Palette;
        card.Controls.Clear();

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = theme.Card,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        card.Controls.Add(body);

        var details = CreateInspectorSection("ДЕТАЛИ ПРОГНОЗА");
        AddInspectorRow(details, 1,
            CreateCompactField("Биржа", _exchangeBox),
            CreateCompactField("Актив", _symbolBox),
            CreateCompactField("Таймфрейм", _timeframeBox));
        body.Controls.Add(details, 0, 0);

        var direction = CreateInspectorSection("НАПРАВЛЕНИЕ");
        AddFullInspectorRow(direction, 1, _directionSwitch);
        body.Controls.Add(direction, 0, 1);

        var time = CreateInspectorSection("ПАРАМЕТРЫ ВРЕМЕНИ");
        AddInspectorRow(time, 1,
            CreateCompactField("Режим", _checkModeBox),
            CreateCompactField("Дата", _checkDatePicker),
            CreateCompactField("Время", _checkTimePicker));
        body.Controls.Add(time, 0, 2);

        var targets = CreateInspectorSection("ЦЕЛИ");
        AddInspectorRow(targets, 1,
            CreateCompactField("Level", _levelBox),
            CreateCompactField("SL", _stopBox),
            CreateCompactField("TP", _targetBox));
        body.Controls.Add(targets, 0, 3);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 42,
            BackColor = theme.Card,
            Margin = new Padding(0, 0, 0, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _createForecastButton = new ModernButton
        {
            Text = "▣  Зафиксировать",
            Dock = DockStyle.Fill,
            Primary = true,
            Margin = new Padding(0, 4, 10, 4)
        };
        _createForecastButton.Click += async (_, _) => await CreateForecastAsync();
        bottom.Controls.Add(_createForecastButton, 0, 0);

        var syncButton = new ModernButton
        {
            Text = "⌁  Взять с графика",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4)
        };
        syncButton.Click += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: true);
        bottom.Controls.Add(syncButton, 1, 0);

        body.Controls.Add(bottom, 0, 4);
    }

    private static TableLayoutPanel CreateInspectorSection(string title)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = ThemeManager.Palette.Card,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 6)
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddInspectorTitle(section, title, 0);
        return section;
    }

    private static void AddInspectorTitle(TableLayoutPanel layout, string title, int row)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = UiFonts.Title,
            ForeColor = ThemeManager.Palette.Text,
            Margin = new Padding(0, row == 0 ? 0 : 4, 0, 4)
        }, 0, row);
    }

    private static void AddFullInspectorRow(TableLayoutPanel layout, int row, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Height = 31;
        control.Margin = new Padding(0, 0, 0, 4);
        layout.Controls.Add(control, 0, row);
    }

    private static void AddInspectorRow(TableLayoutPanel layout, int row, params Control[] controls)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var rowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = controls.Length,
            RowCount = 1,
            BackColor = ThemeManager.Palette.Card,
            Margin = new Padding(0, 0, 0, 4)
        };
        for (var index = 0; index < controls.Length; index++)
        {
            rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            controls[index].Margin = new Padding(index == 0 ? 0 : 8, 0, index == controls.Length - 1 ? 0 : 8, 0);
            rowPanel.Controls.Add(controls[index], index, 0);
        }

        layout.Controls.Add(rowPanel, 0, row);
    }

    private static Control CreateCompactField(string label, Control control)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = string.IsNullOrWhiteSpace(label) ? 1 : 2,
            BackColor = ThemeManager.Palette.Card,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        if (!string.IsNullOrWhiteSpace(label))
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
            panel.Controls.Add(new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeManager.Palette.MutedText,
                Font = UiFonts.Small
            }, 0, 0);
        }

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        control.Dock = DockStyle.Fill;
        control.Margin = Padding.Empty;
        if (control is CheckBox checkBox)
        {
            checkBox.Height = 27;
            checkBox.TextAlign = ContentAlignment.MiddleLeft;
        }
        panel.Controls.Add(control, 0, string.IsNullOrWhiteSpace(label) ? 0 : 1);
        return panel;
    }

    private static Label CreateInlineSuffix(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(8, 7, 0, 0),
            ForeColor = ThemeManager.Palette.MutedText,
            Font = UiFonts.Small
        };
    }

    private void BuildJournalTab(Control parent)
    {
        var theme = ThemeManager.Palette;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = theme.Panel,
            Padding = new Padding(10, 6, 10, 8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (parent is Panel panel)
        {
            panel.BorderStyle = BorderStyle.None;
            panel.BackColor = theme.Background;
        }

        parent.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3),
            BackColor = theme.Panel
        };
        layout.Controls.Add(toolbar, 0, 0);

        toolbar.Controls.Add(new Label
        {
            Text = "Журнал прогнозов",
            Font = UiFonts.Title,
            ForeColor = theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 7, 18, 0)
        });

        AddFilterButton(toolbar, "Все", true);
        AddFilterButton(toolbar, "Активные", false);
        AddFilterButton(toolbar, "Сработавшие", false);
        AddFilterButton(toolbar, "Не сработавшие", false);
        AddFilterButton(toolbar, "По активу", false);
        AddFilterButton(toolbar, "По дате", false);

        _checkSelectedButton = new ModernButton
        {
            Text = "Проверить сейчас",
            Width = 140,
            Height = 28,
            Margin = new Padding(0, 0, 8, 0)
        };
        _checkSelectedButton.Click += async (_, _) => await CheckSelectedForecastAsync();
        toolbar.Controls.Add(_checkSelectedButton);

        var openScreenshotsButton = new ModernButton
        {
            Text = "Папка скриншотов",
            Width = 146,
            Height = 28,
            Margin = new Padding(0, 0, 0, 0)
        };
        openScreenshotsButton.Click += (_, _) => OpenScreenshotsFolder();
        toolbar.Controls.Add(openScreenshotsButton);

        _forecastGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = theme.Panel,
            GridColor = theme.Border,
            EnableHeadersVisualStyles = false,
            Margin = new Padding(0, 2, 0, 0)
        };
        _forecastGrid.ColumnHeadersDefaultCellStyle.BackColor = theme.Card;
        _forecastGrid.ColumnHeadersDefaultCellStyle.ForeColor = theme.MutedText;
        _forecastGrid.ColumnHeadersDefaultCellStyle.Font = UiFonts.Small;
        _forecastGrid.DefaultCellStyle.BackColor = theme.Panel;
        _forecastGrid.DefaultCellStyle.ForeColor = theme.Text;
        _forecastGrid.DefaultCellStyle.SelectionBackColor = theme.RowHover;
        _forecastGrid.DefaultCellStyle.SelectionForeColor = theme.Text;
        _forecastGrid.AlternatingRowsDefaultCellStyle.BackColor = theme.CardAlt;
        _forecastGrid.AlternatingRowsDefaultCellStyle.ForeColor = theme.Text;
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", Visible = false });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Статус", FillWeight = 82 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Symbol", HeaderText = "Актив", FillWeight = 86 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Direction", HeaderText = "Напр.", FillWeight = 58 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mode", HeaderText = "Режим", FillWeight = 74 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Start", HeaderText = "Старт", FillWeight = 75 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Last", HeaderText = "Факт", FillWeight = 75 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Change", HeaderText = "%", FillWeight = 55 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CheckAt", HeaderText = "Проверка", FillWeight = 120 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Extensions", HeaderText = "Продл.", FillWeight = 54 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Language", HeaderText = "Язык", FillWeight = 48 });
        _forecastGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CreatedAt", HeaderText = "Создан", FillWeight = 110 });
        _forecastGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Actions",
            HeaderText = "Действия",
            FillWeight = 58,
            FlatStyle = FlatStyle.Flat,
            Text = "Удалить",
            UseColumnTextForButtonValue = false
        });
        _forecastGrid.ColumnHeadersHeight = 20;
        _forecastGrid.RowTemplate.Height = 19;
        _forecastGrid.CellFormatting += ForecastGrid_CellFormatting;
        _forecastGrid.CellClick += ForecastGrid_CellClick;
        _forecastGrid.KeyDown += ForecastGrid_KeyDown;
        _forecastGrid.CellDoubleClick += (_, _) => NavigateToSelectedForecast();
        layout.Controls.Add(_forecastGrid, 0, 1);

        RefreshForecastGrid();
    }

    private void ForecastGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle is null)
        {
            return;
        }

        if (_forecastGrid.Columns[e.ColumnIndex].Name == "Status" && e.Value is string status)
        {
            var (back, fore) = GetStatusColors(NormalizeStatusCell(status));
            e.CellStyle.BackColor = back;
            e.CellStyle.ForeColor = fore;
            e.CellStyle.Font = UiFonts.Strong;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        if (_forecastGrid.Columns[e.ColumnIndex].Name == "Direction" && e.Value is string direction)
        {
            e.CellStyle.ForeColor = direction.Equals("UP", StringComparison.OrdinalIgnoreCase)
                ? ThemeManager.Palette.Up
                : ThemeManager.Palette.Down;
            e.CellStyle.Font = UiFonts.Strong;
        }

        if (_forecastGrid.Columns[e.ColumnIndex].Name == "Actions")
        {
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            e.CellStyle.ForeColor = ThemeManager.Palette.Down;
        }

        if (_forecastGrid.Columns[e.ColumnIndex].Name == "Change" && e.Value is string change)
        {
            if (change.StartsWith("+", StringComparison.Ordinal))
            {
                e.CellStyle.ForeColor = ThemeManager.Palette.Success;
            }
            else if (change.StartsWith("-", StringComparison.Ordinal))
            {
                e.CellStyle.ForeColor = ThemeManager.Palette.Down;
            }
        }
    }

    private void ForecastGrid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0
            || e.ColumnIndex < 0
            || _forecastGrid.Columns[e.ColumnIndex].Name != "Actions")
        {
            return;
        }

        DeleteForecastAtRow(e.RowIndex);
    }

    private void ForecastGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || _forecastGrid.SelectedRows.Count == 0)
        {
            return;
        }

        DeleteForecastAtRow(_forecastGrid.SelectedRows[0].Index);
        e.Handled = true;
    }

    private void DeleteForecastAtRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _forecastGrid.Rows.Count)
        {
            return;
        }

        var id = _forecastGrid.Rows[rowIndex].Cells["Id"].Value?.ToString();
        var forecast = _data.Forecasts.FirstOrDefault(item => item.Id == id);
        if (forecast is null)
        {
            SetStatus("Это демонстрационная строка журнала, ее удалять не нужно.");
            return;
        }

        var result = MessageBox.Show(
            this,
            "Удалить этот прогноз?",
            "Журнал прогнозов",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _data.Forecasts.Remove(forecast);
        _dataStore.Save(_data);
        RefreshForecastGrid();
        SetStatus("Прогноз удален из журнала.");
    }

    private void BuildTelegramTab(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        parent.Controls.Add(layout);

        _telegramTokenBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "token от BotFather"
        };
        _telegramChatBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "chat_id"
        };
        _telegramTopicBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "message_thread_id (optional)"
        };
        _sendInitialBox = new CheckBox { Text = "Отправлять прогноз сразу после фиксации", AutoSize = true };
        _sendResultBox = new CheckBox { Text = "Отправлять результат проверки", AutoSize = true };

        AddRow(layout, 0, "Bot Token", _telegramTokenBox);
        AddRow(layout, 1, "Chat ID", _telegramChatBox);
        AddRow(layout, 2, "Topic ID", _telegramTopicBox);
        AddFullRow(layout, 3, _sendInitialBox);
        AddFullRow(layout, 4, _sendResultBox);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };

        var saveButton = new Button
        {
            Text = "Сохранить настройки",
            Width = 160,
            Height = 32
        };
        saveButton.Click += (_, _) =>
        {
            SaveSettingsFromControls();
            SetStatus("Настройки сохранены.");
        };
        buttons.Controls.Add(saveButton);

        var testButton = new Button
        {
            Text = "Тест",
            Width = 80,
            Height = 32
        };
        testButton.Click += async (_, _) => await SendTelegramTestAsync();
        buttons.Controls.Add(testButton);
        AddFullRow(layout, 5, buttons);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = Color.DimGray,
            Text = "Bot Token и Chat ID хранятся локально в AppData пользователя. Для канала добавь бота администратором."
        };
        AddFullRow(layout, 6, note);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            if (_webView.CoreWebView2 is null)
            {
                SetStatus("Запускаю WebView2 с постоянным профилем...");
                Directory.CreateDirectory(_dataStore.WebViewProfileDirectory);
                _webViewEnvironment ??= await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: _dataStore.WebViewProfileDirectory);
                await _webView.EnsureCoreWebView2Async(_webViewEnvironment);
                var core = _webView.CoreWebView2
                    ?? throw new InvalidOperationException("WebView2 не вернул CoreWebView2 после запуска.");
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                core.SourceChanged += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: false);
                core.NavigationCompleted += async (_, _) => await SyncForecastFieldsFromChartAsync(showStatus: false);
            }

            NavigateToCurrentChart();
            SetStatus("TradingView открыт. Cookies и вход сохраняются в профиле Forecast Desk.");
        }
        catch (Exception ex)
        {
            SetStatus("WebView2 не запустился.");
            MessageBox.Show(this, ex.Message, "WebView2", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SyncForecastFieldsFromChartAsync(bool showStatus)
    {
        if (_syncingFieldsFromChart)
        {
            return;
        }

        var source = _webView.CoreWebView2?.Source ?? _webView.Source?.ToString();
        var domState = new TradingViewDomState();
        if (_webView.CoreWebView2 is not null)
        {
            try
            {
                var rawLocation = await _webView.CoreWebView2.ExecuteScriptAsync("location.href");
                source = JsonSerializer.Deserialize<string>(rawLocation) ?? source;

                var rawState = await _webView.CoreWebView2.ExecuteScriptAsync(TradingViewStateScript);
                domState = JsonSerializer.Deserialize<TradingViewDomState>(rawState) ?? new TradingViewDomState();
            }
            catch
            {
                // Some TradingView states can block script timing briefly; the WebView source is still useful.
            }
        }

        SyncForecastFieldsFromChartState(source, domState, showStatus);
    }

    private void SyncForecastFieldsFromChartState(string? url, TradingViewDomState domState, bool showStatus)
    {
        _ = MarketDataService.TryParseTradingViewUrl(
            url,
            out var urlExchange,
            out var urlSymbol,
            out var urlTimeframe);

        var exchange = FirstNonEmpty(domState.Exchange, urlExchange);
        var symbol = FirstNonEmpty(domState.Symbol, urlSymbol);
        var timeframe = FirstNonEmpty(domState.Timeframe, urlTimeframe);
        var stateKey = $"{exchange}|{symbol}|{timeframe}|{url}";

        if (stateKey == _lastSyncedChartState && !showStatus)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(exchange)
            && string.IsNullOrWhiteSpace(symbol)
            && string.IsNullOrWhiteSpace(timeframe))
        {
            if (showStatus)
            {
                SetStatus("Не удалось прочитать актив и таймфрейм из текущего графика TradingView.");
            }

            return;
        }

        _syncingFieldsFromChart = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(exchange))
            {
                _exchangeBox.Text = exchange;
            }

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                _symbolBox.Text = symbol;
            }

            if (!string.IsNullOrWhiteSpace(timeframe))
            {
                _timeframeBox.Text = timeframe;
            }

            _lastSyncedChartState = stateKey;
            SaveSettingsFromControls();
        }
        finally
        {
            _syncingFieldsFromChart = false;
        }

        if (showStatus)
        {
            SetStatus($"Поля прогноза обновлены с графика: {_exchangeBox.Text}:{_symbolBox.Text} / {_timeframeBox.Text}.");
        }

        if (_toolbarStatusLabel is not null)
        {
            _toolbarStatusLabel.Text =
                $"TradingView подключен  •  Telegram активен  •  {_exchangeBox.Text}:{_symbolBox.Text}  •  {_timeframeBox.Text}  •  Sync {DateTime.Now:HH:mm:ss}";
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private const string TradingViewStateScript = """
(() => {
  const read = (value) => (value || "").trim();
  const normalizeText = (value) => read(value).replace(/\s+/g, " ");
  const compact = (value) => read(value).replace(/[\s\/\-.]/g, "").toUpperCase();
  const normalizeExchange = (value) => read(value).replace(/[^a-z0-9]/gi, "").toUpperCase();
  const mapTimeframe = (value) => {
    const v = read(value).toLowerCase().replace(/\s+/g, "");
    const map = {
      "1": "1M", "1m": "1M", "1мин": "1M",
      "1minute": "1M", "1minutes": "1M",
      "3": "3M", "3m": "3M", "3мин": "3M",
      "3minute": "3M", "3minutes": "3M",
      "5": "5M", "5m": "5M", "5мин": "5M",
      "5minute": "5M", "5minutes": "5M",
      "15": "15M", "15m": "15M", "15мин": "15M",
      "15minute": "15M", "15minutes": "15M",
      "30": "30M", "30m": "30M", "30мин": "30M",
      "30minute": "30M", "30minutes": "30M",
      "60": "1H", "1h": "1H", "1ч": "1H",
      "1hour": "1H", "1hours": "1H",
      "120": "2H", "2h": "2H", "2ч": "2H",
      "2hour": "2H", "2hours": "2H",
      "240": "4H", "4h": "4H", "4ч": "4H",
      "4hour": "4H", "4hours": "4H",
      "d": "1D", "1d": "1D", "1д": "1D",
      "1day": "1D", "1days": "1D"
    };
    return map[v] || "";
  };

  const text = `${document.title || ""}\n${document.body ? document.body.innerText : ""}`;
  const elements = Array
    .from(document.querySelectorAll("button,[role='button'],[data-name]"))
    .map((element) => normalizeText(element.innerText || element.textContent || ""))
    .filter(Boolean);

  let symbol = "";
  for (const item of elements) {
    const candidate = compact(item);
    if (/^[A-Z0-9]{2,24}(USDT|USDC|USD|BTC|ETH)$/.test(candidate)) {
      symbol = candidate;
      break;
    }
  }
  if (!symbol) {
    const match = text.match(/\b[A-Z0-9]{2,24}(USDT|USDC|USD|BTC|ETH)\b/i);
    if (match) symbol = match[0].toUpperCase();
  }

  let exchange = "";
  const knownExchanges = [
    "BINANCE", "KUCOIN", "GEMINI", "COINBASE", "BITSTAMP", "KRAKEN",
    "BYBIT", "OKX", "BITFINEX", "BITGET", "MEXC", "COINEX", "HUOBI",
    "POLONIEX", "OANDA", "FOREXCOM", "FXCM", "PEPPERSTONE", "ICMARKETS",
    "CAPITALCOM", "NASDAQ", "NYSE", "AMEX", "TVC"
  ];
  for (const item of elements) {
    const candidate = normalizeExchange(item);
    if (knownExchanges.includes(candidate)) {
      exchange = candidate;
      break;
    }
  }
  if (!exchange) {
    for (const candidate of knownExchanges) {
      if (new RegExp(`\\b${candidate}\\b`, "i").test(text)) {
        exchange = candidate;
        break;
      }
    }
  }

  let timeframe = "";
  const intervalElement = document.querySelector("[data-name='interval-dialog-button']");
  if (intervalElement) {
    timeframe = mapTimeframe(normalizeText(intervalElement.innerText || intervalElement.textContent || ""));
  }
  if (!timeframe) {
    for (const item of elements) {
      timeframe = mapTimeframe(item);
      if (timeframe) break;
    }
  }
  if (!timeframe) {
    const legend = text.match(/[·•]\s*(1|3|5|15|30|60|120|240|1h|2h|4h|D|1D)\s*[·•]\s*([A-Za-z][A-Za-z0-9._-]{1,32})/i);
    if (legend) {
      timeframe = mapTimeframe(legend[1]);
      if (!exchange) exchange = normalizeExchange(legend[2]);
    }
  }
  if (!exchange) {
    const legend = text.match(/[·•]\s*(1|3|5|15|30|60|120|240|1h|2h|4h|D|1D)\s*[·•]\s*([A-Za-z][A-Za-z0-9._-]{1,32})/i);
    if (legend) exchange = normalizeExchange(legend[2]);
  }

  return { Exchange: exchange, Symbol: symbol, Timeframe: timeframe };
})()
""";

    private sealed class TradingViewDomState
    {
        public string Exchange { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = "";
    }

    private void LoadSettingsIntoControls()
    {
        var settings = _data.Settings;
        _exchangeBox.Text = settings.Exchange;
        _symbolBox.Text = settings.Symbol;
        _timeframeBox.Text = settings.Timeframe;
        _directionBox.SelectedIndex = 0;
        _directionSwitch.Value = "UP";
        _checkModeBox.Text = ForecastCheckMode.TimeOnly;
        SelectTimeZoneOffset(settings.TimeZoneOffsetMinutes);
        _barsBox.Value = 90;
        _levelBox.Clear();
        _targetBox.Clear();
        _stopBox.Clear();
        SetDefaultCheckDateTime();
        UpdateCheckModeUi();
        _autoExtendBox.Checked = true;
        _maxExtensionsBox.Value = 3;
        _extendBarsBox.Value = 24;
        _telegramTokenBox.Text = settings.TelegramBotToken;
        _telegramChatBox.Text = settings.TelegramChatId;
        _telegramTopicBox.Text = settings.TelegramMessageThreadId;
        _sendInitialBox.Checked = settings.SendInitialToTelegram;
        _sendResultBox.Checked = settings.SendResultToTelegram;
        if (_languageBox is not null)
        {
            _languageBox.Text = NormalizeLanguage(settings.Language);
        }
    }

    private void SaveSettingsFromControls()
    {
        _data.Settings.Exchange = _exchangeBox.Text.Trim();
        _data.Settings.Symbol = _symbolBox.Text.Trim();
        _data.Settings.Timeframe = _timeframeBox.Text.Trim();
        _data.Settings.TimeZoneOffsetMinutes = GetSelectedTimeZoneOffsetMinutes();
        _data.Settings.TelegramBotToken = _telegramTokenBox.Text.Trim();
        _data.Settings.TelegramChatId = _telegramChatBox.Text.Trim();
        _data.Settings.TelegramMessageThreadId = _telegramTopicBox.Text.Trim();
        _data.Settings.Language = GetSelectedLanguage();
        _data.Settings.SendInitialToTelegram = _sendInitialBox.Checked;
        _data.Settings.SendResultToTelegram = _sendResultBox.Checked;
        _dataStore.Save(_data);
    }

    private string GetSelectedLanguage()
    {
        return NormalizeLanguage(_languageBox?.Text ?? _data.Settings.Language);
    }

    private static string NormalizeLanguage(string? language)
    {
        var value = (language ?? "RU").Trim().ToUpperInvariant();
        return value is "EN" or "DE" ? value : "RU";
    }

    private void ToggleTheme()
    {
        _data.Settings.Theme = ThemeManager.CurrentTheme == AppTheme.Dark ? "Light" : "Dark";
        _dataStore.Save(_data);
        BuildUi(preserveWindow: true);
        LoadSettingsIntoControls();
        _ = InitializeWebViewAsync();
    }

    private void NavigateToCurrentChart()
    {
        SaveSettingsFromControls();
        var url = MarketDataService.BuildTradingViewUrl(_exchangeBox.Text, _symbolBox.Text, _timeframeBox.Text, _data.Settings.Theme);
        NavigateToUrl(url);
    }

    private void NavigateToSelectedForecast()
    {
        var forecast = GetSelectedForecast();
        if (forecast is null)
        {
            return;
        }

        NavigateToForecast(forecast);
    }

    private void NavigateToForecast(ForecastRecord forecast)
    {
        _exchangeBox.Text = forecast.Exchange;
        _symbolBox.Text = forecast.Symbol;
        _timeframeBox.Text = forecast.Timeframe;
        _directionBox.Text = forecast.Direction;
        _directionSwitch.Value = forecast.Direction;
        NavigateToUrl(MarketDataService.BuildTradingViewUrl(forecast.Exchange, forecast.Symbol, forecast.Timeframe, _data.Settings.Theme));
    }

    private void NavigateToUrl(string url)
    {
        if (_webView.CoreWebView2 is null)
        {
            _webView.Source = new Uri(url);
            return;
        }

        _webView.CoreWebView2.Navigate(url);
    }

    private async Task CreateForecastAsync()
    {
        try
        {
            _createForecastButton.Enabled = false;
            SaveSettingsFromControls();
            ValidateForecastInputs();
            SetStatus("Фиксирую прогноз и сохраняю скриншот...");

            var startPrice = ReadRequiredDecimal(_levelBox.Text, "Level");
            var bars = (int)_barsBox.Value;
            var hasTimeMode = ForecastCheckMode.HasTime(_checkModeBox.Text);
            var timeLineAtUtc = hasTimeMode
                ? BuildExactLineDateTimeOffset().UtcDateTime
                : (DateTime?)null;
            var timeframeSpan = MarketDataService.TimeframeToTimeSpan(_timeframeBox.Text);
            var checkAtUtc = hasTimeMode
                ? timeLineAtUtc!.Value + timeframeSpan * (bars + 1)
                : DateTime.UtcNow + timeframeSpan * bars;

            var forecast = new ForecastRecord
            {
                Exchange = _exchangeBox.Text.Trim().ToUpperInvariant(),
                Symbol = _symbolBox.Text.Trim().ToUpperInvariant(),
                Timeframe = _timeframeBox.Text.Trim().ToUpperInvariant(),
                Direction = _directionBox.Text.Trim().ToUpperInvariant(),
                CheckMode = _checkModeBox.Text,
                Language = GetSelectedLanguage(),
                TimeZoneLabel = hasTimeMode ? _timeZoneBox.Text : "",
                TimeZoneOffsetMinutes = hasTimeMode ? GetSelectedTimeZoneOffsetMinutes() : 0,
                Comment = _commentBox.Text.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                TimeLineAtUtc = timeLineAtUtc,
                CheckAtUtc = checkAtUtc,
                StartPrice = startPrice,
                TargetPrice = ReadOptionalDecimal(_targetBox.Text, "TP"),
                TakeProfitPercent = null,
                LineBaseMode = "",
                StopPrice = ReadOptionalDecimal(_stopBox.Text, "SL"),
                CheckAfterBars = bars,
                AutoExtend = false,
                MaxExtensions = 0,
                ExtendBars = 0,
                Status = ForecastStatus.Waiting
            };

            forecast.InitialScreenshotPath = await CaptureChartAsync(forecast.Id, "before");
            forecast.LastScreenshotPath = forecast.InitialScreenshotPath;

            _data.Forecasts.Add(forecast);
            _dataStore.Save(_data);
            RefreshForecastGrid();

            SetStatus("Прогноз сохранен.");
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось зафиксировать прогноз.");
            MessageBox.Show(this, ex.Message, "Прогноз", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _createForecastButton.Enabled = true;
        }
    }

    private async Task CheckSelectedForecastAsync()
    {
        var forecast = GetSelectedForecast();
        if (forecast is null)
        {
            MessageBox.Show(this, "Выберите прогноз в журнале.", "Журнал", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await CheckForecastAsync(forecast, automatic: false);
    }

    private async Task ProcessDueForecastsAsync()
    {
        if (_processingDueForecasts)
        {
            return;
        }

        var due = _data.Forecasts
            .Where(IsDueForCheck)
            .OrderBy(forecast => forecast.CheckAtUtc)
            .ToList();

        if (due.Count == 0)
        {
            return;
        }

        _processingDueForecasts = true;
        try
        {
            foreach (var forecast in due)
            {
                await CheckForecastAsync(forecast, automatic: true);
            }
        }
        finally
        {
            _processingDueForecasts = false;
        }
    }

    private async Task CheckForecastAsync(ForecastRecord forecast, bool automatic)
    {
        try
        {
            _checkSelectedButton.Enabled = false;
            SetStatus($"Проверяю {forecast.Exchange}:{forecast.Symbol}...");
            NavigateToForecast(forecast);
            await WaitForChartAfterNavigationAsync();

            var checkedAtUtc = DateTime.UtcNow;
            var currentPrice = await _marketData.GetLastPriceAsync(forecast.Exchange, forecast.Symbol);
            var timeframeSpan = MarketDataService.TimeframeToTimeSpan(forecast.Timeframe);
            var evaluationStartUtc = forecast.TimeLineAtUtc.HasValue
                ? forecast.TimeLineAtUtc.Value - timeframeSpan
                : forecast.CreatedAtUtc;
            var candles = await _marketData.GetCandlesAsync(
                forecast.Exchange,
                forecast.Symbol,
                forecast.Timeframe,
                evaluationStartUtc,
                checkedAtUtc);
            var screenshot = await CaptureChartAsync(forecast.Id, "after");
            var result = ForecastEvaluator.Evaluate(forecast, currentPrice, candles);

            forecast.LastCheckAtUtc = checkedAtUtc;
            forecast.LastPrice = currentPrice;
            forecast.HighestPrice = result.HighestPrice;
            forecast.LowestPrice = result.LowestPrice;
            forecast.HitPrice = result.HitPrice;
            forecast.HitAtUtc = result.HitAtUtc;
            forecast.ChangePercent = result.ChangePercent;
            forecast.TargetPrice = result.TargetPrice ?? forecast.TargetPrice;
            forecast.LineBasePrice = result.LineBasePrice ?? forecast.LineBasePrice;
            forecast.LineBaseCandleOpenUtc = result.LineBaseCandleOpenUtc ?? forecast.LineBaseCandleOpenUtc;
            forecast.LineBaseCandleCloseUtc = result.LineBaseCandleCloseUtc ?? forecast.LineBaseCandleCloseUtc;
            forecast.LineBaseCandleKind = string.IsNullOrWhiteSpace(result.LineBaseCandleKind)
                ? forecast.LineBaseCandleKind
                : result.LineBaseCandleKind;
            forecast.LastScreenshotPath = screenshot;
            forecast.ResultText = result.ResultText;

            if (result.ShouldExtend)
            {
                forecast.ExtensionsUsed += 1;
                forecast.Status = ForecastStatus.Extended;
                forecast.CheckAtUtc = DateTime.UtcNow + timeframeSpan * forecast.ExtendBars;
            }
            else if (result.Status == ForecastStatus.Error)
            {
                forecast.Status = ForecastStatus.Error;
                forecast.CheckAtUtc = DateTime.UtcNow.AddMinutes(10);
            }
            else
            {
                forecast.Status = result.Status;
                forecast.CompletedAtUtc = DateTime.UtcNow;
            }

            _dataStore.Save(_data);
            RefreshForecastGrid();

            if (_sendResultBox.Checked)
            {
                await SendTelegramPhotoAsync(screenshot, BuildResultMessage(forecast));
            }

            var prefix = automatic ? "Автопроверка" : "Проверка";
            SetStatus($"{prefix}: {forecast.Symbol} - {forecast.Status}. {forecast.ResultText}");
        }
        catch (Exception ex)
        {
            forecast.Status = ForecastStatus.Error;
            forecast.ResultText = ex.Message;
            forecast.LastCheckAtUtc = DateTime.UtcNow;
            forecast.CheckAtUtc = DateTime.UtcNow.AddMinutes(10);
            _dataStore.Save(_data);
            RefreshForecastGrid();
            SetStatus($"Ошибка проверки {forecast.Symbol}. Повтор через 10 минут.");

            if (!automatic)
            {
                MessageBox.Show(this, ex.Message, "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _checkSelectedButton.Enabled = true;
        }
    }

    private async Task SaveManualScreenshotAsync()
    {
        try
        {
            var path = await CaptureChartAsync("manual", "chart");
            SetStatus($"Скриншот сохранен: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Скриншот", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SendCurrentSignalToTelegramAsync()
    {
        try
        {
            _sendTelegramButton.Enabled = false;
            SaveSettingsFromControls();
            ValidateForecastInputs();
            SetStatus("Готовлю сигнал для Telegram и делаю скриншот...");

            var startPrice = ReadRequiredDecimal(_levelBox.Text, "Level");
            var bars = (int)_barsBox.Value;
            var hasTimeMode = ForecastCheckMode.HasTime(_checkModeBox.Text);
            var timeLineAtUtc = hasTimeMode
                ? BuildExactLineDateTimeOffset().UtcDateTime
                : (DateTime?)null;
            var timeframeSpan = MarketDataService.TimeframeToTimeSpan(_timeframeBox.Text);
            var checkAtUtc = hasTimeMode
                ? timeLineAtUtc!.Value + timeframeSpan * (bars + 1)
                : DateTime.UtcNow + timeframeSpan * bars;

            var draft = new ForecastRecord
            {
                Exchange = _exchangeBox.Text.Trim().ToUpperInvariant(),
                Symbol = _symbolBox.Text.Trim().ToUpperInvariant(),
                Timeframe = _timeframeBox.Text.Trim().ToUpperInvariant(),
                Direction = _directionBox.Text.Trim().ToUpperInvariant(),
                CheckMode = _checkModeBox.Text,
                Language = GetSelectedLanguage(),
                TimeZoneLabel = hasTimeMode ? _timeZoneBox.Text : "",
                TimeZoneOffsetMinutes = hasTimeMode ? GetSelectedTimeZoneOffsetMinutes() : 0,
                Comment = _commentBox.Text.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                TimeLineAtUtc = timeLineAtUtc,
                CheckAtUtc = checkAtUtc,
                StartPrice = startPrice,
                TargetPrice = ReadOptionalDecimal(_targetBox.Text, "TP"),
                TakeProfitPercent = null,
                LineBaseMode = "",
                StopPrice = ReadOptionalDecimal(_stopBox.Text, "SL"),
                CheckAfterBars = bars,
                AutoExtend = false,
                MaxExtensions = 0,
                ExtendBars = 0,
                Status = ForecastStatus.Waiting
            };

            var screenshot = await CaptureChartAsync("telegram", "signal");
            await SendTelegramPhotoAsync(screenshot, BuildInitialMessage(draft));

            SetStatus("Сигнал отправлен в Telegram.");
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось отправить сигнал в Telegram.");
            MessageBox.Show(this, ex.Message, "Telegram", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _sendTelegramButton.Enabled = true;
        }
    }

    private async Task SendTelegramTestAsync()
    {
        try
        {
            SaveSettingsFromControls();
            await SendTelegramMessageAsync("Forecast Desk: тестовое сообщение.");
            SetStatus("Тестовое сообщение отправлено.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Telegram", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private ForecastRecord BuildCurrentDraftForecast()
    {
        SaveSettingsFromControls();
        ValidateForecastInputs();

        var startPrice = ReadRequiredDecimal(_levelBox.Text, "Level");
        var bars = (int)_barsBox.Value;
        var hasTimeMode = ForecastCheckMode.HasTime(_checkModeBox.Text);
        var timeLineAtUtc = hasTimeMode
            ? BuildExactLineDateTimeOffset().UtcDateTime
            : (DateTime?)null;
        var timeframeSpan = MarketDataService.TimeframeToTimeSpan(_timeframeBox.Text);
        var checkAtUtc = hasTimeMode
            ? timeLineAtUtc!.Value + timeframeSpan * (bars + 1)
            : DateTime.UtcNow + timeframeSpan * bars;

        return new ForecastRecord
        {
            Exchange = _exchangeBox.Text.Trim().ToUpperInvariant(),
            Symbol = _symbolBox.Text.Trim().ToUpperInvariant(),
            Timeframe = _timeframeBox.Text.Trim().ToUpperInvariant(),
            Direction = _directionBox.Text.Trim().ToUpperInvariant(),
            CheckMode = _checkModeBox.Text,
            Language = GetSelectedLanguage(),
            TimeZoneLabel = hasTimeMode ? _timeZoneBox.Text : "",
            TimeZoneOffsetMinutes = hasTimeMode ? GetSelectedTimeZoneOffsetMinutes() : 0,
            Comment = _commentBox.Text.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            TimeLineAtUtc = timeLineAtUtc,
            CheckAtUtc = checkAtUtc,
            StartPrice = startPrice,
            TargetPrice = ReadOptionalDecimal(_targetBox.Text, "TP"),
            TakeProfitPercent = null,
            LineBaseMode = "",
            StopPrice = ReadOptionalDecimal(_stopBox.Text, "SL"),
            CheckAfterBars = bars,
            AutoExtend = false,
            MaxExtensions = 0,
            ExtendBars = 0,
            Status = ForecastStatus.Waiting
        };
    }

    private void ShowTelegramPreviewDialog()
    {
        try
        {
            var draft = BuildCurrentDraftForecast();
            var message = BuildInitialMessage(draft);
            var theme = ThemeManager.Palette;

            using var dialog = new Form
            {
                Text = "Telegram Preview",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(520, 430),
                Font = Font,
                BackColor = theme.Card,
                ForeColor = theme.Text
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(12),
                BackColor = theme.Card
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            dialog.Controls.Add(layout);

            var previewBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = false,
                ScrollBars = ScrollBars.Vertical,
                Text = message,
                BackColor = theme.Input,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiFonts.Body
            };
            layout.Controls.Add(previewBox, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = theme.Card
            };
            layout.Controls.Add(buttons, 0, 1);

            var closeButton = new ModernButton { Text = "Close", Width = 86, Height = 30, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(closeButton);

            var copyButton = new ModernButton { Text = "Copy", Width = 86, Height = 30, Margin = new Padding(0, 0, 8, 0) };
            copyButton.Click += (_, _) =>
            {
                Clipboard.SetText(previewBox.Text);
                SetStatus("Preview message copied.");
            };
            buttons.Controls.Add(copyButton);

            var sendButton = new ModernButton
            {
                Text = "Send to Telegram",
                Width = 150,
                Height = 30,
                Success = true,
                Margin = new Padding(0, 0, 8, 0)
            };
            sendButton.Click += async (_, _) =>
            {
                sendButton.Enabled = false;
                try
                {
                    var screenshot = await CaptureChartAsync("telegram", "preview");
                    await SendTelegramPhotoAsync(screenshot, previewBox.Text);
                    SetStatus("Preview sent to Telegram.");
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, ex.Message, "Telegram", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    sendButton.Enabled = true;
                }
            };
            buttons.Controls.Add(sendButton);

            dialog.CancelButton = closeButton;
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowTelegramSettingsDialog()
    {
        var theme = ThemeManager.Palette;
        using var dialog = new Form
        {
            Text = "Telegram Settings",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(520, 440),
            Font = Font,
            BackColor = theme.Card,
            ForeColor = theme.Text
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 8,
            BackColor = theme.Card
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dialog.Controls.Add(layout);

        var enabledBox = new CheckBox
        {
            Text = "Telegram enabled",
            AutoSize = true,
            Checked = _data.Settings.TelegramEnabled,
            BackColor = theme.Card,
            ForeColor = theme.Text
        };
        var tokenBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            Text = _telegramTokenBox.Text,
            PlaceholderText = "Bot Token",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var chatBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = _telegramChatBox.Text,
            PlaceholderText = "-100...",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var topicBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = _telegramTopicBox.Text,
            PlaceholderText = "optional",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var templateBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 130,
            ScrollBars = ScrollBars.Vertical,
            Text = _data.Settings.TelegramMessageTemplate,
            PlaceholderText = "Optional message template",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };

        AddFullRow(layout, 0, enabledBox);
        AddRow(layout, 1, "Bot Token", tokenBox);
        AddRow(layout, 2, "Chat ID", chatBox);
        AddRow(layout, 3, "Topic ID", topicBox);
        AddRow(layout, 4, "Template", templateBox);

        var hint = new Label
        {
            Text = "Если группа не принимает сообщения, сделайте бота администратором и разрешите отправку сообщений/медиа.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = theme.MutedText,
            Font = UiFonts.Small
        };
        AddFullRow(layout, 5, hint);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = theme.Card
        };
        var closeButton = new ModernButton { Text = "Close", Width = 86, Height = 30, DialogResult = DialogResult.Cancel };
        var saveButton = new ModernButton { Text = "Save", Width = 86, Height = 30, Primary = true, Margin = new Padding(0, 0, 8, 0) };
        var testButton = new ModernButton { Text = "Test", Width = 86, Height = 30, Margin = new Padding(0, 0, 8, 0) };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(testButton);
        AddFullRow(layout, 6, buttons);

        void SaveTelegramSettings()
        {
            _telegramTokenBox.Text = tokenBox.Text.Trim();
            _telegramChatBox.Text = chatBox.Text.Trim();
            _telegramTopicBox.Text = topicBox.Text.Trim();
            _data.Settings.TelegramEnabled = enabledBox.Checked;
            _data.Settings.TelegramMessageTemplate = templateBox.Text.Trim();
            SaveSettingsFromControls();
        }

        saveButton.Click += (_, _) =>
        {
            SaveTelegramSettings();
            SetStatus("Telegram settings saved.");
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        testButton.Click += async (_, _) =>
        {
            try
            {
                SaveTelegramSettings();
                await SendTelegramMessageAsync("Forecast Desk: test message.");
                SetStatus("Telegram test message sent.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(dialog, ex.Message, "Telegram", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        dialog.AcceptButton = saveButton;
        dialog.CancelButton = closeButton;
        dialog.ShowDialog(this);
    }

    private async Task SendTelegramPhotoAsync(string imagePath, string caption)
    {
        if (!_data.Settings.TelegramEnabled)
        {
            throw new InvalidOperationException("Telegram отключен в настройках.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _telegram.SendPhotoAsync(_telegramTokenBox.Text, _telegramChatBox.Text, imagePath, caption, _telegramTopicBox.Text);
                return;
            }
            catch (TelegramChatMigratedException ex) when (attempt < 2)
            {
                await HandleTelegramMigrationAsync(ex.ChatId);
            }
            catch (TelegramMessageThreadNotFoundException) when (attempt < 2)
            {
                await HandleTelegramThreadNotFoundAsync();
            }
        }
    }

    private async Task SendTelegramMessageAsync(string text)
    {
        if (!_data.Settings.TelegramEnabled)
        {
            throw new InvalidOperationException("Telegram отключен в настройках.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _telegram.SendMessageAsync(_telegramTokenBox.Text, _telegramChatBox.Text, text, _telegramTopicBox.Text);
                return;
            }
            catch (TelegramChatMigratedException ex) when (attempt < 2)
            {
                await HandleTelegramMigrationAsync(ex.ChatId);
            }
            catch (TelegramMessageThreadNotFoundException) when (attempt < 2)
            {
                await HandleTelegramThreadNotFoundAsync();
            }
        }
    }

    private Task HandleTelegramMigrationAsync(string newChatId)
    {
        _telegramChatBox.Text = newChatId;
        _data.Settings.TelegramChatId = newChatId;
        _dataStore.Save(_data);
        SetStatus($"Telegram перенес группу в supergroup. Chat ID обновлен: {newChatId}");
        return Task.CompletedTask;
    }

    private Task HandleTelegramThreadNotFoundAsync()
    {
        _telegramTopicBox.Text = "";
        _data.Settings.TelegramMessageThreadId = "";
        _dataStore.Save(_data);
        SetStatus("Telegram не нашел Topic ID. Отправляю в общий чат.");
        return Task.CompletedTask;
    }

    private async Task<string> CaptureChartAsync(string forecastId, string phase)
    {
        if (_webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 еще не готов.");
        }

        var path = _dataStore.BuildScreenshotPath(forecastId, phase);
        await using var stream = File.Create(path);
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        return path;
    }

    private async Task WaitForChartAfterNavigationAsync()
    {
        if (_webView.CoreWebView2 is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            completion.TrySetResult();
            _webView.CoreWebView2.NavigationCompleted -= Handler;
        }

        _webView.CoreWebView2.NavigationCompleted += Handler;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var _ = timeout.Token.Register(() =>
        {
            completion.TrySetResult();
            _webView.CoreWebView2.NavigationCompleted -= Handler;
        });

        await completion.Task;
        await Task.Delay(TimeSpan.FromSeconds(4));
    }

    private ForecastRecord? GetSelectedForecast()
    {
        if (_forecastGrid.SelectedRows.Count == 0)
        {
            return null;
        }

        var id = _forecastGrid.SelectedRows[0].Cells["Id"].Value?.ToString();
        return _data.Forecasts.FirstOrDefault(forecast => forecast.Id == id);
    }

    private bool IsDueForCheck(ForecastRecord forecast)
    {
        return forecast.CheckAtUtc <= DateTime.UtcNow
            && forecast.Status is ForecastStatus.Waiting or ForecastStatus.Extended or ForecastStatus.Error;
    }

    private void RefreshForecastGrid()
    {
        if (_forecastGrid is null)
        {
            return;
        }

        _forecastGrid.Rows.Clear();
        if (_data.Forecasts.Count == 0)
        {
            return;
        }

        foreach (var forecast in _data.Forecasts.OrderByDescending(item => item.CreatedAtUtc))
        {
            _forecastGrid.Rows.Add(
                forecast.Id,
                FormatStatusCell(forecast.Status),
                forecast.Symbol,
                forecast.Direction,
                forecast.CheckMode,
                FormatPrice(forecast.StartPrice),
                forecast.LastPrice.HasValue ? FormatPrice(forecast.LastPrice.Value) : "-",
                forecast.ChangePercent.HasValue ? $"{forecast.ChangePercent.Value:+0.####;-0.####;0}%" : "-",
                FormatLocal(forecast.CheckAtUtc),
                forecast.ExtensionsUsed.ToString(_ruCulture),
                NormalizeLanguage(forecast.Language),
                FormatLocal(forecast.CreatedAtUtc),
                "×");
        }
    }

    private void AddDemoJournalRows()
    {
        var rows = new object[][]
        {
            ["demo-1", FormatStatusCell(ForecastStatus.Waiting), "BTCUSDT", "UP", "Time", "23.06.2026 15:57", "-", "-", "23.06.2026 15:57", "0", "RU", "23.06.2026 12:57", "×"],
            ["demo-2", FormatStatusCell(ForecastStatus.Success), "BTCUSDT", "UP", "Price", "23.06.2026 11:20", "23.06.2026 11:35", "+1.25%", "23.06.2026 11:20", "0", "RU", "23.06.2026 11:19", "×"],
            ["demo-3", FormatStatusCell(ForecastStatus.Success), "BTCUSDT", "UP", "Price + Time", "23.06.2026 11:00", "23.06.2026 11:11", "+0.78%", "23.06.2026 11:00", "0", "RU", "23.06.2026 10:59", "×"],
            ["demo-4", FormatStatusCell(ForecastStatus.Failed), "ETHUSDT", "DOWN", "Price + Time", "22.06.2026 22:15", "22.06.2026 23:10", "-0.45%", "22.06.2026 22:15", "2", "RU", "22.06.2026 22:14", "×"],
            ["demo-5", FormatStatusCell(ForecastStatus.Success), "SOLUSDT", "UP", "Price", "22.06.2026 18:30", "22.06.2026 19:05", "+2.14%", "22.06.2026 18:30", "1", "EN", "22.06.2026 18:29", "×"]
        };

        foreach (var row in rows)
        {
            _forecastGrid.Rows.Add(row);
        }
    }

    private static string FormatStatusCell(string status)
    {
        return $"● {status}";
    }

    private static string NormalizeStatusCell(string status)
    {
        return status.Replace("●", "", StringComparison.Ordinal).Trim();
    }

    private void ValidateForecastInputs()
    {
        if (string.IsNullOrWhiteSpace(_exchangeBox.Text))
        {
            throw new InvalidOperationException("Укажите биржу.");
        }

        if (string.IsNullOrWhiteSpace(_symbolBox.Text))
        {
            throw new InvalidOperationException("Укажите актив.");
        }

        if (string.IsNullOrWhiteSpace(_directionBox.Text))
        {
            throw new InvalidOperationException("Укажите направление.");
        }

        var level = ReadRequiredDecimal(_levelBox.Text, "Level");
        if (level <= 0)
        {
            throw new InvalidOperationException("Level должен быть больше нуля.");
        }

        _ = ReadOptionalDecimal(_targetBox.Text, "TP");
        _ = ReadOptionalDecimal(_stopBox.Text, "SL");
    }

    private decimal ReadRequiredDecimal(string value, string label)
    {
        return ReadOptionalDecimal(value, label)
            ?? throw new InvalidOperationException($"{label}: укажите число.");
    }

    private decimal? ReadOptionalDecimal(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        if (decimal.TryParse(value, NumberStyles.Float, _ruCulture, out var localValue))
        {
            return localValue;
        }

        throw new InvalidOperationException($"{label}: укажите число, например 65000 или 65000,5.");
    }

    private bool TryReadDecimal(string value, out decimal result)
    {
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        return decimal.TryParse(value, NumberStyles.Float, _ruCulture, out result);
    }

    private void SetDefaultCheckDateTime()
    {
        if (_checkDatePicker is null || _checkTimePicker is null)
        {
            return;
        }

        var defaultLocal = DateTime.Now;
        _checkDatePicker.Value = defaultLocal.Date;
        _checkTimePicker.Value = defaultLocal;
    }

    private DateTimeOffset BuildExactLineDateTimeOffset()
    {
        var date = _checkDatePicker.Value.Date;
        var time = _checkTimePicker.Value;
        var lineTime = date.AddHours(time.Hour).AddMinutes(time.Minute);
        return new DateTimeOffset(
            lineTime.Year,
            lineTime.Month,
            lineTime.Day,
            lineTime.Hour,
            lineTime.Minute,
            0,
            TimeSpan.FromMinutes(GetSelectedTimeZoneOffsetMinutes()));
    }

    private void UpdateCheckModeUi()
    {
        if (_barsBox is null || _checkDatePicker is null || _checkTimePicker is null)
        {
            return;
        }

        var isTimeMode = ForecastCheckMode.HasTime(_checkModeBox.Text);
        _barsBox.Enabled = true;
        _checkDatePicker.Enabled = isTimeMode;
        _checkTimePicker.Enabled = isTimeMode;
        _timeZoneBox.Enabled = isTimeMode;
        if (_targetLabel is not null)
        {
            _targetLabel.Text = "TP";
        }

        if (_targetBox is not null)
        {
            _targetBox.PlaceholderText = "";
        }
    }

    private string BuildInitialMessage(ForecastRecord forecast)
    {
        var message = BuildLocalizedInitialMessage(
            forecast,
            string.IsNullOrWhiteSpace(forecast.Language) ? GetSelectedLanguage() : forecast.Language);
        return ApplyTelegramTemplate(_data.Settings.TelegramMessageTemplate, forecast, message);

#pragma warning disable CS0162
        var lines = new List<string>
        {
            "Прогноз",
            $"{forecast.Exchange}:{forecast.Symbol} / {forecast.Timeframe}",
            $"Направление: {forecast.Direction}",
            $"Старт: {FormatPrice(forecast.StartPrice)}",
            $"Проверка: {FormatLocal(forecast.CheckAtUtc)}"
        };

        if (forecast.CheckMode == ForecastCheckMode.Time)
        {
            lines.Add($"Время линии: {FormatLocal(forecast.TimeLineAtUtc ?? forecast.CheckAtUtc)}");
            lines.Add($"Окно реакции: {forecast.CheckAfterBars} баров после свечи линии");
            if (forecast.TakeProfitPercent.HasValue)
            {
                lines.Add($"TP: {forecast.TakeProfitPercent.Value:0.####}% от свечи линии");
                lines.Add($"База TP: {(string.IsNullOrWhiteSpace(forecast.LineBaseMode) ? ForecastLineBaseMode.CandleColor : forecast.LineBaseMode)}");
            }

            lines.Add($"Итоговая проверка: {FormatLocal(forecast.CheckAtUtc)}");
        }
        else
        {
            lines.Add($"Через баров: {forecast.CheckAfterBars}");
        }

        if (forecast.TargetPrice.HasValue)
        {
            lines.Add($"Цель: {FormatPrice(forecast.TargetPrice.Value)}");
        }

        if (forecast.StopPrice.HasValue)
        {
            lines.Add($"Стоп: {FormatPrice(forecast.StopPrice.Value)}");
        }

        if (forecast.AutoExtend)
        {
            lines.Add($"Автопродление: до {forecast.MaxExtensions} раз по {forecast.ExtendBars} баров");
        }

        if (!string.IsNullOrWhiteSpace(forecast.Comment))
        {
            lines.Add("");
            lines.Add(forecast.Comment);
        }

        return string.Join(Environment.NewLine, lines);
    }
#pragma warning restore CS0162

    private string BuildLocalizedInitialMessage(ForecastRecord forecast, string language)
    {
        language = NormalizeLanguage(language);
        var title = language switch
        {
            "EN" => "📊 Trading Signal",
            "DE" => "📊 Trading-Signal",
            _ => "📊 Торговый сигнал"
        };
        var labels = GetTelegramLabels(language);

        var lines = new List<string>
        {
            title,
            "",
            $"💱 {forecast.Exchange}:{forecast.Symbol}  •  {forecast.Timeframe}",
            $"{DirectionIcon(forecast.Direction)} {labels.Direction}: {forecast.Direction}",
            $"⚙️ {labels.Mode}: {forecast.CheckMode}",
            $"📍 {labels.Level}: {FormatPrice(forecast.StartPrice)}",
            $"🛡 {labels.Stop}: {(forecast.StopPrice.HasValue ? FormatPrice(forecast.StopPrice.Value) : "-")}",
            $"🎯 {labels.Target}: {(forecast.TargetPrice.HasValue ? FormatPrice(forecast.TargetPrice.Value) : "-")}"
        };

        if (ForecastCheckMode.HasTime(forecast.CheckMode))
        {
            lines.Add($"🕒 {labels.TimeLine}: {FormatLocal(forecast.TimeLineAtUtc ?? forecast.CheckAtUtc)}");
        }
        else
        {
            lines.Add($"🕒 {labels.Check}: {FormatLocal(forecast.CheckAtUtc)}");
        }

        if (!string.IsNullOrWhiteSpace(forecast.Comment))
        {
            lines.Add("");
            lines.Add(forecast.Comment);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static TelegramLabels GetTelegramLabels(string language)
    {
        return NormalizeLanguage(language) switch
        {
            "EN" => new TelegramLabels(
                Direction: "Direction",
                Mode: "Mode",
                Level: "Entry",
                Stop: "SL",
                Target: "TP",
                Check: "Check",
                TimeLine: "Reaction line",
                Window: "Reaction window",
                Status: "Status",
                Waiting: "Waiting",
                AutoExtend: "Auto extend",
                Comment: "Comment",
                Bars: "bars",
                ResultTitle: "📋 Forecast Result",
                Fact: "Current",
                Change: "Change",
                HighLow: "High / Low",
                Hit: "Hit",
                Result: "Result",
                NextCheck: "Next check",
                Extension: "Extension"),
            "DE" => new TelegramLabels(
                Direction: "Richtung",
                Mode: "Modus",
                Level: "Einstieg",
                Stop: "SL",
                Target: "TP",
                Check: "Prüfung",
                TimeLine: "Reaktionslinie",
                Window: "Reaktionsfenster",
                Status: "Status",
                Waiting: "Wartet",
                AutoExtend: "Auto-Verlängerung",
                Comment: "Kommentar",
                Bars: "Kerzen",
                ResultTitle: "📋 Prognose-Ergebnis",
                Fact: "Aktuell",
                Change: "Änderung",
                HighLow: "High / Low",
                Hit: "Treffer",
                Result: "Ergebnis",
                NextCheck: "Nächste Prüfung",
                Extension: "Verlängerung"),
            _ => new TelegramLabels(
                Direction: "Направление",
                Mode: "Режим",
                Level: "Старт",
                Stop: "SL",
                Target: "TP",
                Check: "Проверка",
                TimeLine: "Линия реакции",
                Window: "Окно реакции",
                Status: "Статус",
                Waiting: "Ожидает",
                AutoExtend: "Автопродление",
                Comment: "Комментарий",
                Bars: "баров",
                ResultTitle: "📋 Результат прогноза",
                Fact: "Факт",
                Change: "Изменение",
                HighLow: "High / Low",
                Hit: "Касание",
                Result: "Итог",
                NextCheck: "Следующая проверка",
                Extension: "Продление")
        };
    }

    private static string DirectionIcon(string direction)
    {
        return direction.Equals("DOWN", StringComparison.OrdinalIgnoreCase)
            ? "🔴 ↓"
            : "🟢 ↑";
    }

    private static string StatusIcon(string status)
    {
        if (status == ForecastStatus.Success)
        {
            return "✅";
        }

        if (status == ForecastStatus.Failed)
        {
            return "❌";
        }

        if (status == ForecastStatus.Extended)
        {
            return "🔁";
        }

        if (status == ForecastStatus.Error)
        {
            return "⚠️";
        }

        if (status == ForecastStatus.Ambiguous)
        {
            return "🟡";
        }

        return "⏳";
    }

    private sealed record TelegramLabels(
        string Direction,
        string Mode,
        string Level,
        string Stop,
        string Target,
        string Check,
        string TimeLine,
        string Window,
        string Status,
        string Waiting,
        string AutoExtend,
        string Comment,
        string Bars,
        string ResultTitle,
        string Fact,
        string Change,
        string HighLow,
        string Hit,
        string Result,
        string NextCheck,
        string Extension);

    private string ApplyTelegramTemplate(string template, ForecastRecord forecast, string fallback)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return fallback;
        }

        var target = forecast.TakeProfitPercent.HasValue
            ? $"{forecast.TakeProfitPercent.Value:0.####}%"
            : forecast.TargetPrice.HasValue ? FormatPrice(forecast.TargetPrice.Value) : "";

        return template
            .Replace("{message}", fallback, StringComparison.OrdinalIgnoreCase)
            .Replace("{exchange}", forecast.Exchange, StringComparison.OrdinalIgnoreCase)
            .Replace("{symbol}", forecast.Symbol, StringComparison.OrdinalIgnoreCase)
            .Replace("{timeframe}", forecast.Timeframe, StringComparison.OrdinalIgnoreCase)
            .Replace("{direction}", forecast.Direction, StringComparison.OrdinalIgnoreCase)
            .Replace("{mode}", forecast.CheckMode, StringComparison.OrdinalIgnoreCase)
            .Replace("{level}", FormatPrice(forecast.StartPrice), StringComparison.OrdinalIgnoreCase)
            .Replace("{sl}", forecast.StopPrice.HasValue ? FormatPrice(forecast.StopPrice.Value) : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{tp}", target, StringComparison.OrdinalIgnoreCase)
            .Replace("{check}", forecast.CheckAfterBars.ToString(_ruCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{status}", forecast.Status, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildResultMessage(ForecastRecord forecast)
    {
        var labels = GetTelegramLabels(forecast.Language);
        var statusIcon = StatusIcon(forecast.Status);
        var changeText = forecast.ChangePercent.HasValue
            ? $"{forecast.ChangePercent.Value:+0.####;-0.####;0}%"
            : "-";

        var lines = new List<string>
        {
            labels.ResultTitle,
            "",
            $"💱 {forecast.Exchange}:{forecast.Symbol}  •  {forecast.Timeframe}",
            $"{DirectionIcon(forecast.Direction)} {labels.Direction}: {forecast.Direction}",
            $"{statusIcon} {labels.Status}: {forecast.Status}",
            $"📍 {labels.Level}: {FormatPrice(forecast.StartPrice)}",
            $"🏁 {labels.Fact}: {(forecast.LastPrice.HasValue ? FormatPrice(forecast.LastPrice.Value) : "-")}",
            $"📈 {labels.Change}: {changeText}"
        };

        if (forecast.TimeLineAtUtc.HasValue)
        {
            lines.Add($"🕒 {labels.TimeLine}: {FormatLocal(forecast.TimeLineAtUtc.Value)}");
            lines.Add($"🕯 {labels.Window}: {forecast.CheckAfterBars} {labels.Bars}");
        }

        if (forecast.TakeProfitPercent.HasValue)
        {
            lines.Add($"🎯 {labels.Target}: {forecast.TakeProfitPercent.Value:0.####}%");
        }

        if (forecast.LineBasePrice.HasValue)
        {
            lines.Add($"🧮 Base: {FormatPrice(forecast.LineBasePrice.Value)} ({forecast.LineBaseCandleKind}, {forecast.LineBaseMode})");
        }

        if (forecast.TargetPrice.HasValue)
        {
            lines.Add($"🎯 {labels.Target}: {FormatPrice(forecast.TargetPrice.Value)}");
        }

        if (forecast.HighestPrice.HasValue || forecast.LowestPrice.HasValue)
        {
            lines.Add($"📊 {labels.HighLow}: {(forecast.HighestPrice.HasValue ? FormatPrice(forecast.HighestPrice.Value) : "-")} / {(forecast.LowestPrice.HasValue ? FormatPrice(forecast.LowestPrice.Value) : "-")}");
        }

        if (forecast.HitAtUtc.HasValue)
        {
            lines.Add($"✅ {labels.Hit}: {FormatLocal(forecast.HitAtUtc.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(forecast.ResultText))
        {
            lines.Add("");
            lines.Add($"📝 {labels.Result}");
            lines.Add(forecast.ResultText);
        }

        if (forecast.Status == ForecastStatus.Extended)
        {
            lines.Add($"🔁 {labels.NextCheck}: {FormatLocal(forecast.CheckAtUtc)}");
            lines.Add($"🔢 {labels.Extension}: {forecast.ExtensionsUsed}/{forecast.MaxExtensions}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void OpenScreenshotsFolder()
    {
        Directory.CreateDirectory(_dataStore.ScreenshotsDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _dataStore.ScreenshotsDirectory,
            UseShellExecute = true
        });
    }

    private void ShowSignalSettingsDialog()
    {
        var theme = ThemeManager.Palette;
        using var dialog = new Form
        {
            Text = "Настройки сигнала",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(470, 360),
            Font = Font,
            BackColor = theme.Card,
            ForeColor = theme.Text
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 8,
            BackColor = theme.Card
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dialog.Controls.Add(layout);

        var themeBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.Input,
            ForeColor = theme.Text
        };
        themeBox.Items.AddRange(new object[] { "Dark", "Light" });
        themeBox.Text = _data.Settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

        var timeZoneBox = CreateTimeZoneComboBox();
        SelectTimeZoneOffset(GetSelectedTimeZoneOffsetMinutes(), timeZoneBox);

        var tokenBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            Text = _telegramTokenBox.Text,
            PlaceholderText = "token от BotFather",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var chatBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = _telegramChatBox.Text,
            PlaceholderText = "chat_id или @channel",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var topicBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = _telegramTopicBox.Text,
            PlaceholderText = "message_thread_id, например 2",
            BackColor = theme.Input,
            ForeColor = theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var sendInitialBox = new CheckBox
        {
            Text = "Отправлять прогноз при фиксации",
            AutoSize = true,
            Checked = _sendInitialBox.Checked,
            BackColor = theme.Card,
            ForeColor = theme.Text
        };
        var sendResultBox = new CheckBox
        {
            Text = "Отправлять результат проверки",
            AutoSize = true,
            Checked = _sendResultBox.Checked,
            BackColor = theme.Card,
            ForeColor = theme.Text
        };

        AddRow(layout, 0, "Тема", themeBox);
        AddRow(layout, 1, "GMT", timeZoneBox);
        AddRow(layout, 2, "Bot Token", tokenBox);
        AddRow(layout, 3, "Chat ID", chatBox);
        AddRow(layout, 4, "Topic ID", topicBox);
        AddFullRow(layout, 5, sendInitialBox);
        AddFullRow(layout, 6, sendResultBox);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        var okButton = new ModernButton
        {
            Text = "Сохранить",
            Width = 110,
            Height = 32,
            Primary = true,
            DialogResult = DialogResult.OK
        };
        var cancelButton = new ModernButton
        {
            Text = "Отмена",
            Width = 90,
            Height = 32,
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        AddFullRow(layout, 7, buttons);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var previousTheme = _data.Settings.Theme;
        _data.Settings.Theme = themeBox.Text;
        SelectTimeZoneOffset(ParseTimeZoneOffsetMinutes(timeZoneBox.Text), _timeZoneBox);
        _telegramTokenBox.Text = tokenBox.Text.Trim();
        _telegramChatBox.Text = chatBox.Text.Trim();
        _telegramTopicBox.Text = topicBox.Text.Trim();
        _sendInitialBox.Checked = sendInitialBox.Checked;
        _sendResultBox.Checked = sendResultBox.Checked;
        SaveSettingsFromControls();
        SetStatus("Настройки сигнала сохранены.");

        if (!previousTheme.Equals(_data.Settings.Theme, StringComparison.OrdinalIgnoreCase))
        {
            BuildUi(preserveWindow: true);
            LoadSettingsIntoControls();
            _ = InitializeWebViewAsync();
        }
    }

    private void SetStatus(string text)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = text;
    }

    private string FormatLocal(DateTime utc)
    {
        return utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", _ruCulture);
    }

    private string FormatPrice(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private void StyleForecastInputs()
    {
        foreach (var control in new Control[]
        {
            _exchangeBox,
            _symbolBox,
            _timeframeBox,
            _checkModeBox,
            _checkDatePicker,
            _checkTimePicker,
        _timeZoneBox,
            _levelBox,
            _targetBox,
            _stopBox,
            _commentBox
        })
        {
            ThemeManager.StyleInput(control);
        }

        _autoExtendBox.ForeColor = ThemeManager.Palette.Text;
        _autoExtendBox.BackColor = ThemeManager.Palette.Card;
        _autoExtendBox.Font = UiFonts.Body;
    }

    private FlowLayoutPanel CreateStatusStrip()
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = ThemeManager.Palette.Card
        };
        AddStatusBadge(strip, "Ожидает", ThemeManager.Palette.MutedText);
        AddStatusBadge(strip, "Сработал", ThemeManager.Palette.Success);
        AddStatusBadge(strip, "Не сработал", ThemeManager.Palette.Down);
        AddStatusBadge(strip, "Продлен", ThemeManager.Palette.Accent);
        return strip;
    }

    private static void AddStatusBadge(Control parent, string text, Color color)
    {
        parent.Controls.Add(new BadgeLabel
        {
            Text = text,
            Width = 78,
            BadgeBackColor = ControlPaint.Dark(ThemeManager.Palette.CardAlt),
            BadgeForeColor = color,
            Margin = new Padding(0, 2, 6, 4)
        });
    }

    private static void AddFilterButton(FlowLayoutPanel toolbar, string text, bool selected)
    {
        toolbar.Controls.Add(new ModernButton
        {
            Text = text,
            Primary = selected,
            Width = text.Length > 9 ? 112 : 86,
            Height = 26,
            Font = UiFonts.Small,
            Margin = new Padding(0, 1, 6, 0)
        });
    }

    private static (Color Back, Color Fore) GetStatusColors(string status)
    {
        var theme = ThemeManager.Palette;
        if (status == ForecastStatus.Success)
        {
            return (Color.FromArgb(15, 68, 50), theme.Success);
        }

        if (status == ForecastStatus.Failed)
        {
            return (Color.FromArgb(76, 30, 36), theme.Down);
        }

        if (status == ForecastStatus.Extended)
        {
            return (Color.FromArgb(25, 49, 95), theme.Accent);
        }

        if (status == ForecastStatus.Error || status == ForecastStatus.Ambiguous)
        {
            return (Color.FromArgb(78, 55, 18), theme.Warning);
        }

        return (Color.FromArgb(22, 31, 44), theme.Warning);
    }

    private static ComboBox CreateComboBox(params string[] values)
    {
        var box = new ModernComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = ThemeManager.Palette.Input,
            ForeColor = ThemeManager.Palette.Text,
            Font = UiFonts.Body
        };
        box.Items.AddRange(values.Cast<object>().ToArray());
        if (values.Length > 0)
        {
            box.Text = values[0];
        }

        return box;
    }

    private static TextBox CreateReadOnlyTextBox(string value)
    {
        return new TextBox
        {
            Text = value,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = ThemeManager.Palette.Input,
            ForeColor = ThemeManager.Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            TabStop = false,
            Font = UiFonts.Mono
        };
    }

    private ComboBox CreateTimeZoneComboBox()
    {
        var box = new ModernComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ThemeManager.Palette.Input,
            ForeColor = ThemeManager.Palette.Text,
            Font = UiFonts.Body
        };

        for (var hours = -12; hours <= 14; hours++)
        {
            box.Items.Add(FormatTimeZoneOffset(hours * 60));
        }

        SelectTimeZoneOffset((int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes, box);
        return box;
    }

    private int GetSelectedTimeZoneOffsetMinutes()
    {
        return ParseTimeZoneOffsetMinutes(_timeZoneBox?.Text);
    }

    private static int ParseTimeZoneOffsetMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var signIndex = value.IndexOf('+');
        var sign = 1;
        if (signIndex < 0)
        {
            signIndex = value.IndexOf('-');
            sign = -1;
        }

        if (signIndex < 0)
        {
            return 0;
        }

        var parts = value[(signIndex + 1)..].Split(':', 2);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var hours)
            && int.TryParse(parts[1], out var minutes))
        {
            return sign * (hours * 60 + minutes);
        }

        return 0;
    }

    private void SelectTimeZoneOffset(int offsetMinutes)
    {
        SelectTimeZoneOffset(offsetMinutes, _timeZoneBox);
    }

    private static void SelectTimeZoneOffset(int offsetMinutes, ComboBox? box)
    {
        if (box is null)
        {
            return;
        }

        var value = FormatTimeZoneOffset(offsetMinutes);
        if (!box.Items.Contains(value))
        {
            box.Items.Add(value);
        }

        box.Text = value;
    }

    private static string FormatTimeZoneOffset(int offsetMinutes)
    {
        var sign = offsetMinutes >= 0 ? "+" : "-";
        var absolute = Math.Abs(offsetMinutes);
        return $"GMT{sign}{absolute / 60:00}:{absolute % 60:00}";
    }

    private static ModernNumberBox CreateNumber(decimal value, decimal min, decimal max)
    {
        return new ModernNumberBox
        {
            Dock = DockStyle.Left,
            Width = 92,
            Minimum = min,
            Maximum = max,
            Value = Math.Min(Math.Max(value, min), max),
            Font = UiFonts.Body
        };
    }

    private static FlowLayoutPanel BuildInlinePanel(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static TableLayoutPanel BuildSplitPanel(Control left, Control right, float leftPercent, float rightPercent)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Height = 26,
            MinimumSize = new Size(0, 26),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, rightPercent));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        left.Margin = new Padding(0, 0, 4, 0);
        right.Margin = new Padding(4, 0, 0, 0);
        panel.Controls.Add(left, 0, 0);
        panel.Controls.Add(right, 1, 0);
        return panel;
    }

    private TableLayoutPanel CreateSection(string title)
    {
        var theme = ThemeManager.Palette;
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = theme.Card,
            Padding = new Padding(9, 7, 9, 8),
            Margin = new Padding(0, 0, 0, 7),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = title,
            AutoSize = true,
            Font = UiFonts.Title,
            ForeColor = theme.Text,
            Margin = new Padding(0, 0, 0, 4)
        };
        AddFullRow(section, 0, header);
        return section;
    }

    private static Label AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var labelControl = new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 3),
            ForeColor = ThemeManager.Palette.MutedText,
            Font = UiFonts.Small
        };
        layout.Controls.Add(labelControl, 0, row);
        control.Margin = new Padding(0, 2, 0, 4);
        layout.Controls.Add(control, 1, row);
        return labelControl;
    }

    private static void AddFullRow(TableLayoutPanel layout, int row, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 4, 0, 4);
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 2);
    }
}

