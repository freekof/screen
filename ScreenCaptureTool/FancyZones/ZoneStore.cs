using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenCaptureTool.FancyZones
{
    public sealed class ZoneStore
    {
        private static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fancyzones.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public FancyZonesData Data { get; private set; } = new FancyZonesData();

        public event Action? Changed;

        public ZoneLayout? ActiveLayout
        {
            get
            {
                if (Data.Layouts.Count == 0) return null;
                return Data.Layouts.FirstOrDefault(l => l.Id == Data.ActiveLayoutId) ?? Data.Layouts[0];
            }
        }

        public static ZoneStore Load()
        {
            var store = new ZoneStore();
            FancyZonesData? loaded = null;

            if (File.Exists(FilePath))
            {
                try
                {
                    string json = File.ReadAllText(FilePath);
                    loaded = JsonSerializer.Deserialize<FancyZonesData>(json, JsonOptions);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Load fancyzones.json failed: " + ex.Message);
                }
            }

            if (loaded?.Layouts != null)
            {
                loaded.Layouts = loaded.Layouts.Where(IsLayoutValid).ToList();
            }

            if (loaded == null || loaded.Layouts == null || loaded.Layouts.Count == 0)
            {
                var defaults = LayoutTemplates.CreateDefaults();
                store.Data = new FancyZonesData
                {
                    Version = 1,
                    Layouts = defaults,
                    ActiveLayoutId = defaults[0].Id
                };
                store.Save();
                return store;
            }

            if (string.IsNullOrEmpty(loaded.ActiveLayoutId) || !loaded.Layouts.Any(l => l.Id == loaded.ActiveLayoutId))
            {
                loaded.ActiveLayoutId = loaded.Layouts[0].Id;
            }

            store.Data = loaded;
            return store;
        }

        public void SetActive(string id)
        {
            var match = Data.Layouts.FirstOrDefault(l => l.Id == id);
            if (match != null)
            {
                Data.ActiveLayoutId = match.Id;
            }
            else if (Data.Layouts.Count > 0)
            {
                Data.ActiveLayoutId = Data.Layouts[0].Id;
            }
            Save();
            Changed?.Invoke();
        }

        public void AddOrUpdate(ZoneLayout layout)
        {
            if (layout == null) return;
            int idx = Data.Layouts.FindIndex(l => l.Id == layout.Id);
            if (idx >= 0)
            {
                Data.Layouts[idx] = layout;
            }
            else
            {
                Data.Layouts.Add(layout);
            }
            Save();
            Changed?.Invoke();
        }

        public bool Remove(string id)
        {
            if (Data.Layouts.Count <= 1) return false;
            int idx = Data.Layouts.FindIndex(l => l.Id == id);
            if (idx < 0) return false;

            Data.Layouts.RemoveAt(idx);
            if (Data.ActiveLayoutId == id || !Data.Layouts.Any(l => l.Id == Data.ActiveLayoutId))
            {
                Data.ActiveLayoutId = Data.Layouts[0].Id;
            }
            Save();
            Changed?.Invoke();
            return true;
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(Data, JsonOptions);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Save fancyzones.json failed: " + ex.Message);
            }
        }

        private static bool IsLayoutValid(ZoneLayout? layout)
        {
            if (layout == null) return false;
            return layout.Kind switch
            {
                LayoutKind.Grid => layout.IsGridValid(),
                LayoutKind.Canvas => layout.CanvasZones != null && layout.CanvasZones.Count > 0,
                _ => false
            };
        }
    }
}
