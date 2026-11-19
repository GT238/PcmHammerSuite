using System.Collections.Generic;

namespace PcmSuite.Tuning.Core.Models
{
    public class PatchEntry
    {
        public string Name    { get; set; } = "";
        public uint   Address { get; set; }
        public byte[] Data    { get; set; } = System.Array.Empty<byte>();
    }

    public class PatchDefinition
    {
        public string           Name        { get; set; } = "Unnamed";
        public string           Description { get; set; } = "";
        public string           Category    { get; set; } = "Patches";
        public string           SubCategory { get; set; } = "";
        public List<PatchEntry> Entries     { get; set; } = new();
    }
}
