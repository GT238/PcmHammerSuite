using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tuning.Core.Models
{
    public class ParameterNode : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Name { get; set; } = "";
        public TableDefinition?  Table { get; set; }
        public PatchDefinition?  Patch { get; set; }
        public bool IsExpanded { get; set; } = false;
        public ObservableCollection<ParameterNode> Children { get; set; } = new();
        public bool IsCategory => Table == null && Patch == null;

        private bool _hasChanges;
        public bool HasChanges
        {
            get => _hasChanges;
            set { if (_hasChanges != value) { _hasChanges = value; Notify(nameof(HasChanges)); } }
        }

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; Notify(nameof(IsPinned)); } }
        }

        private bool _hasNote;
        public bool HasNote
        {
            get => _hasNote;
            set { if (_hasNote != value) { _hasNote = value; Notify(nameof(HasNote)); } }
        }

        private string _tag = "";
        public string Tag
        {
            get => _tag;
            set
            {
                if (_tag == value) return;
                _tag = value;
                Notify(nameof(Tag));
                Notify(nameof(HasTag));
                Notify(nameof(TagColor));
            }
        }

        public bool HasTag => !string.IsNullOrEmpty(_tag);

        // True when this table has no meaningful scaling equation (shows raw values).
        public bool IsUnscaled { get; set; }

        public string TagColor => _tag switch
        {
            "Done"       => "#4CAF50",
            "Needs Work" => "#FF9800",
            "WOT"        => "#2196F3",
            "Review"     => "#9C27B0",
            "Skip"       => "#F44336",
            _            => "Transparent",
        };

        /// <summary>
        /// Rich tooltip text shown when hovering the tree item.
        /// Returns null for category/folder nodes.
        /// </summary>
        public string? TooltipText
        {
            get
            {
                if (Table != null) return BuildTableTooltip(Table);
                if (Patch != null) return BuildPatchTooltip(Patch);
                return null;
            }
        }

        private static string BuildTableTooltip(TableDefinition t)
        {
            var sb = new StringBuilder();
            sb.AppendLine(t.Name);

            if (!string.IsNullOrWhiteSpace(t.Description))
                sb.AppendLine(t.Description);

            sb.AppendLine();

            // Dimensions
            if (t.Rows > 1)
                sb.AppendLine($"Size:      {t.Rows} rows × {t.Columns} columns");
            else
                sb.AppendLine($"Size:      {t.Columns} value{(t.Columns > 1 ? "s" : "")}");

            // Axes
            if (!string.IsNullOrEmpty(t.XUnits))
                sb.AppendLine($"X axis:    {t.XUnits}");
            if (!string.IsNullOrEmpty(t.YUnits))
                sb.AppendLine($"Y axis:    {t.YUnits}");
            if (!string.IsNullOrEmpty(t.ZUnits))
                sb.AppendLine($"Units:     {t.ZUnits}");

            // Scaling
            if (!string.IsNullOrEmpty(t.ScalingEquation) && t.ScalingEquation != "X")
                sb.AppendLine($"Equation:  {t.ScalingEquation}");

            // Storage
            sb.AppendLine($"Address:   0x{t.Address:X6}");
            sb.AppendLine($"Storage:   {t.ElementBits}-bit {(t.IsSigned ? "signed" : "unsigned")} {(t.IsBigEndian ? "big-endian" : "little-endian")}");

            return sb.ToString().TrimEnd();
        }

        private static string BuildPatchTooltip(PatchDefinition p)
        {
            var sb = new StringBuilder();
            sb.AppendLine(p.Name);

            if (!string.IsNullOrWhiteSpace(p.Description))
                sb.AppendLine(p.Description);

            sb.AppendLine();
            sb.Append($"Entries:   {p.Entries.Count}");

            return sb.ToString().TrimEnd();
        }
    }
}