using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace InventoryRemoteAgent;

/// <summary>پیاده‌سازی فاز ۱ برای ویندوز: کپچر GDI، ورودی SendInput، رضایت MessageBox.</summary>
public sealed class WindowsRemotePlatform : IRemotePlatform
{
    public async Task<bool> RequestConsentAsync(string technicianName, int timeoutSeconds, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var text = $"کارشناس «{technicianName}» درخواست مشاهده/کنترل از راه دور این سیستم را دارد.\n\nآیا اجازه می‌دهید؟";
            const string caption = "ریموت دسکتاپ — درخواست دسترسی";
            const uint type = 0x00000004 | 0x00000020 | 0x00040000 | 0x00010000; // YESNO | ICONQUESTION | TOPMOST | SETFOREGROUND
            try
            {
                var result = MessageBoxTimeoutW(IntPtr.Zero, text, caption, type, 0, (uint)Math.Max(1, timeoutSeconds) * 1000);
                return result == 6; // IDYES
            }
            catch (EntryPointNotFoundException)
            {
                var result = MessageBoxW(IntPtr.Zero, text, caption, type);
                return result == 6;
            }
        }, ct);
    }

    public CapturedFrame CaptureFrame(int maxWidth, int jpegQuality)
    {
        var screenW = GetSystemMetrics(0);
        var screenH = GetSystemMetrics(1);
        if (screenW <= 0 || screenH <= 0) throw new InvalidOperationException("اندازه صفحه نمایش قابل خواندن نیست.");
        using var full = new Bitmap(screenW, screenH, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(0, 0, 0, 0, full.Size, CopyPixelOperation.SourceCopy);
        }
        var outW = screenW; var outH = screenH;
        Bitmap target = full;
        var scaled = false;
        if (maxWidth > 0 && screenW > maxWidth)
        {
            outW = maxWidth;
            outH = (int)Math.Round(screenH * (double)maxWidth / screenW);
            target = new Bitmap(outW, outH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(target))
            {
                g.InterpolationMode = InterpolationMode.Low;
                g.DrawImage(full, 0, 0, outW, outH);
            }
            scaled = true;
        }
        try
        {
            using var ms = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(jpegQuality, 10, 95));
            target.Save(ms, codec, ep);
            return new CapturedFrame(ms.ToArray(), outW, outH);
        }
        finally { if (scaled) target.Dispose(); }
    }

    public void InjectInput(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "mouse") InjectMouse(root);
            else if (type == "key") InjectKey(root);
        }
        catch { }
    }

    private static void InjectMouse(JsonElement root)
    {
        var action = root.TryGetProperty("action", out var a) ? a.GetString() : "";
        var x = root.TryGetProperty("x", out var xv) ? xv.GetDouble() : 0;
        var y = root.TryGetProperty("y", out var yv) ? yv.GetDouble() : 0;
        var button = root.TryGetProperty("button", out var b) ? b.GetInt32() : 0;
        var deltaY = root.TryGetProperty("deltaY", out var d) ? d.GetDouble() : 0;
        var input = new INPUT { type = 0 };
        input.U.mi.dx = (int)(Math.Clamp(x, 0, 1) * 65535);
        input.U.mi.dy = (int)(Math.Clamp(y, 0, 1) * 65535);
        uint flags = 0x8000; // ABSOLUTE
        switch (action)
        {
            case "move": flags |= 0x0001; break;
            case "down":
                flags |= 0x0001 | (button switch { 1 => 0x0020u, 2 => 0x0008u, _ => 0x0002u });
                break;
            case "up":
                flags |= 0x0001 | (button switch { 1 => 0x0040u, 2 => 0x0010u, _ => 0x0004u });
                break;
            case "wheel":
                flags = 0x0800;
                input.U.mi.mouseData = unchecked((uint)(int)(deltaY * 120));
                break;
            default: return;
        }
        input.U.mi.dwFlags = flags;
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void InjectKey(JsonElement root)
    {
        var action = root.TryGetProperty("action", out var a) ? a.GetString() : "";
        var key = root.TryGetProperty("key", out var k) ? k.GetString() : null;
        var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
        var ctrl = root.TryGetProperty("ctrl", out var cv) && cv.GetBoolean();
        var alt = root.TryGetProperty("alt", out var av) && av.GetBoolean();
        var meta = root.TryGetProperty("meta", out var mv) && mv.GetBoolean();
        var up = action == "up";

        // نویسهٔ printable بدون Ctrl/Alt/Meta → Unicode (مستقل از layout کیبورد)
        if (!string.IsNullOrEmpty(key) && key.Length == 1 && !ctrl && !alt && !meta)
        {
            var ch = key[0];
            if (!char.IsControl(ch))
            {
                var uni = new INPUT { type = 1 };
                uni.U.ki.wVk = 0;
                uni.U.ki.wScan = ch;
                uni.U.ki.dwFlags = 0x0004 | (up ? 0x0002u : 0u); // UNICODE | KEYUP
                SendInput(1, new[] { uni }, Marshal.SizeOf<INPUT>());
                return;
            }
        }

        var vk = MapVirtualKey(code, key);
        if (vk == 0) return;
        var extended = IsExtended(code);
        var inp = new INPUT { type = 1 };
        inp.U.ki.wVk = vk;
        inp.U.ki.dwFlags = (up ? 0x0002u : 0u) | (extended ? 0x0001u : 0u);
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }

    private static bool IsExtended(string? code) => code is "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown"
        or "Home" or "End" or "PageUp" or "PageDown" or "Insert" or "Delete" or "MetaLeft" or "MetaRight";

    private static ushort MapVirtualKey(string? code, string? key)
    {
        if (!string.IsNullOrEmpty(code))
        {
            if (code.Length == 4 && code.StartsWith("Key") && char.IsLetter(code[3])) return (ushort)char.ToUpperInvariant(code[3]);
            if (code.Length == 6 && code.StartsWith("Digit") && char.IsDigit(code[5])) return (ushort)code[5];
            switch (code)
            {
                case "Enter": return 0x0D;
                case "Backspace": return 0x08;
                case "Tab": return 0x09;
                case "Escape": return 0x1B;
                case "Space": return 0x20;
                case "ArrowLeft": return 0x25;
                case "ArrowUp": return 0x26;
                case "ArrowRight": return 0x27;
                case "ArrowDown": return 0x28;
                case "Delete": return 0x2E;
                case "Insert": return 0x2D;
                case "Home": return 0x24;
                case "End": return 0x23;
                case "PageUp": return 0x21;
                case "PageDown": return 0x22;
                case "ShiftLeft": case "ShiftRight": return 0x10;
                case "ControlLeft": case "ControlRight": return 0x11;
                case "AltLeft": case "AltRight": return 0x12;
                case "MetaLeft": return 0x5B;
                case "MetaRight": return 0x5C;
                case "CapsLock": return 0x14;
                case "NumLock": return 0x90;
                case "ScrollLock": return 0x91;
                case "ContextMenu": return 0x5D;
                case "PrintScreen": return 0x2C;
                case "Pause": return 0x13;
            }
            if (code.Length >= 2 && code[0] == 'F' && ushort.TryParse(code[1..], out var fn) && fn is >= 1 and <= 24)
                return (ushort)(0x70 + fn - 1);
        }
        if (!string.IsNullOrEmpty(key) && key.Length == 1)
        {
            var r = VkKeyScanW(key[0]);
            if (r != -1) return (ushort)(r & 0xFF);
        }
        return 0;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanW(char ch);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxTimeoutW(IntPtr hWnd, string text, string caption, uint type, ushort languageId, uint milliseconds);

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
}
