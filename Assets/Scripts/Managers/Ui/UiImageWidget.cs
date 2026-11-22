// UiImageWidget.cs —— 静态图句柄：支持 Sprite（PNG/SHP 转出的精灵）与 RawImage（纹理/视频帧）。
using UnityEngine;
using UnityEngine.UI;

namespace Ra2md.Managers.Ui
{
    public sealed class UiImageWidget : UiWidget
    {
        public Image Image { get; }
        public RawImage Raw { get; }

        internal UiImageWidget(GameObject go, Image image, RawImage raw) : base(go)
        {
            Image = image;
            Raw = raw;
        }

        public void SetSprite(Sprite sprite)
        {
            if (Image != null) Image.sprite = sprite;
        }

        public void SetTexture(Texture texture)
        {
            if (Raw != null) Raw.texture = texture;
        }

        public Color Tint
        {
            get => Image != null ? Image.color : (Raw != null ? Raw.color : Color.white);
            set
            {
                if (Image != null) Image.color = value;
                if (Raw != null) Raw.color = value;
            }
        }
    }
}
