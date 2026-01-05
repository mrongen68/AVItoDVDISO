using System;
using System.IO;
using System.Threading;
using System.Windows;
using AVItoDVDISO.Tools;

namespace AVItoDVDISO.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var vm = new MainViewModel();
            DataContext = vm;

            Loaded += (_, _) =>
            {
                try
                {
                    vm.Initialize();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        private MainViewModel VM => (MainViewModel)DataContext;

        // XAML expects Add_Click
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select video files",
                Filter = "Video files|*.avi;*.mp4;*.mkv;*.mov;*.mpg;*.mpeg;*.wmv|All files|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                foreach (var f in dlg.FileNames)
                    VM.AddSource(f);
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            VM.RemoveSelected();
        }

        // XAML expects Up_Click
        private void Up_Click(object sender, RoutedEventArgs e)
        {
            VM.MoveSelected(-1);
        }

        // XAML expects Down_Click
        private void Down_Click(object sender, RoutedEventArgs e)
        {
            VM.MoveSelected(1);
        }

        // XAML expects Browse_Click
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK && Directory.Exists(dlg.SelectedPath))
            {
                VM.OutputPath = dlg.SelectedPath;
            }
        }

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            VM.OpenOutputFolder();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            VM.Cancel();
        }

        private async void Convert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
                var needDvdauthor = VM.ExportFolder;
                var needXorriso = VM.ExportIso;

                var mgr = new ToolManager();
                if (!mgr.HasAllRequiredTools(toolsDir, needDvdauthor, needXorriso))
                {
                    var msg =
                        "Required tools are missing and will be downloaded to the tools folder.\n\n" +
                        "This may take a few minutes.\n\n" +
                        "Continue?";
                    var res = System.Windows.MessageBox.Show(this, msg, "Download tools", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (res != MessageBoxResult.OK)
                        return;

                    VM.LogText += "Downloading required tools..." + Environment.NewLine;

                    var status = await mgr.EnsureToolsAsync(
                        toolsDir,
                        needDvdauthor,
                        needXorriso,
                        line => VM.LogText += line + Environment.NewLine,
                        CancellationToken.None);

                    if (!status.Ok)
                    {
                        System.Windows.MessageBox.Show(this, status.Message, "Tools download failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        VM.LogText += "Tools download failed: " + status.Message + Environment.NewLine;
                        return;
                    }

                    VM.LogText += "Tools ready." + Environment.NewLine;
                }

                await VM.StartConvertAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
