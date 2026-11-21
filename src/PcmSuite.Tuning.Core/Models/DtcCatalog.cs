using System.Text.RegularExpressions;

namespace PcmSuite.Tuning.Core.Models;

/// <summary>
/// Short descriptions of the standard (generic) trouble codes, written for PcmSuite from the published SAE J2012 meanings.
/// Only codes whose meaning is the same on every make are listed; GM-specific P1xxx codes are deliberately left out because
/// their meaning varies by model.
/// </summary>
public static partial class DtcCatalog
{
    [GeneratedRegex(@"^[PBCU][0-3][0-9A-Fa-f]{3}$")]
    private static partial Regex CodePattern();

    private static readonly Dictionary<string, string> Descriptions = Build();

    /// <summary>How many codes have a description.</summary>
    public static int Count => Descriptions.Count;

    /// <summary>The description for a code such as "P0101", or null. The text may carry more after the code ("P0101 MAF"); only the first word is used.</summary>
    public static string? Describe(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        string code = label.Trim().Split(' ', '\t')[0].ToUpperInvariant();
        return CodePattern().IsMatch(code) && Descriptions.TryGetValue(code, out string? text) ? text : null;
    }

    /// <summary>True when the label starts with something shaped like a trouble code (P0101, U0107), described or not.</summary>
    public static bool IsCode(string? label) =>
        !string.IsNullOrWhiteSpace(label) && CodePattern().IsMatch(label.Trim().Split(' ', '	')[0]);

    /// <summary>True when the label is a trouble code and the text is part of the code ("P04", "0420") or of its description ("catalyst").</summary>
    public static bool Matches(string? label, string? text)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string code = label.Trim().Split(' ', '	')[0].ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
        {
            return false;
        }

