// SkirmishSkin.cs —— 遭遇战对话框（资源 258 / 0x102）的贴图与取色来源。
// 控件本体已抽成可复用组件，见 SkirmishControls.cs（SkinCell / SkinPcx / SkinValuePlate /
// SkinGrip / SkinToggle / SkinStepper）；本文件只负责“贴图从哪来、颜色取多少”。
//
// 依据（不目测）：
//  1. 控件矩形：gamemd.exe 内 RT_DIALOG 258 的 DLGTEMPLATE（533×369 DLU），见 SkirmishPage 常量。
//  2. 贴图清单：code\sub_\sub_61F210（PCX::ForceLoadFile 全表）——
//     arrow_dd/du/ud/uu.pcx、trofl/trofm/trofr.pcx、trakgrip.pcx、sbgrip*.pcx、bar_ll/lr/ul/ur.pcx……
//     全部位于 ra2.mix\local.mix，运行时按名字走 VirtualFileSystem 取。
//  3. 滑条布局：code\sub_\sub_61D950.c 的 WM_PAINT 分支（数值板 50px、把手 12px、里程 = 宽-50-13）。
//  4. 颜色：原版 800×600 大厅实机截图逐像素取样（取样坐标写在常量旁）。
using System;
using System.Collections.Generic;
using UnityEngine;
using Ra2md.Core;

namespace Ra2md.Menu
{
    public static class SkirmishSkin
    {
        // ---- 取色（原版截图取样坐标见注释）
        public static readonly Color32 CellEdgeHi = new Color32(0xA5, 0xBE, 0xC6, 255);   // (285,95)
        public static readonly Color32 CellEdgeLo = new Color32(0x6B, 0x79, 0x84, 255);   // (286,95)
        public static readonly Color32 MetalEdgeHi = new Color32(0x84, 0x86, 0x84, 255);  // (639,90)
        public static readonly Color32 MetalEdgeLo = new Color32(0x52, 0x51, 0x52, 255);  // (640,90)
        public static readonly Color32 ToggleEdgeHi = new Color32(0x94, 0x8E, 0xB5, 255);  // (74,288)
        public static readonly Color32 ToggleEdgeLo = new Color32(0x63, 0x59, 0x84, 255);  // (75,289)
        public static readonly Color32 ArrowEdge = new Color32(0xCE, 0x24, 0x10, 255);     // (388,87)
        public static readonly Color32 PlateTop = new Color32(0x7B, 0x10, 0x08, 255);      // (388,88)
        public static readonly Color32 PlateBottom = new Color32(0x5A, 0x00, 0x00, 255);   // (388,105)

        private static readonly Dictionary<long, Texture2D> DitherCache = new Dictionary<long, Texture2D>();
        private static readonly Dictionary<string, Texture2D> PcxCache = new Dictionary<string, Texture2D>();
        private static Texture2D _orb, _grip, _valueFace;

        /// <summary>50% 黑点阵（1px 棋盘）：叠在背景上即原版 ownerdraw 的点刷填充。</summary>
        public static Texture2D Dither(int w, int h)
        {
            if (w <= 0) w = 1;
            if (h <= 0) h = 1;
            var key = ((long)w << 20) | (uint)h;
            Texture2D tex;
            if (DitherCache.TryGetValue(key, out tex) && tex != null) return tex;

            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    pixels[y * w + x] = ((x + y) & 1) == 0
                        ? new Color32(0, 0, 0, 255)
                        : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            DitherCache[key] = tex;
            return tex;
        }

        /// <summary>按名字取 local.mix 里的 PCX（带缓存；失败返回 null，调用方自行兜底）。</summary>
        public static Texture2D Tex(string fileName)
        {
            Texture2D tex;
            if (PcxCache.TryGetValue(fileName, out tex)) return tex;
            tex = LoadPcxFromLocalMix(fileName);
            PcxCache[fileName] = tex;
            return tex;
        }

