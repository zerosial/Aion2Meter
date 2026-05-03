using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;

namespace A2Meter.Forms;

/// Combat history list. Clicking a row restores the full snapshot view.
internal sealed class CombatHistoryForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly DataGridView _grid;
    private readonly Panel _headerPanel;
    private CombatHistory? _history;
    private Action<CombatRecord>? _onRecordSelected;

    public CombatHistoryForm()
    {
        Text = "전투 기록";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 22, 34);
        Size = new Size(
            AppSettings.Instance.CombatRecordsPanelWidth,
            AppSettings.Instance.CombatRecordsPanelHeight);

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(24, 28, 42),
        };

        var lblTitle = new Label
        {
            Text = "전투 기록",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f),
            AutoSize = true,
            Location = new Point(12, 8),
        };
        _headerPanel.Controls.Add(lblTitle);

        var btnClose = new Label
        {
            Text = "✕",
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 12f),
            AutoSize = true,
            Cursor = Cursors.Hand,
        };
        btnClose.Click += (_, _) => Close();
        _headerPanel.Controls.Add(btnClose);
        _headerPanel.Resize += (_, _) => btnClose.Location = new Point(_headerPanel.Width - btnClose.Width - 12, 6);

        _headerPanel.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Color.FromArgb(18, 22, 34),
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(18, 22, 34),
                ForeColor = Color.FromArgb(220, 220, 220),
                SelectionBackColor = Color.FromArgb(45, 55, 80),
                SelectionForeColor = Color.White,
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(4, 2, 4, 2),
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(28, 34, 52),
                ForeColor = Color.FromArgb(180, 190, 210),
                Font = new Font("Segoe UI Semibold", 9f),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
            },
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 32,
            RowTemplate = { Height = 34 },
        };

        _grid.Columns.AddRange(
            Col("시간", 130),
            Col("보스", 140),
            Col("시간(초)", 70, DataGridViewContentAlignment.MiddleRight),
            Col("총 대미지", 110, DataGridViewContentAlignment.MiddleRight),
            Col("평균 DPS", 100, DataGridViewContentAlignment.MiddleRight),
            Col("최대 DPS", 100, DataGridViewContentAlignment.MiddleRight)
        );
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.CellDoubleClick += OnRowDoubleClick;
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OnRowSelect(); };

        Controls.Add(_grid);
        Controls.Add(_headerPanel);
    }

    public void SetData(CombatHistory history, Action<CombatRecord> onSelect)
    {
        _history = history;
        _onRecordSelected = onSelect;
        RefreshList();
    }

    private void RefreshList()
    {
        _grid.Rows.Clear();
        if (_history == null) return;
        foreach (var rec in _history.Records)
        {
            _grid.Rows.Add(
                rec.Timestamp.ToString("MM/dd HH:mm:ss"),
                rec.BossName ?? "-",
                $"{rec.DurationSec:0.0}",
                $"{rec.TotalDamage:N0}",
                $"{rec.AverageDps:N0}",
                $"{rec.PeakDps:N0}");
        }
        _grid.ClearSelection();
    }

    private void OnRowDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        SelectRow(e.RowIndex);
    }

    private void OnRowSelect()
    {
        if (_grid.SelectedRows.Count == 0) return;
        SelectRow(_grid.SelectedRows[0].Index);
    }

    private void SelectRow(int index)
    {
        if (_history == null || index < 0 || index >= _history.Records.Count) return;
        _onRecordSelected?.Invoke(_history.Records[index]);
    }

    private static DataGridViewTextBoxColumn Col(string header, int width,
        DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleLeft)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            Width = width,
            MinimumWidth = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = align },
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var s = AppSettings.Instance;
        s.CombatRecordsPanelWidth = Width;
        s.CombatRecordsPanelHeight = Height;
        s.SaveDebounced();
        base.OnFormClosing(e);
    }
}
