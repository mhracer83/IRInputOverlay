using System;
using System.IO;
using System.Text.Json;

namespace IRInputOverlay
{
    public class AppSettings
    {
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Width { get; set; } = 860;
        public double Height { get; set; } = 380;
        public int BackgroundOpacity { get; set; } = 100;
        public bool LockAspectRatio { get; set; } = false;
        public int BarAlphaPercent { get; set; } = 100; // 100 = instant

        // Absolute path to the user-picked wheel image (managed copy in AppData). Null/empty => use default.
        public string? WheelImagePath { get; set; }

        // settings.json in LocalAppData\IRInputOverlay
        public static string GetPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir  = Path.Combine(root, "IRInputOverlay");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        // Folder for user assets: LocalAppData\IRInputOverlay\Assets
        public static string GetUserAssetsDir()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir  = Path.Combine(root, "IRInputOverlay", "Assets");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static AppSettings Load()
        {
            try
            {
                var path = GetPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var path = GetPath();
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
