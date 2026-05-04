using System.Drawing;
using System.Drawing.Drawing2D;

string outDir = args.Length > 0 ? args[0] : "../../src/Grimly/Resources";
Directory.CreateDirectory(outDir);

var sizes = new[] { 16, 32, 48, 256 };
var bitmaps = new List<Bitmap>();

foreach (var size in sizes)
{
    var bmp = new Bitmap(size, size);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    DrawFace(g, size);
    bitmaps.Add(bmp);
}

// Save 256px PNG for reference
bitmaps[3].Save(Path.Combine(outDir, "icon-256.png"));

// Build ICO file
using var ms = new MemoryStream();
var writer = new BinaryWriter(ms);

writer.Write((short)0);
writer.Write((short)1);
writer.Write((short)sizes.Length);

int dataOffset = 6 + sizes.Length * 16;
var imageData = new List<byte[]>();

foreach (var bmp in bitmaps)
{
    using var pngMs = new MemoryStream();
    bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
    imageData.Add(pngMs.ToArray());
}

for (int i = 0; i < sizes.Length; i++)
{
    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((short)1);
    writer.Write((short)32);
    writer.Write(imageData[i].Length);
    writer.Write(dataOffset);
    dataOffset += imageData[i].Length;
}

foreach (var data in imageData)
    writer.Write(data);

var icoPath = Path.Combine(outDir, "TrayIcon.ico");
File.WriteAllBytes(icoPath, ms.ToArray());

foreach (var bmp in bitmaps)
    bmp.Dispose();

Console.WriteLine($"Icon generated: {icoPath}");

// Generate floating icon — Ed's head filling a circle, no body
var floatingSize = 128;
var floatingBmp = new Bitmap(floatingSize, floatingSize);
using (var fg = Graphics.FromImage(floatingBmp))
{
    fg.SmoothingMode = SmoothingMode.AntiAlias;
    DrawHeadOnly(fg, floatingSize);
}
floatingBmp.Save(Path.Combine(outDir, "FloatingIcon.png"));
floatingBmp.Dispose();
Console.WriteLine("Floating icon generated: FloatingIcon.png");

static void DrawHeadOnly(Graphics g, int size)
{
    float s = size / 32f;

    // Clip to circle first
    using var clipPath = new GraphicsPath();
    clipPath.AddEllipse(0, 0, size, size);
    g.SetClip(clipPath);

    // Dark background for floating icon
    g.Clear(Color.FromArgb(34, 34, 34));

    var dark = Color.FromArgb(40, 30, 30);
    using var darkBrush = new SolidBrush(dark);
    using var skinBrush = new SolidBrush(Color.FromArgb(240, 210, 180));

    using var outlinePen = new Pen(dark, Math.Max(2f * s, 1.5f));
    outlinePen.StartCap = LineCap.Round;
    outlinePen.EndCap = LineCap.Round;
    outlinePen.LineJoin = LineJoin.Round;

    using var thinPen = new Pen(dark, Math.Max(1.4f * s, 1f));
    thinPen.StartCap = LineCap.Round;
    thinPen.EndCap = LineCap.Round;

    // Head centered and large — fills the circle
    float faceX = 15 * s, faceY = 18 * s;
    float faceW = 20 * s, faceH = 22 * s;
    g.FillEllipse(skinBrush, faceX - faceW / 2, faceY - faceH / 2, faceW, faceH);
    g.DrawEllipse(outlinePen, faceX - faceW / 2, faceY - faceH / 2, faceW, faceH);

    // Hair — slicked back
    using var hairPath = new GraphicsPath();
    hairPath.AddArc(5 * s, 6 * s, 22 * s, 18 * s, 180, 180);
    hairPath.AddLine(27 * s, 15 * s, 25 * s, 10 * s);
    hairPath.AddLine(25 * s, 10 * s, 22 * s, 7 * s);
    hairPath.CloseFigure();
    g.FillPath(darkBrush, hairPath);

    // The signature pompadour — tall and prominent
    using var tuftPath = new GraphicsPath();
    tuftPath.AddCurve(new PointF[]
    {
        new(10 * s, 9 * s),
        new(12 * s, 4 * s),
        new(14 * s, 1.5f * s),
        new(16.5f * s, 0.3f * s),  // tip
        new(18 * s, 2 * s),
        new(20 * s, 5 * s),
        new(22 * s, 7 * s),
    }, 0.7f);
    tuftPath.AddLine(22 * s, 7 * s, 10 * s, 9 * s);
    tuftPath.CloseFigure();
    g.FillPath(darkBrush, tuftPath);

    // Eyes
    float eyeR = 1.4f * s;
    g.FillEllipse(darkBrush, 11 * s - eyeR, 16.5f * s - eyeR, eyeR * 2, eyeR * 2);
    g.FillEllipse(darkBrush, 18 * s - eyeR, 16 * s - eyeR, eyeR * 2, eyeR * 2);

    // Eyebrows
    g.DrawArc(thinPen, 8 * s, 12.5f * s, 6 * s, 4.5f * s, 200, 80);
    g.DrawArc(thinPen, 15.5f * s, 12 * s, 6 * s, 4.5f * s, 240, 80);

    // Nose
    g.DrawLine(thinPen, 14.5f * s, 18 * s, 13.5f * s, 21 * s);

    // Grin
    g.DrawArc(thinPen, 10 * s, 21 * s, 12 * s, 7 * s, 10, 120);

    // Ear
    g.DrawArc(thinPen, 24 * s, 14 * s, 5 * s, 7 * s, 280, 160);

}

