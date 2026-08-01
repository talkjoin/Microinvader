using UnityEditor;
using UnityEngine;
using UnityEditor.Sprites;
using Unity.Mathematics;
using UnityEditor.ShortcutManagement;
using UnityEditor.EditorTools;
using System.Collections.Generic;
using UnityEngine.U2D;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEditor.U2D;
using System.Linq;

namespace ProtoSprite.Editor
{
    public class PaintTool : ProtoSpriteTool
    {
        // Blend modes
        // set color and alpha
        // alpha blend (alpha is added)
        // set color, keep alpha (alpha of ink is used only to change the color?)
        // do you need both alpha blend and lock alpha, no because that doesn't make sense, alpha blend changes the alpha whereas lock doesn't
        // linear dodge
        //



        Texture2D m_TargetTexture = null;

        int2 m_BrushResizeMousePrevious = int2.zero;
        int2 m_BrushResizeMouseStart = int2.zero;
        Vector2 m_BrushResizeMouseScreenPrevious = Vector2.zero;
        bool m_IsResizingBrush = false;

        bool m_SceneSelectionOutlineGizmoEnabled = false;

        Color m_PaintColor = Color.white;
        int m_BrushSize = 25;

        bool m_IsPreviewDrawn = false;
        bool m_IsDirty = false;

        bool m_TryingToPaint = false;

        UndoDataPaint m_UndoData = null;

        int2 m_PreviousMousePixel = int2.zero;

        EditorWindow m_PaintStartWindow = null;

        BrushShape m_BrushShape = BrushShape.CIRCLE;

        bool m_PixelPerfect = false;

        static int s_ColorPickID = "EyeDropper".GetHashCode();

        List<int2> m_PreviousPixelCoords = new List<int2>();

        RenderTexture m_PreviousPaintedPixelsRT = null;

        RenderTexture m_StrokeRT = null;

        Sprite m_CustomBrushSprite = null;

        //bool m_AlphaBlend = false;
        //bool m_LockAlpha = false;
        //bool m_StrokeMode = false;

        float m_Softness = 0.0f;

        //bool m_ConvertToBump = false;

        BlendMode m_InkBlendMode = BlendMode.REPLACE;

        public override GUIContent toolbarIcon
        {
            get
            {
                GUIContent content = new GUIContent(EditorGUIUtility.IconContent("Grid.PaintTool"));
                content.tooltip = "Paint (" + ShortcutManager.instance.GetShortcutBinding("ProtoSprite/Paint Tool") + ")";
                return content;
            }
        }

		public Color PaintColor { get => m_PaintColor; set => m_PaintColor = value; }
        public int BrushSize
        {
            get => m_BrushSize;
            set
            {
                value = Mathf.Clamp(value, 1, ProtoSpriteWindow.kMaxTextureSize);
                m_BrushSize = value;
            }
        }

		public BrushShape BrushShape { get => m_BrushShape; set => m_BrushShape = value; }
        public bool PixelPerfect { get => m_PixelPerfect; set => m_PixelPerfect = value; }
		public Sprite CustomBrushSprite { get => m_CustomBrushSprite; set => m_CustomBrushSprite = value; }

		//public bool AlphaBlend { get => m_AlphaBlend; set => m_AlphaBlend = value; }

        public BlendMode InkBlendMode { get => m_InkBlendMode; set => m_InkBlendMode = value; }
        //public bool LockAlpha { get => m_LockAlpha; set => m_LockAlpha = value; }

        //public bool StrokeMode { get => m_StrokeMode; set => m_StrokeMode = value; }
		public float Softness { get => m_Softness; set => m_Softness = Mathf.Clamp01(value); }

		//public bool ConvertToBump { get => m_ConvertToBump; set => m_ConvertToBump = value; }

		public void OnEnable()
        {
            PaintColor = ProtoSpriteData.EditorPrefs_GetColor("ProtoSprite.Editor.PaintTool.PaintColor", Color.white);
            BrushSize = EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.BrushSize", 25);
            BrushShape = (BrushShape)EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.BrushShape", (int)BrushShape.CIRCLE);
            PixelPerfect = EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.PixelPerfect", 0) == 0? false : true;
            //AlphaBlend = EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.AlphaBlend", 0) == 0? false : true;
            //LockAlpha = EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.LockAlpha", 0) == 0? false : true;
            InkBlendMode = (BlendMode)EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.InkBlendMode", (int)BlendMode.REPLACE);
            //StrokeMode = EditorPrefs.GetInt("ProtoSprite.Editor.PaintTool.StrokeMode", 0) == 0? false : true;
            Softness = EditorPrefs.GetFloat("ProtoSprite.Editor.PaintTool.Softness", 0.0f);

            // Custom brush sprite
            {
                var assetGUID = EditorPrefs.GetString("ProtoSprite.Editor.PaintTool.CustomBrushSpriteGUID", "");
                var spriteID = EditorPrefs.GetString("ProtoSprite.Editor.PaintTool.CustomBrushSpriteID", "");

                var spriteIDGUID = new GUID(spriteID);

                var assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);

                var objects = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
                var sprites = objects.Where(q => q is Sprite).Cast<Sprite>();

                foreach (var sprite in sprites)
                {
                    if (sprite.GetSpriteID() == spriteIDGUID)
                    {
                        m_CustomBrushSprite = sprite;
                    }
                }
            }
        }

		public void OnDisable()
		{
            // If the tool is active and we exit play mode then OnDisable is called but OnWillBeDeactivated isn't so we force call it here
            if (ToolManager.activeToolType == GetType())
                OnWillBeDeactivated();

            if (m_PreviousPaintedPixelsRT != null)
                RenderTexture.ReleaseTemporary(m_PreviousPaintedPixelsRT);

            if (m_StrokeRT != null)
            {
                RenderTexture.ReleaseTemporary(m_StrokeRT);
                m_StrokeRT = null;
            }

            ProtoSpriteData.EditorPrefs_SetColor("ProtoSprite.Editor.PaintTool.PaintColor", PaintColor);
            EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.BrushSize", BrushSize);
            EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.BrushShape", (int)BrushShape);
            EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.PixelPerfect", PixelPerfect? 1 : 0);
            //EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.AlphaBlend", AlphaBlend ? 1 : 0);
            //EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.LockAlpha", LockAlpha ? 1 : 0);
            EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.InkBlendMode", (int)InkBlendMode);
            //EditorPrefs.SetInt("ProtoSprite.Editor.PaintTool.StrokeMode", StrokeMode ? 1 : 0);
            EditorPrefs.SetFloat("ProtoSprite.Editor.PaintTool.Softness", Softness);

            if (m_CustomBrushSprite != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(m_CustomBrushSprite);

                var assetGUID = AssetDatabase.GUIDFromAssetPath(assetPath);

                var spriteID = m_CustomBrushSprite.GetSpriteID();

                EditorPrefs.SetString("ProtoSprite.Editor.PaintTool.CustomBrushSpriteID", spriteID.ToString());
                EditorPrefs.SetString("ProtoSprite.Editor.PaintTool.CustomBrushSpriteGUID", assetGUID.ToString());

            }
            else
            {
                EditorPrefs.SetString("ProtoSprite.Editor.PaintTool.CustomBrushSpriteID", "");
                EditorPrefs.SetString("ProtoSprite.Editor.PaintTool.CustomBrushSpriteGUID", "");
            }
        }

        [Shortcut("ProtoSprite/Paint Tool", typeof(InternalEngineBridge.ShortcutContext), ProtoSpriteWindow.kToolShortcutsTag, KeyCode.B)]
        public static void ToggleTool()
        {
            ProtoSpriteWindow.ToggleTool<PaintTool>();
        }

