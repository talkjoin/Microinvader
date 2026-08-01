using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEditor.Sprites;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEditor.ShortcutManagement;

namespace ProtoSprite.Editor
{
    public class FillTool : ProtoSpriteTool
    {
        Texture2D m_TargetTexture = null;

        int m_Tolerance = 25;
        bool m_Contiguous = true;
        Connectivity m_Connectivity = Connectivity.FOUR;

        bool m_IsDirty = false;
        bool m_IsPreviewDrawn = false;

        UndoDataPaint m_UndoData = null;

        bool m_SceneSelectionOutlineGizmoEnabled = false;

        static int s_ColorPickID = "EyeDropper".GetHashCode();

        public Color PaintColor
        {
            get
            {
                return ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().PaintColor;
            }
            set
            {
                ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().PaintColor = value;
            }
        }

        enum Connectivity
        {
            FOUR,
            EIGHT
        }

        public int Tolerance
        {
            get => m_Tolerance; set
            {
                value = Mathf.Clamp(value, 0, 255);
                m_Tolerance = value;
            }
        }

        public override GUIContent toolbarIcon
        {
            get
            {
                GUIContent content = new GUIContent(EditorGUIUtility.IconContent("Grid.FillTool"));
                content.tooltip = "Fill (" + ShortcutManager.instance.GetShortcutBinding("ProtoSprite/Fill Tool") + ")";
                return content;
            }
        }

		public void OnEnable()
        {
            Tolerance = EditorPrefs.GetInt("ProtoSprite.Editor.FillTool.Tolerance", 25);
            m_Contiguous = EditorPrefs.GetBool("ProtoSprite.Editor.FillTool.Contiguous", true);
            m_Connectivity = (Connectivity)EditorPrefs.GetInt("ProtoSprite.Editor.FillTool.Connectivity", (int)Connectivity.FOUR);
        }

		public void OnDisable()
        {
            if (ToolManager.activeToolType == GetType())
                OnWillBeDeactivated();

            EditorPrefs.SetInt("ProtoSprite.Editor.FillTool.Tolerance", Tolerance);
            EditorPrefs.SetBool("ProtoSprite.Editor.FillTool.Contiguous", m_Contiguous);
            EditorPrefs.SetInt("ProtoSprite.Editor.FillTool.Connectivity", (int)m_Connectivity);
        }

        [Shortcut("ProtoSprite/Fill Tool", typeof(InternalEngineBridge.ShortcutContext), ProtoSpriteWindow.kToolShortcutsTag, KeyCode.G)]
        public static void ToggleTool()
        {
            ProtoSpriteWindow.ToggleTool<FillTool>();
        }

        public override void ProtoSpriteWindowGUI()
        {
            base.ProtoSpriteWindowGUI();

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

            Tolerance = EditorGUILayout.IntField("Tolerance", Tolerance);
            m_Contiguous = EditorGUILayout.Toggle("Contiguous", m_Contiguous);

            string[] connectivityLabels = new string[2];
            connectivityLabels[0] = "4-way";
            connectivityLabels[1] = "8-way";

            m_Connectivity = (Connectivity)EditorGUILayout.Popup("Connectivity", (int)m_Connectivity, connectivityLabels);

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

            m_IsPreviewDrawn = false;
            m_IsDirty = false;
        }

