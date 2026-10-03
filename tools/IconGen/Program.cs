using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

// Generates the v2crackN icon set and writes it into both UI projects.
//   dotnet run --project tools/IconGen -c Release
//
// Uses _icon/icon.{png,jpg,svg...} as the artwork when present, otherwise a built-in bolt.
//
// Output:
//   v2rayN/v2rayN/Resources/{v2rayN,NotifyIcon1..4}.ico        (WPF)
//   v2rayN/v2rayN.Desktop/Assets/{v2rayN,NotifyIcon1..4}.ico    (Avalonia)
//   v2rayN/v2rayN.Desktop/v2rayN.png, v2rayN.icns               (packaging)
//   _icon/preview_*.png                                         (for review)

var repo = FindRepoRoot();
var dropDir = Path.Combine(repo, "_icon");
Directory.CreateDirectory(dropDir);

var resDir = Path.Combine(repo, "v2rayN", "v2rayN", "Resources");
var assetsDir = Path.Combine(repo, "v2rayN", "v2rayN.Desktop", "Assets");
var desktopDir = Path.Combine(repo, "v2rayN", "v2rayN.Desktop");

// one variant per ESysProxyType
// (0 ForcedClear, 1 ForcedChange, 2 Unchanged, 3 Pac)
var tray = new (string Name, Color Top, Color Bottom)[]
{
    ("NotifyIcon1", C("#94A3B8"), C("#475569")),  // proxy cleared  - slate
    ("NotifyIcon2", C("#4ADE80"), C("#15803D")),  // proxy forced   - green
    ("NotifyIcon3", C("#38BDF8"), C("#1D4ED8")),  // unchanged      - blue
    ("NotifyIcon4", C("#FBBF24"), C("#B45309")),  // pac            - amber
};
var brandTop = C("#22D3EE");
var brandBottom = C("#4F46E5");

int[] sizes = [16, 24, 32, 48, 64, 128, 256];

// ---------------------------------------------------------------------------
// 1. artwork from _icon/icon.*
// ---------------------------------------------------------------------------
var artPath = new[] { "icon.png", "icon.jpg", "icon.jpeg", "icon.bmp", "icon.gif" }
    .Select(n => Path.Combine(dropDir, n))
    .FirstOrDefault(File.Exists);

Bitmap art = null;      // full colour artwork, cropped and squared
Bitmap silhouette = null;

if (artPath != null)
{
    art = PrepareArtwork(artPath);
    silhouette = MakeSilhouette(art);
    Console.WriteLine($"art  : {Path.GetFileName(artPath)} -> {art.Width}x{art.Height} (bg removed, squared)");
}
else
{
    Console.WriteLine("art  : none in _icon\\ -> falling back to the built-in bolt");
}

// ---------------------------------------------------------------------------
// 2. app icon
// ---------------------------------------------------------------------------
{
    var frames = new List<Bitmap>();
    try
    {
        foreach (var size in sizes)
        {
            frames.Add(art != null ? RenderArtwork(size, art) : Render(size, brandTop, brandBottom));
        }
        var ico = BuildIco(frames);
        File.WriteAllBytes(Path.Combine(resDir, "v2rayN.ico"), ico);
        File.WriteAllBytes(Path.Combine(assetsDir, "v2rayN.ico"), ico);
        Console.WriteLine($"ico  : v2rayN.ico  ({ico.Length} bytes)");
    }
    finally
    {
        foreach (var f in frames) f.Dispose();
    }
}

// ---------------------------------------------------------------------------
// 3. tray icons: state coloured plate + white silhouette of the artwork
// ---------------------------------------------------------------------------
foreach (var (name, top, bottom) in tray)
{
    var frames = new List<Bitmap>();
    try
    {
        foreach (var size in sizes)
        {
            frames.Add(RenderPlate(size, top, bottom, silhouette));
        }
        var ico = BuildIco(frames);
        File.WriteAllBytes(Path.Combine(resDir, name + ".ico"), ico);
        File.WriteAllBytes(Path.Combine(assetsDir, name + ".ico"), ico);
        Console.WriteLine($"ico  : {name}.ico  ({ico.Length} bytes)");
    }
    finally
    {
        foreach (var f in frames) f.Dispose();
    }
}

