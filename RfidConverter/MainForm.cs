using System.IO.Ports;
using System.Text;

namespace RfidConverter;

/// <summary>
/// Conversie ID brut (reader 125 kHz) -&gt; format "FFF,CCCCC".
/// Structura pe 32 biti: bitii 16..23 = facility, bitii 0..15 = card.
/// Ex: 4217212212 (0xFB5D8D34) -&gt; facility 0x5D=93, card 0x8D34=36148.
/// </summary>
static class RfidConvert
{
    public static (int Facility, int Card, string Formatted, string Hex) Convert(long value)
    {
        int facility = (int)((value >> 16) & 0xFF);
        int card = (int)(value & 0xFFFF);
        return (facility, card, $"{facility:D3},{card}", $"0x{value:X8}");
    }

    public static string DigitsOnly(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}

sealed class MainForm : Form
{
    private readonly TextBox txtInput = new();
    private readonly CheckBox chkClear = new();
    private readonly Label lblResult = new();
    private readonly Label lblDetail = new();
    private readonly ComboBox cmbPort = new();
    private readonly ComboBox cmbBaud = new();
    private readonly Button btnConnect = new();
    private readonly Label lblStatus = new();
    private readonly ListBox lstHistory = new();
    private readonly Label lblCount = new();
    private readonly System.Windows.Forms.Timer timer = new();

    private SerialPort? serial;
    private string serBuffer = "";
    private string lastFormatted = "";

    // Istoric structurat pentru export CSV: data | numar_125 (ID brut) | numar_13_5 (cod FFF,CCCCC)
    private sealed class ScanRecord
    {
        public DateTime Time;
        public long Raw125;
        public string Code = "";
    }
    private readonly List<ScanRecord> records = new();

    public MainForm()
    {
        Text = "RFID Converter  -  125 kHz  ->  format FFF,CCCCC";
        ClientSize = new Size(584, 602);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        // --- Grup citire manuala / HID -------------------------------------
        var grpManual = new GroupBox
        {
            Text = "Citire manuala / reader HID (tasteaza automat)",
            Location = new Point(12, 12),
            Size = new Size(560, 210),
        };

        var lblIn = new Label { Text = "ID brut:", Location = new Point(12, 28), Size = new Size(60, 23) };

        txtInput.Location = new Point(70, 25);
        txtInput.Size = new Size(330, 23);
        txtInput.Font = new Font("Consolas", 11);
        txtInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ProcessRawInput(txtInput.Text);
            }
        };

        var btnConvert = new Button
        {
            Text = "Converteste",
            Location = new Point(410, 23),
            Size = new Size(135, 27),
        };
        btnConvert.Click += (s, e) => ProcessRawInput(txtInput.Text);
        AcceptButton = btnConvert;

        chkClear.Text = "Goleste campul dupa conversie (recomandat pt. scanari repetate)";
        chkClear.Location = new Point(12, 55);
        chkClear.Size = new Size(530, 23);
        chkClear.Checked = true;

        lblResult.Text = "---, -----";
        lblResult.Location = new Point(12, 85);
        lblResult.Size = new Size(533, 55);
        lblResult.Font = new Font("Consolas", 28, FontStyle.Bold);
        lblResult.ForeColor = Color.DarkGreen;
        lblResult.TextAlign = ContentAlignment.MiddleCenter;
        lblResult.BorderStyle = BorderStyle.FixedSingle;

        lblDetail.Text = "Hex: -  |  Facility: -  |  Card: -";
        lblDetail.Location = new Point(12, 148);
        lblDetail.Size = new Size(400, 23);
        lblDetail.Font = new Font("Consolas", 9);

        var btnCopy = new Button { Text = "Copiaza", Location = new Point(420, 145), Size = new Size(125, 27) };
        btnCopy.Click += (s, e) =>
        {
            if (lastFormatted != "") Clipboard.SetText(lastFormatted);
        };

        grpManual.Controls.AddRange([lblIn, txtInput, btnConvert, chkClear, lblResult, lblDetail, btnCopy]);

        // --- Grup citire seriala --------------------------------------------
        var grpSerial = new GroupBox
        {
            Text = "Citire continua de pe port serial",
            Location = new Point(12, 232),
            Size = new Size(560, 70),
        };

        cmbPort.Location = new Point(12, 28);
        cmbPort.Size = new Size(110, 23);
        cmbPort.DropDownStyle = ComboBoxStyle.DropDownList;

        cmbBaud.Location = new Point(130, 28);
        cmbBaud.Size = new Size(100, 23);
        cmbBaud.Items.AddRange(["9600", "19200", "38400", "57600", "115200"]);
        cmbBaud.SelectedItem = "9600";

        var btnRefresh = new Button { Text = "Refresh", Location = new Point(238, 26), Size = new Size(80, 27) };
        btnRefresh.Click += (s, e) => RefreshPorts();

        btnConnect.Text = "Conectare";
        btnConnect.Location = new Point(326, 26);
        btnConnect.Size = new Size(100, 27);
        btnConnect.Click += (s, e) => ToggleSerial();