        void Finish()
        {
            if (m_TargetTexture == null)
                return;

            if (m_IsPreviewDrawn)
            {
                m_TargetTexture.Apply(true, false);
                m_IsPreviewDrawn = false;
            }

            if (m_IsDirty)
            {
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

            Texture2D selectedTexture = null;

            if (validSelection)
            {
                Transform t = Selection.activeTransform;
                SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
                Sprite sprite = spriteRenderer.sprite;
                selectedTexture = ProtoSpriteWindow.GetTargetTexture();// SpriteUtility.GetSpriteTexture(sprite, false);
            }

            bool changedTarget = m_TargetTexture != selectedTexture;

            if (changedTarget)
            {
                Finish();
            }

            m_TargetTexture = selectedTexture;

            /*bool validSelection = ProtoSpriteWindow.IsSelectionValidProtoSprite(out string reason);
            bool changedTarget = false;

            Texture2D selectedTexture = null;

            if (m_TargetTexture != null && !validSelection)
            {
                changedTarget = true;
            }

            if (m_TargetTexture != null && validSelection)
            {
                Transform t = Selection.activeTransform;
                SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
                Sprite sprite = spriteRenderer.sprite;
                selectedTexture = SpriteUtility.GetSpriteTexture(sprite, false);

                changedTarget = m_TargetTexture != selectedTexture;
            }

            if (changedTarget)
            {
                Finish();
            }

            m_TargetTexture = selectedTexture;*/
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

        void HandleEyeDropper()
        {
            Event e = Event.current;

            if (e.type == EventType.ExecuteCommand)
            {
                if (e.commandName == "EyeDropperClicked")
                {
                    PaintColor = ProtoSpriteData.GetEyeDropperLastPickedColor();
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
            Texture2D texture = m_TargetTexture;
            int2 pixelCoord = ProtoSpriteData.GetPixelCoord();

            bool isColorPickerOpened = ProtoSpriteData.IsColorPickerOpen();

            DrawHandles(t);

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
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x - 2, mousePosition.y - 2, 44, 24), Color.white, Color.white);//, Vector3.forward, 16.0f);
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x - 1, mousePosition.y - 1, 42, 22), Color.black, Color.black);//, Vector3.forward, 16.0f);
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x, mousePosition.y, 40, 15), sampledColorRGB, sampledColorRGB);//, Vector3.forward, 16.0f);
                Handles.DrawSolidRectangleWithOutline(new Rect(mousePosition.x - 1, mousePosition.y + 17, Mathf.Lerp(0, 42, sampledColor.a), 4), Color.white, Color.clear);//, Vector3.forward, 16.0f);
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

            if (e.rawType == EventType.MouseUp && e.button == 0)
            {
                Finish();
            }
            else if (e.rawType == EventType.MouseDown && e.button == 0)
            {
                if (e.modifiers == EventModifiers.None)
                {
                    if (!m_IsDirty)
                    {
                        m_UndoData = new UndoDataPaint();
                        m_UndoData.pixelDataBefore = texture.GetPixels32(0);
                        m_UndoData.texture = texture;
                        ProtoSpriteData.SubmitUndoData(m_UndoData, "ProtoSprite Fill Tool");
                    }

                    m_IsDirty = true;

                    var pixelData = texture.GetPixelData<Color32>(0);
                    var textureSize = new int2(texture.width, texture.height);

                    Fill(pixelCoord, ref pixelData, textureSize, sprite.rect, PaintColor);
                    texture.Apply(true, false);
                }
            }

            // Preview
            if (e.type != EventType.Repaint)
                return;

            var mouseOverWindow = EditorWindow.mouseOverWindow;

            if (mouseOverWindow != window)
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

            bool shouldDrawPreview = !isColorPickerOpened && ((mouseOverWindow != null && mouseOverWindow == window && mouseOverWindow.GetType() == typeof(SceneView)));

