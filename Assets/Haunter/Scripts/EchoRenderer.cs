using UnityEngine;

namespace Haunter
{
    [RequireComponent(typeof(Camera))]
    public sealed class EchoRenderer : MonoBehaviour
    {
        public Shader OutlineShader;
        [Range(.5f, 3f)] public float LineWidth = 1.15f;
        [Range(.1f, 2f)] public float Brightness = 1.2f;
        Material composite;

        void OnEnable()
        {
            var view = GetComponent<Camera>();
            view.depthTextureMode |= DepthTextureMode.DepthNormals;
            view.backgroundColor = Color.black;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.allowHDR = false;
            view.allowMSAA = false;
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!OutlineShader) OutlineShader = Shader.Find("Hidden/Haunter/OutlineComposite");
            if (!composite && OutlineShader) composite = new Material(OutlineShader) { hideFlags = HideFlags.HideAndDontSave };
            if (!composite) { Graphics.Blit(source, destination); return; }
            composite.SetFloat("_LineWidth", LineWidth);
            composite.SetFloat("_Brightness", Brightness);
            Graphics.Blit(source, destination, composite);
        }

        void OnDisable()
        {
            if (!composite) return;
            if (Application.isPlaying) Destroy(composite); else DestroyImmediate(composite);
        }
    }

    public static class EchoMaterial
    {
        public static void SetPersistent(GameObject root, bool persistent)
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetFloat("_AlwaysVisible", persistent ? 1f : 0f);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