// ---------------------------------------------------------------------------
// 4. png + icns for the packaging scripts
// ---------------------------------------------------------------------------
{
    int[] bigSizes = [16, 32, 64, 128, 256, 512, 1024];
    var frames = new List<Bitmap>();
    try
    {
        foreach (var size in bigSizes)
        {
            frames.Add(art != null ? RenderArtwork(size, art) : Render(size, brandTop, brandBottom));
        }
        frames[5].Save(Path.Combine(desktopDir, "v2rayN.png"), ImageFormat.Png);   // 512
        var icns = BuildIcns(frames);
        File.WriteAllBytes(Path.Combine(desktopDir, "v2rayN.icns"), icns);
        Console.WriteLine($"png  : v2rayN.png");
        Console.WriteLine($"icns : v2rayN.icns ({icns.Length} bytes)");
    }
    finally
    {
        foreach (var f in frames) f.Dispose();
    }
}

// ---------------------------------------------------------------------------
// 5. previews
// ---------------------------------------------------------------------------
foreach (var size in new[] { 256, 64, 16 })
{
    using var bmp = art != null ? RenderArtwork(size, art) : Render(size, brandTop, brandBottom);
    bmp.Save(Path.Combine(dropDir, $"preview_{size}.png"), ImageFormat.Png);
}

// contact sheet of the 4 tray icons
{
    const int s = 64, gap = 16;
    using var sheet = new Bitmap(4 * s + 3 * gap, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(sheet))
    {
        g.Clear(Color.FromArgb(244, 244, 244));
        for (var i = 0; i < tray.Length; i++)
        {
            using var icon = RenderPlate(s, tray[i].Top, tray[i].Bottom, silhouette);
            g.DrawImage(icon, i * (s + gap), 0, s, s);
        }
    }
    sheet.Save(Path.Combine(dropDir, "preview_tray.png"), ImageFormat.Png);
}
Console.WriteLine("prev : _icon/preview_256.png, preview_64.png, preview_16.png, preview_tray.png");
Console.WriteLine("done.");

return 0;

// ===========================================================================
// artwork
// ===========================================================================

/// <summary>
/// Loads the dropped artwork, knocks out the flat background (flood fill from the
/// border, feathered edge), crops to the content and pads it to a square.
/// </summary>
static Bitmap PrepareArtwork(string path)
{
    using var src = new Bitmap(path);
    var w = src.Width;
    var h = src.Height;

    var work = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(work))
    {
        g.Clear(Color.Transparent);
        g.DrawImageUnscaled(src, 0, 0);
    }

    // background colour = the corner colour that wins the vote
    var corners = new[]
    {
        work.GetPixel(0, 0), work.GetPixel(w - 1, 0),
        work.GetPixel(0, h - 1), work.GetPixel(w - 1, h - 1),
    };
    var bg = Color.FromArgb(corners.GroupBy(c => c.ToArgb())
                                   .OrderByDescending(g => g.Count())
                                   .First().Key);

    var dist = new int[w * h];
    for (var y = 0; y < h; y++)
    {
        for (var x = 0; x < w; x++)
        {
            var p = work.GetPixel(x, y);
            var dr = p.R - bg.R;
            var dg = p.G - bg.G;
            var db = p.B - bg.B;
            dist[y * w + x] = dr * dr + dg * dg + db * db;
        }
    }

    const int tolerance = 60 * 60;
    var isBg = new bool[w * h];
    var stack = new Stack<int>();

    void Try(int x, int y)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        var i = y * w + x;
        if (isBg[i] || dist[i] > tolerance) return;
        isBg[i] = true;
        stack.Push(i);
    }

    for (var x = 0; x < w; x++) { Try(x, 0); Try(x, h - 1); }
    for (var y = 0; y < h; y++) { Try(0, y); Try(w - 1, y); }
    while (stack.Count > 0)
    {
        var i = stack.Pop();
        var x = i % w;
        var y = i / w;
        Try(x - 1, y);
        Try(x + 1, y);
        Try(x, y - 1);
        Try(x, y + 1);
    }

    // write alpha: 0 on the background, feathered where the edge is, 255 inside
    int minX = w, minY = h, maxX = -1, maxY = -1;
    for (var y = 0; y < h; y++)
    {
        for (var x = 0; x < w; x++)
        {
            var i = y * w + x;
            if (isBg[i])
            {
                work.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                continue;
            }

            var edge = IsEdge(isBg, w, h, x, y);
            byte a = 255;
            if (edge)
            {
                var d = (int)Math.Sqrt(dist[i]);
                a = (byte)Math.Clamp(d * 3, 0, 255);
            }
            var p = work.GetPixel(x, y);
            work.SetPixel(x, y, Color.FromArgb(a, p.R, p.G, p.B));

            if (a > 16)
            {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
    }

    if (maxX < minX || maxY < minY)
    {
        Console.WriteLine("art  : WARNING - background detection ate everything, keeping the image as is");
        return work;
    }

    var cw = maxX - minX + 1;
    var ch = maxY - minY + 1;

    var side = Math.Max(cw, ch);
    var margin = Math.Max(1, side * 4 / 100);
    var outSide = side + margin * 2;

    var result = new Bitmap(outSide, outSide, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(result))
    {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(work,
            new Rectangle(margin + (side - cw) / 2, margin + (side - ch) / 2, cw, ch),
            new Rectangle(minX, minY, cw, ch),
            GraphicsUnit.Pixel);
    }

    work.Dispose();
    return result;
}

static bool IsEdge(bool[] isBg, int w, int h, int x, int y)
{
    for (var dy = -1; dy <= 1; dy++)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
            if (isBg[ny * w + nx]) return true;
        }
    }
    return false;
}

