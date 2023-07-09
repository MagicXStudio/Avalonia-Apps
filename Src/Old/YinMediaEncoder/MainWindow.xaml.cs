﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Controls;
using EmergenceGuardian.WpfCommon;
using EmergenceGuardian.FFmpeg;
using EmergenceGuardian.MediaEncoder;
using EmergenceGuardian.Avisynth;

namespace YinMediaEncoder {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public static MainWindow Instance { get; private set; }

        private WindowHelper helper;
        private WmpPlayerWindow playerOriginal;
        private WmpPlayerWindow playerChanges;
        private MediaEncoderSettings encodeSettings = new MediaEncoderSettings();
        public MediaEncoderBusiness business = new MediaEncoderBusiness();
        private bool isBinding = false;

        public MainWindow() {
            Instance = this;
            // Don't let Yin Media Encoder run if there are FFmpeg processes running.
            if (MediaProcesses.GetFFmpegProcesses().Any()) {
                if (MessageBox.Show("There are FFmpeg processes running. Would you like to stop them?\r\n\r\nTo avoid conflicts, please wait until these processes are finished before running Yin Media Encoder.", "FFmpeg Processes Running", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) == MessageBoxResult.Yes) {
                    foreach (Process item in MediaProcesses.GetFFmpegProcesses()) {
                        MediaProcesses.SoftKill(item);
                    }
                }
                if (MediaProcesses.GetFFmpegProcesses().Any()) {
                    Application.Current.Shutdown();
                    return;
                }
            }

            InitializeComponent();
            // SessionCore.Instance.Start(this, Properties.Resources.AppIcon);
            helper = new WindowHelper(this);
            AppPaths.ConfigureFFmpegPaths(this);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) {
            this.DataContext = encodeSettings;
            SettingsTab.Items.CurrentChanging += new CurrentChangingEventHandler(Items_CurrentChanging);
            ((FFmpegUserInterfaceManager)FFmpegConfig.UserInterfaceManager).JobClosed += UserInterfaceManager_JobClosed;
            if (MediaPlayer.WindowsMediaPlayer.IsWmpInstalled) {
                playerOriginal = new WmpPlayerWindow();
                playerOriginal.Title = "Original";
                playerOriginal.WindowState = WindowState.Maximized;
                playerChanges = new WmpPlayerWindow();
                playerChanges.Title = "Preview Changes";
                playerChanges.WindowState = WindowState.Maximized;
                playerChanges.Player.Player.PositionChanged += Player_PositionChanged;
            } else {
                PreviewOriginalButton.Visibility = Visibility.Hidden;
                PreviewChangesButton.Visibility = Visibility.Hidden;
            }
            //if (Settings.SavedFile.MediaPlayerApp != MediaPlayerApplication.Mpc)
            //    PreviewMpcButton.Visibility = Visibility.Hidden;
            business.EncodingCompleted += business_EncodingCompleted;
            business.EncodingFailed += business_EncodingFailed;
            ProcessingQueueList.ItemsSource = business.ProcessingQueue;
            //MpcConfigBusiness.IsSvpEnabled = false;

            MediaEncoderSettings RecoverSettings = await business.AutoLoadPreviewFileAsync();
            if (RecoverSettings != null)
                SetEncodeSettings(RecoverSettings);

            encodeSettings.AutoCalculateSize = true;

            business.AutoLoadJobs();
            if (business.ProcessingQueue.Count == 0)
                PauseButton_Click(null, null);

            // Run GPU test on startup.
            await business.RunGpuTest();
        }

        /// <summary>
        /// When position changes in PreviewChanges and both players are on pause, the Original will go to the same position.
        /// </summary>
        private void Player_PositionChanged(object sender, EventArgs e) {
            MediaPlayer.WindowsMediaPlayer Player1 = playerChanges.Player.Player;
            MediaPlayer.WindowsMediaPlayer Player2 = playerOriginal.Player.Player;
            if (Player1 != null && Player2 != null && playerOriginal.IsVisible && playerChanges.IsVisible && !Player1.IsPlaying && !Player2.IsPlaying)
                Player2.SetFramePosition(encodeSettings.TrimStart ?? 0 + Player1.Position + .195);
        }

