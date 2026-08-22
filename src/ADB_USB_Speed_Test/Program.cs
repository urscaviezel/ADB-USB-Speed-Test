using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AdbUsbSpeedTest;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class AppSettings
{
    public string Language { get; set; } = "Deutsch";
    public int Port { get; set; } = 5001;
    public string Mode { get; set; } = "max";
    public double RateMbps { get; set; } = 1900;
    public double DurationSeconds { get; set; } = 600;
}

internal sealed class ConnectionLostException : Exception
{
    public double AverageMbps { get; init; }
    public double Elapsed { get; init; }
    public long Sent { get; init; }

    public ConnectionLostException(string message) : base(message) { }
}

public sealed class MainForm : Form
{
    private const int ChunkSize = 1024 * 1024;
    private const int MaxRetries = 8;
    private const double BackoffFactor = 0.90;
    private const double MinRetryMbps = 50.0;

    private readonly string _adbPath;
    private readonly string _settingsPath;

    private readonly ComboBox _language = new();
    private readonly TextBox _port = new();
    private readonly RadioButton _limited = new();
    private readonly RadioButton _max = new();
    private readonly TextBox _rate = new();
    private readonly TextBox _duration = new();

    private readonly Button _check = new();
    private readonly Button _start = new();
    private readonly Button _cancel = new();

    private readonly Label _hmdValue = new();
    private readonly Label _statusValue = new();
    private readonly Label _attemptValue = new();
    private readonly Label _targetValue = new();
    private readonly Label _elapsedValue = new();
    private readonly Label _remainingValue = new();
    private readonly Label _sentValue = new();
    private readonly Label _currentValue = new();
    private readonly Label _averageValue = new();
    private readonly Label _lastDisconnectValue = new();

    private readonly ProgressBar _progress = new();
    private readonly Label _info = new();

    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private TcpClient? _client;
    private Process? _ncProcess;

    private AppSettings _settings = new();

    public MainForm()
    {
        Text = "ADB USB Speed Test";

        // Use the icon embedded in the final EXE for the WinForms window too.
        // This makes the same icon appear in the title bar, taskbar and Alt+Tab.
        try
        {
            Icon? appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon is not null)
                Icon = appIcon;
        }
        catch
        {
            // The application still works if Windows cannot extract the icon.
        }

        ShowIcon = true;
        ShowInTaskbar = true;

        Width = 920;
        Height = 940;
        MinimumSize = new Size(820, 780);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScroll = true;

        string appDir = AppContext.BaseDirectory;
        _adbPath = Path.Combine(appDir, "adb", "adb.exe");

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string settingsDir = Path.Combine(local, "ADB_USB_Speed_Test");
        Directory.CreateDirectory(settingsDir);
        _settingsPath = Path.Combine(settingsDir, "settings.json");

        LoadSettings();
        _settings.Mode = "max"; // v1.3 startup default: always start in Max-Speed mode
        BuildUi();
        ApplySettingsToUi();
        ApplyLanguage();