		public override void ProtoSpriteWindowGUI()
        {
            if (ProtoSpriteData.IsColorPickerOpen() && ProtoSpriteData.GetEyeDropperColorPickID() == s_ColorPickID)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUILayout.ColorField(ProtoSpriteData.GetEyeDropperPickedColor());
                }
                else
                {
                    EditorGUILayout.ColorField(PaintColor);
                }
            }
            else
            {
                PaintColor = EditorGUILayout.ColorField(PaintColor);
            }

            

            BrushShape = (BrushShape)EditorGUILayout.Popup("Brush Shape", (int)BrushShape, new string[] { "Circle", "Square", "Custom" });

            //if (BrushShape != BrushShape.CUSTOM)
                BrushSize = EditorGUILayout.IntField("Brush Size", BrushSize);


            InkBlendMode = (BlendMode)EditorGUILayout.Popup("Blend Mode", (int)InkBlendMode, new string[] { "Simple", "Alpha Blend", "Color Only" });

            //if (BrushShape != BrushShape.CUSTOM)
            {
                GUIContent label = new GUIContent("Pixel Perfect");
                label.tooltip = "Dynamically adjusts painted pixels to remove L-shapes.";
                PixelPerfect = EditorGUILayout.Toggle(label, PixelPerfect);
            }

            /*{
                GUIContent label = new GUIContent("Alpha Blend");
                label.tooltip = "Blends paint based on alpha rather than replacing it.";
                AlphaBlend = EditorGUILayout.Toggle(label, AlphaBlend);
            }

            {
                GUIContent label = new GUIContent("Lock Alpha");
                label.tooltip = "Locks alpha to the current pixel's alpha.";
                LockAlpha = EditorGUILayout.Toggle(label, LockAlpha);
            }*/



           /*{
                GUIContent label = new GUIContent("Stroke Mode");
                label.tooltip = "Blends per stroke instead of per brush segment.";
                StrokeMode = EditorGUILayout.Toggle(label, StrokeMode);
            }*/
            /*
            {
                GUIContent label = new GUIContent("Softness");
                label.tooltip = "Softness of the brush.";
                Softness = EditorGUILayout.Slider(label, Softness, 0.0f, 1.0f);
            }*/

            /*{
                GUIContent label = new GUIContent("Convert to Bump");
                label.tooltip = "Converts the brush into a normal map that can be used to paint on normal map textures.";
                ConvertToBump = EditorGUILayout.Toggle(label, ConvertToBump);
            }*/

            if (BrushShape == BrushShape.CUSTOM)
            {
                string customBrushOriginalSizeString = "";

                if (CustomBrushSprite != null)
                {
                    customBrushOriginalSizeString += " (" + (int)CustomBrushSprite.rect.width + "x" + (int)CustomBrushSprite.rect.height + ")";
                }

                GUIContent label = new GUIContent("Custom Brush" + customBrushOriginalSizeString);
                label.tooltip = "Custom sprite used for stamping/painting. The final color is a result of multiplying the brush sprite with the active palette color.";

                var previous = m_CustomBrushSprite;
                m_CustomBrushSprite = (Sprite)EditorGUILayout.ObjectField(label, m_CustomBrushSprite, typeof(Sprite), false);

                bool isValid = ProtoSpriteWindow.IsSpriteValidProtoSprite(m_CustomBrushSprite, out string invalidReason, out bool isAutoFixable);

                if (!isValid && m_CustomBrushSprite != null)
                {
                    m_CustomBrushSprite = null;
                    Debug.LogError("ProtoSprite: Selected custom brush sprite is not valid. Reason: " + invalidReason);
                }

                if (m_CustomBrushSprite != null && m_CustomBrushSprite != previous)
                {
                    BrushSize = (int)math.max(m_CustomBrushSprite.rect.width, m_CustomBrushSprite.rect.height);
                }
            }

            // Palette
            EditorGUILayout.Space(10);
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));

            ProtoSpriteWindow.GetInstance().ColorPalette.OnGUI();
        }

        public override void OnActivated()
        {
            m_SceneSelectionOutlineGizmoEnabled = ProtoSpriteData.SceneSelectionGizmo;
            ProtoSpriteData.SceneSelectionGizmo = false;
        }

        public override void OnWillBeDeactivated()
        {
            ProtoSpriteData.SceneSelectionGizmo = m_SceneSelectionOutlineGizmoEnabled;

            Finish();

            m_TargetTexture = null;

            m_BrushResizeMousePrevious = int2.zero;
            m_BrushResizeMouseStart = int2.zero;
            m_BrushResizeMouseScreenPrevious = Vector2.zero;
            m_IsResizingBrush = false;
            EditorGUIUtility.SetWantsMouseJumping(0);

            m_IsPreviewDrawn = false;
            m_IsDirty = false;

            m_TryingToPaint = false;
        }

        void Finish()
        {
            m_IsResizingBrush = false;
            EditorGUIUtility.SetWantsMouseJumping(0);

            if (m_TargetTexture == null)
                return;

            if (m_IsPreviewDrawn)
            {
                m_TargetTexture.Apply(true, false);
                m_IsPreviewDrawn = false;
            }

            if (m_IsDirty)
            {
                //BlendMode blendMode = AlphaBlend ? BlendMode.ALPHA : BlendMode.REPLACE;
                //DrawCustomBrush(m_StrokeRT, new Rect(0, 0, m_StrokeRT.width, m_StrokeRT.height), Vector2.zero, m_TargetTexture, new Rect(0, 0, m_StrokeRT.width, m_StrokeRT.height), int2.zero, int2.zero, Color.white, blendMode, true);

                //Graphics.CopyTexture(m_StrokeRT, m_TargetTexture);

                ProtoSpriteData.ReadGPUToCPU(m_TargetTexture);

                m_IsDirty = false;

                m_UndoData.pixelDataAfter = m_TargetTexture.GetPixels32(0);
                m_UndoData = null;

                ProtoSpriteData.SaveData saveData = new ProtoSpriteData.SaveData(m_TargetTexture);
                ProtoSpriteData.SubmitSaveData(saveData);

                m_TargetTexture = null;
            }
        }

        void UpdateTarget()
        {
            bool validSelection = ProtoSpriteWindow.IsSelectionValidProtoSprite(out string reason);
            bool changedTarget = false;

            Texture2D selectedTexture = null;

            if (validSelection)
            {
                Transform t = Selection.activeTransform;
                SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
                Sprite sprite = spriteRenderer.sprite;
                selectedTexture = ProtoSpriteWindow.GetTargetTexture();// SpriteUtility.GetSpriteTexture(sprite, false);
            }

            changedTarget = m_TargetTexture != selectedTexture;

            /*if (m_TargetTexture != null && !validSelection)
            {
                changedTarget = true;
            }

            if (m_TargetTexture != null && validSelection)
            {
                Transform t = Selection.activeTransform;
                SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
                Sprite sprite = spriteRenderer.sprite;
                selectedTexture = ProtoSpriteWindow.GetTargetTexture();// SpriteUtility.GetSpriteTexture(sprite, false);

                changedTarget = m_TargetTexture != selectedTexture;
            }*/

            if (changedTarget)
            {
                Finish();
            }

            m_TargetTexture = selectedTexture;
        }

        void DrawHandles(Transform t)
        {
            SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
            Sprite sprite = spriteRenderer.sprite;

            // Draw rect outline
            {
                Rect spriteRect = sprite.rect;
                Vector2 scale = (new Vector2(spriteRect.width, spriteRect.height) / sprite.pixelsPerUnit);

                Matrix4x4 tempMatrix = Handles.matrix;
                Handles.matrix = t.localToWorldMatrix;

                Vector2 spritePivot = sprite.pivot;
                if (spriteRenderer.flipX)
                    spritePivot.x = spriteRect.width - sprite.pivot.x;
                if (spriteRenderer.flipY)
                    spritePivot.y = spriteRect.height - sprite.pivot.y;
                Handles.DrawWireCube(scale * 0.5f - spritePivot / sprite.pixelsPerUnit, scale);
                Handles.matrix = tempMatrix;
            }
        }

        public static void DrawDebugSprite(SpriteRenderer spriteRenderer, Texture2D targetTexture, SceneView sceneView)
        {
            Sprite sprite = spriteRenderer.sprite;
            Transform t = spriteRenderer.transform;

            Rect spriteRect = sprite.rect;
            //Vector2 brushPreviewSize = Vector2.one * brushSize;

            Vector2 scale = (new Vector2(spriteRect.width, spriteRect.height) / sprite.pixelsPerUnit);

            Vector2 spritePivot = sprite.pivot;
            //Vector2 pixelOffset = new Vector2(pixelCoord.x, pixelCoord.y) - sprite.rect.position;


            if (spriteRenderer.flipX)
            {
                spritePivot.x = spriteRect.width - spritePivot.x;
                //pixelOffset.x = spriteRect.width - pixelOffset.x;
                //pixelOffset.x -= Mathf.CeilToInt(brushSize * 0.5f);
            }
            else
            {
                //pixelOffset.x -= Mathf.FloorToInt(brushSize * 0.5f);
            }

            if (spriteRenderer.flipY)
            {
                spritePivot.y = spriteRect.height - spritePivot.y;
                //pixelOffset.y = spriteRect.height - pixelOffset.y;
                //pixelOffset.y -= Mathf.CeilToInt(brushSize * 0.5f);
            }
            else
            {
                //pixelOffset.y -= Mathf.FloorToInt(brushSize * 0.5f);
            }



            Vector2 meshQuadSize = sprite.rect.size / sprite.pixelsPerUnit;

            Rect uvRect = sprite.rect;
            uvRect.xMin /= sprite.texture.width;
            uvRect.xMax /= sprite.texture.width;
            uvRect.yMin /= sprite.texture.height;
            uvRect.yMax /= sprite.texture.height;

            Rect quadRect = new Rect(-meshQuadSize.x * 0.5f, -meshQuadSize.y * 0.5f, meshQuadSize.x, meshQuadSize.y);

            Mesh mesh = ProtoSpriteData.GetTempQuadMesh(quadRect, uvRect, spriteRenderer.flipX, spriteRenderer.flipY);

            var activeRenderTarget = RenderTexture.active;


            var spriteVertexPositions = sprite.GetVertexAttribute<Vector3>(UnityEngine.Rendering.VertexAttribute.Position);

            var spriteVerticesCenter = Vector3.zero;

            foreach (var pos in spriteVertexPositions)
            {
                spriteVerticesCenter += pos;
            }

            spriteVerticesCenter /= 4.0f;

            //RenderTexture tempTex = RenderTexture.GetTemporary(brushSize, brushSize);
            //tempTex.filterMode = FilterMode.Point;
            //tempTex.Create();

            //Graphics.SetRenderTarget(tempTex);
            //GL.Clear(true, true, Color.clear);

            //int2 drawPixel = new int2(Mathf.FloorToInt(tempTex.width * 0.5f), Mathf.FloorToInt(tempTex.height * 0.5f));
            //ProtoSpriteData.DrawLineGPU(tempTex, new Rect(0, 0, tempTex.width, tempTex.height), drawPixel, drawPixel, brushSize, Color.white, brushShape);

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(sceneView.camera.pixelWidth, sceneView.camera.pixelHeight);
            rtDesc.colorFormat = RenderTextureFormat.ARGBHalf;// sceneView.camera.targetTexture.format;// RenderTextureFormat.ARGBHalf;
            rtDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
            rtDesc.mipCount = 1;// dstTexture.mipmapCount;
            rtDesc.autoGenerateMips = false;
            rtDesc.sRGB = false;//dstTexture.isDataSRGB;
            rtDesc.enableRandomWrite = true;

            RenderTexture tempRT = RenderTexture.GetTemporary(rtDesc);

            Graphics.SetRenderTarget(tempRT);
            //Graphics.SetRenderTarget(sceneView.camera.targetTexture);

            ColorUtility.TryParseHtmlString("#C0C0C0", out Color bgColor1);
            ColorUtility.TryParseHtmlString("#808080", out Color bgColor2);

            //GL.Clear(true, true, Color.clear);

            Material tempMat = ProtoSpriteData.instance.OutlineMaterial;

            if (PlayerSettings.colorSpace == ColorSpace.Linear && !targetTexture.isDataSRGB)
            {
                tempMat.EnableKeyword("GAMMATOLINEAR");
            }
            else
            {
                tempMat.DisableKeyword("GAMMATOLINEAR");
            }


            if (spriteRenderer.flipY)
            {
                spriteVerticesCenter.y = -spriteVerticesCenter.y;
            }

            if (spriteRenderer.flipX)
            {
                spriteVerticesCenter.x = -spriteVerticesCenter.x;
            }

            //Debug.Log(spriteVerticesCenter);


            Matrix4x4 matrix = Matrix4x4.TRS(t.TransformPoint((Vector2)spriteVerticesCenter), t.rotation, t.lossyScale);

            //Graphics.SetRenderTarget(activeRenderTarget);

            //GL.sRGBWrite = false;
            GL.Clear(true, true, Color.clear);

            Camera previousCamera = Camera.current;

            var rgbWrite = GL.sRGBWrite;

            //Camera.SetupCurrent(sceneView.camera);

            //Graphics.SetRenderTarget(sceneView.camera.targetTexture);

            //GL.Clear(true, true, Color.red);

            //GL.sRGBWrite = rgbWrite;
            //GL.

            GL.PushMatrix();

            //GL.LoadProjectionMatrix(sceneView.camera.projectionMatrix);

            // Set the view matrix
            Matrix4x4 viewMatrix = sceneView.camera.worldToCameraMatrix;
            //GL.modelview = viewMatrix;

            RenderTexture spriteTempRT = null;

            /*{
                RenderTextureDescriptor spriteRTDesc = new RenderTextureDescriptor(sprite.texture.width, sprite.texture.height);
                spriteRTDesc.colorFormat = RenderTextureFormat.ARGB32;
                spriteRTDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
                spriteRTDesc.mipCount = 1;// dstTexture.mipmapCount;
                spriteRTDesc.autoGenerateMips = false;
                spriteRTDesc.sRGB = false;// dstTexture.isDataSRGB;
                
                spriteRTDesc.enableRandomWrite = true;

                spriteTempRT = RenderTexture.GetTemporary(spriteRTDesc);
                spriteTempRT.filterMode = FilterMode.Point;
                spriteTempRT.Create();

                Graphics.Blit(sprite.texture, spriteTempRT);
                RenderTexture.active = sceneView.camera.targetTexture;
            }*/

            tempMat.SetTexture("_MainTex", targetTexture);// sprite.texture);

            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                bgColor1 = bgColor1.linear;
                bgColor2 = bgColor2.linear;
            }

            tempMat.SetColor("_BGColor1", bgColor1);
            tempMat.SetColor("_BGColor2", bgColor2);
            tempMat.SetVector("_SpriteRect", new Vector4(0, 0, sprite.rect.width, sprite.rect.height));

            float pixelAmount = 16;

            int spriteLargestSide = (int)Mathf.Max(sprite.rect.width, sprite.rect.height);

            if (spriteLargestSide <= 64)
            {
                pixelAmount = 4;
            }

            tempMat.SetFloat("_PixelAmount", pixelAmount);
            tempMat.SetPass(2);

            //`GL.sRGBWrite = false;


            /*{
                Debug.Log(Time.frameCount + " BEFORE: " + sceneView.camera.targetTexture.format);

                RenderTextureDescriptor rtDescSceneView = new RenderTextureDescriptor(sceneView.camera.pixelWidth, sceneView.camera.pixelHeight);
                rtDesc.colorFormat = RenderTextureFormat.ARGBFloat;
                rtDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
                rtDesc.mipCount = 1;// dstTexture.mipmapCount;
                rtDesc.autoGenerateMips = false;
                rtDesc.sRGB = false;// dstTexture.isDataSRGB;
                rtDesc.enableRandomWrite = true;

                RenderTexture rt1 = RenderTexture.GetTemporary(rtDescSceneView);

                RenderTexture activeRT = RenderTexture.active;

                RenderTexture.active = null;

                Graphics.Blit(activeRT, rt1);

                
                activeRT.Release();
                //activeRT.format = RenderTextureFormat.ARGBFloat;
                activeRT.Create();

                Graphics.Blit(rt1, activeRT);

                RenderTexture.active = activeRT;
                //GL.Clear(true, true, Color.clear);

                Debug.Log("AFTER: " + sceneView.camera.targetTexture.format);

            }*/







            //GL.sRGBWrite = true;

            //GL.sRGBWrite = false;
            Graphics.DrawMeshNow(mesh, matrix);

            /*{
                Texture2D testTex = new Texture2D(tempRT.width, tempRT.height, TextureFormat.RGBAFloat, false, false);
                testTex.filterMode = FilterMode.Point;
                testTex.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);

                //Debug.Log(ColorUtility.ToHtmlStringRGB(testTex.GetPixel(tempRT.width / 2, tempRT.height / 2)));
                //Debug.Log(sceneView.camera.targetTexture.format + " " + sceneView.camera.targetTexture.isDataSRGB + " " + tempRT.format + " " + tempRT.isDataSRGB + " " + testTex.format + " " + testTex.isDataSRGB);

                DestroyImmediate(testTex);
            }*/


            //Graphics.Blit(tempRT, sceneView.camera.targetTexture);
            Graphics.SetRenderTarget(activeRenderTarget);

            

            GL.PopMatrix();

            Handles.BeginGUI();
            GUI.DrawTexture(new Rect(0, 0, tempRT.width / EditorGUIUtility.pixelsPerPoint, tempRT.height / EditorGUIUtility.pixelsPerPoint), tempRT);
            Handles.EndGUI();


            Camera.SetupCurrent(previousCamera);

            //RenderTexture.ReleaseTemporary(tempTex);

            

            /*{
                ColorUtility.TryParseHtmlString("#FEE761", out Color color);
                bool sceneLightingValue = sceneView.sceneLighting;
                sceneView.sceneLighting = true;

                bool tempHdr = sceneView.camera.allowHDR;

                sceneView.camera.allowHDR = false;
                Handles.color = color;
                //Handles.DrawSolidDisc(spriteRenderer.transform.position, Vector3.forward, 4.0f * HandleUtility.GetHandleSize(spriteRenderer.transform.position));
                Handles.DrawSolidRectangleWithOutline(new Rect(0, 0, 300, 300), Color.white, Color.white);

                sceneView.camera.allowHDR = tempHdr;
                sceneView.sceneLighting = sceneLightingValue;
            }

            {
                Texture2D testTex = new Texture2D(tempRT.width, tempRT.height);
                testTex.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);

                Debug.Log(ColorUtility.ToHtmlStringRGB(testTex.GetPixel(tempRT.width / 2, tempRT.height / 2)));

                DestroyImmediate(testTex);
            }*/

            RenderTexture.ReleaseTemporary(tempRT);
            RenderTexture.ReleaseTemporary(spriteTempRT);
            GL.sRGBWrite = rgbWrite;
        }


        public static void DrawBrushOutline(SpriteRenderer spriteRenderer, Texture2D targetTexture, int2 pixelCoord, int brushSize, BrushShape brushShape, SceneView sceneView)
        {
            //Debug.Log(Time.frameCount + " Draw brush outline");

            Sprite sprite = spriteRenderer.sprite;
            Transform t = spriteRenderer.transform;

            Rect spriteRect = sprite.rect;


            RenderTexture brushTexture = GetBrushTexture(brushSize, brushShape, false);

            //DrawCustomBrush(brushTexture, new Rect(0, 0, BrushSize, BrushSize), new Vector2(Mathf.FloorToInt(BrushSize * 0.5f), Mathf.FloorToInt(BrushSize * 0.5f)), sprite, m_PreviousMousePixel, pixelCoord, PaintColor, AlphaBlend, true);




            int2 brushSize2 = new int2(brushTexture.width, brushTexture.height);

            /*if (brushShape == BrushShape.CUSTOM && ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite != null)
            {
                Sprite customBrushSprite = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite;

                brushSize2.x = (int)customBrushSprite.rect.width;
                brushSize2.y = (int)customBrushSprite.rect.height;

                //brushSize = Mathf.CeilToInt(Mathf.Max(customBrushSprite.rect.width, customBrushSprite.rect.height)) * 2;
            }*/

            Vector2 brushPreviewSize = (float2)brushSize2;



            Vector2 spritePivot = sprite.pivot;
            Vector2 pixelOffset = new Vector2(pixelCoord.x, pixelCoord.y) - sprite.rect.position;


            if (spriteRenderer.flipX)
            {
                spritePivot.x = spriteRect.width - spritePivot.x;
                pixelOffset.x = spriteRect.width - pixelOffset.x;
                pixelOffset.x -= Mathf.CeilToInt(brushSize2.x * 0.5f);
            }
            else
            {
                pixelOffset.x -= Mathf.FloorToInt(brushSize2.x * 0.5f);
            }

            if (spriteRenderer.flipY)
            {
                spritePivot.y = spriteRect.height - spritePivot.y;
                pixelOffset.y = spriteRect.height - pixelOffset.y;
                pixelOffset.y -= Mathf.CeilToInt(brushSize2.y * 0.5f);
            }
            else
            {
                pixelOffset.y -= Mathf.FloorToInt(brushSize2.y * 0.5f);
            }



            Vector2 meshQuadSize = brushPreviewSize / sprite.pixelsPerUnit;

            

            var activeRenderTarget = RenderTexture.active;

            RenderTexture tempTex = RenderTexture.GetTemporary(brushSize2.x, brushSize2.y);

            //Debug.Log("tempTex : " + tempTex.width + " " + tempTex.height);
            tempTex.filterMode = FilterMode.Point;
            tempTex.Create();

            Graphics.SetRenderTarget(tempTex);
            GL.Clear(true, true, Color.clear);

            int2 drawPixel = new int2(Mathf.FloorToInt(tempTex.width * 0.5f), Mathf.FloorToInt(tempTex.height * 0.5f));


            ProtoSpriteData.DrawCustomBrushGPUToCPU(brushTexture, new Rect(0,0, brushTexture.width, brushTexture.height), tempTex, new Rect(0, 0, tempTex.width, tempTex.height), int2.zero, int2.zero, Color.white, new Rect(0, 0, tempTex.width, tempTex.height), BlendMode.REPLACE, false);


            /*if (brushShape == BrushShape.CUSTOM && ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite != null)
            {
                Sprite srcSprite = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite;

                int2 srcSpritePivot = new int2(Mathf.FloorToInt(srcSprite.pivot.x), Mathf.FloorToInt(srcSprite.pivot.y));


                //drawPixel -= srcSpritePivot;
                //drawPixel -= srcSpritePivot;

                ProtoSpriteData.DrawCustomBrushGPUToCPU(srcSprite.texture, srcSprite.rect, tempTex, new Rect(0, 0, tempTex.width, tempTex.height), int2.zero, int2.zero, Color.white, new Rect(0, 0, tempTex.width, tempTex.height), false, false);

                //DrawCustomBrush(ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite, tempTex, new Rect(0, 0, tempTex.width, tempTex.height), drawPixel, drawPixel, Color.white);
            }
            else
            {
                ProtoSpriteData.DrawLineGPU(tempTex, new Rect(0, 0, tempTex.width, tempTex.height), drawPixel, drawPixel, brushSize, Color.white, brushShape);
            }*/


            RenderTexture tempRT = RenderTexture.GetTemporary(sceneView.camera.pixelWidth, sceneView.camera.pixelHeight, 24);
            RenderTexture tempRT2 = RenderTexture.GetTemporary(sceneView.camera.pixelWidth, sceneView.camera.pixelHeight, 24);

            RenderTexture grabPassRT = RenderTexture.GetTemporary(sceneView.camera.pixelWidth, sceneView.camera.pixelHeight, 24);
            grabPassRT.filterMode = FilterMode.Bilinear;
            grabPassRT.Create();

            Graphics.SetRenderTarget(grabPassRT);
            GL.Clear(true, true, Color.clear);

            var sceneViewOriginalRT = sceneView.camera.targetTexture;
            var sceneCamEnabled = sceneView.camera.enabled;

            sceneView.camera.targetTexture = grabPassRT;
            if (sceneView.cameraMode != SceneView.GetBuiltinCameraMode(DrawCameraMode.Wireframe)) // Rendering artifacts occur if in wireframe draw mode and on URP or HDRP
                sceneView.camera.Render();

            if (ProtoSpriteWindow.GetInstance().DebugRender)
                DrawDebugSprite(spriteRenderer, targetTexture, sceneView);

            sceneView.camera.targetTexture = sceneViewOriginalRT;
            sceneView.camera.enabled = sceneCamEnabled;


            

            Graphics.SetRenderTarget(tempRT2);
            GL.Clear(true, true, Color.clear);

            Graphics.SetRenderTarget(tempRT);
            GL.Clear(true, true, Color.clear);



            //Shader blurShader = Shader.Find("Hidden/ProtoSprite/Outline");
            Material tempMat = ProtoSpriteData.instance.OutlineMaterial;// new Material(blurShader);

            tempMat.SetTexture("_MainTex", brushTexture);
            tempMat.SetTexture("_GrabPass", grabPassRT);
            tempMat.SetPass(1);

            Matrix4x4 matrix = Matrix4x4.TRS(t.TransformPoint(-(Vector3)(spritePivot / sprite.pixelsPerUnit) + (Vector3)(pixelOffset / sprite.pixelsPerUnit) + (Vector3)(brushPreviewSize * 0.5f / sprite.pixelsPerUnit)), t.rotation, t.lossyScale);

            if (brushShape == BrushShape.CUSTOM && ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite != null)
            {
                Sprite srcSprite = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite;

                Vector2 srcSpritePivot = new Vector2(Mathf.FloorToInt((srcSprite.pivot.x/srcSprite.rect.width) * brushTexture.width), Mathf.FloorToInt((srcSprite.pivot.y / srcSprite.rect.height) * brushTexture.height));

                /*if (spriteRenderer.flipX)
                {
                    srcSpritePivot.x = srcSprite.rect.width - srcSpritePivot.x;
                }
                if (spriteRenderer.flipY)
                {
                    srcSpritePivot.y = srcSprite.rect.height - srcSpritePivot.y;
                }*/
                //Debug.Log(srcSpritePivot + " " + pixelOffset + " " + spritePivot);
                //Matrix4x4 tMat = t.localToWorldMatrix;
                //tMat = tMat * Matrix4x4.Scale(new Vector3(-1, 1, 1));

                float ppu = sprite.pixelsPerUnit;

                Matrix4x4 meshMatrix = Matrix4x4.identity;
                meshMatrix = Matrix4x4.Translate((new Vector2(meshQuadSize.x, meshQuadSize.y) * 0.5f)) * meshMatrix;
                meshMatrix = Matrix4x4.Translate(-srcSpritePivot / ppu) * meshMatrix;
                meshMatrix = Matrix4x4.Translate(new Vector2(pixelCoord.x, pixelCoord.y) / ppu) * meshMatrix;
                meshMatrix = Matrix4x4.Translate(-sprite.pivot / ppu) * meshMatrix;
                meshMatrix = Matrix4x4.Translate(-sprite.rect.position / ppu) * meshMatrix;
                meshMatrix = Matrix4x4.Scale(new Vector3(spriteRenderer.flipX? -1 : 1, spriteRenderer.flipY? -1 : 1, 1)) * meshMatrix;
                //meshMatrix = Matrix4x4.Translate(brushPreviewSize / ppu) * meshMatrix; // offset mesh vertices so origin is bottom left corner
                //meshMatrix = Matrix4x4.Translate(srcSpritePivot / ppu) * meshMatrix; // offset mesh vertices so origin is at pivot
                //meshMatrix = Matrix4x4.Scale(new Vector3(spriteRenderer.flipX ? -1 : 1, spriteRenderer.flipY ? -1 : 1, 1)) * meshMatrix; // flip it based on target dst sprite renderer flipped or not

                matrix = t.localToWorldMatrix * meshMatrix;

                //Vector3 offset = (Vector3)(brushPreviewSize * 1.0f / sprite.pixelsPerUnit);

                /*if (spriteRenderer.flipX)
                {
                    offset.x += Mathf.CeilToInt(brushPreviewSize.x * 0.5f) / sprite.pixelsPerUnit;
                }
                else
                {
                    offset.x += Mathf.FloorToInt(brushPreviewSize.x * 0.5f) / sprite.pixelsPerUnit;
                }

                if (spriteRenderer.flipY)
                {
                    offset.y += Mathf.CeilToInt(brushPreviewSize.y * 0.5f) / sprite.pixelsPerUnit;
                }
                else
                {
                    offset.y += Mathf.FloorToInt(brushPreviewSize.y * 0.5f) / sprite.pixelsPerUnit;
                }*/

                //matrix = Matrix4x4.TRS(t.TransformPoint(-(Vector3)(spritePivot / sprite.pixelsPerUnit) + (Vector3)(pixelOffset / sprite.pixelsPerUnit) + offset - (Vector3)(srcSpritePivot / sprite.pixelsPerUnit)), t.rotation, t.lossyScale);
                //matrix = matrix * Matrix4x4.Translate((Vector3)(new Vector2(0.0f, Mathf.FloorToInt(brushPreviewSize.y * 0.5f)) / sprite.pixelsPerUnit)) * Matrix4x4.Scale(new Vector3(spriteRenderer.flipX? -1 : 1, spriteRenderer.flipY ? -1 : 1, 1));
            }
            Camera previousCamera = Camera.current;
            var sceneViewTarget = sceneView.camera.targetTexture;
            sceneView.camera.targetTexture = tempRT;
            Camera.SetupCurrent(sceneView.camera);

            //Graphics.SetRenderTarget(tempRT);
            //RenderTexture.active = tempRT;
            GL.Clear(true, true, Color.clear);

            GL.PushMatrix();

            GL.LoadProjectionMatrix(sceneView.camera.projectionMatrix);

            // Set the view matrix
            Matrix4x4 viewMatrix = sceneView.camera.worldToCameraMatrix;
            GL.modelview = viewMatrix;

            Rect quadRect = new Rect(-meshQuadSize.x * 0.5f, -meshQuadSize.y * 0.5f, meshQuadSize.x, meshQuadSize.y);

            Mesh mesh = ProtoSpriteData.GetTempQuadMesh(quadRect, new Rect(0, 0, 1, 1), false, false);
            Graphics.DrawMeshNow(mesh, matrix);

            //Graphics.DrawTexture(new Rect(0, 0, tempRT2.width, tempRT2.height), tempRT);



            Camera.SetupCurrent(sceneView.camera);


            GL.LoadProjectionMatrix(sceneView.camera.projectionMatrix);

            // Set the view matrix
            GL.modelview = viewMatrix;


            sceneView.camera.targetTexture = sceneViewTarget;

            GL.PopMatrix();

            Camera.SetupCurrent(previousCamera);


            Graphics.SetRenderTarget(activeRenderTarget);

            Graphics.Blit(tempRT, tempMat, 0);

            //Handles.BeginGUI();
            //Graphics.DrawTexture(new Rect(0, 0, tempRT2.width, tempRT2.height), tempRT2);
            //GUI.DrawTexture(
            //GUI.DrawTexture(new Rect(0, 0, tempRT2.width, tempRT2.height), tempRT2, ScaleMode.StretchToFill);
            //Handles.EndGUI();

            RenderTexture.ReleaseTemporary(tempTex);
            RenderTexture.ReleaseTemporary(tempRT);
            RenderTexture.ReleaseTemporary(tempRT2);
            RenderTexture.ReleaseTemporary(grabPassRT);

            RenderTexture.ReleaseTemporary(brushTexture);
        }

        void HandleEyeDropper()
        {
            Event e = Event.current;

            if (e.type == EventType.ExecuteCommand)
            {
                if (e.commandName == "EyeDropperClicked")
                {
                    m_PaintColor = ProtoSpriteData.GetEyeDropperLastPickedColor();
                }
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
            {
                ProtoSpriteData.EyeDropperStart();
                ProtoSpriteData.SetEyeDropperColorPickID(s_ColorPickID);
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            Event e = Event.current;

            HandleEyeDropper();

            UpdateTarget();

            if (!(window is SceneView))
                return;

            ProtoSpriteData.RepaintSceneViewsIfUnityFocused();
            ProtoSpriteData.RepaintSpriteEditorWindow();

            if (!ProtoSpriteWindow.IsSelectionValidProtoSprite(out string invalidReason))
            {
                ProtoSpriteData.DrawInvalidHandles();
                return;
            }

            Transform t = Selection.activeTransform;

            SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
            Sprite sprite = spriteRenderer.sprite;
            Texture2D texture = m_TargetTexture;// SpriteUtility.GetSpriteTexture(sprite, false);

            //Debug.Log(m_TargetTexture);
            //m_TargetTexture = texture;
            int2 pixelCoord = ProtoSpriteData.GetPixelCoord();

            bool isColorPickerOpened = ProtoSpriteData.IsColorPickerOpen();

            DrawHandles(t);

            // Draw brush outline, a bit distracting when painting so only show outline when resizing to help with seeing it
            if (m_IsResizingBrush && e.type == EventType.Repaint)
            {
                int2 brushOutlinePixelCoord = pixelCoord;
                if (m_IsResizingBrush)
                {
                    brushOutlinePixelCoord = new int2(m_BrushResizeMouseStart.x, m_BrushResizeMouseStart.y);
                }
                PaintTool.DrawBrushOutline(spriteRenderer, m_TargetTexture, brushOutlinePixelCoord, m_BrushSize, m_BrushShape, window as SceneView);
            }

            

            int passiveControl = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(passiveControl);

            if (Event.current.GetTypeForControl(passiveControl) == EventType.MouseDown && e.button == 0)
            {
                GUIUtility.hotControl = passiveControl;
            }

            bool preventPreview = false;

            if (e.modifiers == EventModifiers.Alt)
            {
                Color sampledColor = Color.clear;

                if (sprite.rect.Contains(new Vector2(pixelCoord.x, pixelCoord.y)))
                {
                    //sampledColor = texture.GetPixelData<Color32>(0)[pixelCoord.x + pixelCoord.y * texture.width];
                    sampledColor = texture.GetPixel(pixelCoord.x, pixelCoord.y);
                }

                Event currentEvent = Event.current;
                Vector2 mousePosition = currentEvent.mousePosition;
                Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
                Plane xy = new Plane(Vector3.forward, Vector3.zero);
                float distance;
                xy.Raycast(ray, out distance);
                Vector3 worldPosition = ray.GetPoint(distance);


                Color sampledColorRGB = sampledColor;
                sampledColorRGB.a = 1.0f;

                Color sampledColorA = Color.Lerp(Color.black, Color.white, sampledColor.a);

                Handles.BeginGUI();
                Handles.matrix = Matrix4x4.Translate(new Vector2(20, -20));
                Handles.color = Color.white;
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x-2, mousePosition.y-2, 44, 24), Color.white, Color.white);//, Vector3.forward, 16.0f);
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x-1, mousePosition.y-1, 42, 22), Color.black, Color.black);//, Vector3.forward, 16.0f);
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x, mousePosition.y, 40, 15), sampledColorRGB, sampledColorRGB);//, Vector3.forward, 16.0f);
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x-1, mousePosition.y+17, Mathf.Lerp(0, 42, sampledColor.a), 4), Color.white, Color.clear);//, Vector3.forward, 16.0f);
                //Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x, mousePosition.y + 20, 40, 5), Color.black, Color.black);//, Vector3.forward, 16.0f);
                //Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x, mousePosition.y + 20, Mathf.Lerp(0,40,sampledColor.a), 5), Color.white, Color.white);//, Vector3.forward, 16.0f);
                Handles.color = Color.black;
                //Handles.DrawSolidDisc(mousePosition, Vector3.forward, 13.0f);
                Handles.color = sampledColor;
                //Handles.DrawSolidDisc(mousePosition, Vector3.forward, 10.0f);
                Handles.EndGUI();

                preventPreview = true;
                //texture.Apply(true, false);

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    e.Use();
                    PaintColor = sampledColor;
                }
            }

            // Resizing brush
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                

                if (e.modifiers == EventModifiers.Control || e.modifiers == EventModifiers.Command)
                {
                    m_IsResizingBrush = true;
                    EditorGUIUtility.SetWantsMouseJumping(1);
                    m_BrushResizeMousePrevious = pixelCoord;
                    m_BrushResizeMouseStart = pixelCoord;
                    m_BrushResizeMouseScreenPrevious = e.mousePosition;
                }
                else
                {
                    m_IsResizingBrush = false;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                }
            }

            if (m_IsResizingBrush && e.rawType == EventType.MouseDrag)
            {
                m_BrushResizeMouseScreenPrevious.x += e.delta.x;
                int2 pixelCoordNew = ProtoSpriteData.GetPixelCoord(m_BrushResizeMouseScreenPrevious);
                int2 mouseDiff = pixelCoordNew - m_BrushResizeMousePrevious;
                m_BrushResizeMousePrevious = pixelCoordNew;

                float distance = math.length(mouseDiff);

                Vector2 mouseScreenDiff = e.delta;

                if (mouseScreenDiff.x > 0.0f)
                {
                    distance = Mathf.Abs(distance);
                }
                else
                {
                    distance = -Mathf.Abs(distance);
                }

                BrushSize += (int)distance;
            }

            // Painting
            if (e.rawType == EventType.MouseDown && e.button == 0 && !isColorPickerOpened)
            {
                m_PaintStartWindow = window;
                m_TryingToPaint = true;

                if (m_StrokeRT != null)
                {
                    RenderTexture.ReleaseTemporary(m_StrokeRT);
                    m_StrokeRT = null;
                }

                RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(m_TargetTexture.width, m_TargetTexture.height);
                rtDesc.colorFormat = RenderTextureFormat.ARGB32;
                rtDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
                rtDesc.mipCount = 1;// dstTexture.mipmapCount;
                rtDesc.autoGenerateMips = false;
                rtDesc.sRGB = true;//dstTexture.isDataSRGB;
                rtDesc.enableRandomWrite = true;
                m_StrokeRT = RenderTexture.GetTemporary(rtDesc);
                m_StrokeRT.filterMode = FilterMode.Point;

                var activeRT = RenderTexture.active;
                RenderTexture.active = m_StrokeRT;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = activeRT;

                // Holding shift allows for drawing quick lines from previous location
                if (e.modifiers != EventModifiers.Shift)
                    m_PreviousMousePixel = pixelCoord;
            }

            if (e.rawType == EventType.MouseUp && e.button == 0)
            {
                m_TryingToPaint = false;

                m_PreviousPixelCoords.Clear();

                if (m_IsResizingBrush)
                {
                    m_IsResizingBrush = false;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                }
                else
                {
                    Finish();
                }
            }
            else if (m_TryingToPaint && window == m_PaintStartWindow && (e.rawType == EventType.MouseDrag || e.rawType == EventType.MouseDown))
            {
                if (!m_IsResizingBrush && (e.modifiers == EventModifiers.None || e.modifiers == EventModifiers.Shift))
                {
                    if (!m_IsDirty)
                    {
                        m_UndoData = new UndoDataPaint();
                        m_UndoData.pixelDataBefore = texture.GetPixels32(0);
                        m_UndoData.texture = texture;
                        ProtoSpriteData.SubmitUndoData(m_UndoData, "ProtoSprite Paint Tool");
                    }

                    m_IsDirty = true;


                    if (m_PreviousPixelCoords.Count == 0 || math.any(pixelCoord != m_PreviousPixelCoords[m_PreviousPixelCoords.Count - 1]))
                    {
                        //Debug.Log("Painting: " + pixelCoord + " " + m_PreviousPixelCoords.Count);

                        if (sprite.rect.Contains(new Vector2(pixelCoord.x, pixelCoord.y)))
                        {
                            m_PreviousPixelCoords.Add(pixelCoord);
                        }

                        RenderTexture tempPixelDataRT = null;

                        if (PixelPerfect)
                        {
                            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(texture.width, texture.height);
                            rtDesc.colorFormat = RenderTextureFormat.ARGB32;
                            rtDesc.useMipMap = texture.mipmapCount > 1;
                            rtDesc.mipCount = texture.mipmapCount;
                            rtDesc.autoGenerateMips = false;
                            rtDesc.sRGB = texture.isDataSRGB;
                            rtDesc.enableRandomWrite = true;
                            tempPixelDataRT = RenderTexture.GetTemporary(rtDesc);
                            tempPixelDataRT.filterMode = texture.filterMode;
                            Graphics.CopyTexture(texture, tempPixelDataRT);

                            // Pixel perfect mode, undo previous pixel if current pixel would create an L-shape
                            if (m_PreviousPixelCoords.Count >= 3)
                            {
                                var a = m_PreviousPixelCoords;

                                var a_0 = m_PreviousPixelCoords[m_PreviousPixelCoords.Count - 1];
                                var a_1 = m_PreviousPixelCoords[m_PreviousPixelCoords.Count - 2];
                                var a_2 = m_PreviousPixelCoords[m_PreviousPixelCoords.Count - 3];

                                if ((math.abs(a_0.x - a_1.x) == 1 && a_2.x == a_1.x && math.abs(a_2.y - a_1.y) == 1 && a_0.y == a_1.y) || (math.abs(a_0.y - a_1.y) == 1 && a_2.y == a_1.y && math.abs(a_2.x - a_1.x) == 1 && a_0.x == a_1.x))
                                {
                                    Graphics.CopyTexture(m_PreviousPaintedPixelsRT, texture);
                                    m_PreviousPixelCoords.RemoveAt(m_PreviousPixelCoords.Count - 2);
                                }
                            }
                        }

                       

                        if (m_PreviousPixelCoords.Count < 2 || !math.all(pixelCoord == m_PreviousPixelCoords[m_PreviousPixelCoords.Count - 2]))
                        {
                            if (m_IsPreviewDrawn)
                            {
                                texture.Apply(true, false);
                                m_IsPreviewDrawn = false;
                            }

                            RenderTexture brushTexture = GetBrushTexture(BrushSize, BrushShape, false);
                            Vector2 pivot = new Vector2(Mathf.FloorToInt(brushTexture.width * 0.5f), Mathf.FloorToInt(brushTexture.height * 0.5f));

                            if (BrushShape == BrushShape.CUSTOM)
                            {
                                Sprite customBrushSprite = CustomBrushSprite;
                                if (customBrushSprite != null)
                                    pivot = new Vector2(Mathf.FloorToInt((customBrushSprite.pivot.x / customBrushSprite.rect.width) * brushTexture.width), Mathf.FloorToInt((customBrushSprite.pivot.y / customBrushSprite.rect.height) * brushTexture.height));
                            }


                            //BlendMode blendMode = AlphaBlend ? BlendMode.ALPHA : BlendMode.REPLACE;
                            //if (LockAlpha)
                            //blendMode = BlendMode.SOURCE;

                            //BlendMode.MAX
                            //BlendMode blendMode = StrokeMode? BlendMode.MAX : (AlphaBlend? BlendMode.ALPHA : BlendMode.REPLACE);
                            //blendMode = BlendMode.REPLACE;
                            DrawCustomBrush(brushTexture, new Rect(0, 0, brushTexture.width, brushTexture.height), pivot, texture, sprite.rect, m_PreviousMousePixel, pixelCoord, PaintColor, InkBlendMode, true);

                            //m_TargetTexture.Apply(true, false);

                            //blendMode = AlphaBlend ? BlendMode.ALPHA : BlendMode.REPLACE;
                            //DrawCustomBrush(m_StrokeRT, new Rect(0, 0, m_StrokeRT.width, m_StrokeRT.height), Vector2.zero, m_TargetTexture, new Rect(0, 0, m_StrokeRT.width, m_StrokeRT.height), int2.zero, int2.zero, Color.white, blendMode, true);


                            RenderTexture.ReleaseTemporary(brushTexture);
                        }

                        if (m_PreviousPaintedPixelsRT != null)
                        {
                            RenderTexture.ReleaseTemporary(m_PreviousPaintedPixelsRT);
                            m_PreviousPaintedPixelsRT = null;
                        }

                        m_PreviousPaintedPixelsRT = tempPixelDataRT;
                    }
                }
            }

            if ((e.rawType == EventType.MouseDrag || e.rawType == EventType.MouseDown) && e.button == 0 && !isColorPickerOpened  && window == m_PaintStartWindow)
            {
                m_PreviousMousePixel = pixelCoord;
            }

            if (m_TryingToPaint && !m_IsResizingBrush)
                preventPreview = true;

            // Preview
            //return;

            if (e.type != EventType.Repaint)
                return;

            if (m_TryingToPaint && m_PaintStartWindow != window)
                return;


            var mouseOverWindow = EditorWindow.mouseOverWindow;

            if (mouseOverWindow != window && !m_IsResizingBrush)
            {
                if (mouseOverWindow == null || mouseOverWindow.GetType() != typeof(SceneView))
                {
                    if (m_IsPreviewDrawn)
                    {
                        m_TargetTexture.Apply(true, false);
                        m_IsPreviewDrawn = false;
                    }
                }
            }


            if (preventPreview)
            {
                if (m_IsPreviewDrawn)
                {
                    m_TargetTexture.Apply(true, false);
                    m_IsPreviewDrawn = false;
                }
                return;
            }

            bool shouldDrawPreview = !isColorPickerOpened && ((mouseOverWindow != null && mouseOverWindow == window && mouseOverWindow.GetType() == typeof(SceneView)) || m_IsResizingBrush || (m_TryingToPaint && m_PaintStartWindow == window));

            if (shouldDrawPreview)
            {
                int2 endTexel = pixelCoord;
                

                int2 startTexel = pixelCoord;
                if (e.modifiers == EventModifiers.Shift)
                {
                    startTexel = m_PreviousMousePixel;
                }

                if (m_IsResizingBrush)
                {
                    startTexel = m_BrushResizeMouseStart;
                    endTexel = m_BrushResizeMouseStart;
                }


                texture.Apply(true, false);


                RenderTexture brushTexture = GetBrushTexture(BrushSize, BrushShape, false);
                Vector2 pivot = new Vector2(Mathf.FloorToInt(brushTexture.width * 0.5f), Mathf.FloorToInt(brushTexture.height * 0.5f));

                if (BrushShape == BrushShape.CUSTOM)
                {
                    Sprite customBrushSprite = CustomBrushSprite;
                    if (customBrushSprite != null)
                        pivot = new Vector2(Mathf.FloorToInt((customBrushSprite.pivot.x / customBrushSprite.rect.width) * brushTexture.width), Mathf.FloorToInt((customBrushSprite.pivot.y / customBrushSprite.rect.height) * brushTexture.height));
                }

                //BlendMode blendMode = AlphaBlend ? BlendMode.ALPHA : BlendMode.REPLACE;
                //if (LockAlpha)
                    //blendMode = BlendMode.SOURCE;

                DrawCustomBrush(brushTexture, new Rect(0, 0, brushTexture.width, brushTexture.height), pivot, texture, sprite.rect, startTexel, endTexel, PaintColor, InkBlendMode, false);

                RenderTexture.ReleaseTemporary(brushTexture);


                /*if (m_CustomBrushSprite != null && m_BrushShape == BrushShape.CUSTOM)
                {
                    DrawCustomBrush(CustomBrushSprite.texture, CustomBrushSprite.rect, CustomBrushSprite.pivot, sprite, startTexel, endTexel, PaintColor, AlphaBlend, false);
                }
                else
                {
                    var brushTexture = BrushShape == BrushShape.CIRCLE? GetCircleBrushTexture(BrushSize) : GetSquareBrushTexture(BrushSize);


                    DrawCustomBrush(brushTexture, new Rect(0, 0, BrushSize, BrushSize), new Vector2(Mathf.FloorToInt(BrushSize * 0.5f), Mathf.FloorToInt(BrushSize * 0.5f)), sprite, startTexel, endTexel, PaintColor, AlphaBlend, false);

                    RenderTexture.ReleaseTemporary(brushTexture);
                    //ProtoSpriteData.DrawPreviewGPU(sprite, startTexel, endTexel, BrushSize, PaintColor, BrushShape);
                }*/

                m_IsPreviewDrawn = true;
            }
        }

        public static void DrawCustomBrush(Texture srcTexture, Rect srcSpriteRect, Vector2 srcPivot, Texture dstTexture, Rect dstSpriteRect, int2 startTexel, int2 endTexel, Color color, BlendMode alphaBlend, bool copyToCPU)
        {
            int2 srcSpritePivot = new int2(Mathf.RoundToInt(srcPivot.x), Mathf.RoundToInt(srcPivot.y));

            startTexel -= srcSpritePivot;
            endTexel -= srcSpritePivot;


            ProtoSpriteData.DrawCustomBrushGPUToCPU(srcTexture, srcSpriteRect, dstTexture, dstSpriteRect, startTexel, endTexel, color, dstSpriteRect, alphaBlend, copyToCPU);

            //ProtoSpriteData.DrawCustomBrush(srcTexture, srcSpriteRect, dstTexture, dstSpriteRect, startTexel, endTexel, color, dstSpriteRect, alphaBlend, copyToCPU);
        }


        /*public static void DrawCustomBrush(Texture srcTexture, Rect srcSpriteRect, Vector2 srcPivot, Sprite sprite, int2 startTexel, int2 endTexel, Color color, bool alphaBlend)
        {
            int2 srcSpritePivot = new int2(Mathf.RoundToInt(srcPivot.x), Mathf.RoundToInt(srcPivot.y));

            startTexel -= srcSpritePivot;
            endTexel -= srcSpritePivot;

            ProtoSpriteData.DrawCustomBrushGPUToCPU(srcTexture, srcSpriteRect, sprite.texture, sprite.rect, startTexel, endTexel, color, sprite.rect, alphaBlend);
        }*/

        public static RenderTexture GetBrushTexture(int brushSize, BrushShape brushShape, bool convertToBump)
        {
            RenderTexture rt = null;

            /*float softness = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().Softness * 1.0f;

            if (softness > 0.0f)
            {
                //brushSize = (int)(brushSize * Mathf.Lerp(1.0f, 1.0f / 2f, softness));// (int)(brushSize / (2.0f / (1.0f - ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().Softness)));// Mathf.CeilToInt(brushSize * 0.5f * ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().Softness * 3.0f) / 2;
                //if (brushSize <= 0)
                    //brushSize = 1;

                Debug.Log("ammending brushsize: " + brushSize);
            }*/


            if (brushShape == BrushShape.CIRCLE)
            {
                rt = GetCircleBrushTexture(brushSize);
            }
            else if (brushShape == BrushShape.SQUARE)
            {
                rt = GetSquareBrushTexture(brushSize);
            }
            else
            {
                rt = GetScaledCustomBrushTexture(brushSize);
            }

            // Smoothness
            /*{
                //return rt;
                int2 howManyPixels = new int2(Mathf.CeilToInt(rt.width * 0.5f * softness), Mathf.CeilToInt(rt.height * 0.5f * softness));

                //int2 blurSize = new int2(Mathf.FloorToInt(rt.width * softness), Mathf.FloorToInt(rt.height * softness));

                var activeRT = RenderTexture.active;

                RenderTexture.active = rt;

                Material blurMat = ProtoSpriteData.instance.OutlineMaterial;
                blurMat.SetTexture("_GrabPass", null);
                blurMat.SetTexture("_MainTex", null);

                //RenderTexture rt = source;
                //Material mat = new Material(Shader.Find("Blur"));
                var rtDescriptor = rt.descriptor;
                rtDescriptor.width += howManyPixels.x * 2;
                rtDescriptor.height += howManyPixels.y * 2;

                //RenderTexture.ReleaseTemporary(rt);
                //int actualBrushSize = Mathf.FloorToInt(Mathf.Max(brushSize - howManyPixels.x * 0.25f, 1));
                //Debug.Log("actual brush size: " + actualBrushSize);
                //rt = GetCircleBrushTexture(actualBrushSize);

                Debug.Log(rtDescriptor.width + " " + rtDescriptor.height);

                RenderTexture blitA = RenderTexture.GetTemporary(rtDescriptor);
                blitA.filterMode = FilterMode.Point;

                RenderTexture blitB = RenderTexture.GetTemporary(rtDescriptor);
                blitB.filterMode = FilterMode.Point;

                blurMat.EnableKeyword("PREMULTIPLY");
                //GL.sRGBWrite = false;
                //ProtoSpriteData.instance.OutlineMaterial.SetPass(4);
                RenderTexture.active = blitA;
                GL.Clear(true, true, Color.clear);
                //rt.filterMode = FilterMode.Bilinear;
                //Graphics.DrawTexture(new Rect(0, 0, blitA.width, blitA.height), rt);
                //Graphics.Blit(rt, blitA, Vector2.one * 2, Vector2.one * 0.5f);
                ProtoSpriteData.BlitRect(rt, new Rect(0, 0, rt.width, rt.height), blitA, new Rect(blitA.width * 0.5f - rt.width * 0.5f, blitA.height * 0.5f - rt.height * 0.5f, rt.width, rt.height), blurMat, 4);


                Debug.Log("howmanyPixels: " + howManyPixels);

                int loops = Mathf.FloorToInt(howManyPixels.x / 16.0f);

                int remainder = howManyPixels.x % 16;

                for (int i = 0; i < loops; i++)
                {
                    blurMat.SetVector("_BlurDirection", new Vector2(0.25f * 16, 0));
                    RenderTexture.active = (blitB);
                    GL.Clear(true, true, Color.clear);
                    Graphics.Blit(blitA, blitB, blurMat, 5);
                    RenderTexture.active = (blitA);
                    blurMat.SetVector("_BlurDirection", new Vector2(0, 0.25f * 16));
                    GL.Clear(true, true, Color.clear);
                    Graphics.Blit(blitB, blitA, blurMat, 5);
                }

                for (int i = 0; i < remainder; i++)
                {
                    blurMat.SetVector("_BlurDirection", new Vector2(0.25f * 1, 0));
                    RenderTexture.active = (blitB);
                    GL.Clear(true, true, Color.clear);
                    Graphics.Blit(blitA, blitB, blurMat, 5);
                    RenderTexture.active = (blitA);
                    blurMat.SetVector("_BlurDirection", new Vector2(0, 0.25f * 1));
                    GL.Clear(true, true, Color.clear);
                    Graphics.Blit(blitB, blitA, blurMat, 5);
                }


                blurMat.DisableKeyword("PREMULTIPLY");
                //GL.sRGBWrite = false;
                blurMat.SetTexture("_GrabPass", null);
                blurMat.SetTexture("_MainTex", null);

                if (Mathf.Max(blitB.width, blitB.height) > 2048)
                {
                    Graphics.Blit(blitA, rt, ProtoSpriteData.instance.OutlineMaterial, 4);
                    RenderTexture.ReleaseTemporary(blitA);
                    //Graphics.Blit(blitB, rt);
                    RenderTexture.ReleaseTemporary(blitB);
                    RenderTexture.active = activeRT;
                    return rt;
                }
                else
                {
                    Graphics.Blit(blitA, blitB, ProtoSpriteData.instance.OutlineMaterial, 4);
                    RenderTexture.ReleaseTemporary(blitA);
                    RenderTexture.ReleaseTemporary(rt);
                    RenderTexture.active = activeRT;
                    return blitB;
                }



                //return blitB;
                //return rt;

            }*/

            /*if (convertToBump)
            {
                RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(rt.width, rt.height);
                rtDesc.colorFormat = rt.format;// RenderTextureFormat.ARGB32;
                rtDesc.useMipMap = rt.useMipMap;// dstTexture.mipmapCount > 1;
                rtDesc.mipCount = rt.mipmapCount;// dstTexture.mipmapCount;
                rtDesc.autoGenerateMips = rt.autoGenerateMips;
                rtDesc.sRGB = rt.sRGB;// dstTexture.isDataSRGB;
                rtDesc.enableRandomWrite = rt.enableRandomWrite;
                RenderTexture rtBump = RenderTexture.GetTemporary(rtDesc);
                rtBump.filterMode = rt.filterMode;

                var activeRT = RenderTexture.active;

                RenderTexture.active = rtBump;

                GL.Clear(true, true, Color.clear);

                Material mat = new Material(Shader.Find("Hidden/ProtoSprite/ConvertToBump"));

                Graphics.Blit(rt, rtBump, mat, 0);

                DestroyImmediate(mat);

                RenderTexture.active = activeRT;

                RenderTexture.ReleaseTemporary(rt);

                rt = rtBump;
            }*/

            return rt;
        }

        public static RenderTexture GetCircleBrushTexture(int brushSize)
        {
            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(brushSize, brushSize);
            rtDesc.colorFormat = RenderTextureFormat.ARGB32;
            rtDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
            rtDesc.mipCount = 1;// dstTexture.mipmapCount;
            rtDesc.autoGenerateMips = false;
            rtDesc.sRGB = true;// dstTexture.isDataSRGB;
            rtDesc.enableRandomWrite = true;
            RenderTexture rt = RenderTexture.GetTemporary(rtDesc);
            rt.filterMode = FilterMode.Point;

            var activeRT = RenderTexture.active;

            RenderTexture.active = rt;

            GL.Clear(true, true, Color.clear);

            int2 drawPixel = new int2(Mathf.FloorToInt(brushSize * 0.5f), Mathf.FloorToInt(brushSize * 0.5f));

            //int fakeBrushSize = brushSize / 2;

            ProtoSpriteData.DrawLineGPU(rt, new Rect(0, 0, rt.width, rt.height), drawPixel, drawPixel, brushSize, Color.white, BrushShape.CIRCLE);

            RenderTexture.active = activeRT;

            return rt;
        }

        public static RenderTexture GetSquareBrushTexture(int brushSize)
        {
            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(brushSize, brushSize);
            rtDesc.colorFormat = RenderTextureFormat.ARGB32;
            rtDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
            rtDesc.mipCount = 1;// dstTexture.mipmapCount;
            rtDesc.autoGenerateMips = false;
            rtDesc.sRGB = true;// dstTexture.isDataSRGB;
            rtDesc.enableRandomWrite = true;
            RenderTexture rt = RenderTexture.GetTemporary(rtDesc);
            rt.filterMode = FilterMode.Point;

            var activeRT = RenderTexture.active;

            RenderTexture.active = rt;

            GL.Clear(true, true, Color.white);

            //int fakeBrushSize = brushSize / 2;

            //int2 drawPixel = new int2(Mathf.FloorToInt(brushSize * 0.5f), Mathf.FloorToInt(brushSize * 0.5f));
            //ProtoSpriteData.DrawLineGPU(rt, new Rect(0, 0, rt.width, rt.height), drawPixel, drawPixel, brushSize, Color.white, BrushShape.SQUARE);

            //int2 drawPixel = new int2(Mathf.FloorToInt(brushSize * 0.5f), Mathf.FloorToInt(brushSize * 0.5f));

            //ProtoSpriteData.DrawLineGPU(rt, new Rect(0, 0, rt.width, rt.height), drawPixel, drawPixel, brushSize, Color.white, BrushShape.CIRCLE);

            RenderTexture.active = activeRT;

            return rt;
        }

        public static RenderTexture GetScaledCustomBrushTexture(int brushSize)
        {
            int2 targetSize = new int2(brushSize, brushSize);

            Sprite customBrushSprite = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().CustomBrushSprite;

            if (customBrushSprite != null)
            {
                if (customBrushSprite.rect.width > customBrushSprite.rect.height)
                {
                    float ratio = customBrushSprite.rect.width / brushSize;

                    targetSize.x = (int)(customBrushSprite.rect.width / ratio);
                    targetSize.y = (int)(customBrushSprite.rect.height / ratio);
                }
                else
                {
                    float ratio = customBrushSprite.rect.height / brushSize;

                    targetSize.x = (int)(customBrushSprite.rect.width / ratio);
                    targetSize.y = (int)(customBrushSprite.rect.height / ratio);
                }
            }


            targetSize = math.max(targetSize, new int2(1, 1));

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(targetSize.x, targetSize.y);
            rtDesc.colorFormat = RenderTextureFormat.ARGB32;
            rtDesc.useMipMap = false;// dstTexture.mipmapCount > 1;
            rtDesc.mipCount = 1;// dstTexture.mipmapCount;
            rtDesc.autoGenerateMips = false;
            rtDesc.sRGB = true;//dstTexture.isDataSRGB;
            rtDesc.enableRandomWrite = true;
            RenderTexture rt = RenderTexture.GetTemporary(rtDesc);
            rt.filterMode = FilterMode.Point;

            var activeRT = RenderTexture.active;

            RenderTexture.active = rt;

            GL.Clear(true, true, Color.white);


            if (customBrushSprite != null)
            {
                Graphics.Blit(customBrushSprite.texture, rt, customBrushSprite.rect.size / new Vector2(customBrushSprite.texture.width, customBrushSprite.texture.height), customBrushSprite.rect.position / new Vector2(customBrushSprite.texture.width, customBrushSprite.texture.height));
            }

            //int2 drawPixel = new int2(Mathf.FloorToInt(brushSize * 0.5f), Mathf.FloorToInt(brushSize * 0.5f));

            //ProtoSpriteData.DrawLineGPU(rt, new Rect(0, 0, rt.width, rt.height), drawPixel, drawPixel, brushSize, Color.white, BrushShape.CIRCLE);

            RenderTexture.active = activeRT;

            return rt;
        }
    }
}