        private void business_EncodingCompleted(object sender, EncodingCompletedEventArgs e) {
            Application.Current.Dispatcher.Invoke(() => EncodingCompletedWindow.Instance(this, e));
        }

        private void business_EncodingFailed(object sender, EncodingCompletedEventArgs e) {
            Application.Current.Dispatcher.Invoke(() => EncodingFailedWindow.Instance(this, e));
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            FFmpegConfig.UserInterfaceManager.AppExited = true;
            foreach (Window item in OwnedWindows) {
                item.Close();
            }
            playerOriginal?.Player?.Close();
            playerChanges?.Player?.Close();
            await business.DeletePreviewFilesAsync();
        }

        private void Window_Activated(object sender, EventArgs e) {
            if (playerChanges?.Player.Position > 0)
                encodeSettings.Position = playerChanges.Player.Position;
        }

        private async void SelectVideoButton_Click(object sender, RoutedEventArgs e) {
            string SelectedFile = null;
            string DisplayName = null;
            //if (SelectVideoButton.Content == MenuSelectFromPlaylist.Header) {
            //VideoListItem PlaylistItem = SearchVideoWindow.Instance(new SearchSettings() {
            //    MediaType = MediaType.Video,
            //    ConditionField = FieldConditionEnum.FileExists,
            //    ConditionValue = BoolConditionEnum.Yes,
            //    RatingCategory = "Height",
            //    RatingOperator = OperatorConditionEnum.Smaller
            //});
            //if (PlaylistItem != null) {
            //    SelectedFile = Settings.NaturalGroundingFolder + PlaylistItem.FileName;
            //    DisplayName = PlaylistItem.FileName;
            //}
            //} else {
            string ExtFilter = string.Format("Video Files|*{0}", string.Join(";*", AppPaths.VideoExtensions));
            SelectedFile = FileFolderDialog.ShowFileDialog(null, ExtFilter);
            DisplayName = SelectedFile;
            //}
            if (!string.IsNullOrEmpty(SelectedFile)) {
                HidePreview();
                encodeSettings.AutoCalculateSize = false;
                encodeSettings.FilePath = null;
                encodeSettings.CustomScript = null;
                SettingsTab.SelectedIndex = 0;
                encodeSettings.FilePath = SelectedFile;
                encodeSettings.DisplayName = DisplayName;
                encodeSettings.CropBottom = 0;
                encodeSettings.CropLeft = 0;
                encodeSettings.CropRight = 0;
                encodeSettings.CropTop = 0;

                try {
                    await business.PreparePreviewFile(encodeSettings, true, true);
                } catch (Exception ex) {
                    MessageBox.Show(this, ex.Message, "Cannot Open File", MessageBoxButton.OK, MessageBoxImage.Error);
                    encodeSettings.FilePath = "";
                }
                encodeSettings.AutoCalculateSize = true;
            }
        }

        //private void MenuSelect_Click(object sender, RoutedEventArgs e) {
        //    SelectVideoButton.IsOpen = false;
        //    SelectVideoButton.Content = ((MenuItem)sender).Header;
        //    SelectVideoButton_Click(sender, e);
        //}

        public void SetEncodeSettings(MediaEncoderSettings value) {
            isBinding = true;
            SettingsTab.SelectedIndex = (String.IsNullOrEmpty(value.CustomScript) ? 0 : 2);
            encodeSettings = value;
            this.DataContext = value;
            isBinding = false;
        }

        public void HidePreview() {
            if (playerOriginal?.Visibility == Visibility.Visible)
                playerOriginal.Visibility = Visibility.Hidden;
            if (playerChanges?.Visibility == Visibility.Visible)
                playerChanges.Visibility = Visibility.Hidden;
        }

        private async void PreviewOriginalButton_Click(object sender, RoutedEventArgs e) {
            await PlayVideoAsync(playerOriginal, encodeSettings.FilePath);
        }

