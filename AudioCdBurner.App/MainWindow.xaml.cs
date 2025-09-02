using Microsoft.Win32;
using System.Windows;

namespace AudioCdBurner.App
{
    public partial class MainWindow : Window
    {
        public MainWindow() => InitializeComponent();

        private void AddFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Audio|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg|Alle filer|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var path in dlg.FileNames)
                    FilesList.Items.Add(path);

                CountText.Text = $"Valgt: {FilesList.Items.Count}";
            }
        }
    }
}