        /// <summary>复选框里的红球（原版取样 #FFBE00 → #C63A00 → #7B1E00）。</summary>
        public static Texture2D OrbTexture()
        {
            if (_orb != null) return _orb;
            const int size = 16;
            _orb = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _orb.filterMode = FilterMode.Point;
            _orb.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            var c0 = new Color32(0xFF, 0xBE, 0x00, 255);
            var c1 = new Color32(0xC6, 0x3A, 0x00, 255);
            var c2 = new Color32(0x7B, 0x1E, 0x00, 255);
            var r = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - r) / r;
                    var dy = (y - r) / r;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 c;
                    if (d > 1f) c = c2;
                    else if (d < 0.45f) c = c0;
                    else
                    {
                        var t = (d - 0.45f) / 0.55f;
                        c = new Color32(
                            (byte)Mathf.Lerp(c0.r, c1.r, t),
                            (byte)Mathf.Lerp(c0.g, c1.g, t),
                            (byte)Mathf.Lerp(c0.b, c1.b, t), 255);
                    }
                    pixels[y * size + x] = c;
                }
            }
            _orb.SetPixels32(pixels);
            _orb.Apply(false, false);
            return _orb;
        }

        /// <summary>
        /// 滑条把手 12×22：原版截图 (470..481, 296) 逐像素取样，
        /// #BD0000 | #101818 | #52595A | #84868C | #ADAEB5 | #CECFCE | 对称回落 | #B50000。
        /// </summary>
        public static Texture2D GripTexture()
        {
            if (_grip != null) return _grip;
            var cols = new[]
            {
                new Color32(0xBD, 0x00, 0x00, 255), new Color32(0x10, 0x18, 0x18, 255),
                new Color32(0x52, 0x59, 0x5A, 255), new Color32(0x84, 0x86, 0x8C, 255),
                new Color32(0xAD, 0xAE, 0xB5, 255), new Color32(0xCE, 0xCF, 0xCE, 255),
                new Color32(0xAD, 0xAE, 0xB5, 255), new Color32(0x84, 0x86, 0x8C, 255),
                new Color32(0x52, 0x59, 0x5A, 255), new Color32(0x29, 0x30, 0x31, 255),
                new Color32(0x08, 0x10, 0x10, 255), new Color32(0xB5, 0x00, 0x00, 255),
            };
            _grip = new Texture2D(cols.Length, 1, TextureFormat.RGBA32, false);
            _grip.filterMode = FilterMode.Point;
            _grip.wrapMode = TextureWrapMode.Clamp;
            _grip.SetPixels32(cols);
            _grip.Apply(false, false);
            return _grip;
        }

        /// <summary>数值板兜底：trofl/trofm/trofr 缺失时的红底渐变。</summary>
        public static Texture2D ValueFace()
        {
            if (_valueFace != null) return _valueFace;
            const int h = 24;
            _valueFace = new Texture2D(2, h, TextureFormat.RGBA32, false);
            _valueFace.filterMode = FilterMode.Point;
            _valueFace.wrapMode = TextureWrapMode.Clamp;
            var top = new Color32(0x21, 0x00, 0x00, 255);
            var mid = PlateBottom;
            var pixels = new Color32[2 * h];
            for (var y = 0; y < h; y++)
            {
                var t = y < h / 2 ? y / (h / 2f) : (h - 1 - y) / (h / 2f);
                var c = new Color32(
                    (byte)Mathf.Lerp(top.r, mid.r, t),
                    (byte)Mathf.Lerp(top.g, mid.g, t),
                    (byte)Mathf.Lerp(top.b, mid.b, t), 255);
                pixels[y * 2] = c;
                pixels[y * 2 + 1] = c;
            }
            _valueFace.SetPixels32(pixels);
            _valueFace.Apply(false, false);
            return _valueFace;
        }

        /// <summary>按原版归档路径 ra2.mix\local.mix\xxx.pcx 取 PCX，调色板用 PCX 自带尾部块。</summary>
        public static Texture2D LoadPcxFromLocalMix(string fileName)
        {
            var candidates = new[]
            {
                "ra2.mix/local.mix/" + fileName,
                "ra2.mix\\local.mix\\" + fileName,
                "local.mix/" + fileName,
                "local.mix\\" + fileName,
                fileName
            };

            byte[] data;
            string origin;
            if (!MenuResourceLibrary.TryLoadFirst(candidates, out data, out origin) || data == null)
            {
                Debug.LogWarning("[遭遇战皮肤] 找不到 " + fileName + "（应在 ra2.mix/local.mix）");
                return null;
            }

            byte[] palette;
            string palOrigin;
            MenuResourceLibrary.TryLoadFirst(new[]
            {
                "ra2.mix/local.mix/dialog.pal", "local.mix/dialog.pal", "dialog.pal"
            }, out palette, out palOrigin);

            byte[] usedPalette;
            Texture2D texture = null;
            try
            {
                texture = PcxDecoder.ToTexture(data, palette, out usedPalette);
            }
            catch (Exception e)
            {
                // 单张贴图解码失败不能把整页 Build 打断（原版缺资源时只是该控件不画）。
                Debug.LogWarning("[遭遇战皮肤] " + fileName + " 解码异常：" + e);
                return null;
            }
            if (texture == null)
            {
                Debug.LogWarning("[遭遇战皮肤] " + fileName + " 解码失败（来源 " + origin + "）");
                return null;
            }
            texture.name = fileName;
            Debug.Log("[遭遇战皮肤] " + fileName + " " + texture.width + "×" + texture.height +
                " 来源 " + origin);
            return texture;
        }
    }
}