        private async void PreviewChangesButton_Click(object sender, RoutedEventArgs e) {
            if (Validate()) {
                business.GenerateScript(encodeSettings, true, false);
                await PlayVideoAsync(playerChanges, PathManager.TempFilesPath + "Preview.avs");
            }
        }

        private void PreviewMpcButton_Click(object sender, RoutedEventArgs e) {
            if (Validate()) {
                // Show cropping borders if WMP isn't available.
                bool Preview = false; // !MediaPlayer.WindowsMediaPlayer.IsWmpInstalled;
                business.GenerateScript(encodeSettings, Preview, false);
                // MpcConfigBusiness.StartMpc(PathManager.TempFilesPath + "Preview.avs");
            }
        }

        private async Task PlayVideoAsync(WmpPlayerWindow playerWindow, string fileName) {
            playerWindow.Show();
            playerWindow.Activate();
            await playerWindow.Player.OpenFileAsync(fileName);
            playerWindow.Player.MediaOpened += (s2, e2) => {
                if (encodeSettings.Position.HasValue)
                    playerWindow.Player.Position = encodeSettings.Position.Value;
                playerWindow.Player.Player.Pause();
            };

            //await Task.FromResult(0);
        }

        private async void EncodeButton_Click(object sender, RoutedEventArgs e) {
            if (!Validate())
                return;

            MediaEncoderSettings EncodeSettings = encodeSettings;
            try {
                HidePreview();
                SetEncodeSettings((MediaEncoderSettings)encodeSettings.Clone());
                encodeSettings.FilePath = "";
                encodeSettings.DisplayName = "";
                await Task.Delay(100); // Wait for media player file to be released.
                business.PrepareJobFiles(EncodeSettings);
                business.AddJobToQueue(EncodeSettings);
            } catch (Exception ex) {
                if (!encodeSettings.ConvertToAvi || System.IO.File.Exists(PathManager.TempFilesPath + "Preview.avi"))
                    SetEncodeSettings(EncodeSettings);
                MessageBox.Show(this, ex.Message, "Encoding Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        private bool Validate() {
            bool Error = !this.IsValid() ||
                string.IsNullOrEmpty(encodeSettings.FilePath) ||
                !encodeSettings.SourceHeight.HasValue ||
                !encodeSettings.SourceWidth.HasValue ||
                !encodeSettings.SourceFrameRate.HasValue ||
                !encodeSettings.SourceAspectRatio.HasValue;
            if (Error)
                MessageBox.Show(this, "You must enter required file information.", "Validation Error");
            else if (encodeSettings.Trim && encodeSettings.TrimStart.HasValue && encodeSettings.TrimEnd.HasValue && encodeSettings.TrimEnd < encodeSettings.TrimStart) {
                Error = true;
                MessageBox.Show(this, "Trim End must be greater than TrimStart.", "Validation Error");
            } else if (string.IsNullOrEmpty(encodeSettings.Container)) {
                Error = true;
                MessageBox.Show(this, "You must select valid encoding options.");
            }
            return !Error;
        }

        private async void ConvertToAviCheckbox_Click(object sender, RoutedEventArgs e) {
            if (isBinding)
                return;
            ConvertToAviCheckbox.IsEnabled = false;
            await business.PreparePreviewFile(encodeSettings, false, false);
            ConvertToAviCheckbox.IsEnabled = true;
        }

        /// <summary>
        /// Generates script when going to Script tab, and prevents returning to Transform tab without a confirmation to lose changes.
        /// </summary>
        private void Items_CurrentChanging(object sender, CurrentChangingEventArgs e) {
            if (string.IsNullOrEmpty(encodeSettings.FilePath))
                return;

            SettingsTab.Focus();
            var item = ((ICollectionView)sender).CurrentItem;
            bool Cancel = false;
            // Generate script when going to Script tab.
            if (SettingsTab.SelectedIndex == 2 && string.IsNullOrEmpty(encodeSettings.CustomScript)) {
                if (Validate())
                    business.GenerateCustomScript(encodeSettings);
                else
                    Cancel = true;
            } else if (SettingsTab.SelectedIndex == 0 && !string.IsNullOrEmpty(encodeSettings.CustomScript)) {
                // Ask for confirmation before going back to Transform tab and losing changes.
                if (business.CustomScriptHasChanges(encodeSettings))
                    if (MessageBox.Show("You will lose any changes to your script. Are you sure?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.No)
                        Cancel = true;
                if (!Cancel)
                    encodeSettings.CustomScript = null;
            }

            // Revert to previously-selected tab.
            if (Cancel) {
                e.Cancel = true;
                SettingsTab.SelectedItem = item;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private async void CalculateAudioGain_Click(object sender, RoutedEventArgs e) {
            CalculateAudioGain.IsEnabled = false;
            CalculateAudioGain.Content = "Wait";
            float? Gain = await Task.Run(() => AvisynthTools.GetAudioGain(encodeSettings.FilePath, null));
            if (Gain.HasValue)
                encodeSettings.AudioGain = Gain;
            CalculateAudioGain.IsEnabled = true;
            CalculateAudioGain.Content = "Auto";
        }

        private void DeshakerSettingsButton_Click(object sender, RoutedEventArgs e) {
            if (Validate()) {
                if (DeshakerWindow.Instance(business, encodeSettings) == true) {
                    encodeSettings.DeshakerSettings.PrescanCompleted = false;
                }
            }
        }

        private async void DeshakerPrescanButton_Click(object sender, RoutedEventArgs e) {
            encodeSettings.DeshakerSettings.PrescanCompleted = false;
            if (Validate() && DeshakerPrescanWindow.Instance(business, encodeSettings)) {
                DeshakerPrescanButton.IsEnabled = false;
                business.GenerateScript(encodeSettings, false, true);
                CompletionStatus Result = await Task.Run(() => business.GenerateDeshakerLog(encodeSettings, business.GetPreviewSourceFile(encodeSettings))).ConfigureAwait(false);
                encodeSettings.DeshakerSettings.PrescanCompleted = Result == CompletionStatus.Success;
                business.DeshakerSourceSettings = encodeSettings;
                Dispatcher.Invoke(() => DeshakerPrescanButton.IsEnabled = true);
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e) {
            if (business.IsEncoding) {
                if (business.PauseEncoding())
                    PauseButton.IsEnabled = false;
                PauseButtonImage.Source = new BitmapImage(new Uri(@"/YinMediaEncoder;component/Icons/play.png", UriKind.Relative));
                PauseButton.ToolTip = "Start";
            } else {
                business.ResumeEncoding();
                PauseButtonImage.Source = new BitmapImage(new Uri(@"/YinMediaEncoder;component/Icons/pause.png", UriKind.Relative));
                PauseButton.ToolTip = "Pause";
            }
        }

        private void UserInterfaceManager_JobClosed(object sender, JobEventArgs e) {
            PauseButton.IsEnabled = true;
        }

        /// <summary>
        /// Cancels an encoding task and display it to change settings.
        /// </summary>
        /// <param name="settings">The encoding task to display.</param>
        public async void EditEncodingTask(MediaEncoderSettings settings) {
            HidePreview();
            await business.MovePreviewFilesAsync(settings);
            SetEncodeSettings(settings);
            PathManager.DeleteJobFiles(settings.JobIndex);
            settings.JobIndex = -1;
            business.ProcessingQueue.Remove(settings);
        }

        private void ProcessingQueueList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            var dataContext = ((FrameworkElement)e.OriginalSource).DataContext as MediaEncoderSettings;
            if (dataContext != null && e.LeftButton == MouseButtonState.Pressed) {
                if (!business.IsEncoding || business.ProcessingQueue.IndexOf(dataContext) > 0)
                    EditEncodingTask(dataContext);
            }
        }

        private void VideoActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            VideoAction Value = (VideoAction)VideoActionCombo.SelectedIndex;
            if (Value == VideoAction.x264 || Value == VideoAction.x264_10bit) {
                encodeSettings.EncodeQuality = 23;
                encodeSettings.EncodePreset = EncodePresets.veryslow;
            } else if (Value == VideoAction.x265) {
                encodeSettings.EncodeQuality = 22;
                encodeSettings.EncodePreset = EncodePresets.medium;
            }
        }
    }
}
