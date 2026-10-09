using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the PC Health Monitor icon (graphite rounded square + green pulse line) and writes
// app.ico (16–256 px, PNG-compressed entries) and app-256.png into the given directory.
var output = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(output);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = new List<byte[]>();

var thread = new Thread(() =>
{
    foreach (var size in sizes)
    {
        images.Add(Render(size));
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

WriteIco(Path.Combine(output, "app.ico"), sizes, images);
File.WriteAllBytes(Path.Combine(output, "app-256.png"), images[^1]);
Console.WriteLine($"Icon written to {Path.GetFullPath(output)}");

static byte[] Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        double s = size;
        var margin = s * 0.04;
        var rect = new Rect(margin, margin, s - (2 * margin), s - (2 * margin));
        var radius = s * 0.22;

        var background = new LinearGradientBrush(
            Color.FromRgb(0x24, 0x29, 0x2D), Color.FromRgb(0x12, 0x15, 0x17), 90);
        var border = new Pen(new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x40)), Math.Max(1, s * 0.03));
        dc.DrawRoundedRectangle(background, border, rect, radius, radius);

        // Pulse line: flat → spike up → deep spike down → recover → flat.
        var green = Color.FromRgb(0x3F, 0xB3, 0x7F);
        var stroke = Math.Max(1.6, s * (size <= 24 ? 0.12 : 0.085));
        var pen = new Pen(new SolidColorBrush(green), stroke) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var mid = s * 0.54;
        var points = new[]
        {
            new Point(s * 0.16, mid),
            new Point(s * 0.36, mid),
            new Point(s * 0.45, s * 0.26),
            new Point(s * 0.56, s * 0.78),
            new Point(s * 0.65, mid),
            new Point(s * 0.84, mid),
        };
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points[1..], true, true);
        }

        dc.DrawGeometry(null, pen, geometry);
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}

static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
{
    using var file = File.Create(path);
    using var writer = new BinaryWriter(file);
    writer.Write((ushort)0);            // reserved
    writer.Write((ushort)1);            // type: icon
    writer.Write((ushort)sizes.Length); // image count

    var offset = 6 + (16 * sizes.Length);
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // width (0 = 256)
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // height
        writer.Write((byte)0);   // palette
        writer.Write((byte)0);   // reserved
        writer.Write((ushort)1); // color planes
        writer.Write((ushort)32); // bits per pixel
        writer.Write(pngs[i].Length);
        writer.Write(offset);
        offset += pngs[i].Length;
    }

    foreach (var png in pngs)
    {
        writer.Write(png);
    }
}
