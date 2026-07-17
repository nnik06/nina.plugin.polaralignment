using System.Text.RegularExpressions;

namespace NINA.Plugins.PolarAlignment.Avalon {
    public partial class UniversalPolarAlignment : UniversalPolarAlignmentBase {
        protected override string SystemName => "Avalon Polar Alignment System";

        private float xGearRatio = Properties.Settings.Default.AvalonXGearRatio;
        private float yGearRatio = Properties.Settings.Default.AvalonYGearRatio;

        public override float XGearRatio { get => xGearRatio; set => xGearRatio = value; }
        public override float YGearRatio { get => yGearRatio; set => yGearRatio = value; }

        protected override Regex GetStatusRegex() => StatusRegex();

        [GeneratedRegex(@"<(?<status>[^|>]+)\|(?:MPos|WPos):(?<x>[+-]?(?:\d+(?:\.\d*)?|\.\d+)),(?<y>[+-]?(?:\d+(?:\.\d*)?|\.\d+)),(?<z>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\|")]
        internal static partial Regex StatusRegex();
    }
}
