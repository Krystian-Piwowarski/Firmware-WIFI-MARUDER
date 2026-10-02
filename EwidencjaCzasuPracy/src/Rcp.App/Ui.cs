using Rcp.Core;
using Rcp.Core.Data;

namespace Rcp.App;

/// <summary>Pomocnicze metody budowania interfejsu w kodzie.</summary>
internal static class Ui
{
    public static readonly Color InColor = Color.FromArgb(220, 245, 220);
    public static readonly Color OutColor = Color.FromArgb(220, 232, 250);
    public static readonly Color ErrorColor = Color.FromArgb(255, 220, 220);
    public static readonly Color WarnColor = Color.FromArgb(255, 243, 205);
    public static readonly Color MutedColor = Color.FromArgb(240, 240, 240);

    public static void ShowError(Exception ex)
    {
        if (ex is RcpException)
        {
            MessageBox.Show(ex.Message, "Ewidencja RCP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Log.Error("Błąd", ex);
        MessageBox.Show($"Wystąpił błąd:\n{ex.Message}", "Ewidencja RCP", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Wykonuje akcję, a błędy pokazuje użytkownikowi. Zwraca true przy powodzeniu.</summary>
    public static bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception ex) { ShowError(ex); return false; }
    }

    public static bool Confirm(string text)
        => MessageBox.Show(text, "Potwierdzenie", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static void Info(string text)
        => MessageBox.Show(text, "Ewidencja RCP", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static Button Button(string text, EventHandler onClick, int width = 0)
    {
        var b = new Button { Text = text, AutoSize = width == 0, Height = 30, Margin = new Padding(3) };
        if (width > 0) b.Width = width;
        b.Click += onClick;
        return b;
    }

    public static Label Label(string text, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 8, 3, 3),
        Font = bold ? new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) : SystemFonts.MessageBoxFont,
    };

    public static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(4),
        };
        p.Controls.AddRange(controls);
        return p;
    }

    /// <summary>Formularz „etykieta: pole” w dwóch kolumnach.</summary>
    public static TableLayoutPanel FormGrid(params (string Label, Control Field)[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (label, field) in rows)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(Label(label));
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(3);
            t.Controls.Add(field);
        }
        return t;
    }

    /// <summary>Okno dialogowe z przyciskami OK/Anuluj; <paramref name="onOk"/> zwraca false, aby nie zamykać.</summary>
    public static Form Dialog(string title, Control content, Func<bool> onOk, int width = 460)
    {
        var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(width, 0),
            Font = SystemFonts.MessageBoxFont,
        };
        EnableDpiScaling(form);
        var ok = new Button { Text = "OK", Width = 100, Height = 30 };
        var cancel = new Button { Text = "Anuluj", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            bool close = false;
            Try(() => close = onOk());
            if (close) form.DialogResult = DialogResult.OK;
        };
        // OK pierwszy w kolejności tabulacji – Enter (także z czytnika kart) nie trafi w „Anuluj”
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = Point.Empty };
        layout.Controls.Add(content);
        layout.Controls.Add(buttons);
        content.Dock = DockStyle.Fill;
        content.MinimumSize = new Size(width - 30, 0);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) =>
        {
            // bez pól do edycji fokus na OK
            if (form.ActiveControl == null || form.ActiveControl == cancel) ok.Focus();
        };
        return form;
    }

    /// <summary>Wymiary w kodzie podane są dla 96 DPI – przy 125/150% Windows je przeskaluje.</summary>
    public static void EnableDpiScaling(ContainerControl c)
    {
        c.AutoScaleDimensions = new SizeF(96F, 96F);
        c.AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Skaluje szerokości kolumn tabel i list (podane dla 96 DPI) do DPI ekranu. Wywoływać raz, w Load.</summary>
    public static void ScaleColumns(Control root)
    {
        float f = root.DeviceDpi / 96f;
        if (Math.Abs(f - 1f) < 0.01f) return;
        void Walk(Control c)
        {
            switch (c)
            {
                case ListView lv:
                    foreach (ColumnHeader h in lv.Columns) h.Width = (int)(h.Width * f);
                    break;
                case DataGridView g:
                    foreach (DataGridViewColumn col in g.Columns)
                    {
                        col.Width = (int)(col.Width * f);
                        col.MinimumWidth = Math.Max(2, (int)(col.MinimumWidth * f));
                    }
                    g.RowTemplate.Height = (int)(g.RowTemplate.Height * f);
                    break;
            }
            foreach (Control child in c.Controls) Walk(child);
        }
        Walk(root);
    }

    public static void DoubleBuffer(Control c)
        => typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(c, true);

    public static string Dir(Direction d) => d == Direction.In ? "Wejście" : "Wyjście";

    public static string? SaveFile(string defaultName, string filter = "Plik CSV (*.csv)|*.csv")
    {
        using var d = new SaveFileDialog { FileName = defaultName, Filter = filter };
        return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
    }

    /// <summary>Opóźnione wywołanie (np. wyszukiwanie podczas pisania).</summary>
    public static System.Windows.Forms.Timer Debouncer(int ms, Action action)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (_, _) => { t.Stop(); action(); };
        return t;
    }

    public static void Restart(this System.Windows.Forms.Timer t) { t.Stop(); t.Start(); }
}