        string find = text.Trim();
        return code.Contains(find, StringComparison.OrdinalIgnoreCase)
            || (Descriptions.TryGetValue(code, out string? description) && description.Contains(find, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when any row or column label of the table is a trouble code that matches the text. Needs two characters, so a stray digit does not match every table.</summary>
    public static bool TableMentions(TableDefinition table, string? text) =>
        text != null && text.Trim().Length >= 2
        && (table.YLabels.Any(l => Matches(l, text)) || table.XLabels.Any(l => Matches(l, text)));

    private static Dictionary<string, string> Build()
    {
        (string Code, string Text)[] rows =
        {
            ("P0016", "Crankshaft / camshaft position correlation (bank 1 sensor A)"),
            ("P0068", "MAP / MAF vs throttle position correlation"),
            ("P0101", "Mass airflow (MAF) sensor range / performance"),
            ("P0102", "MAF sensor circuit low"),
            ("P0103", "MAF sensor circuit high"),
            ("P0106", "MAP sensor range / performance"),
            ("P0107", "MAP sensor circuit low"),
            ("P0108", "MAP sensor circuit high"),
            ("P0110", "Intake air temperature (IAT) sensor circuit"),
            ("P0111", "IAT sensor range / performance"),
            ("P0112", "IAT sensor circuit low"),
            ("P0113", "IAT sensor circuit high"),
            ("P0116", "Coolant temperature (ECT) sensor range / performance"),
            ("P0117", "ECT sensor circuit low"),
            ("P0118", "ECT sensor circuit high"),
            ("P0120", "Throttle position sensor A circuit"),
            ("P0121", "Throttle position sensor A range / performance"),
            ("P0122", "Throttle position sensor A circuit low"),
            ("P0123", "Throttle position sensor A circuit high"),
            ("P0125", "Coolant too cool for closed loop fuel control"),
            ("P0128", "Coolant thermostat (below regulating temperature)"),
            ("P0131", "O2 sensor circuit low voltage (bank 1 sensor 1)"),
            ("P0132", "O2 sensor circuit high voltage (bank 1 sensor 1)"),
            ("P0133", "O2 sensor slow response (bank 1 sensor 1)"),
            ("P0134", "O2 sensor no activity (bank 1 sensor 1)"),
            ("P0135", "O2 sensor heater circuit (bank 1 sensor 1)"),
            ("P0136", "O2 sensor circuit (bank 1 sensor 2)"),
            ("P0137", "O2 sensor circuit low voltage (bank 1 sensor 2)"),
            ("P0138", "O2 sensor circuit high voltage (bank 1 sensor 2)"),
            ("P0140", "O2 sensor no activity (bank 1 sensor 2)"),
            ("P0141", "O2 sensor heater circuit (bank 1 sensor 2)"),
            ("P0147", "O2 sensor heater circuit (bank 1 sensor 3)"),
            ("P0151", "O2 sensor circuit low voltage (bank 2 sensor 1)"),
            ("P0152", "O2 sensor circuit high voltage (bank 2 sensor 1)"),
            ("P0153", "O2 sensor slow response (bank 2 sensor 1)"),
            ("P0154", "O2 sensor no activity (bank 2 sensor 1)"),
            ("P0155", "O2 sensor heater circuit (bank 2 sensor 1)"),
            ("P0156", "O2 sensor circuit (bank 2 sensor 2)"),
            ("P0157", "O2 sensor circuit low voltage (bank 2 sensor 2)"),
            ("P0158", "O2 sensor circuit high voltage (bank 2 sensor 2)"),
            ("P0160", "O2 sensor no activity (bank 2 sensor 2)"),
            ("P0161", "O2 sensor heater circuit (bank 2 sensor 2)"),
            ("P0167", "O2 sensor heater circuit (bank 2 sensor 3)"),
            ("P0169", "Incorrect fuel composition"),
            ("P0170", "Fuel trim malfunction (bank 1)"),
            ("P0171", "System too lean (bank 1)"),
            ("P0172", "System too rich (bank 1)"),
            ("P0173", "Fuel trim malfunction (bank 2)"),
            ("P0174", "System too lean (bank 2)"),
            ("P0175", "System too rich (bank 2)"),
            ("P0177", "Fuel composition sensor range / performance"),
            ("P0178", "Fuel composition sensor circuit low"),
            ("P0179", "Fuel composition sensor circuit high"),
            ("P0181", "Fuel temperature sensor A range / performance"),
            ("P0182", "Fuel temperature sensor A circuit low"),
            ("P0183", "Fuel temperature sensor A circuit high"),
            ("P0200", "Injector circuit"),
            ("P0218", "Transmission over temperature"),
            ("P0220", "Throttle position sensor B circuit"),
            ("P0230", "Fuel pump primary circuit"),
            ("P0234", "Turbo / supercharger overboost"),
            ("P0300", "Random / multiple cylinder misfire"),
            ("P0315", "Crankshaft position system variation not learned"),
            ("P0325", "Knock sensor 1 circuit"),
            ("P0327", "Knock sensor 1 circuit low"),
            ("P0332", "Knock sensor 2 circuit low (bank 2)"),
            ("P0335", "Crankshaft position sensor A circuit"),
            ("P0336", "Crankshaft position sensor A range / performance"),
            ("P0341", "Camshaft position sensor A range / performance"),
            ("P0342", "Camshaft position sensor A circuit low"),
            ("P0343", "Camshaft position sensor A circuit high"),
            ("P0351", "Ignition coil A primary / secondary circuit"),
            ("P0352", "Ignition coil B primary / secondary circuit"),
            ("P0353", "Ignition coil C primary / secondary circuit"),
            ("P0354", "Ignition coil D primary / secondary circuit"),
            ("P0355", "Ignition coil E primary / secondary circuit"),
            ("P0356", "Ignition coil F primary / secondary circuit"),
            ("P0357", "Ignition coil G primary / secondary circuit"),
            ("P0358", "Ignition coil H primary / secondary circuit"),
            ("P0400", "Exhaust gas recirculation (EGR) flow"),
            ("P0401", "EGR flow insufficient"),
            ("P0402", "EGR flow excessive"),
            ("P0404", "EGR range / performance"),
            ("P0405", "EGR sensor A circuit low"),
            ("P0409", "EGR sensor A circuit"),
            ("P0410", "Secondary air injection system"),
            ("P0412", "Secondary air injection switching valve A circuit"),
            ("P0418", "Secondary air injection relay A circuit"),
            ("P0420", "Catalyst efficiency below threshold (bank 1)"),
            ("P0430", "Catalyst efficiency below threshold (bank 2)"),
            ("P0440", "Evaporative emission (EVAP) system"),
            ("P0442", "EVAP system small leak"),
            ("P0443", "EVAP purge control valve circuit"),
            ("P0446", "EVAP vent control circuit"),
            ("P0449", "EVAP vent valve / solenoid circuit"),
            ("P0452", "EVAP pressure sensor circuit low"),
            ("P0453", "EVAP pressure sensor circuit high"),
            ("P0455", "EVAP system large leak"),
            ("P0461", "Fuel level sensor range / performance"),
            ("P0462", "Fuel level sensor circuit low"),
            ("P0463", "Fuel level sensor circuit high"),
            ("P0480", "Cooling fan 1 control circuit"),
            ("P0481", "Cooling fan 2 control circuit"),
            ("P0483", "Cooling fan rationality check"),
            ("P0491", "Secondary air injection insufficient flow (bank 1)"),
            ("P0492", "Secondary air injection insufficient flow (bank 2)"),
            ("P0493", "Cooling fan overspeed"),
            ("P0495", "Cooling fan speed high"),
            ("P0496", "EVAP flow during non-purge"),
            ("P0500", "Vehicle speed sensor"),
            ("P0502", "Vehicle speed sensor circuit low"),
            ("P0503", "Vehicle speed sensor intermittent / erratic"),
            ("P0506", "Idle speed lower than expected"),
            ("P0507", "Idle speed higher than expected"),
            ("P0522", "Oil pressure sensor circuit low"),
            ("P0523", "Oil pressure sensor circuit high"),
            ("P0525", "Cruise control servo range / performance"),
            ("P0526", "Cooling fan speed sensor circuit"),
            ("P0530", "A/C refrigerant pressure sensor circuit"),
            ("P0531", "A/C refrigerant pressure sensor range / performance"),
            ("P0560", "System voltage"),
            ("P0562", "System voltage low"),
            ("P0563", "System voltage high"),
            ("P0567", "Cruise control resume signal"),
            ("P0568", "Cruise control set signal"),
            ("P0571", "Brake switch A circuit"),
            ("P0601", "Control module memory checksum error"),
            ("P0602", "Control module programming error"),
            ("P0604", "Control module internal RAM"),
            ("P0605", "Control module internal ROM"),
            ("P0606", "Control module processor"),
            ("P0608", "Control module vehicle speed output A"),
            ("P0609", "Control module vehicle speed output B"),
            ("P0615", "Starter relay circuit"),
            ("P0622", "Generator field control circuit"),
            ("P0641", "Sensor reference voltage A circuit"),
            ("P0645", "A/C clutch relay control circuit"),
            ("P0650", "Malfunction indicator lamp (MIL) control circuit"),
            ("P0651", "Sensor reference voltage B circuit"),
            ("P0654", "Engine RPM output circuit"),
            ("P0700", "Transmission control system"),
            ("P0704", "Clutch switch input circuit"),
            ("P0705", "Transmission range sensor circuit (PRNDL)"),
            ("P0706", "Transmission range sensor range / performance"),
            ("P0711", "Transmission fluid temperature sensor range / performance"),
            ("P0712", "Transmission fluid temperature sensor circuit low"),
            ("P0713", "Transmission fluid temperature sensor circuit high"),
            ("P0716", "Input / turbine speed sensor range / performance"),
            ("P0717", "Input / turbine speed sensor circuit low"),
            ("P0719", "Brake switch B circuit low"),
            ("P0724", "Brake switch B circuit high"),
            ("P0730", "Incorrect gear ratio"),
            ("P0740", "Torque converter clutch circuit"),
            ("P0741", "Torque converter clutch performance or stuck off"),
            ("P0742", "Torque converter clutch stuck on"),
            ("P0748", "Pressure control solenoid A electrical"),
            ("P0751", "Shift solenoid A performance or stuck off"),
            ("P0752", "Shift solenoid A stuck on"),
            ("P0753", "Shift solenoid A electrical"),
            ("P0756", "Shift solenoid B performance or stuck off"),
            ("P0757", "Shift solenoid B stuck on"),
            ("P0758", "Shift solenoid B electrical"),
            ("P0785", "Shift / timing solenoid"),
            ("P0801", "Reverse inhibit control circuit"),
            ("P0802", "Transmission control MIL request circuit"),
            ("P0803", "1-4 upshift (skip shift) solenoid control circuit"),
            ("P0804", "1-4 upshift (skip shift) lamp control circuit"),
            ("P0833", "Clutch pedal switch B circuit"),
            ("P0850", "Park / neutral switch input circuit"),
            ("P0856", "Traction control input signal"),
            ("P0894", "Transmission component slipping"),
            ("P2066", "Fuel level sensor B performance"),
            ("P2067", "Fuel level sensor B circuit low"),
            ("P2068", "Fuel level sensor B circuit high"),
            ("P2101", "Throttle actuator control motor circuit range / performance"),
            ("P2108", "Throttle actuator control module processor performance"),
            ("P2119", "Throttle actuator control throttle body range / performance"),
            ("P2120", "Accelerator pedal position sensor D circuit"),
            ("P2121", "Accelerator pedal position sensor D range / performance"),
            ("P2125", "Accelerator pedal position sensor E circuit"),
            ("P2126", "Accelerator pedal position sensor E range / performance"),
            ("P2135", "Throttle position sensor A / B voltage correlation"),
            ("P2279", "Intake air system leak"),
            ("P2610", "Control module engine off timer performance"),
            ("U0107", "Lost communication with throttle actuator control module"),
        };

        return rows.ToDictionary(r => r.Code, r => r.Text, StringComparer.Ordinal);
    }
}
