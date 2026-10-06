namespace ReplayKit.Core;

// EncodedImage is owned by the capture pipeline and is never mutated after publication.
public sealed record CaptureFrame(DateTimeOffset CapturedAt, string MonitorId, int Width, int Height, byte[] EncodedImage);
