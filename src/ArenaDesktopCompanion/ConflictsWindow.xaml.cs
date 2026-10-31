using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArenaDesktopCompanion.Core.Conflicts;
using ArenaDesktopCompanion.Infrastructure;
using ArenaDesktopCompanion.Core.Diff;
using ArenaDesktopCompanion.Core.Sync;

namespace ArenaDesktopCompanion;

/// <summary>
/// Der Dialog für Punkt 6.19: Gegenüberstellung beider Versionen, vier Wege, Standard bleibt
/// „nichts überschreiben ohne mich“. Entscheidungen laufen über dieselbe, getestete
/// <see cref="DecisionApplier"/>-Schleife wie eine handschriftlich beantwortete Vorlage.
/// </summary>
public partial class ConflictsWindow : Window
{
    private sealed record Row(DecisionItem Item)
    {
        public string Relative => Item.Relative;
        public string SubLine => Item.Found.ToLocalTime().ToString("dd.MM. HH:mm") + " · Antwort: " + (Item.Answer ?? "offen");
    }

    private readonly string _root;
    private readonly SyncPolicy _policy;

    public ConflictsWindow(string root, SyncPolicy policy)
    {
        InitializeComponent();
        Loaded+=(_,_)=>Product.UiText.Apply(this);
        _root = root;
        _policy = policy;
        Refresh();
    }

    public event Action<string>? Logged;

    private IReadOnlyList<DecisionItem> _items = Array.Empty<DecisionItem>();
    private Row? _current;

    private void Refresh()
    {
        _items = DecisionApplier.ReadPending(_root);
        var selected = _current?.Item.Id;

        var rows = _items.Select(i => new Row(i)).ToList();
        LstConflicts.ItemsSource = rows;

        if (rows.Count == 0)
        {
            TxtHint.Text = Product.UiText.Translate("Keine offenen Konflikte. Was die App nicht einfach überschreiben durfte, ist erledigt.");
            ClearPanes();
            return;
        }

        TxtHint.Text = App.Services.Settings.UiLanguage=="en"?$"{rows.Count} file(s) differ from your copy. Decide per file — nothing is overwritten without your review.":$"{rows.Count} Datei(en) unterscheiden sich von deinem Stand. Entscheide pro Datei – " +
                       "nichts wird überschrieben, ohne dass du es hier gesehen hast.";

        var restore = rows.FirstOrDefault(r => string.Equals(r.Item.Id, selected, StringComparison.Ordinal)) ?? rows[0];
        LstConflicts.SelectedItem = restore;
    }

    private void ClearPanes()
    {
        _current = null;
        TxtMine.Text = string.Empty;
        TxtArena.Text = string.Empty;
        TxtDiff.Text = string.Empty;
        TxtDiffLabel.Text = Product.UiText.Translate("kein Eintrag ausgewählt");
    }

    private void LstConflicts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current = LstConflicts.SelectedItem as Row;
        if (_current is null)
        {
            ClearPanes();
            return;
        }

        var mine = Read(_current.Item.Mine);
        var arena = Read(_current.Item.Arena);
        TxtMine.Text = mine ?? "(keine Kopie gefunden)";
        TxtArena.Text = arena ?? "(keine Kopie gefunden)";

        var diff = DiffSummary.Build(mine, arena, maxLines: 80);
        TxtDiffLabel.Text = diff.IsBinary ? diff.Label : $"{diff.Label} · {Path.GetFileName(_current.Item.Relative)}";
        TxtDiff.Text = string.Join(Environment.NewLine, diff.Lines);
    }

    private string? Read(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private async void BtnArena_Click(object sender, RoutedEventArgs e) => await ResolveAsync("arena");
    private async void BtnKeepMine_Click(object sender, RoutedEventArgs e) => await ResolveAsync("mine");
    private async void BtnBoth_Click(object sender, RoutedEventArgs e) => await ResolveAsync("both");

    private async Task ResolveAsync(string answer)
    {
        if (_current is null)
        {
            TxtHint.Text = "Bitte links eine Datei auswählen.";
            return;
        }

        // Ohne Rückfrage (rc24): Du hast den Knopf gedrückt – das ist die Antwort. Deine Fassung
        // wird vorher in Backup/ gesichert, du kannst sie von dort zurückholen.

        if (!DecisionApplier.SetAnswer(_root, _current.Item.Id, answer))
        {
            TxtHint.Text = "Anfrage nicht gefunden – die Liste ist neu geladen.";
            Refresh();
            return;
        }

        await Task.Run(() => DecisionApplier.ApplyPending(_root, _policy, msg => Logged?.Invoke(msg)));
        Logged?.Invoke($"Konflikt entschieden: {answer} → {_current.Item.Relative}");
        Refresh();
    }

    private async void BtnApplyAll_Click(object sender, RoutedEventArgs e)
    {
        var applied = await Task.Run(() => DecisionApplier.ApplyPending(_root, _policy, msg => Logged?.Invoke(msg)));
        Logged?.Invoke(applied > 0 ? $"{applied} Entscheidung(en) aus der Vorlage übernommen." : "Keine beantworteten Anfragen gefunden.");
        Refresh();
    }

    private void BtnOpenTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = ConflictPolicy.TemplatePath(_root);
        if (!File.Exists(template))
        {
            TxtHint.Text = "Es gibt gerade keine Vorlage – nichts zu entscheiden.";
            return;
        }
        Process.Start(new ProcessStartInfo(template) { UseShellExecute = true });
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Dunkle Titelleiste, damit der Dialog nicht hell aus dem Rahmen fällt.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }
}
