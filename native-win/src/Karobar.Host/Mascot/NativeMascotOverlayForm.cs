#if WINDOWS_DESKTOP
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Karobar.Host.Mascot;

/// <summary>
/// Vector GDI+ renderer for the Cube mascot reproducing all 3 skins (`cube`, `pixel`, `bot`),
/// all 12 tricks/states, 5 eye &amp; mouth expressions, blinking, live gaze, hoverboard glow,
/// ground shadow, and floating status pill onto a 32-bit premultiplied ARGB bitmap.
/// </summary>
public static class MascotVectorRenderer
{
    public static void DrawMascot(
        Graphics g,
        int width,
        int height,
        string skin,
        MascotTrick trick,
        double animTime,
        double trickElapsed,
        float eyeOffsetX,
        float eyeOffsetY,
        bool isBlinking,
        int talkPhase,
        bool isHovered,
        bool facingRight,
        string? pillText,
        double vanishProgress = 0)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var normSkin = MascotPreferences.NormalizeSkin(skin);
        var cx = width / 2f;
        var baseY = height - 14f;

        // Apply vanish click scale/lift
        if (vanishProgress > 0.01)
        {
            var scale = (float)(1.0 - vanishProgress * 0.3);
            g.TranslateTransform(cx, baseY - 36f - (float)(vanishProgress * 22.0));
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-cx, -(baseY - 36f));
        }

        // 1. Floating Pill Badge above head
        if (!string.IsNullOrWhiteSpace(pillText))
        {
            DrawPillBadge(g, cx, 10f, pillText!, isHovered);
        }

        // 2. Ground Shadow (glowing cyan on hoverboard)
        var shadowY = baseY - 4f;
        var shadowColor = trick == MascotTrick.Hoverboard
            ? Color.FromArgb(105, 6, 182, 212)
            : Color.FromArgb(65, 15, 23, 42);
        using (var shBrush = new SolidBrush(shadowColor))
        {
            var sw = trick == MascotTrick.Hoverboard ? 56f : 44f;
            g.FillEllipse(shBrush, cx - sw / 2f, shadowY - 4f, sw, 9f);
        }

        // 3. Compute body bounce, tilt, rotation (backflip / hoverboard / dance / sleep / wake)
        float bodyDy = 0f;
        float bodyTiltDeg = 0f;
        float scaleX = 1f, scaleY = 1f;

        switch (trick)
        {
            case MascotTrick.Hoverboard:
                bodyDy = -8f + MathF.Sin((float)animTime * 6f) * 2.5f;
                bodyTiltDeg = facingRight ? 6.5f : -6.5f;
                break;
            case MascotTrick.Walk:
                bodyDy = -MathF.Abs(MathF.Sin((float)animTime * 9f)) * 4f;
                bodyTiltDeg = MathF.Sin((float)animTime * 9f) * 3.5f;
                break;
            case MascotTrick.Dance:
                bodyDy = -MathF.Abs(MathF.Sin((float)animTime * 7.5f)) * 5f;
                bodyTiltDeg = MathF.Sin((float)animTime * 3.75f) * 8f;
                break;
            case MascotTrick.Backflip:
                var bt = Math.Clamp((float)(trickElapsed / 1.5), 0f, 1f);
                if (bt < 0.2f)
                {
                    scaleY = 1f - bt * 0.7f;
                    scaleX = 1f + bt * 0.5f;
                    bodyDy = bt * 14f;
                }
                else if (bt < 0.8f)
                {
                    var air = (bt - 0.2f) / 0.6f;
                    bodyDy = -MathF.Sin(air * MathF.PI) * 28f;
                    bodyTiltDeg = -air * 360f;
                }
                else
                {
                    var land = (bt - 0.8f) / 0.2f;
                    scaleY = 0.86f + land * 0.14f;
                    scaleX = 1.12f - land * 0.12f;
                }
                break;
            case MascotTrick.Sleep:
                var breathe = MathF.Sin((float)animTime * 2.2f);
                scaleY = 0.94f + breathe * 0.03f;
                scaleX = 1.04f - breathe * 0.02f;
                bodyDy = 3f;
                break;
            case MascotTrick.Wake:
                bodyDy = -MathF.Sin(Math.Clamp((float)(trickElapsed / 0.48), 0f, 1f) * MathF.PI) * 14f;
                break;
            default:
                bodyDy = MathF.Sin((float)animTime * 3.0f) * 1.8f;
                break;
        }

        // Save transform for robot body
        var state = g.Save();
        var pivotY = baseY - 36f + bodyDy;
        g.TranslateTransform(cx, pivotY);
        if (MathF.Abs(bodyTiltDeg) > 0.01f) g.RotateTransform(bodyTiltDeg);
        if (MathF.Abs(scaleX - 1f) > 0.001f || MathF.Abs(scaleY - 1f) > 0.001f) g.ScaleTransform(scaleX, scaleY);

        // 4. Hoverboard deck underneath feet
        if (trick == MascotTrick.Hoverboard)
        {
            DrawHoverboard(g, animTime, facingRight);
        }

        // 5. Legs & Boots with white sneaker soles
        var legSwing = trick == MascotTrick.Walk ? MathF.Sin((float)animTime * 9f) * 4f : 0f;
        DrawLeg(g, -9f, 19f + legSwing * 0.4f);
        DrawLeg(g, 9f, 19f - legSwing * 0.4f);

        // 6. Torso (rounded black cube with trick-colored accent rim + chest equalizer)
        var torsoStroke = trick switch
        {
            MascotTrick.Charge => Color.FromArgb(16, 185, 129),
            MascotTrick.Headphones => Color.FromArgb(56, 189, 248),
            MascotTrick.Rubiks => Color.FromArgb(245, 158, 11),
            _ => Color.FromArgb(39, 39, 42)
        };
        var torsoRx = normSkin == "pixel" ? 3.5f : 6.5f;
        using (var torsoPath = RoundedRect(-15.5f, -1f, 31f, 21.5f, torsoRx))
        using (var fill = new SolidBrush(Color.FromArgb(9, 9, 11)))
        using (var pen = new Pen(torsoStroke, 1.6f))
        {
            g.FillPath(fill, torsoPath);
            g.DrawPath(pen, torsoPath);
        }

        // Chest screen + 3-bar equalizer
        using (var screenPath = RoundedRect(-10f, 3.5f, 20f, 11f, 3f))
        using (var sFill = new SolidBrush(Color.FromArgb(24, 24, 27)))
        {
            g.FillPath(sFill, screenPath);
        }
        DrawChestEqualizer(g, trick, animTime, talkPhase);

        // 7. Arms (with hover wave / dance / trick poses + white cuffs)
        DrawArms(g, trick, animTime, isHovered);

        // 8. Neck joint
        using (var neckBrush = new SolidBrush(Color.FromArgb(24, 24, 27)))
        {
            g.FillRectangle(neckBrush, -5f, -5.5f, 10f, 5f);
        }

        // 9. Head (3 skins: cube, pixel, bot)
        DrawHead(g, normSkin, trick, animTime, eyeOffsetX, eyeOffsetY, isBlinking, talkPhase, isHovered);

        // 10. Trick Props (Rubik's cube, headphones, shades, mug, scan cone, juggling cubes, spy glass, sleep Zzz, wake !)
        DrawTrickProps(g, trick, animTime, trickElapsed);

        g.Restore(state);
    }

    private static void DrawLeg(Graphics g, float x, float y)
    {
        using var legPath = RoundedRect(x - 4.5f, y, 9f, 12f, 3.2f);
        using var fill = new SolidBrush(Color.FromArgb(9, 9, 11));
        using var pen = new Pen(Color.FromArgb(39, 39, 42), 1.2f);
        g.FillPath(fill, legPath);
        g.DrawPath(pen, legPath);

        using var soleBrush = new SolidBrush(Color.White);
        g.FillRectangle(soleBrush, x - 4.2f, y + 9.5f, 8.4f, 2.3f);
    }

    private static void DrawChestEqualizer(Graphics g, MascotTrick trick, double animTime, int talkPhase)
    {
        var active = talkPhase > 0 || trick is MascotTrick.Dance or MascotTrick.Charge or MascotTrick.Headphones;
        var eqColor = trick switch
        {
            MascotTrick.Charge => Color.FromArgb(16, 185, 129),
            MascotTrick.Headphones => Color.FromArgb(168, 85, 247),
            _ => Color.FromArgb(56, 189, 248)
        };
        using var barBrush = new SolidBrush(eqColor);
        float[] heights = active
            ? [3f + MathF.Abs(MathF.Sin((float)animTime * 9f)) * 5.5f, 4f + MathF.Abs(MathF.Cos((float)animTime * 11f)) * 5f, 3f + MathF.Abs(MathF.Sin((float)animTime * 8f + 1f)) * 5.5f]
            : [3.2f, 5.2f, 3.2f];
        for (var i = 0; i < 3; i++)
        {
            var bx = -5.5f + i * 4.5f;
            var bh = heights[i];
            g.FillRectangle(barBrush, bx, 12.2f - bh, 2.6f, bh);
        }
    }

    private static void DrawArms(Graphics g, MascotTrick trick, double animTime, bool isHovered)
    {
        var waveAngle = isHovered ? -135f + MathF.Sin((float)animTime * 12f) * 24f : (trick == MascotTrick.Dance ? -60f + MathF.Sin((float)animTime * 7.5f) * 35f : 12f);
        var leftAngle = trick == MascotTrick.Dance ? 60f - MathF.Sin((float)animTime * 7.5f) * 35f : (trick == MascotTrick.Scan ? -55f + MathF.Sin((float)animTime * 5f) * 18f : -12f);

        DrawSingleArm(g, -16f, 3f, leftAngle);
        DrawSingleArm(g, 16f, 3f, waveAngle);
    }

    private static void DrawSingleArm(Graphics g, float ax, float ay, float angleDeg)
    {
        var st = g.Save();
        g.TranslateTransform(ax, ay);
        g.RotateTransform(angleDeg);
        using var armPath = RoundedRect(-3.4f, 0f, 6.8f, 15f, 3f);
        using var fill = new SolidBrush(Color.FromArgb(9, 9, 11));
        using var pen = new Pen(Color.FromArgb(39, 39, 42), 1.2f);
        g.FillPath(fill, armPath);
        g.DrawPath(pen, armPath);

        using var cuff = new SolidBrush(Color.White);
        g.FillRectangle(cuff, -3.2f, 11f, 6.4f, 2.2f);
        g.Restore(st);
    }

    private static void DrawHead(
        Graphics g,
        string skin,
        MascotTrick trick,
        double animTime,
        float ex,
        float ey,
        bool isBlinking,
        int talkPhase,
        bool isHovered)
    {
        var headRx = skin == "pixel" ? 4f : (skin == "bot" ? 11f : 8.5f);

        // Skin accessories behind/top of head
        if (skin == "pixel")
        {
            using var earBrush = new SolidBrush(Color.FromArgb(9, 9, 11));
            g.FillRectangle(earBrush, -24.5f, -26f, 4.5f, 10f);
            g.FillRectangle(earBrush, 20f, -26f, 4.5f, 10f);
        }
        else if (skin == "bot")
        {
            using var antPen = new Pen(Color.FromArgb(148, 163, 184), 2f);
            g.DrawLine(antPen, 0f, -35f, 0f, -43f);
            using var tipBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
            g.FillEllipse(tipBrush, -3.5f, -47f, 7f, 7f);
            using var podBrush = new SolidBrush(Color.FromArgb(39, 39, 42));
            g.FillEllipse(podBrush, -24f, -25f, 5f, 9f);
            g.FillEllipse(podBrush, 19f, -25f, 5f, 9f);
        }
        else
        {
            // Default `cube` skin top node + status LED
            using var nodePath = RoundedRect(-6.5f, -41f, 13f, 7f, 2.5f);
            using var nFill = new SolidBrush(Color.FromArgb(9, 9, 11));
            using var nPen = new Pen(Color.FromArgb(39, 39, 42), 1.3f);
            g.FillPath(nFill, nodePath);
            g.DrawPath(nPen, nodePath);

            var ledColor = trick switch
            {
                MascotTrick.Charge => Color.FromArgb(16, 185, 129),
                MascotTrick.Scan => Color.FromArgb(56, 189, 248),
                _ => Color.White
            };
            using var ledBrush = new SolidBrush(ledColor);
            g.FillEllipse(ledBrush, -2f, -39.5f, 4f, 4f);
        }

        // Main Head Box
        using (var headPath = RoundedRect(-21f, -35f, 42f, 30f, headRx))
        using (var fill = new SolidBrush(Color.FromArgb(9, 9, 11)))
        using (var pen = new Pen(Color.FromArgb(39, 39, 42), 1.7f))
        {
            g.FillPath(fill, headPath);
            g.DrawPath(pen, headPath);
        }

        // Eyes & Mouth translated by live gaze offset
        var faceSt = g.Save();
        g.TranslateTransform(ex, ey);

        var showHappyArcs = isHovered || trick is MascotTrick.Backflip or MascotTrick.Dance;
        using var whitePen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var whiteBrush = new SolidBrush(Color.White);

        if (trick == MascotTrick.Sleep)
        {
            // Peaceful downward arcs
            g.DrawArc(whitePen, -12.5f, -25f, 8f, 6f, 15, 150);
            g.DrawArc(whitePen, 4.5f, -25f, 8f, 6f, 15, 150);
            g.DrawEllipse(whitePen, -2.5f, -15f, 5f, 5f);
        }
        else if (showHappyArcs)
        {
            // Laughing ^ ^ arcs
            g.DrawArc(whitePen, -12.5f, -26f, 8f, 7f, 200, 140);
            g.DrawArc(whitePen, 4.5f, -26f, 8f, 7f, 200, 140);
            g.DrawArc(whitePen, -6f, -17.5f, 12f, 7f, 15, 150);
        }
        else
        {
            var eyeH = isBlinking ? 1.4f : (trick == MascotTrick.Wake ? 10.8f : (trick == MascotTrick.Hoverboard ? 5.2f : 8.5f));
            var eyeRx = skin == "pixel" ? 1.2f : 2.6f;
            var leftEyeW = trick == MascotTrick.Spy ? 7.5f : 5.6f;
            using (var lEye = RoundedRect(-11.5f, -21.5f - eyeH / 2f, leftEyeW, eyeH, eyeRx))
            using (var rEye = RoundedRect(5.8f, -21.5f - eyeH / 2f, 5.6f, eyeH, eyeRx))
            {
                g.FillPath(whiteBrush, lEye);
                g.FillPath(whiteBrush, rEye);
            }

            // Mouth (5 states)
            if (trick == MascotTrick.Wake)
            {
                g.DrawEllipse(whitePen, -3.5f, -15.5f, 7f, 7f);
            }
            else if (talkPhase == 1)
            {
                g.FillPie(whiteBrush, -5f, -16f, 10f, 8f, 0, 180);
            }
            else if (talkPhase == 2)
            {
                using var mp = RoundedRect(-4f, -13.5f, 8f, 4.2f, 2f);
                g.FillPath(whiteBrush, mp);
            }
            else
            {
                g.DrawArc(whitePen, -5.5f, -17f, 11f, 6.5f, 15, 150);
            }
        }

        g.Restore(faceSt);
    }

    private static void DrawHoverboard(Graphics g, double animTime, bool facingRight)
    {
        using var boardPath = RoundedRect(-24f, 30f, 48f, 7f, 3.5f);
        using var grad = new LinearGradientBrush(new PointF(-24f, 30f), new PointF(24f, 37f),
            Color.FromArgb(6, 182, 212), Color.FromArgb(16, 185, 129));
        g.FillPath(grad, boardPath);

        using var glowPen = new Pen(Color.FromArgb(190, 56, 189, 248), 2f);
        var trailDir = facingRight ? -1f : 1f;
        var pulse = 6f + MathF.Sin((float)animTime * 16f) * 4f;
        g.DrawLine(glowPen, trailDir * 25f, 33.5f, trailDir * (25f + pulse), 33.5f);
    }

    private static void DrawTrickProps(Graphics g, MascotTrick trick, double animTime, double trickElapsed)
    {
        switch (trick)
        {
            case MascotTrick.Headphones:
            {
                using var bandPen = new Pen(Color.FromArgb(56, 189, 248), 3f);
                g.DrawArc(bandPen, -23f, -43f, 46f, 32f, 185, 170);
                using var cupBrush = new SolidBrush(Color.FromArgb(147, 51, 234));
                using var leftCup = RoundedRect(-26f, -26f, 6f, 14f, 3f);
                using var rightCup = RoundedRect(20f, -26f, 6f, 14f, 3f);
                g.FillPath(cupBrush, leftCup);
                g.FillPath(cupBrush, rightCup);
                DrawFloatingText(g, "♪ ♫", 24f, -38f - MathF.Sin((float)animTime * 5f) * 4f, Color.FromArgb(56, 189, 248));
                break;
            }
            case MascotTrick.Shades:
            {
                var drop = Math.Min(1f, (float)(trickElapsed / 0.45));
                var sy = -45f + drop * 21f;
                using var shadeBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
                using var rimPen = new Pen(Color.FromArgb(56, 189, 248), 1.4f);
                g.FillRectangle(shadeBrush, -15f, sy, 12f, 6.5f);
                g.FillRectangle(shadeBrush, 3f, sy, 12f, 6.5f);
                g.DrawLine(rimPen, -16f, sy + 1f, 16f, sy + 1f);
                break;
            }
            case MascotTrick.Rubiks:
            {
                Color[] colors = [Color.FromArgb(239, 68, 68), Color.FromArgb(59, 130, 246), Color.FromArgb(16, 185, 129), Color.FromArgb(245, 158, 11)];
                var shift = (int)(animTime * 3);
                for (var r = 0; r < 2; r++)
                {
                    for (var c = 0; c < 2; c++)
                    {
                        using var cb = new SolidBrush(colors[(r * 2 + c + shift) % colors.Length]);
                        g.FillRectangle(cb, 19f + c * 5.5f, 4f + r * 5.5f, 4.8f, 4.8f);
                    }
                }
                break;
            }
            case MascotTrick.Juggle:
            {
                Color[] cColors = [Color.FromArgb(56, 189, 248), Color.FromArgb(16, 185, 129), Color.FromArgb(245, 158, 11)];
                for (var i = 0; i < 3; i++)
                {
                    var ang = (float)(animTime * 4.5 + i * 2.094);
                    var jx = MathF.Cos(ang) * 24f;
                    var jy = -26f + MathF.Sin(ang) * 14f;
                    using var b = new SolidBrush(cColors[i]);
                    g.FillRectangle(b, jx - 3f, jy - 3f, 6f, 6f);
                }
                break;
            }
            case MascotTrick.Charge:
            {
                using var mugBrush = new SolidBrush(Color.FromArgb(16, 185, 129));
                using var mug = RoundedRect(18f, 3f, 9f, 10f, 2f);
                g.FillPath(mugBrush, mug);
                DrawFloatingText(g, "100%", 20f, -34f - MathF.Abs(MathF.Sin((float)animTime * 3f)) * 5f, Color.FromArgb(16, 185, 129));
                break;
            }
            case MascotTrick.Scan:
            {
                using var coneBrush = new SolidBrush(Color.FromArgb(75, 56, 189, 248));
                var sweep = MathF.Sin((float)animTime * 5f) * 8f;
                g.FillPolygon(coneBrush, new PointF[] { new PointF(0, -39f), new PointF(-18f + sweep, -58f), new PointF(18f + sweep, -58f) });
                break;
            }
            case MascotTrick.Sleep:
            {
                var lift = (float)((animTime * 12) % 14);
                DrawFloatingText(g, "z Z Z", 14f, -38f - lift, Color.FromArgb(148, 163, 184));
                break;
            }
            case MascotTrick.Wake:
            {
                DrawFloatingText(g, "!", 18f, -44f, Color.FromArgb(245, 158, 11));
                break;
            }
            case MascotTrick.Dance:
            {
                DrawFloatingText(g, "♪", -26f, -34f - MathF.Abs(MathF.Sin((float)animTime * 6f)) * 5f, Color.FromArgb(56, 189, 248));
                DrawFloatingText(g, "♫", 22f, -38f - MathF.Abs(MathF.Cos((float)animTime * 6f)) * 5f, Color.FromArgb(168, 85, 247));
                break;
            }
            case MascotTrick.Backflip:
            {
                DrawFloatingText(g, "✦", -26f, -36f, Color.FromArgb(250, 204, 21));
                DrawFloatingText(g, "✦", 22f, -34f, Color.FromArgb(56, 189, 248));
                break;
            }
        }
    }

    private static void DrawFloatingText(Graphics g, string text, float x, float y, Color color)
    {
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y);
    }

    private static void DrawPillBadge(Graphics g, float centerX, float topY, string text, bool isHovered)
    {
        using var font = new Font("Segoe UI", 7.8f, FontStyle.Bold);
        var sz = g.MeasureString(text, font);
        var pw = Math.Min(152f, sz.Width + 14f);
        var ph = 20f;
        var rectX = centerX - pw / 2f;

        var bg = isHovered ? Color.FromArgb(240, 37, 99, 235) : Color.FromArgb(228, 15, 23, 42);
        var border = isHovered ? Color.FromArgb(96, 165, 250) : Color.FromArgb(71, 85, 105);

        using var path = RoundedRect(rectX, topY, pw, ph, 10f);
        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f);
        using var textBrush = new SolidBrush(Color.White);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(text, font, textBrush, new RectangleF(rectX, topY, pw, ph), sf);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        var d = Math.Min(r * 2f, Math.Min(w, h));
        if (d <= 0.1f)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// True Win32 Per-Pixel Alpha Layered Window (`WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`)