            if (shouldDrawPreview)
            {
                texture.Apply(false, false);

                RenderTexture brushTexture = PaintTool.GetBrushTexture(1, BrushShape.CIRCLE, false);
                Vector2 pivot = new Vector2(Mathf.FloorToInt(1 * 0.5f), Mathf.FloorToInt(1 * 0.5f));
                PaintTool.DrawCustomBrush(brushTexture, new Rect(0, 0, brushTexture.width, brushTexture.height), pivot, texture, sprite.rect, pixelCoord, pixelCoord, PaintColor, BlendMode.REPLACE, false);

                RenderTexture.ReleaseTemporary(brushTexture);

                //ProtoSpriteData.DrawPreviewGPU(sprite, pixelCoord, pixelCoord, 1, PaintColor, BrushShape.CIRCLE);

                m_IsPreviewDrawn = true;
            }
        }


        [BurstCompile]
        struct FillJob : IJob
        {
            [NativeDisableParallelForRestriction] public NativeArray<Color32> pixelData;
            [NativeDisableParallelForRestriction] public NativeArray<int> done;
            [NativeDisableParallelForRestriction] public NativeList<int2> todo;

            [ReadOnly] public int2 pixel;
            [ReadOnly] public int2 textureSize;
            [ReadOnly] public Color32 paintColor;
            [ReadOnly] public Color32 color;
            [ReadOnly] public int tolerance;
            [ReadOnly] public Connectivity connectivity;
            [ReadOnly] public Rect spriteRect;

            public void Execute()
            {
                while (todo.Length > 0)
                {
                    int2 currentPixel = todo[0];

                    todo.RemoveAtSwapBack(0);

                    pixelData[currentPixel.x + currentPixel.y * textureSize.x] = paintColor;

                    for (int x = -1; x <= 1; x++)
                    {
                        for (int y = -1; y <= 1; y++)
                        {
                            if (x == 0 && y == 0)
                                continue;

                            if (connectivity == Connectivity.FOUR)
                            {
                                if ((x == -1 && y == -1) || (x == 1 && y == -1) || (x == 1 && y == 1) || (x == -1 && y == 1))
                                    continue;
                            }

                            int2 neighbour = currentPixel + new int2(x, y);

                            if (neighbour.x < spriteRect.xMin || neighbour.y < spriteRect.yMin || neighbour.x >= spriteRect.xMax || neighbour.y >= spriteRect.yMax)
                                continue;

                            if (neighbour.x < 0 || neighbour.y < 0 || neighbour.x >= textureSize.x || neighbour.y >= textureSize.y)
                                continue;

                            int doneIndex = neighbour.x + neighbour.y * textureSize.x;

                            if (done[doneIndex] == 1)
                                continue;

                            done[doneIndex] = 1;


                            Color32 neighbourColor = pixelData[neighbour.x + neighbour.y * textureSize.x];

                            if (neighbourColor.a == 0 && color.a == 0)
                            {
                                todo.Add(neighbour);
                                continue;
                            }

                            if (math.abs(neighbourColor.r - color.r) > tolerance ||
                            math.abs(neighbourColor.g - color.g) > tolerance ||
                            math.abs(neighbourColor.b - color.b) > tolerance ||
                            math.abs(neighbourColor.a - color.a) > tolerance)
                            {
                                continue;
                            }

                            todo.Add(neighbour);
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct FillNonContiguousJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Color32> pixelData;

            [ReadOnly] public int2 textureSize;
            [ReadOnly] public Color32 paintColor;
            [ReadOnly] public Color32 sourceColor;
            [ReadOnly] public int tolerance;
            [ReadOnly] public Rect spriteRect;

            public void Execute(int i)
            {
                Color32 color = pixelData[i];

                int2 pixelCoord = new int2(i % textureSize.x, i / textureSize.x);

                if (pixelCoord.x < spriteRect.xMin || pixelCoord.y < spriteRect.yMin || pixelCoord.x >= spriteRect.xMax || pixelCoord.y >= spriteRect.yMax)
                    return;

                if (sourceColor.a == 0 && color.a == 0)
                {
                    pixelData[i] = paintColor;
                    return;
                }

                if (math.abs(sourceColor.r - color.r) > tolerance ||
                math.abs(sourceColor.g - color.g) > tolerance ||
                math.abs(sourceColor.b - color.b) > tolerance ||
                math.abs(sourceColor.a - color.a) > tolerance)
                {
                    return;
                }

                pixelData[i] = paintColor;
            }
        }

        void Fill(int2 pixel, ref NativeArray<Color32> pixelData, int2 textureSize, Rect spriteRect, Color paintColor)
        {
            if (pixel.x < 0 || pixel.y < 0 || pixel.x >= textureSize.x || pixel.y >= textureSize.y)
                return;

            Color32 color = pixelData[pixel.x + pixel.y * textureSize.x];

            var done = new NativeArray<int>(pixelData.Length, Allocator.Persistent);
            done[pixel.x + pixel.y * textureSize.x] = 1;

            var todo = new NativeList<int2>(0, Allocator.Persistent);
            todo.Add(pixel);

            var fillable = new NativeList<int2>(0, Allocator.Persistent);
            fillable.Add(pixel);

            if (m_Contiguous)
            {
                FillJob fillJob;
                fillJob.done = done;
                fillJob.todo = todo;
                fillJob.paintColor = paintColor;
                fillJob.color = color;
                fillJob.pixelData = pixelData;
                fillJob.textureSize = textureSize;
                fillJob.pixel = pixel;
                fillJob.tolerance = Tolerance;
                fillJob.connectivity = m_Connectivity;
                fillJob.spriteRect = spriteRect;

                fillJob.Schedule().Complete();
            }
            else
            {
                FillNonContiguousJob fillJob;
                fillJob.paintColor = paintColor;
                fillJob.sourceColor = color;
                fillJob.pixelData = pixelData;
                fillJob.textureSize = textureSize;
                fillJob.tolerance = Tolerance;
                fillJob.spriteRect = spriteRect;

                fillJob.Schedule(pixelData.Length, 100).Complete();
            }

            done.Dispose();
            todo.Dispose();
            fillable.Dispose();
        }
    }
}
