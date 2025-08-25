using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace IRInputOverlay
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _main;
        private readonly AppSettings _settings;

        public SettingsWindow(MainWindow main, AppSettings settings)
        {
            InitializeComponent();
            _main = main;
            _settings = settings;

            // ----- Existing controls wiring -----
            OpacitySlider.Value   = _settings.BackgroundOpacity;
            OpacityValue.Text     = _settings.BackgroundOpacity.ToString();
            LockAspect.IsChecked  = _settings.LockAspectRatio;
            BarRespSlider.Value   = _settings.BarAlphaPercent;
            BarRespValue.Text     = _settings.BarAlphaPercent.ToString();

            OpacitySlider.ValueChanged += (_, __) =>
            {
                _settings.BackgroundOpacity = (int)OpacitySlider.Value;
                OpacityValue.Text = _settings.BackgroundOpacity.ToString();
                _main.ApplyBackgroundOpacity(_settings.BackgroundOpacity);
                _settings.Save();
            };

            LockAspect.Checked += (_, __) =>
            {
                _settings.LockAspectRatio = true;
                _main.SetLockAspect(true);
                _settings.Save();
            };
            LockAspect.Unchecked += (_, __) =>
            {
                _settings.LockAspectRatio = false;
                _main.SetLockAspect(false);
                _settings.Save();
            };

            BarRespSlider.ValueChanged += (_, __) =>
            {
                _settings.BarAlphaPercent = (int)BarRespSlider.Value;
                BarRespValue.Text = _settings.BarAlphaPercent.ToString();
                _main.SetBarAlpha(_settings.BarAlphaPercent);
                _settings.Save();
            };

            ResetBtn.Click += (_, __) =>
            {
                _settings.BackgroundOpacity = 100;
                _settings.LockAspectRatio   = false;
                _settings.BarAlphaPercent   = 100;
                _settings.Save();

                OpacitySlider.Value  = _settings.BackgroundOpacity;
                LockAspect.IsChecked = _settings.LockAspectRatio;
                BarRespSlider.Value  = _settings.BarAlphaPercent;

                _main.ApplyBackgroundOpacity(_settings.BackgroundOpacity);
                _main.SetLockAspect(_settings.LockAspectRatio);
                _main.SetBarAlpha(_settings.BarAlphaPercent);
            };

            CloseBtn.Click += (_, __) => Close();
            // ------------------------------------

            // ----- Wheel image picker -----
            WheelPathLabel.Text = string.IsNullOrWhiteSpace(_settings.WheelImagePath)
                ? "(default)"
                : _settings.WheelImagePath!;

            ChooseWheelBtn.Click += (_, __) =>
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Choose wheel image",
                    Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                    Multiselect = false
                };
                if (dlg.ShowDialog(this) == true && File.Exists(dlg.FileName))
                {
                    try
                    {
                        var assetsDir = AppSettings.GetUserAssetsDir();
                        var ext = Path.GetExtension(dlg.FileName);
                        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                        var destPath = Path.Combine(assetsDir, "wheel_user" + ext);

                        File.Copy(dlg.FileName, destPath, overwrite: true);

                        _settings.WheelImagePath = destPath;
                        _settings.Save();

                        WheelPathLabel.Text = _settings.WheelImagePath ?? "(default)";
                        _main.ApplyWheelImage(); // refresh immediately
                    }
                    catch (Exception)
                    {
                        MessageBox.Show(this,
                            "Couldn't copy the image. Try a different file.",
                            "Wheel image",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            };

            ClearWheelBtn.Click += (_, __) =>
            {
                _settings.WheelImagePath = null;
                _settings.Save();
                WheelPathLabel.Text = "(default)";
                _main.ApplyWheelImage();
            };
            // --------------------------------
        }
    }
}