/// <summary>Solid white version of the artwork - used on the coloured tray plates.</summary>
static Bitmap MakeSilhouette(Bitmap art)
{
    var s = new Bitmap(art.Width, art.Height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < art.Height; y++)
    {
        for (var x = 0; x < art.Width; x++)
        {
            var p = art.GetPixel(x, y);
            s.SetPixel(x, y, Color.FromArgb(p.A, 255, 255, 255));
        }
    }
    return s;
}

static Bitmap RenderArtwork(int size, Bitmap art)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.Transparent);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.SmoothingMode = SmoothingMode.AntiAlias;

    var d = size * 0.94f;
    var o = (size - d) / 2f;
    g.DrawImage(art, new RectangleF(o, o, d, d));
    return bmp;
}

// ===========================================================================
// tray plate
// ===========================================================================

static Bitmap RenderPlate(int size, Color top, Color bottom, Bitmap silhouette)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    var rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);

    using (var plate = RoundedRect(rect, size * 0.22f))
    using (var gradient = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.ForwardDiagonal))
    {
        g.FillPath(gradient, plate);

        using (var pen = new Pen(Color.FromArgb(70, Color.White), Math.Max(1f, size * 0.012f)))
        {
            g.DrawPath(pen, plate);
        }

        if (silhouette != null)
        {
            g.SetClip(plate);
            var d = size * 0.62f;
            var o = (size - d) / 2f;
            g.DrawImage(silhouette, new RectangleF(o, o, d, d));
            g.ResetClip();
        }
    }

    return bmp;
}

// ===========================================================================
// fallback artwork (no _icon/icon.* dropped)
// ===========================================================================

static GraphicsPath RoundedRect(RectangleF rect, float radius)
{
    var d = radius * 2f;
    var path = new GraphicsPath();
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

static PointF[] BoltPoints(RectangleF r)
{
    float X(float t) => r.X + t * r.Width;
    float Y(float t) => r.Y + t * r.Height;
    return
    [
        new(X(0.60f), Y(0.15f)),
        new(X(0.27f), Y(0.55f)),
        new(X(0.47f), Y(0.55f)),
        new(X(0.41f), Y(0.86f)),
        new(X(0.75f), Y(0.45f)),
        new(X(0.54f), Y(0.45f)),
    ];
}

static Bitmap Render(int size, Color top, Color bottom)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    var rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);

    using var plate = RoundedRect(rect, size * 0.22f);
    using var gradient = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.ForwardDiagonal);
    g.FillPath(gradient, plate);

    using (var glow = new SolidBrush(Color.FromArgb(38, Color.White)))
    {
        var glowRect = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.5f);
        using var glowPath = RoundedRect(glowRect, size * 0.22f);
        g.SetClip(plate);
        g.FillPath(glow, glowPath);
        g.ResetClip();
    }

    using (var pen = new Pen(Color.FromArgb(70, Color.White), Math.Max(1f, size * 0.012f)))
    {
        g.DrawPath(pen, plate);
    }

    var boltRect = new RectangleF(rect.X + rect.Width * 0.10f, rect.Y + rect.Height * 0.10f,
                                  rect.Width * 0.80f, rect.Height * 0.80f);
    using var bolt = new SolidBrush(Color.White);
    g.FillPolygon(bolt, BoltPoints(boltRect));

    return bmp;
}