        lblStatus.Text = "Deconectat";
        lblStatus.Location = new Point(434, 30);
        lblStatus.Size = new Size(115, 23);
        lblStatus.ForeColor = Color.DarkRed;

        grpSerial.Controls.AddRange([cmbPort, cmbBaud, btnRefresh, btnConnect, lblStatus]);
        RefreshPorts();

        timer.Interval = 100;
        timer.Tick += (s, e) => PollSerial();

        // --- Grup istoric ----------------------------------------------------
        var grpHist = new GroupBox
        {
            Text = "Istoric scanari",
            Location = new Point(12, 312),
            Size = new Size(560, 240),
        };

        lstHistory.Location = new Point(12, 25);
        lstHistory.Size = new Size(533, 165);
        lstHistory.Font = new Font("Consolas", 9);

        var btnClearHist = new Button
        {
            Text = "Sterge istoric",
            Location = new Point(12, 198),
            Size = new Size(120, 27),
        };
        btnClearHist.Click += (s, e) => { records.Clear(); lstHistory.Items.Clear(); lblCount.Text = "Inregistrari: 0"; };

        var btnExport = new Button
        {
            Text = "Export CSV",
            Location = new Point(140, 198),
            Size = new Size(120, 27),
        };
        btnExport.Click += (s, e) => ExportCsv();

        lblCount.Text = "Inregistrari: 0";
        lblCount.Location = new Point(400, 202);
        lblCount.Size = new Size(145, 23);
        lblCount.TextAlign = ContentAlignment.MiddleRight;

        grpHist.Controls.AddRange([lstHistory, btnClearHist, btnExport, lblCount]);

        Controls.AddRange([grpManual, grpSerial, grpHist]);

        FormClosing += (s, e) =>
        {
            timer.Stop();
            if (serial?.IsOpen == true) serial.Close();
            serial?.Dispose();
        };

        Shown += (s, e) => txtInput.Focus();
    }

    private void ProcessRawInput(string text)
    {
        string digits = RfidConvert.DigitsOnly(text);
        if (digits == "") return;
        if (!long.TryParse(digits, out long value))
        {
            MessageBox.Show($"Valoare invalida: {text}", "Eroare",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var (facility, card, formatted, hex) = RfidConvert.Convert(value);
        lastFormatted = formatted;
        lblResult.Text = formatted;
        lblDetail.Text = $"Hex: {hex}   |   Facility: {facility}   |   Card: {card}";
        var rec = new ScanRecord { Time = DateTime.Now, Raw125 = value, Code = formatted };
        records.Add(rec);
        lstHistory.Items.Add($"{rec.Time:yyyy-MM-dd HH:mm:ss}  |  {rec.Raw125}  ->  {rec.Code}");
        lstHistory.TopIndex = lstHistory.Items.Count - 1;
        lblCount.Text = $"Inregistrari: {records.Count}";
        if (chkClear.Checked) txtInput.Clear();
        txtInput.Focus();
    }

    private void RefreshPorts()
    {
        string? sel = cmbPort.SelectedItem as string;
        cmbPort.Items.Clear();
        cmbPort.Items.AddRange(SerialPort.GetPortNames());
        if (sel != null && cmbPort.Items.Contains(sel)) cmbPort.SelectedItem = sel;
        else if (cmbPort.Items.Count > 0) cmbPort.SelectedIndex = 0;
    }

    private void ToggleSerial()
    {
        if (serial?.IsOpen == true)
        {
            timer.Stop();
            serial.Close();
            serial.Dispose();
            serial = null;
            btnConnect.Text = "Conectare";
            lblStatus.Text = "Deconectat";
            lblStatus.ForeColor = Color.DarkRed;
            return;
        }
        if (cmbPort.SelectedItem is not string port)
        {
            MessageBox.Show("Selecteaza un port COM.", "Info",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            serial = new SerialPort(port, int.Parse(cmbPort.Text), Parity.None, 8, StopBits.One);
            serial.Open();
            serBuffer = "";
            timer.Start();
            btnConnect.Text = "Deconectare";
            lblStatus.Text = $"Conectat: {port}";
            lblStatus.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Nu pot deschide portul: {ex.Message}", "Eroare",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PollSerial()
    {
        if (serial?.IsOpen != true) return;
        string chunk;
        try { chunk = serial.ReadExisting(); }
        catch { return; }
        if (chunk == "") return;
        serBuffer += chunk;
        string[] parts = serBuffer.Split(["\r\n", "\n"], StringSplitOptions.None);
        serBuffer = parts[^1]; // restul incomplet ramane in buffer
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string line = parts[i].Trim();
            if (line != "") ProcessRawInput(line);
        }
    }

    private void ExportCsv()
    {
        if (records.Count == 0) return;
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "rfid_istoric.csv" };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var lines = new List<string>(records.Count + 1) { "data,numar_125,numar_13_5" };
            foreach (var r in records)
                lines.Add($"{r.Time:yyyy-MM-dd HH:mm:ss},{r.Raw125},{r.Code}");
            File.WriteAllLines(dlg.FileName, lines, new UTF8Encoding(true)); // BOM pentru Excel
            MessageBox.Show($"Salvat: {dlg.FileName} ({records.Count} inregistrari)", "Export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