        FormClosing += async (_, _) =>
        {
            try
            {
                _cts?.Cancel();
                CleanupConnection();
                await KillAdbServerAsync();
            }
            catch { }
        };
    }

    private bool IsEnglish => _language.SelectedItem?.ToString() == "English";

    private string T(string de, string en) => IsEnglish ? en : de;

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                _settings = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(_settingsPath)
                ) ?? new AppSettings();

                // v1.1: the former default test duration was 120 seconds.
                // Move existing installations that still have that old
                // default to the new 600-second default.
                if (Math.Abs(_settings.DurationSeconds - 120.0) < 0.001)
                    _settings.DurationSeconds = 600;
            }
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Language = _language.SelectedItem?.ToString() ?? "Deutsch";
            _settings.Port = int.TryParse(_port.Text, out int port) ? port : 5001;
            _settings.Mode = _max.Checked ? "max" : "limited";
            _settings.RateMbps = ParseDouble(_rate.Text, 1900);
            _settings.DurationSeconds = ParseDouble(_duration.Text, 600);

            File.WriteAllText(
                _settingsPath,
                JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        catch { }
    }

    private void ApplySettingsToUi()
    {
        _language.Items.AddRange(new object[] { "Deutsch", "English" });
        _language.SelectedItem = _settings.Language is "English" ? "English" : "Deutsch";

        _port.Text = _settings.Port.ToString();
        _rate.Text = _settings.RateMbps.ToString("0.###");
        _duration.Text = _settings.DurationSeconds.ToString("0.###");

        // Always start in Max-Speed mode.
        // Setting the language above can fire SelectedIndexChanged and save
        // the radio-button state before the controls are initialized, so we
        // force the desired startup state here, after all fields are loaded.
        _limited.Checked = false;
        _max.Checked = true;
        _settings.Mode = "max";

        UpdateModeState();
        SaveSettings();
    }

    private void BuildUi()
    {
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(14),
            RowCount = 4,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var settingsGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12)
        };
        settingsGroup.Tag = "settingsGroup";
        root.Controls.Add(settingsGroup, 0, 0);

        var settingsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 5
        };
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsGroup.Controls.Add(settingsGrid);

        settingsGrid.Controls.Add(MakeLabel("language"), 0, 0);
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Width = 145;
        _language.SelectedIndexChanged += (_, _) =>
        {
            ApplyLanguage();
            SaveSettings();
        };
        settingsGrid.Controls.Add(_language, 1, 0);

        settingsGrid.Controls.Add(MakeLabel("port"), 0, 1);
        _port.Width = 110;
        settingsGrid.Controls.Add(_port, 1, 1);

        var modePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _limited.Tag = "limited";
        _max.Tag = "max";
        _limited.AutoSize = true;
        _max.AutoSize = true;
        _limited.CheckedChanged += (_, _) => { UpdateModeState(); SaveSettings(); };
        _max.CheckedChanged += (_, _) => { UpdateModeState(); SaveSettings(); };
        modePanel.Controls.Add(_limited);
        modePanel.Controls.Add(_max);
        settingsGrid.Controls.Add(modePanel, 0, 2);
        settingsGrid.SetColumnSpan(modePanel, 3);

        settingsGrid.Controls.Add(MakeLabel("rate"), 0, 3);
        var ratePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _rate.Width = 110;
        ratePanel.Controls.Add(_rate);
        ratePanel.Controls.Add(new Label { Text = "Mbit/s", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        settingsGrid.Controls.Add(ratePanel, 1, 3);

        settingsGrid.Controls.Add(MakeLabel("duration"), 0, 4);
        var durationPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _duration.Width = 110;
        durationPanel.Controls.Add(_duration);
        var secondsLabel = new Label { AutoSize = true, Margin = new Padding(6, 6, 0, 0), Tag = "secondsPerAttempt" };
        durationPanel.Controls.Add(secondsLabel);
        settingsGrid.Controls.Add(durationPanel, 1, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 12)
        };
        root.Controls.Add(buttons, 0, 1);

        _check.Tag = "check";
        _start.Tag = "start";
        _cancel.Tag = "cancel";

        _check.AutoSize = true;
        _start.AutoSize = true;
        _cancel.AutoSize = true;
        _cancel.Enabled = false;

        _check.Click += async (_, _) => await CheckHmdAsync();
        _start.Click += async (_, _) => await StartTestAsync();
        _cancel.Click += (_, _) => CancelTest();

        buttons.Controls.Add(_check);
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_cancel);

        var statusGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Tag = "statusGroup"
        };
        root.Controls.Add(statusGroup, 0, 2);

        var statusGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 11
        };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusGroup.Controls.Add(statusGrid);

        AddStatusRow(statusGrid, 0, "hmd", _hmdValue);
        AddStatusRow(statusGrid, 1, "status", _statusValue);
        AddStatusRow(statusGrid, 2, "attempt", _attemptValue);
        AddStatusRow(statusGrid, 3, "target", _targetValue);
        AddStatusRow(statusGrid, 4, "elapsed", _elapsedValue);
        AddStatusRow(statusGrid, 5, "remaining", _remainingValue);
        AddStatusRow(statusGrid, 6, "sent", _sentValue);
        AddStatusRow(statusGrid, 7, "current", _currentValue);
        AddStatusRow(statusGrid, 8, "average", _averageValue);
        AddStatusRow(statusGrid, 9, "lastDisconnect", _lastDisconnectValue);

        _progress.Dock = DockStyle.Fill;
        _progress.Height = 24;
        _progress.Minimum = 0;
        _progress.Maximum = 1000;
        statusGrid.Controls.Add(_progress, 0, 10);
        statusGrid.SetColumnSpan(_progress, 2);

        _info.Dock = DockStyle.Fill;
        _info.AutoSize = true;
        _info.MaximumSize = new Size(860, 0);
        root.Controls.Add(_info, 0, 3);

        ResetStats(600);
    }

    private Label MakeLabel(string key) =>
        new()
        {
            AutoSize = true,
            Margin = new Padding(0, 6, 14, 6),
            Tag = key
        };

    private void AddStatusRow(TableLayoutPanel grid, int row, string key, Label value)
    {
        var caption = MakeLabel(key);
        value.AutoSize = true;
        value.Margin = new Padding(10, 6, 0, 6);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private void ApplyLanguage()
    {
        foreach (Control control in EnumerateControls(this))
        {
            if (control.Tag is not string tag)
                continue;

            control.Text = tag switch
            {
                "settingsGroup" => T("Verbindung / Testeinstellungen", "Connection / Test settings"),
                "language" => T("Sprache:", "Language:"),
                "port" => T("TCP-Port:", "TCP port:"),
                "limited" => T("Bandbreite begrenzen", "Limit bandwidth"),
                "max" => T("Maximale stabile Transferrate ermitteln", "Determine maximum stable transfer rate"),
                "rate" => T("Zielrate:", "Target rate:"),
                "duration" => T("Testdauer:", "Test duration:"),
                "secondsPerAttempt" => T("Sekunden pro Versuch", "seconds per attempt"),
                "check" => T("HMD prüfen", "Check HMD"),
                "start" => T("Start", "Start"),
                "cancel" => T("Abbrechen", "Cancel"),
                "statusGroup" => T("Status", "Status"),
                "hmd" => "HMD:",
                "status" => T("Status:", "Status:"),
                "attempt" => T("Versuch:", "Attempt:"),
                "target" => T("Aktuelles Ziel:", "Current target:"),
                "elapsed" => T("Laufzeit:", "Elapsed:"),
                "remaining" => T("Verbleibend:", "Remaining:"),
                "sent" => T("Gesendet:", "Transferred:"),
                "current" => T("Aktuell:", "Current:"),
                "average" => T("Durchschnitt:", "Average:"),
                "lastDisconnect" => T("Letzter Abbruch:", "Last disconnect:"),
                _ => control.Text
            };
        }

        if (_hmdValue.Text.StartsWith("Verbunden:") || _hmdValue.Text.StartsWith("Connected:"))
        {
            string model = _hmdValue.Text[( _hmdValue.Text.IndexOf(':') + 1 )..].Trim();
            _hmdValue.Text = $"{T("Verbunden", "Connected")}: {model}";
        }

        _info.Text = T(
            "Max-Speed-Modus: Der erste Versuch läuft ohne Limit. Wird die Verbindung getrennt, baut das Tool ADB/Netcat automatisch neu auf und reduziert die Datenrate schrittweise, bis ein kompletter Test stabil durchläuft.",
            "Max-Speed mode: The first attempt runs without a bandwidth limit. If the connection is lost, the tool automatically rebuilds ADB/Netcat and gradually reduces the transfer rate until a complete test runs stably."
        );
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private void UpdateModeState()
    {
        _rate.Enabled = !_max.Checked;
    }

    private bool ValidateInputs(out int port, out double duration, out double rate)
    {
        port = 0;
        duration = 0;
        rate = 0;

        if (!File.Exists(_adbPath))
        {
            MessageBox.Show(
                T(
                    "Die integrierte adb.exe wurde nicht gefunden. Der Ordner 'adb' muss neben der Anwendung liegen.",
                    "The bundled adb.exe was not found. The 'adb' folder must be located next to the application."
                ),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return false;
        }

        if (!int.TryParse(_port.Text, out port) || port is < 1 or > 65535)
        {
            MessageBox.Show(T("Ungültiger TCP-Port.", "Invalid TCP port."), Text);
            return false;
        }

        duration = ParseDouble(_duration.Text, -1);
        if (duration <= 0)
        {
            MessageBox.Show(T("Ungültige Testdauer.", "Invalid test duration."), Text);
            return false;
        }

        if (_limited.Checked)
        {
            rate = ParseDouble(_rate.Text, -1);
            if (rate <= 0)
            {
                MessageBox.Show(T("Ungültige Zielrate.", "Invalid target rate."), Text);
                return false;
            }
        }

        SaveSettings();
        return true;
    }

    private static double ParseDouble(string text, double fallback)
    {
        string normalized = text.Replace(',', '.');
        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value
        ) ? value : fallback;
    }

    private async Task CheckHmdAsync()
    {
        if (!ValidateInputs(out _, out _, out _))
            return;

        SetBusy(true);
        _hmdValue.Text = T("Prüfe...", "Checking...");

        try
        {
            string model = await GetModelAsync(CancellationToken.None);
            _hmdValue.Text = $"{T("Verbunden", "Connected")}: {model}";
            _statusValue.Text = T("Bereit", "Ready");
        }
        catch (Exception ex)
        {
            _hmdValue.Text = T($"Nicht verbunden: {ex.Message}", $"Not connected: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StartTestAsync()
    {
        if (_cts is not null)
            return;

        if (!ValidateInputs(out int port, out double duration, out double rate))
            return;

        _cts = new CancellationTokenSource();
        SetBusy(true, running: true);
        ResetStats(duration);

        try
        {
            string model = await GetModelAsync(_cts.Token);
            _hmdValue.Text = $"{T("Verbunden", "Connected")}: {model}";

            if (_max.Checked)
                await RunMaxModeAsync(port, duration, _cts.Token);
            else
                await RunLimitedModeAsync(port, duration, rate, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _statusValue.Text = T("Abgebrochen", "Cancelled");
        }
        catch (Exception ex)
        {
            _statusValue.Text = T($"Fehler: {ex.Message}", $"Error: {ex.Message}");
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            CleanupConnection();
            await KillAdbServerAsync();
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private async Task RunLimitedModeAsync(int port, double duration, double rate, CancellationToken token)
    {
        _attemptValue.Text = "1";
        _targetValue.Text = $"{rate:0.0} Mbit/s";

        TestResult result = await RunSingleAttemptAsync(port, duration, rate, 1, token);

        _statusValue.Text = T(
            $"Fertig – Durchschnitt: {result.AverageMbps:0.0} Mbit/s",
            $"Finished – average: {result.AverageMbps:0.0} Mbit/s"
        );
    }

    private async Task RunMaxModeAsync(int port, double duration, CancellationToken token)
    {
        double? target = null;
        double lastFailed = 0;

        for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            token.ThrowIfCancellationRequested();

            _attemptValue.Text = attempt.ToString();

            if (target is null)
            {
                _targetValue.Text = T("Unbegrenzt", "Unlimited");
                _statusValue.Text = T(
                    $"Versuch {attempt}: ermittle maximale Rohgeschwindigkeit...",
                    $"Attempt {attempt}: determining maximum raw speed..."
                );
            }
            else
            {
                _targetValue.Text = $"{target.Value:0.0} Mbit/s";
                _statusValue.Text = T(
                    $"Versuch {attempt}: Stabilitätstest bei {target.Value:0.0} Mbit/s...",
                    $"Attempt {attempt}: stability test at {target.Value:0.0} Mbit/s..."
                );
            }

            try
            {
                TestResult result = await RunSingleAttemptAsync(port, duration, target, attempt, token);
                _progress.Value = 1000;

                _statusValue.Text = T(
                    $"Fertig – stabile Transferrate: {result.AverageMbps:0.0} Mbit/s",
                    $"Finished – stable transfer rate: {result.AverageMbps:0.0} Mbit/s"
                );
                return;
            }
            catch (ConnectionLostException ex)
            {
                lastFailed = ex.AverageMbps;
                _lastDisconnectValue.Text = T(
                    $"{ex.AverageMbps:0.0} Mbit/s nach {ex.Elapsed:0.0} s",
                    $"{ex.AverageMbps:0.0} Mbit/s after {ex.Elapsed:0.0} s"
                );

                if (ex.AverageMbps <= 0)
                    throw new Exception(T(
                        "Die Verbindung wurde getrennt, bevor eine brauchbare Geschwindigkeit gemessen werden konnte.",
                        "The connection was lost before a usable speed could be measured."
                    ));

                target = target is null
                    ? ex.AverageMbps * BackoffFactor
                    : Math.Min(target.Value * BackoffFactor, ex.AverageMbps * BackoffFactor);

                target = Math.Max(target.Value, MinRetryMbps);

                if (attempt > MaxRetries)
                {
                    throw new Exception(T(
                        $"Auch nach mehreren automatischen Reduzierungen konnte keine stabile Transferrate gefunden werden. Letzter Abbruch bei durchschnittlich {lastFailed:0.0} Mbit/s.",
                        $"No stable transfer rate could be found after several automatic reductions. Last disconnect at an average of {lastFailed:0.0} Mbit/s."
                    ));
                }

                _statusValue.Text = T(
                    $"Verbindung bei {ex.AverageMbps:0.0} Mbit/s abgebrochen. Neuer Versuch mit {target.Value:0.0} Mbit/s...",
                    $"Connection lost at {ex.AverageMbps:0.0} Mbit/s. Retrying at {target.Value:0.0} Mbit/s..."
                );

                CleanupConnection();
                await EnsureAdbReadyAsync(token, 20);
                await Task.Delay(500, token);
            }
        }
    }

    private async Task<TestResult> RunSingleAttemptAsync(
        int port,
        double duration,
        double? targetMbps,
        int attemptNo,
        CancellationToken token)
    {
        TcpClient client = await PrepareConnectionAsync(port, token);

        byte[] payload = new byte[ChunkSize];
        Array.Fill(payload, (byte)'X');

        long sent = 0;
        double? rateBytes = targetMbps is null ? null : targetMbps.Value * 1_000_000.0 / 8.0;

        Stopwatch sw = Stopwatch.StartNew();
        double lastUiTime = 0;
        long lastUiBytes = 0;

        _progress.Value = 0;

        _statusValue.Text = targetMbps is null
            ? T($"Versuch {attemptNo}: sende ohne Bandbreitenlimit...", $"Attempt {attemptNo}: sending without bandwidth limit...")
            : T($"Versuch {attemptNo}: teste {targetMbps.Value:0.0} Mbit/s...", $"Attempt {attemptNo}: testing {targetMbps.Value:0.0} Mbit/s...");

        Socket socket = client.Client;

        while (sw.Elapsed.TotalSeconds < duration)
        {
            token.ThrowIfCancellationRequested();

            double elapsed = sw.Elapsed.TotalSeconds;

            if (rateBytes is not null)
            {
                double targetBytes = elapsed * rateBytes.Value;
                if (sent > targetBytes)
                {
                    double waitSec = Math.Min((sent - targetBytes) / rateBytes.Value, 0.01);
                    if (waitSec > 0)
                        await Task.Delay(TimeSpan.FromSeconds(waitSec), token);

                    PublishStats(sw.Elapsed.TotalSeconds, duration, sent, 0, ref lastUiTime, ref lastUiBytes);
                    continue;
                }
            }

            try
            {
                int n = socket.Send(payload, 0, payload.Length, SocketFlags.None);

                if (n == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                sent += n;
            }
            catch (SocketException ex)
            {
                double e = Math.Max(sw.Elapsed.TotalSeconds, 0.000001);
                double avg = sent * 8.0 / e / 1_000_000.0;
                ForceStats(e, duration, sent, 0);

                throw new ConnectionLostException(ex.Message)
                {
                    AverageMbps = avg,
                    Elapsed = e,
                    Sent = sent
                };
            }

            double now = sw.Elapsed.TotalSeconds;
            if (now - lastUiTime >= 0.25)
            {
                double interval = now - lastUiTime;
                long intervalBytes = sent - lastUiBytes;
                double current = interval > 0 ? intervalBytes * 8.0 / interval / 1_000_000.0 : 0;

                ForceStats(now, duration, sent, current);
                lastUiTime = now;
                lastUiBytes = sent;
            }
        }

        double finalElapsed = Math.Max(sw.Elapsed.TotalSeconds, 0.000001);
        double average = sent * 8.0 / finalElapsed / 1_000_000.0;
        ForceStats(finalElapsed, duration, sent, average);

        return new TestResult(finalElapsed, sent, average);
    }

    private void PublishStats(
        double elapsed,
        double duration,
        long sent,
        double current,
        ref double lastUiTime,
        ref long lastUiBytes)
    {
        if (elapsed - lastUiTime < 0.25)
            return;

        double interval = elapsed - lastUiTime;
        long intervalBytes = sent - lastUiBytes;
        double cur = interval > 0 ? intervalBytes * 8.0 / interval / 1_000_000.0 : current;

        ForceStats(elapsed, duration, sent, cur);
        lastUiTime = elapsed;
        lastUiBytes = sent;
    }

    private void ForceStats(double elapsed, double duration, long sent, double current)
    {
        double remaining = Math.Max(duration - elapsed, 0);
        double average = elapsed > 0 ? sent * 8.0 / elapsed / 1_000_000.0 : 0;
        double gib = sent / Math.Pow(1024, 3);
        int progress = duration > 0 ? (int)Math.Clamp(elapsed / duration * 1000.0, 0, 1000) : 0;

        _elapsedValue.Text = $"{elapsed:0.0} s";
        _remainingValue.Text = $"{remaining:0.0} s";
        _sentValue.Text = $"{gib:0.00} GiB";
        _currentValue.Text = $"{current:0.0} Mbit/s";
        _averageValue.Text = $"{average:0.0} Mbit/s";
        _progress.Value = progress;

        Application.DoEvents();
    }

    private async Task<TcpClient> PrepareConnectionAsync(int port, CancellationToken token)
    {
        CleanupConnection();

        _statusValue.Text = T("Richte ADB-Reverse ein...", "Setting up ADB reverse...");
        await SetupReverseAsync(port, token);

        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        _statusValue.Text = T("Starte Empfänger auf dem HMD...", "Starting receiver on HMD...");

        _ncProcess = StartAdbProcess(
            "shell",
            $"toybox nc 127.0.0.1 {port} > /dev/null"
        );

        _statusValue.Text = T("Warte auf HMD-Verbindung...", "Waiting for HMD connection...");

        Task<TcpClient> accept = _listener.AcceptTcpClientAsync(token).AsTask();
        Task timeout = Task.Delay(TimeSpan.FromSeconds(10), token);

        Task finished = await Task.WhenAny(accept, timeout);
        if (finished != accept)
            throw new TimeoutException(T(
                "Timeout: HMD hat keine Testverbindung aufgebaut.",
                "Timeout: HMD did not establish the test connection."
            ));

        _client = await accept;
        _client.NoDelay = true;
        _client.Client.SendBufferSize = 4 * 1024 * 1024;

        return _client;
    }

    private async Task<string> GetModelAsync(CancellationToken token)
    {
        await EnsureAdbReadyAsync(token, 10);

        var result = await RunAdbCaptureAsync(
            token,
            "shell", "getprop", "ro.product.model"
        );

        if (result.ExitCode != 0)
            throw new Exception(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);

        return string.IsNullOrWhiteSpace(result.Output) ? "Android" : result.Output.Trim();
    }

    private async Task EnsureAdbReadyAsync(CancellationToken token, int timeoutSeconds)
    {
        _statusValue.Text = T(
            "ADB-Verbindung wird wiederhergestellt...",
            "Recovering ADB connection..."
        );

        try { await RunAdbCaptureAsync(CancellationToken.None, "start-server"); }
        catch { }

        Stopwatch sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var state = await RunAdbCaptureAsync(token, "get-state");
                if (state.ExitCode == 0 && state.Output.Trim() == "device")
                {
                    await Task.Delay(400, token);

                    var verify = await RunAdbCaptureAsync(token, "get-state");
                    if (verify.ExitCode == 0 && verify.Output.Trim() == "device")
                        return;
                }

                try { await RunAdbCaptureAsync(token, "reconnect"); }
                catch { }
            }
            catch { }

            await Task.Delay(500, token);
        }

        throw new Exception(T(
            "ADB-Gerät wurde nicht wieder online.",
            "ADB device did not come back online."
        ));
    }

    private async Task SetupReverseAsync(int port, CancellationToken token)
    {
        await EnsureAdbReadyAsync(token, 20);

        try { await RunAdbCaptureAsync(CancellationToken.None, "reverse", "--remove", $"tcp:{port}"); }
        catch { }

        var result = await RunAdbCaptureAsync(
            token,
            "reverse",
            $"tcp:{port}",
            $"tcp:{port}"
        );

        if (result.ExitCode != 0)
            throw new Exception(T(
                $"ADB reverse fehlgeschlagen: {result.Error}",
                $"ADB reverse failed: {result.Error}"
            ));
    }

    private Process StartAdbProcess(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_adbPath)!
        };

        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        return Process.Start(psi) ?? throw new Exception("adb.exe konnte nicht gestartet werden.");
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAdbCaptureAsync(
        CancellationToken token,
        params string[] args)
    {
        using Process p = StartAdbProcess(args);

        string stdout = await p.StandardOutput.ReadToEndAsync(token);
        string stderr = await p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);

        return (p.ExitCode, stdout, stderr);
    }

    private async Task KillAdbServerAsync()
    {
        if (!File.Exists(_adbPath))
            return;

        try
        {
            using Process p = StartAdbProcess("kill-server");
            await p.WaitForExitAsync();
        }
        catch { }
    }

    private void CancelTest()
    {
        _cts?.Cancel();
        _statusValue.Text = T("Breche Test ab...", "Cancelling test...");
        CleanupConnection();

        _ = KillAdbServerAsync();
    }

    private void CleanupConnection()
    {
        try { _client?.Close(); } catch { }
        try { _listener?.Stop(); } catch { }

        _client = null;
        _listener = null;

        try
        {
            if (_ncProcess is { HasExited: false })
            {
                _ncProcess.Kill(entireProcessTree: true);
                _ncProcess.WaitForExit(1000);
            }
        }
        catch { }

        _ncProcess?.Dispose();
        _ncProcess = null;
    }

    private void ResetStats(double duration)
    {
        _attemptValue.Text = "-";
        _targetValue.Text = "-";
        _elapsedValue.Text = "0.0 s";
        _remainingValue.Text = $"{duration:0.0} s";
        _sentValue.Text = "0.00 GiB";
        _currentValue.Text = "0.0 Mbit/s";
        _averageValue.Text = "0.0 Mbit/s";
        _lastDisconnectValue.Text = "-";
        _progress.Value = 0;
        _statusValue.Text = T("Bereit", "Ready");
        _hmdValue.Text = T("Nicht geprüft", "Not checked");
    }

    private void SetBusy(bool busy, bool running = false)
    {
        _check.Enabled = !busy;
        _start.Enabled = !busy;
        _cancel.Enabled = busy && running;
        _language.Enabled = !busy;
    }

    private readonly record struct TestResult(double Elapsed, long Sent, double AverageMbps);
}
