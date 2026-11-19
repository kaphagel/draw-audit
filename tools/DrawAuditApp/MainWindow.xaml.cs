using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DrawAudit.Pipeline;
using Microsoft.Win32;

namespace DrawAudit.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    readonly AppSettings _settings = AppSettings.Load();
    CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        try { _vm.TemplateInfo = $"Built into the application ({SummaryTemplate.Version()})"; }
        catch (Exception ex) { _vm.TemplateInfo = "Built-in template could not be read: " + ex.Message; }

        if (_settings.LastFolder is { } last && Directory.Exists(last))
            _ = LoadFolderAsync(last, restoreOutput: true);
    }

    // ---- 1. Documents folder ---------------------------------------------------------------

    void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the documents folder (containing the Sub folders and the inventory)" };
        if (_vm.SourceFolder.Length > 0 && Directory.Exists(_vm.SourceFolder)) dlg.InitialDirectory = _vm.SourceFolder;
        if (dlg.ShowDialog(this) == true)
            _ = LoadFolderAsync(dlg.FolderName, restoreOutput: false);
    }

    async Task LoadFolderAsync(string folder, bool restoreOutput)
    {
        SaveSelection();
        _vm.Subs.Clear(); // the list belongs to the previous folder
        _vm.SourceFolder = folder;
        _vm.InventoryPath = PipelineRunner.FindInventory(folder) ?? "";
        _vm.OutputPath = restoreOutput && !string.IsNullOrEmpty(_settings.OutputPath)
            ? _settings.OutputPath
            : DefaultOutputPath(folder);
        _vm.HasResult = false;

        _settings.LastFolder = folder;
        _settings.Save();
        await LoadSubsAsync();
    }

    // Default output: out.xlsx next to the documents folder (in its parent folder).
    static string DefaultOutputPath(string folder) =>
        Path.Combine(Directory.GetParent(folder)?.FullName ?? folder, "out.xlsx");

    void BrowseInventory_Click(object sender, RoutedEventArgs e)
    {
        if (PickFile("Select the inventory workbook (optional cross-check)", "Excel workbooks (*.xlsm;*.xlsx;*.xls)|*.xlsm;*.xlsx;*.xls", _vm.InventoryPath) is { } path)
        {
            _vm.InventoryPath = path;
            _ = LoadSubsAsync();
        }
    }

    void ClearInventory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.InventoryPath.Length == 0) return;
        _vm.InventoryPath = "";
        _ = LoadSubsAsync();
    }

    string? PickFile(string title, string filter, string current)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        string? dir = current.Length > 0 ? Path.GetDirectoryName(current) : _vm.SourceFolder;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    // ---- 2. Sub selection ------------------------------------------------------------------

    async Task LoadSubsAsync()
    {
        SaveSelection();
        _vm.Subs.Clear();
        if (_vm.SourceFolder.Length == 0) return;

        _vm.IsLoadingSubs = true;
        _vm.Status = "Scanning the documents folder...";
        try
        {
            string folder = _vm.SourceFolder;
            string? inventory = _vm.InventoryPath.Length > 0 ? _vm.InventoryPath : null;
            var subs = await Task.Run(() => PipelineRunner.DiscoverSubs(folder, inventory));
            var deselected = _settings.DeselectedSubs.TryGetValue(folder, out var d)
                ? new HashSet<string>(d, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in subs)
                _vm.Subs.Add(new SubItem(s.Sub, s.Folder, s.Drawings, s.Dls, s.Processable, s.DlWarning)
                    { IsSelected = s.Processable && !deselected.Contains(s.Sub) });

            int n = subs.Count(s => s.Processable);
            _vm.Status = subs.Count == 0 ? "No Sub folders (Sub...) found in the documents folder."
                : inventory is null ? $"Found {n} Subs. No inventory selected - no cross-check."
                : $"Found {n} Subs. Cross-checking against {Path.GetFileName(inventory)}.";
        }
        catch (Exception ex)
        {
            _vm.Status = "Could not scan the documents folder.";
            MessageBox.Show(this, $"Could not scan the documents folder or read the inventory:\n\n{ex.Message}",
                "Draw Audit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _vm.IsLoadingSubs = false;
        }
    }

    void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var s in _vm.Subs) s.IsSelected = true; }
    void DeselectAll_Click(object sender, RoutedEventArgs e) { foreach (var s in _vm.Subs) s.IsSelected = false; }

    void SaveSelection()
    {
        if (_vm.SourceFolder.Length == 0 || _vm.Subs.Count == 0) return;
        _settings.DeselectedSubs[_vm.SourceFolder] = _vm.Subs.Where(s => s.Processable && !s.IsSelected).Select(s => s.Sub).ToList();
    }

    // ---- 3. Output -------------------------------------------------------------------------

    void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Output workbook",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = Path.GetFileName(_vm.OutputPath.Length > 0 ? _vm.OutputPath : "out.xlsx"),
            OverwritePrompt = false, // confirmed when the run starts
        };
        string? dir = Path.GetDirectoryName(_vm.OutputPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) == true) _vm.OutputPath = dlg.FileName;
    }

    // ---- Run -------------------------------------------------------------------------------

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        string output = _vm.OutputPath;
        if (!output.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            _vm.OutputPath = output = output + ".xlsx";

        if (PipelineRunner.CheckOutputWritable(output) is { } problem)
        {
            MessageBox.Show(this, problem, "Draw Audit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (File.Exists(output) && MessageBox.Show(this, $"The output file already exists:\n{output}\n\nOverwrite it?",
                "Draw Audit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        SaveSelection();
        _settings.OutputPath = output;
        _settings.Save();

        var subs = _vm.Subs.Where(s => s.IsSelected).Select(s => s.Sub).ToList();
        bool all = subs.Count == _vm.Subs.Count(s => s.Processable);
        var options = new PipelineOptions(_vm.InventoryPath.Length > 0 ? _vm.InventoryPath : null, _vm.SourceFolder, TemplatePath: null, output,
            DbPath: null, Subs: all ? null : subs);

        LogBox.Clear();
        _vm.HasResult = false;
        _vm.Progress = 0;
        _vm.IsBusy = true;
        _vm.Status = $"Running {subs.Count} Sub(s)...";
        _cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();

        double maxPct = 0;
        var progress = new Progress<PipelineProgress>(p =>
        {
            if (p.Percent >= 0 && p.Percent > maxPct) _vm.Progress = maxPct = p.Percent;
            if (p.Message.Length > 0) AppendLog(p.Message);
        });

        try
        {
            var token = _cts.Token;
            var result = await Task.Run(() => PipelineRunner.Run(options, progress, token));
            _vm.Progress = 100;
            string errors = result.Errors.Count > 0 ? $", {result.Errors.Count} file error(s) - see log" : "";
            _vm.ResultText = $"Done in {sw.Elapsed.TotalSeconds:0} s: {result.Subs.Count} Sub(s), {result.TotalWarnings} warning(s){errors}. " +
                $"See the 'Warnings Summary' sheet in {Path.GetFileName(result.OutputPath)}.";
            _vm.HasResult = true;
            _vm.Status = $"Saved {result.OutputPath}";
            if (result.Errors.Count > 0)
            {
                AppendLog("");
                AppendLog("File errors:");
                foreach (var err in result.Errors) AppendLog("  " + err);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
            _vm.Status = "Cancelled - no output written.";
        }
        catch (Exception ex)
        {
            AppendLog("FAILED: " + ex.Message);
            _vm.Status = "Failed.";
            MessageBox.Show(this, ex.Message, "Draw Audit - run failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _vm.IsBusy = false;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _vm.Status = "Cancelling...";
    }

    void AppendLog(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    void OpenOutput_Click(object sender, RoutedEventArgs e) => Shell(_vm.OutputPath);

    void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_vm.OutputPath}\"") { UseShellExecute = true });

    void Shell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Draw Audit", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsBusy)
        {
            if (MessageBox.Show(this, "A run is in progress. Cancel it and close?", "Draw Audit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
        SaveSelection();
        if (_vm.OutputPath.Length > 0) _settings.OutputPath = _vm.OutputPath;
        _settings.Save();
    }
}
