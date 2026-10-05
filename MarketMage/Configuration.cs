using System.Collections.Generic;
using Dalamud.Configuration;

namespace MarketMage;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public string World { get; set; } = "Cactuar";
    public bool HighQuality { get; set; }
    public bool CraftableOnly { get; set; }
    public bool CompareDataCenter { get; set; } = true;
    public List<uint> SelectedItems { get; set; } = [];
}
