namespace GimDvr;
public sealed record DetectionResult(string State, bool? Motion = null, bool? Human = null, int Frames = 0, int HumanSamples = 0, double Confidence = 0, string? Device = null, string? Decoder = null, string? Error = null, string Version = "nas-person-v1");
