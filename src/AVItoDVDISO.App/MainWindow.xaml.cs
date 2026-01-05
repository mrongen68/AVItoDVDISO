using System.Windows;

namespace AVItoDVDISO.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.Initialize();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select video files",
            Filter = "Video files|*.avi;*.mp4;*.mkv;*.mov;*.mpeg;*.mpg|All files|*.*",
            Multiselect = true
        };
        if (ofd.ShowDialog() == true)
        {
            foreach (var f in ofd.FileNames)
                _vm.AddSource(f);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => _vm.RemoveSelected();
    private void Up_Click(object sender, RoutedEventArgs e) => _vm.MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => _vm.MoveSelected(1);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.Description = "Select output folder";
        dlg.UseDescriptionForTitle = true;

        var res = dlg.ShowDialog();
        if (res == System.Windows.Forms.DialogResult.OK)
            _vm.OutputPath = dlg.SelectedPath;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.StartConvertAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _vm.Cancel();
    private void OpenOutput_Click(object sender, RoutedEventArgs e) => _vm.OpenOutputFolder();

}