/// owned by `NativeWebView2Form` so the bottom roaming Cube mascot floats smoothly above ALL WebView2 tabs
/// with zero WinForms airspace glitches and zero POS/barcode-scanner focus stealing.
/// </summary>
public sealed class NativeMascotOverlayForm : Form
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private const int SpriteWidth = 160;
    private const int SpriteHeight = 136;

    private readonly Form _ownerForm;
    private readonly MascotPreferencesStore _prefsStore;
    private readonly MascotRoamingController _controller = new();
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly ContextMenuStrip _contextMenu;
    private long _lastTickMs;
    private bool _drawerOpen;
    private int _drawerWidth;

    public event Action? MascotClicked;

    public MascotRoamingController Controller => _controller;

    public NativeMascotOverlayForm(Form ownerForm, MascotPreferencesStore prefsStore)
    {
        _ownerForm = ownerForm;
        _prefsStore = prefsStore;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(SpriteWidth, SpriteHeight);
        Cursor = Cursors.Hand;
        Owner = ownerForm;

        ApplyPreferences(_prefsStore.Current);
        _prefsStore.PreferencesChanged += OnPreferencesChanged;

        _contextMenu = BuildContextMenu();

        _animTimer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 FPS crisp smooth animation
        _lastTickMs = Environment.TickCount64;
        _animTimer.Tick += (_, _) => OnAnimTick();

        MouseEnter += (_, _) => { _controller.IsHovered = true; };
        MouseLeave += (_, _) => { _controller.IsHovered = false; };
        MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _contextMenu.Show(Cursor.Position);
            }
            else if (e.Button == MouseButtons.Left)
            {
                _controller.TriggerVanishClick();
                RenderLayeredFrame();
                var openTimer = new System.Windows.Forms.Timer { Interval = 130 };
                openTimer.Tick += (_, _) =>
                {
                    openTimer.Stop();
                    openTimer.Dispose();
                    MascotClicked?.Invoke();
                };
                openTimer.Start();
            }
        };

        _ownerForm.LocationChanged += (_, _) => SyncPositionAndVisibility();
        _ownerForm.SizeChanged += (_, _) => SyncPositionAndVisibility();
        _ownerForm.VisibleChanged += (_, _) => SyncPositionAndVisibility();
        _ownerForm.Activated += (_, _) => _controller.NotifyUserActivity();
        _ownerForm.FormClosing += (_, _) =>
        {
            try { _animTimer.Stop(); Close(); } catch { }
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void StartOverlay()
    {
        if (IsDisposed) return;
        _lastTickMs = Environment.TickCount64;
        SyncPositionAndVisibility();
        _animTimer.Start();
    }

    public void SetDrawerState(bool isOpen, int drawerWidth)
    {
        _drawerOpen = isOpen;
        _drawerWidth = isOpen ? drawerWidth : 0;
        _controller.RightReservedWidth = _drawerWidth + 20;
        if (!isOpen && _controller.IsVanishing)
        {
            _controller.ResetAfterDrawerClosed();
        }
        SyncPositionAndVisibility();
    }

    public void SetBackgroundTaskProgress(string? progressText)
    {
        _controller.BackgroundTaskStatusText = progressText;
    }

    public void NotifyUserActivity() => _controller.NotifyUserActivity();

    public void AvoidFocusedInput(int left, int top, int right, int bottom)
    {
        if (_ownerForm.IsDisposed) return;
        _controller.AvoidFocusedRect(left, top, right, bottom, _ownerForm.ClientSize.Width, _ownerForm.ClientSize.Height);
    }

    private void OnPreferencesChanged(MascotPreferences prefs)
    {
        if (IsDisposed || _ownerForm.IsDisposed) return;
        Control? invoker = IsHandleCreated ? this : (_ownerForm.IsHandleCreated ? _ownerForm : null);
        if (invoker == null) return;
        if (invoker.InvokeRequired)
        {
            try
            {
                invoker.BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !_ownerForm.IsDisposed) ApplyPreferences(prefs);
                }));
            }
            catch { }
        }
        else
        {
            ApplyPreferences(prefs);
        }
    }

    private void ApplyPreferences(MascotPreferences prefs)
    {
        _controller.RoamingEnabled = prefs.RoamingEnabled;
        SyncPositionAndVisibility();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(248, 250, 252),
            Renderer = new DarkMenuStripRenderer()
        };
        menu.Items.Add("Open Cube GuideBot (Ctrl+G)", null, (_, _) => MascotClicked?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        var skinMenu = new ToolStripMenuItem("Mascot Skin");
        skinMenu.DropDownItems.Add("Cube (Default)", null, (_, _) => _prefsStore.Update(p => p.Skin = "cube"));
        skinMenu.DropDownItems.Add("Pixel", null, (_, _) => _prefsStore.Update(p => p.Skin = "pixel"));
        skinMenu.DropDownItems.Add("Bot (Antenna)", null, (_, _) => _prefsStore.Update(p => p.Skin = "bot"));
        menu.Items.Add(skinMenu);
        menu.Items.Add("Toggle Roaming", null, (_, _) => _prefsStore.Update(p => p.RoamingEnabled = !p.RoamingEnabled));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Hide Bottom Mascot", null, (_, _) => _prefsStore.Update(p => p.Enabled = false));
        return menu;
    }

    private void OnAnimTick()
    {
        if (IsDisposed || _ownerForm.IsDisposed) return;
        var prefs = _prefsStore.Current;
        var shouldShow = prefs.Enabled && !_drawerOpen && _ownerForm.Visible && _ownerForm.WindowState != FormWindowState.Minimized && _ownerForm.ClientSize.Width >= 500;
        if (!shouldShow)
        {
            if (Visible) Hide();
            return;
        }
        if (!Visible) Show(_ownerForm);

        var now = Environment.TickCount64;
        var dt = Math.Clamp((now - _lastTickMs) / 1000.0, 0.01, 0.2);
        _lastTickMs = now;

        var clientCursor = _ownerForm.PointToClient(Cursor.Position);
        _controller.Tick(dt, clientCursor.X, clientCursor.Y, _ownerForm.ClientSize.Width, _ownerForm.ClientSize.Height);

        SyncPositionOnly();
        RenderLayeredFrame();
    }

    private void SyncPositionAndVisibility()
    {
        if (IsDisposed || _ownerForm.IsDisposed) return;
        var prefs = _prefsStore.Current;
        var shouldShow = prefs.Enabled && !_drawerOpen && _ownerForm.Visible && _ownerForm.WindowState != FormWindowState.Minimized && _ownerForm.ClientSize.Width >= 500;
        if (!shouldShow)
        {
            if (Visible) Hide();
            return;
        }
        _controller.ClampToViewport(_ownerForm.ClientSize.Width);
        SyncPositionOnly();
        if (!Visible) Show(_ownerForm);
        RenderLayeredFrame();
    }

    private void SyncPositionOnly()
    {
        if (IsDisposed || _ownerForm.IsDisposed) return;
        var clientOrigin = _ownerForm.PointToScreen(Point.Empty);
        var x = clientOrigin.X + (int)Math.Round(_controller.CurrentX);
        var y = clientOrigin.Y + _ownerForm.ClientSize.Height - SpriteHeight - 4;
        if (Left != x || Top != y)
        {
            Location = new Point(x, y);
        }
    }

    private void RenderLayeredFrame()
    {
        if (IsDisposed || !IsHandleCreated) return;
        var prefs = _prefsStore.Current;
        using var bmp = new Bitmap(SpriteWidth, SpriteHeight, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            MascotVectorRenderer.DrawMascot(
                g,
                SpriteWidth,
                SpriteHeight,
                prefs.Skin,
                _controller.ActiveTrick,
                _controller.AnimTimeSec,
                _controller.TrickElapsedSec,
                _controller.EyeOffsetX,
                _controller.EyeOffsetY,
                _controller.IsBlinking,
                _controller.TalkPhase,
                _controller.IsHovered,
                _controller.FacingRight,
                _controller.GetPillBadgeText(),
                _controller.VanishProgress);
        }
        ApplyBitmapToLayeredWindow(bmp);
    }

    private void ApplyBitmapToLayeredWindow(Bitmap bmp)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);
            var size = new NativeSize { cx = bmp.Width, cy = bmp.Height };
            var srcPt = new NativePoint { x = 0, y = 0 };
            var topPos = new NativePoint { x = Left, y = Top };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };
            UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref srcPt, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
            }
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _prefsStore.PreferencesChanged -= OnPreferencesChanged;
            try { _animTimer.Stop(); _animTimer.Dispose(); } catch { }
            try { _contextMenu.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}
#endif