// ===========================================================================
// file formats
// ===========================================================================

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null && !Directory.Exists(Path.Combine(dir, "v2rayN")))
    {
        dir = Path.GetDirectoryName(dir);
    }
    return dir ?? throw new InvalidOperationException("repo root not found");
}

static Color C(string html) => ColorTranslator.FromHtml(html);

/// <summary>
/// ICO container: BMP frames up to 64px (System.Drawing.Icon friendly) + a PNG frame for 128/256.
/// </summary>
static byte[] BuildIco(List<Bitmap> frames)
{
    var encoded = new List<byte[]>();
    foreach (var bmp in frames)
    {
        if (bmp.Width >= 128)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            encoded.Add(ms.ToArray());
        }
        else
        {
            encoded.Add(EncodeBmpFrame(bmp));
        }
    }

    using var outStream = new MemoryStream();
    using var bw = new BinaryWriter(outStream);

    bw.Write((ushort)0);                       // reserved
    bw.Write((ushort)1);                       // type = icon
    bw.Write((ushort)encoded.Count);

    var offset = 6 + encoded.Count * 16;
    foreach (var data in encoded)
    {
        var size = data.Length >= 256 ? (byte)0 : (byte)data.Length;
        bw.Write(size);                        // width  (0 = 256)
        bw.Write(size);                        // height (0 = 256)
        bw.Write((byte)0);                     // palette
        bw.Write((byte)0);                     // reserved
        bw.Write((ushort)1);                   // color planes
        bw.Write((ushort)32);                  // bits per pixel
        bw.Write((uint)data.Length);
        bw.Write((uint)offset);
        offset += data.Length;
    }

    foreach (var data in encoded)
    {
        bw.Write(data);
    }

    bw.Flush();
    return outStream.ToArray();
}

/// <summary>32bpp BGRA XOR bitmap + empty AND mask, bottom-up, as expected inside an .ico.</summary>
static byte[] EncodeBmpFrame(Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    bw.Write(40);                              // BITMAPINFOHEADER
    bw.Write(w);
    bw.Write(h * 2);                           // XOR + AND
    bw.Write((ushort)1);                       // planes
    bw.Write((ushort)32);                      // bpp
    bw.Write(0);                               // BI_RGB
    bw.Write(w * h * 4);
    bw.Write(0);
    bw.Write(0);
    bw.Write(0);
    bw.Write(0);

    for (var y = h - 1; y >= 0; y--)
    {
        for (var x = 0; x < w; x++)
        {
            var c = bmp.GetPixel(x, y);
            bw.Write(c.B);
            bw.Write(c.G);
            bw.Write(c.R);
            bw.Write(c.A);
        }
    }

    var rowBytes = ((w + 31) / 32) * 4;        // AND mask, padded to 32 bits
    bw.Write(new byte[rowBytes * h]);

    bw.Flush();
    return ms.ToArray();
}

/// <summary>ICNS container with PNG payloads (ic07..ic10 + ic11..ic14).</summary>
static byte[] BuildIcns(List<Bitmap> frames)
{
    var bySize = frames.ToDictionary(f => f.Width);

    (string Type, int Size)[] layout =
    [
        ("ic11", 32),    // 16@2x
        ("ic12", 64),    // 32@2x
        ("ic07", 128),
        ("ic08", 256),
        ("ic13", 256),   // 128@2x
        ("ic09", 512),
        ("ic14", 512),   // 256@2x
        ("ic10", 1024),  // 512@2x
    ];

    var chunks = new List<(byte[] Type, byte[] Data)>();
    foreach (var (type, size) in layout)
    {
        if (!bySize.TryGetValue(size, out var bmp)) continue;
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        chunks.Add((Encoding.ASCII.GetBytes(type), ms.ToArray()));
    }

    var total = 8 + chunks.Sum(c => 8 + c.Data.Length);

    using var outStream = new MemoryStream();
    using var bw = new BinaryWriter(outStream);
    bw.Write(Encoding.ASCII.GetBytes("icns"));
    bw.Write(System.Net.IPAddress.HostToNetworkOrder(total));
    foreach (var (type, data) in chunks)
    {
        bw.Write(type);
        bw.Write(System.Net.IPAddress.HostToNetworkOrder(8 + data.Length));
        bw.Write(data);
    }

    bw.Flush();
    return outStream.ToArray();
}
