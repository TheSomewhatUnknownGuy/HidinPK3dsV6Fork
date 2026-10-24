using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace pk3DS.WinForms.Controls
{
    public class StatBar : Control
    {
        private int _value = 0;
        private int _maximum = 255;

        private static readonly (int Stat, Color Color)[] StatColorAnchors =
        [
            (0,   Color.FromArgb(220, 50, 50)),   // Deep Red
            (25,  Color.FromArgb(243, 68, 68)),   // Red
            (50,  Color.FromArgb(255, 127, 15)),  // Orange
            (75,  Color.FromArgb(255, 221, 87)),  // Yellow
            (100, Color.FromArgb(160, 229, 21)),  // Light Green
            (125, Color.FromArgb(35, 205, 94)),   // Green
            (150, Color.FromArgb(0, 194, 184)),   // Cyan
            (175, Color.FromArgb(70, 200, 250)),  // Sky Blue
            (200, Color.FromArgb(0, 255, 174)),
            (255, Color.FromArgb(0, 255, 234)),
        ];

        private static readonly (int Stat, Color Color)[] TrainerStatColorAnchors =
        [
            (0,   Color.FromArgb(220, 50, 50)),   // Deep Red
            (60,  Color.FromArgb(243, 68, 68)),   // Red
            (110, Color.FromArgb(255, 127, 15)),  // Orange
            (160, Color.FromArgb(255, 221, 87)),  // Yellow
            (220, Color.FromArgb(160, 229, 21)),  // Light Green
            (280, Color.FromArgb(35, 205, 94)),   // Green
            (350, Color.FromArgb(0, 194, 184)),   // Cyan
            (450, Color.FromArgb(70, 200, 250)),  // Sky Blue
        ];

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(0, Math.Min(value, _maximum));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                }
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                int max = Math.Max(1, value);
                if (_maximum != max)
                {
                    _maximum = max;
                    Invalidate();
                }
            }
        }

        public StatBar()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Height = 15;
            Width = 100;
        }

        public static Color GetDynamicStatColor(int stat)
        {
            return InterpolateColor(StatColorAnchors, stat);
        }

        public static Color GetDynamicTrainerStatColor(int stat)
        {
            return InterpolateColor(TrainerStatColorAnchors, stat);
        }

        private static Color InterpolateColor((int Stat, Color Color)[] anchors, int stat)
        {
            if (stat <= anchors[0].Stat)
                return anchors[0].Color;
            if (stat >= anchors[^1].Stat)
                return anchors[^1].Color;

            for (int i = 0; i < anchors.Length - 1; i++)
            {
                var (minStat, minColor) = anchors[i];
                var (maxStat, maxColor) = anchors[i + 1];

                if (stat >= minStat && stat <= maxStat)
                {
                    float factor = (float)(stat - minStat) / (maxStat - minStat);
                    int r = (int)(minColor.R + factor * (maxColor.R - minColor.R));
                    int g = (int)(minColor.G + factor * (maxColor.G - minColor.G));
                    int b = (int)(minColor.B + factor * (maxColor.B - minColor.B));
                    return Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                }
            }

            return anchors[^1].Color;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            int w = Width;
            int h = Height;

            // 1. Draw sleek empty background track
            using (var trackBrush = new SolidBrush(Color.FromArgb(32, 34, 42)))
            {
                g.FillRectangle(trackBrush, 0, 0, w, h);
            }
            using (var trackBorderPen = new Pen(Color.FromArgb(50, 55, 68)))
            {
                g.DrawRectangle(trackBorderPen, 0, 0, w - 1, h - 1);
            }

            if (_value <= 0) return;

            float percentage = (float)_value / _maximum;
            int barWidth = Math.Max(1, (int)(w * percentage));

            // 2. Solid fill in the color the stat actually reaches - a 252-EV Speed bar reads as
            // uniformly "fast" at a glance instead of trailing off through every color it passed
            // through on the way there, which is what the old red-to-cyan gradient did.
            using (var brush = new SolidBrush(GetDynamicStatColor(_value)))
            {
                g.FillRectangle(brush, 0, 0, barWidth, h);
            }

            // 3. Subtle top-highlight for smooth finish
            using (var highlightBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            {
                g.FillRectangle(highlightBrush, 0, 0, barWidth, Math.Max(1, h / 3));
            }

            // 4. Draw border around the filled bar
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0)))
            {
                g.DrawRectangle(pen, 0, 0, barWidth - 1, h - 1);
            }
        }
    }
}