static void DrawFace(Graphics g, int size)
{
    float s = size / 32f;

    // Warm pink/salmon background
    g.Clear(Color.FromArgb(210, 140, 130));

    var gold = Color.FromArgb(255, 210, 100);
    var dark = Color.FromArgb(40, 30, 30);

    using var outlinePen = new Pen(dark, Math.Max(2.2f * s, 1.5f));
    outlinePen.StartCap = LineCap.Round;
    outlinePen.EndCap = LineCap.Round;
    outlinePen.LineJoin = LineJoin.Round;

    using var thinPen = new Pen(dark, Math.Max(1.5f * s, 1f));
    thinPen.StartCap = LineCap.Round;
    thinPen.EndCap = LineCap.Round;

    using var hairPen = new Pen(dark, Math.Max(3f * s, 2f));
    hairPen.StartCap = LineCap.Round;
    hairPen.EndCap = LineCap.Round;

    using var darkBrush = new SolidBrush(dark);
    using var skinBrush = new SolidBrush(Color.FromArgb(240, 210, 180));

    // Face oval - slightly off-center, looking to the side
    float faceX = 14 * s, faceY = 15 * s;
    float faceW = 16 * s, faceH = 18 * s;
    g.FillEllipse(skinBrush, faceX - faceW / 2, faceY - faceH / 2, faceW, faceH);
    g.DrawEllipse(outlinePen, faceX - faceW / 2, faceY - faceH / 2, faceW, faceH);

    // Hair - slicked back with tall pompadour/tuft sticking up
    // Back of hair
    using var hairPath = new GraphicsPath();
    hairPath.AddArc(6 * s, 5 * s, 18 * s, 14 * s, 180, 180);
    hairPath.AddLine(24 * s, 12 * s, 22 * s, 8 * s);
    hairPath.AddLine(22 * s, 8 * s, 20 * s, 6 * s);
    hairPath.CloseFigure();
    g.FillPath(darkBrush, hairPath);

    // The signature tall hair tuft/pompadour
    using var tuftPath = new GraphicsPath();
    tuftPath.AddCurve(new PointF[]
    {
        new(12 * s, 8 * s),
        new(13 * s, 4 * s),
        new(15 * s, 1.5f * s),
        new(17 * s, 0.5f * s),  // tip - tall and pointed
        new(18 * s, 2 * s),
        new(19 * s, 4 * s),
        new(20 * s, 6 * s),
    }, 0.7f);
    tuftPath.AddLine(20 * s, 6 * s, 12 * s, 8 * s);
    tuftPath.CloseFigure();
    g.FillPath(darkBrush, tuftPath);

    // Eyes - simple dots, looking to the side
    float eyeR = 1.2f * s;
    g.FillEllipse(darkBrush, 11 * s - eyeR, 14 * s - eyeR, eyeR * 2, eyeR * 2);
    g.FillEllipse(darkBrush, 17 * s - eyeR, 13.5f * s - eyeR, eyeR * 2, eyeR * 2);

    // Eyebrows - arched, expressive
    g.DrawArc(thinPen, 8.5f * s, 10.5f * s, 5 * s, 4 * s, 200, 80);
    g.DrawArc(thinPen, 15 * s, 10 * s, 5 * s, 4 * s, 240, 80);

    // Nose - small line
    g.DrawLine(thinPen, 14 * s, 15 * s, 13 * s, 18 * s);

    // Mouth - grinning/smirking
    g.DrawArc(thinPen, 10 * s, 18 * s, 10 * s, 6 * s, 10, 120);

    // Ear - right side
    g.DrawArc(thinPen, 21 * s, 12 * s, 4 * s, 6 * s, 280, 160);

    // Neck/shoulders hint at bottom
    g.DrawLine(outlinePen, 10 * s, 24 * s, 8 * s, 30 * s);
    g.DrawLine(outlinePen, 18 * s, 24 * s, 22 * s, 30 * s);

    // Collar hint - plaid/checkered shirt collar
    using var collarPen = new Pen(Color.FromArgb(180, 160, 80), Math.Max(1.5f * s, 1f));
    if (size >= 32)
    {
        g.DrawLine(collarPen, 8 * s, 28 * s, 12 * s, 25 * s);
        g.DrawLine(collarPen, 22 * s, 28 * s, 18 * s, 25 * s);
    }
}
