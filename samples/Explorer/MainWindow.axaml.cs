using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Leptonica;

namespace Explorer;

public partial class MainWindow : Window
{
    /// <summary>A line of extra information a demo can report (for example the skew angle found).</summary>
    [ThreadStatic] public static string? Note;

    private static readonly string ImageDir = Path.Combine(AppContext.BaseDirectory, "images");

    private readonly Dictionary<string, Pix> _images = new();
    private readonly Dictionary<string, double> _values = new();
    private readonly DispatcherTimer _debounce;
    private Demo? _demo;
    private string? _imagePath;
    private Pix? _result;
    private int _version;
    private bool _loadingDemo;

    public MainWindow()
    {
        InitializeComponent();

        foreach (var group in Demos.All.GroupBy(d => d.Category))
        {
            var category = new TreeViewItem { Header = group.Key, IsExpanded = true };
            foreach (Demo demo in group)
                category.Items.Add(new TreeViewItem { Header = demo.Name, Tag = demo });
            Operations.Items.Add(category);
        }
        Operations.SelectionChanged += (_, _) =>
        {
            if (Operations.SelectedItem is TreeViewItem { Tag: Demo demo })
                SelectDemo(demo);
        };

        foreach (string file in Directory.GetFiles(ImageDir, "*.png").Order())
            Images.Items.Add(Path.GetFileName(file));
        Images.SelectionChanged += (_, _) =>
        {
            if (!_loadingDemo && Images.SelectedItem is string name)
                SetImage(Path.Combine(ImageDir, name));
        };

        OpenButton.Click += OpenImage;
        SaveButton.Click += SaveResult;
        UseGpu.IsCheckedChanged += (_, _) =>
        {
            LeptGpu.Mode = UseGpu.IsChecked == true ? GpuMode.Auto : GpuMode.CpuOnly;
            Recompute();
        };

        // Parameters change many times per second while a slider moves; run the operation
        // when the slider has been still for a moment.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Recompute();
        };

        // Start the GPU in the background; operations started before it is ready run on the CPU.
        Status.Text = "Starting the GPU...";
        Task.Run(() => LeptGpu.Warmup()).ContinueWith(_ => Dispatcher.UIThread.Post(Recompute));

        if (Operations.Items[0] is TreeViewItem first && first.Items[0] is TreeViewItem item)
            Operations.SelectedItem = item;
    }

    private static string GpuName() =>
        LeptGpu.Mode == GpuMode.CpuOnly ? "off" : LeptGpu.IsHardwareGpuAvailable ? LeptGpu.ActiveDeviceName ?? "yes" : "none";

    private void SelectDemo(Demo demo)
    {
        _demo = demo;
        TitleText.Text = demo.Name;
        Description.Text = demo.Description;
        NoteText.Text = "";
        _values.Clear();
        Parameters.Children.Clear();
        foreach (Param p in demo.Params)
            Parameters.Children.Add(CreateEditor(p));

        _loadingDemo = true;
        Images.SelectedItem = demo.Image;
        _loadingDemo = false;
        SetImage(Path.Combine(ImageDir, demo.Image));
    }

    private Control CreateEditor(Param p)
    {
        var panel = new StackPanel { Spacing = 2 };
        var label = new TextBlock();
        panel.Children.Add(label);
        switch (p)
        {
            case Slider s:
                _values[s.Name] = s.Default;
                string Format(double v) => s.Step >= 1 ? $"{s.Name}: {v:F0}" : $"{s.Name}: {v:0.##}";
                label.Text = Format(s.Default);
                var slider = new Avalonia.Controls.Slider
                {
                    Minimum = s.Min, Maximum = s.Max, Value = s.Default,
                    TickFrequency = s.Step, IsSnapToTickEnabled = true,
                };
                slider.ValueChanged += (_, e) =>
                {
                    _values[s.Name] = e.NewValue;
                    label.Text = Format(e.NewValue);
                    _debounce.Stop();
                    _debounce.Start();
                };
                panel.Children.Add(slider);
                break;
            case Choice c:
                _values[c.Name] = c.Default;
                label.Text = c.Name;
                var combo = new ComboBox { ItemsSource = c.Options, SelectedIndex = c.Default, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                combo.SelectionChanged += (_, _) =>
                {
                    _values[c.Name] = combo.SelectedIndex;
                    Recompute();
                };
                panel.Children.Add(combo);
                break;
        }
        return panel;
    }

    private void SetImage(string path)
    {
        try
        {
            if (!_images.TryGetValue(path, out Pix? pix))
                _images[path] = pix = Pix.Read(path);
            _imagePath = path;
            Original.Source = PixBitmap.ToBitmap(pix);
            ImageInfo.Text = $"{pix.Width} x {pix.Height}, {pix.Depth} bpp";
            Recompute();
        }
        catch (Exception e)
        {
            Status.Text = $"Cannot read {Path.GetFileName(path)}: {e.Message}";
        }
    }

    /// <summary>Runs the selected operation on a worker thread; only the newest run is shown.</summary>
    private async void Recompute()
    {
        if (_demo == null || _imagePath == null)
            return;
        Demo demo = _demo;
        Pix input = _images[_imagePath];
        var values = new Values(new Dictionary<string, double>(_values));
        int version = ++_version;
        Busy.IsVisible = true;
        try
        {
            var (pix, ms, note) = await Task.Run(() =>
            {
                Note = null;
                var sw = Stopwatch.StartNew();
                Pix result = demo.Run(input, values);
                return (result, sw.Elapsed.TotalMilliseconds, Note);
            });
            if (version != _version)
                return;   // a newer run has started meanwhile
            _result = pix;
            Bitmap bitmap = await Task.Run(() => (Bitmap)PixBitmap.ToBitmap(pix));
            if (version != _version)
                return;
            Result.Source = bitmap;
            NoteText.Text = note ?? "";
            Status.Text = $"{demo.Name}: {ms:F1} ms    result {pix.Width} x {pix.Height}, {pix.Depth} bpp    GPU: {GpuName()}";
        }
        catch (Exception e)
        {
            if (version == _version)
            {
                Result.Source = null;
                Status.Text = $"{demo.Name}: {e.Message}";
            }
        }
        finally
        {
            if (version == _version)
                Busy.IsVisible = false;
        }
    }

    private async void OpenImage(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open an image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp", "*.gif", "*.webp", "*.jp2", "*.pnm", "*.pgm", "*.ppm", "*.pbm"] },
                FilePickerFileTypes.All,
            ],
        });
        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path == null)
            return;
        _loadingDemo = true;
        Images.SelectedItem = null;
        _loadingDemo = false;
        SetImage(path);
    }

    private async void SaveResult(object? sender, RoutedEventArgs e)
    {
        if (_result == null)
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the result",
            SuggestedFileName = (_demo?.Name ?? "result").Replace(' ', '-').ToLowerInvariant() + ".png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
        });
        string? path = file?.TryGetLocalPath();
        if (path != null)
            Pix.Write(path, _result, L.IFF_PNG);
    }
}
