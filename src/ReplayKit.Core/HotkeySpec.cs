namespace ReplayKit.Core;

public sealed record HotkeySpec(uint Modifiers, uint VirtualKey)
{
    public static HotkeySpec History { get; } = new(6, 0x52);
    public static HotkeySpec Recording { get; } = new(6, 0x50);
    public override string ToString() => string.Join(" + ", new[] {
        (Modifiers & 2) != 0 ? "Ctrl" : null, (Modifiers & 4) != 0 ? "Shift" : null,
        (Modifiers & 1) != 0 ? "Alt" : null, (Modifiers & 8) != 0 ? "Win" : null,
        VirtualKey is >= 0x70 and <= 0x87 ? $"F{VirtualKey - 0x6F}" : ((char)VirtualKey).ToString()
    }.Where(x => x != null));
    public bool IsValid => (Modifiers & 15) != 0 && (Modifiers & ~15u) == 0 &&
        (VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87);
}
