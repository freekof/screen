using System;
using System.IO;
using System.Text.Json;

namespace ScreenCaptureTool
{
    public class Settings
    {
        public int DefaultOpacity { get; set; } = 80;
        public int BorderSize { get; set; } = 2;
        public string Hotkey { get; set; } = "F1";
        public uint HotkeyModifiers { get; set; } = 0; // 0: None, 1: Alt, 2: Control, 4: Shift, 8: Win
        public uint HotkeyCode { get; set; } = 0x70; // Default F1 (0x70)

        private static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static Settings Load()
        {
            if (!File.Exists(FilePath)) return new Settings();
            try 
            { 
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings(); 
            }
            catch 
            { 
                return new Settings(); 
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Save settings failed: " + ex.Message);
            }
        }
    }
}
