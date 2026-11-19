using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawAudit.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SubItem(string sub, string folder, int drawings, int dls, bool processable, string? dlWarning = null) : Observable
{
    bool _isSelected = processable;

    public string Sub { get; } = sub;
    public string Folder { get; } = folder;
    public bool Processable { get; } = processable;
    public string DlWarning { get; } = dlWarning is null ? "" : "\u26A0 " + dlWarning;
    public bool HasDlWarning => DlWarning.Length > 0;
    public string Details { get; } = !processable
        ? "no DL/PDF files - cannot be processed"
        : $"{drawings} drawings, {dls} DL" + (dls == 0 ? " (no DL - will be skipped)" : "");

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, Processable && value);
    }
}

public sealed class MainViewModel : Observable
{
    string _sourceFolder = "", _inventoryPath = "", _templateInfo = "", _outputPath = "";
    string _status = "Select the documents folder to start.";
    bool _isBusy, _isLoadingSubs, _hasResult;
    double _progress;
    string _resultText = "";

    public ObservableCollection<SubItem> Subs { get; } = new();

    public MainViewModel()
    {
        Subs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (SubItem s in e.NewItems) s.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(SelectionText)); OnPropertyChanged(nameof(CanRun)); };
            OnPropertyChanged(nameof(SelectionText));
            OnPropertyChanged(nameof(CanRun));
        };
    }

    public string SourceFolder { get => _sourceFolder; set { if (Set(ref _sourceFolder, value)) OnPropertyChanged(nameof(CanRun)); } }
    public string InventoryPath { get => _inventoryPath; set { if (Set(ref _inventoryPath, value)) OnPropertyChanged(nameof(CanRun)); } }
    public string TemplateInfo { get => _templateInfo; set => Set(ref _templateInfo, value); }
    public string OutputPath { get => _outputPath; set { if (Set(ref _outputPath, value)) OnPropertyChanged(nameof(CanRun)); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string ResultText { get => _resultText; set => Set(ref _resultText, value); }
    public bool HasResult { get => _hasResult; set => Set(ref _hasResult, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (Set(ref _isBusy, value)) { OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanRun)); } }
    }

    public bool IsLoadingSubs
    {
        get => _isLoadingSubs;
        set { if (Set(ref _isLoadingSubs, value)) { OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanRun)); } }
    }

    public bool IsIdle => !IsBusy && !IsLoadingSubs;

    public int SelectedCount => Subs.Count(s => s.IsSelected);

    public string SelectionText =>
        $"{SelectedCount} of {Subs.Count(s => s.Processable)} selected" +
        (Subs.Any(s => s.HasDlWarning) ? $"  \u26A0 {Subs.Count(s => s.HasDlWarning)} with multiple DLs" : "");

    public bool CanRun => IsIdle && SourceFolder.Length > 0
        && OutputPath.Length > 0 && SelectedCount > 0;

    public void RefreshCanRun() => OnPropertyChanged(nameof(CanRun));
}
