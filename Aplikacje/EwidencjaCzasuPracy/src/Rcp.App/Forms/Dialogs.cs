using System.Globalization;
using Rcp.App.Controls;
using Rcp.Core;
using Rcp.Core.Data;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Okna edycji: pracownik, zdarzenie, nieobecność, odczyt karty, wybór pracownika.</summary>
internal static class Dialogs
{
    /// <summary>Czeka na odbicie karty na dowolnym czytniku (lub ręczne wpisanie numeru).</summary>
    public static string? CaptureCard(IWin32Window owner, AppHost host, string title = "Odczyt karty")
    {
        var label = new Label
        {
            Text = "Przyłóż kartę do dowolnego skonfigurowanego czytnika\nalbo wpisz numer karty ręcznie.",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 11f),
            Margin = new Padding(3, 3, 3, 10),
        };
        var box = new TextBox { Font = new Font(FontFamily.GenericMonospace, 14f), CharacterCasing = CharacterCasing.Upper };
        var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(10) };
        panel.Controls.Add(label);
        panel.Controls.Add(box);
        box.Dock = DockStyle.Fill;

        string? result = null;
        using var form = Ui.Dialog(title, panel, () =>
        {
            var uid = CardUid.Normalize(box.Text);
            if (uid.Length < 4) throw new RcpException("Numer karty jest za krótki.");
            result = uid;
            return true;
        });
        host.CardCapture = uid =>
        {
            result = uid;
            form.DialogResult = DialogResult.OK;
        };
        try
        {
            return form.ShowDialog(owner) == DialogResult.OK ? result : null;
        }
        finally
        {
            host.CardCapture = null;
        }
    }

    public static bool EditEmployee(IWin32Window owner, AppHost host, Employee emp)
    {
        var ctx = host.Ctx;
        var no = new TextBox { Text = emp.EmployeeNo };
        var last = new TextBox { Text = emp.LastName };
        var first = new TextBox { Text = emp.FirstName };
        var dept = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        dept.Items.Add("(brak)");
        foreach (var d in ctx.Departments.All()) dept.Items.Add(d);
        dept.SelectedIndex = 0;
        for (int i = 1; i < dept.Items.Count; i++)
            if (dept.Items[i] is Department d && d.Id == emp.DepartmentId) dept.SelectedIndex = i;
        var position = new TextBox { Text = emp.Position ?? "" };

        var card = new TextBox { Text = emp.CardUid ?? "", CharacterCasing = CharacterCasing.Upper, Width = 200 };
        var readCard = Ui.Button("Odczytaj z czytnika…", (_, _) =>
        {
            if (CaptureCard(owner, host) is { } uid) card.Text = uid;
        });
        var clearCard = Ui.Button("Usuń kartę", (_, _) => card.Text = "");
        var cardRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        cardRow.Controls.AddRange(new Control[] { card, readCard, clearCard });

        var norm = new NumericUpDown { DecimalPlaces = 2, Increment = 0.5m, Minimum = 0, Maximum = 24, Value = emp.DailyNormMinutes / 60m };
        var active = new CheckBox { Text = "Aktywny (zatrudniony)", Checked = emp.Active, AutoSize = true };
        var notes = new TextBox { Text = emp.Notes ?? "", Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical };

        var grid = Ui.FormGrid(("Nr ewidencyjny:", no), ("Nazwisko:", last), ("Imię:", first), ("Dział:", dept),
            ("Stanowisko:", position), ("Karta Mifare (UID):", cardRow), ("Norma dobowa [h]:", norm), ("", active), ("Uwagi:", notes));

        using var form = Ui.Dialog(emp.Id == 0 ? "Nowy pracownik" : $"Pracownik: {emp.FullName}", grid, () =>
        {
            var e = new Employee
            {
                Id = emp.Id,
                EmployeeNo = no.Text,
                LastName = last.Text,
                FirstName = first.Text,
                DepartmentId = (dept.SelectedItem as Department)?.Id,
                Position = string.IsNullOrWhiteSpace(position.Text) ? null : position.Text.Trim(),
                CardUid = card.Text,
                DailyNormMinutes = (int)Math.Round(norm.Value * 60),
                Active = active.Checked,
                Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim(),
            };
            ctx.Employees.Save(e);
            ctx.Audit.Log(emp.Id == 0 ? "Dodanie pracownika" : "Edycja pracownika", $"{e.EmployeeNo} {e.FullName} karta={e.CardUid}");

            // jeśli karta odbijała się wcześniej jako nieznana – przypisz te zdarzenia
            if (!string.IsNullOrEmpty(e.CardUid) && e.CardUid != emp.CardUid)
            {
                int n = ctx.Events.AssignUnknownCard(e.CardUid, e.Id);
                if (n > 0) Ui.Info($"Przypisano {n} wcześniejszych odbić tej karty do pracownika.");
            }
            emp.Id = e.Id;
            return true;
        }, 560);
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static Employee? PickEmployee(IWin32Window owner, RcpContext ctx, string title = "Wybierz pracownika")
    {
        var picker = new EmployeePicker(ctx) { Height = 380, IncludeInactive = true };
        Employee? result = null;
        using var form = Ui.Dialog(title, picker, () =>
        {
            result = picker.Selected ?? throw new RcpException("Wybierz pracownika z listy.");
            return true;
        }, 480);
        picker.Activated += e => { result = e; form.DialogResult = DialogResult.OK; };
        form.Shown += (_, _) => picker.FocusSearch();
        return form.ShowDialog(owner) == DialogResult.OK ? result : null;
    }

    /// <summary>Dodanie / korekta zdarzenia wejścia-wyjścia (wpis ręczny).</summary>
    public static bool EditEvent(IWin32Window owner, RcpContext ctx, AttendanceEvent evt, Employee? fixedEmployee = null)
    {
        Employee? employee = fixedEmployee ?? (evt.EmployeeId is { } id ? ctx.Employees.Get(id) : null);
        var empLabel = new Label { AutoSize = true, Text = employee?.ToString() ?? "(nie wybrano)", Margin = new Padding(3, 8, 3, 3) };
        var pick = Ui.Button("Wybierz…", (_, _) =>
        {
            if (PickEmployee(owner, ctx) is { } e) { employee = e; empLabel.Text = e.ToString(); }
        });
        pick.Enabled = fixedEmployee == null;
        var empRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        empRow.Controls.AddRange(new Control[] { empLabel, pick });

        var date = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = evt.Timestamp == default ? DateTime.Today : evt.Timestamp.Date };
        var time = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm:ss",
            ShowUpDown = true,
            Value = evt.Timestamp == default ? DateTime.Now : evt.Timestamp,
        };
        var dirIn = new RadioButton { Text = "Wejście", AutoSize = true, Checked = evt.Direction != Direction.Out };
        var dirOut = new RadioButton { Text = "Wyjście", AutoSize = true, Checked = evt.Direction == Direction.Out };
        var dirRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        dirRow.Controls.AddRange(new Control[] { dirIn, dirOut });
        var note = new TextBox { Text = evt.Note ?? "", PlaceholderText = "np. zapomniana karta, wyjście służbowe" };

        var grid = Ui.FormGrid(("Pracownik:", empRow), ("Data:", date), ("Godzina:", time), ("Kierunek:", dirRow), ("Uzasadnienie:", note));
        using var form = Ui.Dialog(evt.Id == 0 ? "Nowe zdarzenie (wpis ręczny)" : "Korekta zdarzenia", grid, () =>
        {
            if (employee == null) throw new RcpException("Wybierz pracownika.");
            if (string.IsNullOrWhiteSpace(note.Text)) throw new RcpException("Podaj uzasadnienie korekty – jest zapisywane w ewidencji.");
            bool isNew = evt.Id == 0;
            var before = isNew ? "" : $"{evt.Timestamp:yyyy-MM-dd HH:mm:ss} {evt.Direction}";
            evt.EmployeeId = employee.Id;
            if (isNew) evt.CardUid = employee.CardUid ?? "";
            evt.Timestamp = date.Value.Date + time.Value.TimeOfDay;
            evt.Direction = dirOut.Checked ? Direction.Out : Direction.In;
            evt.Note = note.Text.Trim();
            evt.Source = EventSource.Manual;
            if (isNew) ctx.Events.Insert(evt); else ctx.Events.Update(evt);
            ctx.Audit.Log(isNew ? "Ręczne zdarzenie" : "Korekta zdarzenia",
                $"{employee.EmployeeNo} {before} -> {evt.Timestamp:yyyy-MM-dd HH:mm:ss} {evt.Direction}: {evt.Note}");
            return true;
        }, 520);
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static bool EditAbsence(IWin32Window owner, RcpContext ctx, Absence a, Employee employee)
    {
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        foreach (var t in AbsenceTypes.All) type.Items.Add(new KeyValuePair<AbsenceType, string>(t, AbsenceTypes.Name(t)));
        type.DisplayMember = "Value";
        type.SelectedIndex = Math.Max(0, Array.IndexOf(AbsenceTypes.All, a.Type));
        var from = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = a.DateFrom.ToDateTime(TimeOnly.MinValue) };
        var to = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = a.DateTo.ToDateTime(TimeOnly.MinValue) };
        var note = new TextBox { Text = a.Note ?? "" };
        var grid = Ui.FormGrid(("Pracownik:", new Label { Text = employee.ToString(), AutoSize = true }),
            ("Rodzaj:", type), ("Od:", from), ("Do:", to), ("Uwagi:", note));

        using var form = Ui.Dialog(a.Id == 0 ? "Nowa nieobecność" : "Edycja nieobecności", grid, () =>
        {
            a.EmployeeId = employee.Id;
            a.Type = ((KeyValuePair<AbsenceType, string>)type.SelectedItem!).Key;
            a.DateFrom = DateOnly.FromDateTime(from.Value);
            a.DateTo = DateOnly.FromDateTime(to.Value);
            a.Note = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim();
            ctx.Absences.Save(a);
            ctx.Audit.Log("Nieobecność", $"{employee.EmployeeNo} {a.Type} {a.DateFrom}..{a.DateTo}");
            return true;
        });
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static bool EditSettings(IWin32Window owner, AppHost host)
    {
        var s = host.Ctx.Settings;
        var company = new TextBox { Text = s.CompanyName };
        var debounce = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = s.DebounceSeconds };
        var maxShift = new NumericUpDown { Minimum = 4, Maximum = 24, Value = s.MaxShiftHours };
        var dbPath = new TextBox { Text = host.Config.DatabasePath, Width = 320 };
        var browse = Ui.Button("…", (_, _) =>
        {
            using var d = new SaveFileDialog { Filter = "Baza SQLite (*.db)|*.db", FileName = dbPath.Text, OverwritePrompt = false };
            if (d.ShowDialog(owner) == DialogResult.OK) dbPath.Text = d.FileName;
        });
        var dbRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        dbRow.Controls.AddRange(new Control[] { dbPath, browse });

        var grid = Ui.FormGrid(
            ("Nazwa firmy:", company),
            ("Blokada ponownego odbicia [s]:", debounce),
            ("Maks. długość zmiany [h]:", maxShift),
            ("Plik bazy danych:", dbRow));

        using var form = Ui.Dialog("Ustawienia", grid, () =>
        {
            host.Ctx.SaveSettings(new AppSettings
            {
                CompanyName = company.Text.Trim(),
                DebounceSeconds = (int)debounce.Value,
                MaxShiftHours = (int)maxShift.Value,
            });
            if (!string.Equals(dbPath.Text.Trim(), host.Config.DatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                host.Config.DatabasePath = dbPath.Text.Trim();
                host.Config.Save();
                Ui.Info("Nowa lokalizacja bazy zostanie użyta po ponownym uruchomieniu programu.");
            }
            return true;
        }, 560);
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static string FormatHours(int minutes) => (minutes / 60.0).ToString("0.00", CultureInfo.CurrentCulture);
}
