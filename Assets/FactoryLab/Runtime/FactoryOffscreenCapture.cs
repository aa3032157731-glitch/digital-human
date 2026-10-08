using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
namespace FactoryLab
{
    // Actual GPU rendering when Windows suppresses presentation for a hidden window.
    public sealed class FactoryOffscreenCapture : IDisposable
    {
        readonly FactoryLabController c;
        readonly RenderTexture world, output;
        readonly Camera ui;
        readonly GameObject backdrop;
        readonly Texture2D sync;
        readonly int width,height;
        public FactoryOffscreenCapture(FactoryLabController controller,int width,int height)
        {
            c=controller;this.width=width;this.height=height;var viewport=c.sceneCamera.rect;
            world=new RenderTexture(Mathf.RoundToInt(width*viewport.width),Mathf.RoundToInt(height*viewport.height),24);output=new RenderTexture(width,height,24);
            ui=new GameObject("Acceptance UI camera").AddComponent<Camera>();ui.enabled=false;ui.cullingMask=1<<5;
            ui.clearFlags=CameraClearFlags.SolidColor;ui.backgroundColor=c.sceneCamera.backgroundColor;ui.nearClipPlane=.1f;ui.farClipPlane=10;
            ui.transform.position=new Vector3(1000,1000,-1000);ui.targetTexture=output;
            backdrop=new GameObject("Acceptance viewport",typeof(RectTransform),typeof(RawImage));
            var rect=backdrop.GetComponent<RectTransform>();rect.SetParent(c.Hud.transform,false);rect.SetAsFirstSibling();
            rect.anchorMin=viewport.min;rect.anchorMax=viewport.max;rect.offsetMin=rect.offsetMax=Vector2.zero;backdrop.GetComponent<RawImage>().texture=world;
            c.Hud.renderMode=RenderMode.ScreenSpaceCamera;c.Hud.worldCamera=ui;c.Hud.planeDistance=1;sync=new Texture2D(1,1,TextureFormat.RGB24,false);
        }
        public void Draw()
        {
            var camera=c.sceneCamera;var viewport=camera.rect;camera.cullingMask &= ~(1<<5);camera.rect=new Rect(0,0,1,1);camera.targetTexture=world;
            camera.Render();camera.targetTexture=null;camera.rect=viewport;
            if(c.teacherCamera!=null&&c.teacherCamera.isActiveAndEnabled)c.teacherCamera.Render();
            foreach(var t in c.Hud.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
            Canvas.ForceUpdateCanvases();ui.Render();var previous=RenderTexture.active;RenderTexture.active=output;
            sync.ReadPixels(new Rect(0,0,1,1),0,0);RenderTexture.active=previous;
        }
        public void Save(string path)
        {
            Draw();var previous=RenderTexture.active;RenderTexture.active=output;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
            Color a=texture.GetPixel(width/2,height/2),b=texture.GetPixel(40,height-40);
            if(a.maxColorComponent<.02f&&b.maxColorComponent<.02f)throw new InvalidOperationException("Black player capture cannot pass visual acceptance");
            File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.Destroy(texture);RenderTexture.active=previous;
        }
        public void Dispose()
        {
            c.Hud.renderMode=RenderMode.ScreenSpaceOverlay;c.Hud.worldCamera=null;
            UnityEngine.Object.Destroy(backdrop);UnityEngine.Object.Destroy(ui.gameObject);UnityEngine.Object.Destroy(world);UnityEngine.Object.Destroy(output);UnityEngine.Object.Destroy(sync);
        }
    }
}
