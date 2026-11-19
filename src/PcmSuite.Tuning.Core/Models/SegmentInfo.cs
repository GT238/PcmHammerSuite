using System.ComponentModel;

namespace PcmSuite.Tuning.Core.Models
{
    public class SegmentInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public int    Index           { get; init; }
        public string Name            { get; init; } = "";
        public string Description     { get; init; } = "";
        public uint   StartAddress    { get; init; }
        public uint   EndAddress      { get; init; }
        public uint   ChecksumAddress { get; init; }
        public uint   ByteCount       => EndAddress >= StartAddress ? EndAddress - StartAddress + 1 : 0;

        // Checksum words read from each BIN
        public string TargetChecksum  { get; set; } = "—";
        public string DonorChecksum   { get; set; } = "—";

        public bool IsDifferent       { get; set; } = false;

        /// <summary>False when the donor's segment covers different addresses, so it cannot be copied.</summary>
        public bool CanSwap           { get; set; } = true;

        public string Note            { get; set; } = "";

        /// <summary>Copying this one can leave the BIN inconsistent or change the vehicle's identity.</summary>
        public bool IsRisky           { get; set; }

        public uint   SizeBytes       => ByteCount;

        public string SizeText        => $"{ByteCount / 1024.0:F1} KB";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; Notify(nameof(IsSelected)); } }
        }
    }
}
