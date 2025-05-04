using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace The_Evil_Within_Audio_Manager
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _streamedPath;
        private string _tangoresourcePath;
        private string _outputPath;
        private int _progressValue;
        private int _progressMaximum = 100;
        private bool _isExtracting = false;

        public event PropertyChangedEventHandler PropertyChanged;
        public string StreamedPath
        {
            get => _streamedPath;
            set
            {
                _streamedPath = value;
                OnPropertyChanged(nameof(StreamedPath));
                OnPropertyChanged(nameof(CanExtract));
            }
        }

        public string TangoresourcePath
        {
            get => _tangoresourcePath;
            set
            {
                _tangoresourcePath = value;
                OnPropertyChanged(nameof(TangoresourcePath));
                OnPropertyChanged(nameof(CanExtract));
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                _outputPath = value;
                OnPropertyChanged(nameof(OutputPath));
                OnPropertyChanged(nameof(CanExtract));
            }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public int ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                _progressMaximum = value;
                OnPropertyChanged(nameof(ProgressMaximum));
            }
        }

        public bool IsExtracting
        {
            get => _isExtracting;
            set
            {
                _isExtracting = value;
                OnPropertyChanged(nameof(IsExtracting));
                OnPropertyChanged(nameof(CanExtract));
            }
        }

        public bool CanExtract
        {
            get
            {
                return !IsExtracting &&
                       !string.IsNullOrEmpty(OutputPath) &&
                       (!string.IsNullOrEmpty(StreamedPath) || !string.IsNullOrEmpty(TangoresourcePath)) &&
                       (string.IsNullOrEmpty(StreamedPath) || File.Exists(StreamedPath)) &&
                       (string.IsNullOrEmpty(TangoresourcePath) || File.Exists(TangoresourcePath));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Exception ex = (Exception)args.ExceptionObject;
                MessageBox.Show($"Error not treated {ex.Message}\n\nDetails: {ex.StackTrace}",
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBox.AppendText(message + Environment.NewLine);
                StatusTextBox.ScrollToEnd();
            });
        }

        private void SelectStreamedButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select .streamed file",
                Filter = "Streamed Files (*.streamed)|*.streamed|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                StreamedPath = openFileDialog.FileName;

                string baseName = Path.GetFileNameWithoutExtension(StreamedPath);
                if (baseName.EndsWith("_en", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName.Substring(0, baseName.Length - 3);
                }

                string dir = Path.GetDirectoryName(StreamedPath);
                string possibleTangoresource = Path.Combine(dir, $"{baseName}.tangoresource");

                if (File.Exists(possibleTangoresource))
                {
                    TangoresourcePath = possibleTangoresource;
                    LogMessage("Corresponding .tangoresource file found, yay");
                }

                if (string.IsNullOrEmpty(OutputPath))
                {
                    string fileName = Path.GetFileNameWithoutExtension(StreamedPath ?? TangoresourcePath);
                    OutputPath = Path.Combine(dir, fileName + "_Extracted");
                }
            }
        }

        private void SelectTangoresourceButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select .tangoresource",
                Filter = "TangoResource Files (*.tangoresource)|*.tangoresource|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TangoresourcePath = openFileDialog.FileName;

                string dir = Path.GetDirectoryName(TangoresourcePath);
                string baseName = Path.GetFileNameWithoutExtension(TangoresourcePath);

                string[] possibleStreamedPaths = {
                    Path.Combine(dir, $"{baseName}_en.streamed"),
                    Path.Combine(dir, $"{baseName}.streamed")
                };

                foreach (var possiblePath in possibleStreamedPaths)
                {
                    if (File.Exists(possiblePath))
                    {
                        StreamedPath = possiblePath;
                        LogMessage("Corresponding .streamed file found, yay");
                        break;
                    }
                }

                if (string.IsNullOrEmpty(OutputPath))
                {
                    OutputPath = Path.Combine(dir, "Extracted");
                }
            }
        }

        private void SelectOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder for extracted files"
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputPath = folderDialog.SelectedPath;
            }
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(StreamedPath) && string.IsNullOrEmpty(TangoresourcePath))
            {
                MessageBox.Show("Select at least a file for extraction",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(OutputPath))
            {
                MessageBox.Show("Select output folder",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!Directory.Exists(OutputPath))
            {
                Directory.CreateDirectory(OutputPath);
            }

            IsExtracting = true;
            StatusTextBox.Clear();

            Task.Run(() =>
            {
                try
                {
                    var extractor = new TangoExtractor(
                        logMessage => Dispatcher.Invoke(() => LogMessage(logMessage)),
                        progress => Dispatcher.Invoke(() => ProgressValue = progress),
                        maxProgress => Dispatcher.Invoke(() => ProgressMaximum = maxProgress)
                    );

                    if (!string.IsNullOrEmpty(StreamedPath))
                    {
                        extractor.ExtractStreamed(StreamedPath);
                    }
                    else if (!string.IsNullOrEmpty(TangoresourcePath))
                    {
                        extractor.Extract(TangoresourcePath, OutputPath);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Extraction completed, You can found your audio files at the WAVs folder inside the extracted folder",
                            "Sucess", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        string errorMsg = $"Error during extraction {ex.Message}";
                        LogMessage(errorMsg);
                        MessageBox.Show(errorMsg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() => IsExtracting = false);
                }
            });
        }
    }
}
