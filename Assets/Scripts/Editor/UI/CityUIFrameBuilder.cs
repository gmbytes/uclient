using UnityEngine;
using UnityEngine.UI;

namespace Client.Editor.UI
{
    /// <summary>Shared resource positions and one lower separator for city and building pages.</summary>
    public static class CityUIFrameBuilder
    {
        public const float HeaderHeight=160;
        public static RectTransform ResourceFrame(Transform parent)
        {
            var frame=Rect("ResourceFrame",parent);frame.anchorMin=new Vector2(0,1);frame.anchorMax=Vector2.one;
            frame.offsetMin=new Vector2(0,-144);frame.offsetMax=new Vector2(0,-36);
            Line("Bottom",0);
            return frame;
            void Line(string name,float y)
            {
                var rect=Rect(name,frame);rect.anchorMin=new Vector2(0,y);rect.anchorMax=new Vector2(1,y);
                rect.offsetMin=new Vector2(0,y==1?-1:0);rect.offsetMax=new Vector2(0,y==1?0:1);
                var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.67f,.55f,.32f,.55f);image.raycastTarget=false;
            }
        }
        public static RectTransform ResourceStock(Transform parent,string name)
        {
            var stock=Rect(name,parent);stock.anchorMin=new Vector2(.055f,1);stock.anchorMax=new Vector2(.945f,1);
            stock.offsetMin=new Vector2(0,-122);stock.offsetMax=new Vector2(0,-64);
            return stock;
        }
        static RectTransform Rect(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    }
}
