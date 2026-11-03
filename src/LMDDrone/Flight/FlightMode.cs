using System.ComponentModel;

namespace LMDDrone.Flight;

/// <summary>The values name their settings: an entry starting with one belongs to that mode in LMD Mod Menu.</summary>
internal enum FlightMode
{
    Simple,
    Advanced,
}

/// <summary>What the readout's height is measured from.</summary>
internal enum AltitudeFrom
{
    [Description("Finish Camp")]
    Camp,

    [Description("Bike")]
    Bike,
}
