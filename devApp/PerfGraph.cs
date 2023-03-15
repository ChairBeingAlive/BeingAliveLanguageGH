﻿using FontStashSharp;
using NvgSharp;

using System.Drawing;
using Texture2D = System.Object;

namespace BalGL
{
    public class PerfGraph
    {
        public enum Style
        {
            GRAPH_RENDER_FPS,
            GRAPH_RENDER_MS,
            GRAPH_RENDER_PERCENT,
        };

        private Style _style;
        private string _name;
        private float[] _values = new float[100];
        private int _head;
        private readonly FontSystem _fontSystem;

        public PerfGraph(Style style, string name, FontSystem fontSystem)
        {
            _style = style;
            _name = name;
            _fontSystem = fontSystem;
        }

        public void Update(float frameTime)
        {
            _head = (_head + 1) % _values.Length;
            _values[_head] = frameTime;
        }

        public float GetAverage()
        {
            float avg = 0;
            for (var i = 0; i < _values.Length; i++)
            {
                avg += _values[i];
            }
            return avg / _values.Length;
        }

        public void Render(NvgContext vg, float x, float y)
        {
            int i;
            float avg, w, h;
            string str;

            avg = GetAverage();

            w = 200;
            h = 35;

            vg.BeginPath();
            vg.Rect(x, y, w, h);
            vg.FillColor(Utility.FromRGBA(0, 0, 0, 128));
            vg.Fill();

            vg.BeginPath();
            vg.MoveTo(x, y + h);
            if (_style == Style.GRAPH_RENDER_FPS)
            {
                for (i = 0; i < _values.Length; i++)
                {
                    float v = 1.0f / (0.00001f + _values[(_head + i) % _values.Length]);
                    float vx, vy;
                    if (v > 80.0f)
                        v = 80.0f;
                    vx = x + ((float)i / (_values.Length - 1)) * w;
                    vy = y + h - ((v / 80.0f) * h);
                    vg.LineTo(vx, vy);
                }
            }
            else if (_style == Style.GRAPH_RENDER_PERCENT)
            {
                for (i = 0; i < _values.Length; i++)
                {
                    float v = _values[(_head + i) % _values.Length] * 1.0f;
                    float vx, vy;
                    if (v > 100.0f)
                        v = 100.0f;
                    vx = x + ((float)i / (_values.Length - 1)) * w;
                    vy = y + h - ((v / 100.0f) * h);
                    vg.LineTo(vx, vy);
                }
            }
            else
            {
                for (i = 0; i < _values.Length; i++)
                {
                    float v = _values[(_head + i) % _values.Length] * 1000.0f;
                    float vx, vy;
                    if (v > 20.0f)
                        v = 20.0f;
                    vx = x + ((float)i / (_values.Length - 1)) * w;
                    vy = y + h - ((v / 20.0f) * h);
                    vg.LineTo(vx, vy);
                }
            }
            vg.LineTo(x + w, y + h);
            vg.FillColor(Utility.FromRGBA(255, 192, 0, 128));
            vg.Fill();

            if (!string.IsNullOrEmpty(_name))
            {
                var font = _fontSystem.GetFont(14);
                vg.FillColor(Utility.FromRGBA(240, 240, 240, 192));
                vg.TextAligned(font, _name, x + 3, y + 1, TextHorizontalAlignment.Left, TextVerticalAlignment.Top);
            }

            if (_style == Style.GRAPH_RENDER_FPS)
            {
                var font = _fontSystem.GetFont(18);
                vg.FillColor(Utility.FromRGBA(240, 240, 240, 255));
                str = string.Format("{0:0.00} FPS", 1.0f / avg);
                vg.TextAligned(font, str, x + w - 3, y + 1, TextHorizontalAlignment.Right, TextVerticalAlignment.Top);

                font = _fontSystem.GetFont(15);
                vg.FillColor(Utility.FromRGBA(240, 240, 240, 160));
                str = string.Format("{0:0.00} ms", avg * 1000.0f);
                vg.TextAligned(font, str, x + w - 3, y + h - 1, TextHorizontalAlignment.Right, TextVerticalAlignment.Bottom);
            }
            else if (_style == Style.GRAPH_RENDER_PERCENT)
            {
                var font = _fontSystem.GetFont(18);
                vg.FillColor(Utility.FromRGBA(240, 240, 240, 255));
                str = string.Format("{0:0.00} %%", avg);
                vg.TextAligned(font, str, x + w - 3, y + 1, TextHorizontalAlignment.Right, TextVerticalAlignment.Top);
            }
            else
            {
                var font = _fontSystem.GetFont(18);
                vg.FillColor(Utility.FromRGBA(240, 240, 240, 255));
                str = string.Format("{0:0.00} ms", avg * 1000.0f);
                vg.TextAligned(font, str, x + w - 3, y + 1, TextHorizontalAlignment.Right, TextVerticalAlignment.Top);
            }
        }

    }
}
