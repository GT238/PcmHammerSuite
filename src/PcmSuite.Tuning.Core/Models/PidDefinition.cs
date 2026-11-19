namespace PcmSuite.Tuning.Core.Models
{
    /// <summary>OBD2 mode 01 PID definition with scaling.</summary>
    public class PidDefinition
    {
        public byte   Pid      { get; init; }
        public string Name     { get; init; } = "";
        public string Units    { get; init; } = "";
        public int    Bytes    { get; init; } = 1;   // bytes of data expected (A, AB, etc.)
        public string Formula  { get; init; } = "A"; // A, B = raw bytes; e.g. "((A*256)+B)/4"

        /// <summary>Standard GM P01/P59 PIDs relevant to tuning.</summary>
        public static readonly PidDefinition[] Standard = new[]
        {
            new PidDefinition { Pid=0x04, Name="Engine Load",           Units="%",     Bytes=1, Formula="A*100/255" },
            new PidDefinition { Pid=0x05, Name="Coolant Temp",          Units="°C",    Bytes=1, Formula="A-40" },
            new PidDefinition { Pid=0x06, Name="ST Fuel Trim B1",       Units="%",     Bytes=1, Formula="A*100/128-100" },
            new PidDefinition { Pid=0x07, Name="LT Fuel Trim B1",       Units="%",     Bytes=1, Formula="A*100/128-100" },
            new PidDefinition { Pid=0x0B, Name="Manifold Pressure",     Units="kPa",   Bytes=1, Formula="A" },
            new PidDefinition { Pid=0x0C, Name="Engine RPM",            Units="rpm",   Bytes=2, Formula="((A*256)+B)/4" },
            new PidDefinition { Pid=0x0D, Name="Vehicle Speed",         Units="km/h",  Bytes=1, Formula="A" },
            new PidDefinition { Pid=0x0F, Name="Intake Air Temp",       Units="°C",    Bytes=1, Formula="A-40" },
            new PidDefinition { Pid=0x10, Name="MAF Air Flow",          Units="g/s",   Bytes=2, Formula="((A*256)+B)/100" },
            new PidDefinition { Pid=0x11, Name="Throttle Position",     Units="%",     Bytes=1, Formula="A*100/255" },
            new PidDefinition { Pid=0x14, Name="O2 B1S1 Voltage",       Units="V",     Bytes=2, Formula="A/200" },
            new PidDefinition { Pid=0x15, Name="O2 B1S2 Voltage",       Units="V",     Bytes=2, Formula="A/200" },
            new PidDefinition { Pid=0x2F, Name="Fuel Level",            Units="%",     Bytes=1, Formula="A*100/255" },
            new PidDefinition { Pid=0x5C, Name="Oil Temp",              Units="°C",    Bytes=1, Formula="A-40" },
        };
    }
}
