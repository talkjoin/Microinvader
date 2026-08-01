using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEngine;
using Unity.Mathematics;

namespace ProtoSprite.Editor
{
    public class ColorPalette
    {

        bool m_Locked = true;

        int m_SelectedIndex = -1;

        string m_LastSavePaletteFolder = "";

        GUIStyle backgroundStyle = null;

        const int kMaxPaletteSize = 256;

        List<Color> m_Colors = new List<Color>()
        {
            Color.black,
            Color.white,
            Color.green,
            Color.blue,
            Color.red
        };

        struct ButtonTextureData
        {
            public Texture2D texture;
            public Color color;
            public bool selected;
        }

        List<ButtonTextureData> m_ButtonTextures = new List<ButtonTextureData>();

        Color[] m_TempPixels = new Color[kButtonTextureResolution * kButtonTextureResolution];

        const int kButtonTextureResolution = 30;

        Texture2D m_NormalMapColorWheelTexture = null;

        bool m_NormalMapMode = false;
        //int m_NormalMapDiscreteLevels = 8;

        [System.Serializable]
        public class SerializableList<T>
        {
            public List<T> list;
        }

        void UpdateButtonTextures()
        {
            for (int i = 0; i < m_ButtonTextures.Count; i++)
            {
                if (m_ButtonTextures[i].texture == null)
                {
                    m_ButtonTextures.RemoveAt(i);
                    i--;
                }
            }

            for (int i = m_ButtonTextures.Count; i < m_Colors.Count; i++)
            {
                Texture2D texture = new Texture2D(kButtonTextureResolution, kButtonTextureResolution);
                texture.filterMode = FilterMode.Bilinear;
                texture.Apply();
                var data = new ButtonTextureData();
                data.texture = texture;

                data.color = m_Colors[i];
                data.selected = m_SelectedIndex == i;
                m_ButtonTextures.Add(data);

                SetTextureColor(m_ButtonTextures[i].texture, m_Colors[i], m_SelectedIndex == i);
            }

            for (int i = m_Colors.Count; i < m_ButtonTextures.Count; i++)
            {
                GameObject.DestroyImmediate(m_ButtonTextures[0].texture);
                m_ButtonTextures.RemoveAt(0);
            }

            for (int i = 0; i < m_Colors.Count; i++)
            {
                var data = m_ButtonTextures[i];
                if (data.color != m_Colors[i] || data.selected != (m_SelectedIndex == i))
                {
                    data.color = m_Colors[i];
                    data.selected = m_SelectedIndex == i;
                    m_ButtonTextures[i] = data;
                    SetTextureColor(m_ButtonTextures[i].texture, m_Colors[i], m_SelectedIndex == i);
                }
            }
        }

        private void SetTextureColor(Texture2D texture, Color color, bool selected)
        {
            Color[] pixels = m_TempPixels;

            bool isSelected = selected;

            int whiteBorderSize = 2;
            int blackBorderSize = 2;

            int width = texture.width;
            int height = texture.height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (isSelected && (x < whiteBorderSize || y < whiteBorderSize || x >= (width - whiteBorderSize) || y >= (height - whiteBorderSize)))
                    {
                        pixels[x + y * width] = Color.white;

                    }
                    else if (isSelected && (x < whiteBorderSize + blackBorderSize || y < whiteBorderSize + blackBorderSize || x >= width - (whiteBorderSize + blackBorderSize) || y >= height - (whiteBorderSize + blackBorderSize)))
                    {
                        pixels[x + y * width] = Color.black;
                    }
                    else
                    {
                        pixels[x + y * width] = color;

                        // Alpha checkered overlay
                        if (color.a < 1.0f)
                        {
                            float density = 2.0f;

                            float2 uv = new float2(x / (float)width, (float)y / height);
                            uv *= density;

                            float2 c = uv;
                            c = math.floor(c) / 2.0f;

                            float checker = math.frac(c.x + c.y) * 2.0f;

                            Color checkeredColor = new Color(0.3f, 0.3f, 0.3f);
                            if (checker == 0)
                            {
                                checkeredColor = new Color(0.6f, 0.6f, 0.6f);
                            }
                            pixels[x + y * width] = Color.Lerp(checkeredColor, color, color.a);
                        }
                    }

                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);
        }

        public void OnEnable()
        {
            //Debug.Log(Time.frameCount + " palette enable");

            string savedPaletteJSON = EditorPrefs.GetString("ProtoSprite.Editor.ColorPalette.Colors", "");
            m_NormalMapMode = EditorPrefs.GetInt("ProtoSprite.Editor.ColorPalette.NormalMapMode", 0) != 0;
            //m_NormalMapDiscreteLevels = EditorPrefs.GetInt("ProtoSprite.Editor.ColorPalette.NormalMapDiscreteLevels", 8);

            m_NormalMapColorWheelTexture = GetNormalMapPaletteTexture();

            //Debug.Log(savedPaletteJSON);

            if (!string.IsNullOrEmpty(savedPaletteJSON))
            {
                m_Colors = JsonUtility.FromJson<SerializableList<Color>>(savedPaletteJSON).list;
                UpdateButtonTextures();
            }
            else
            {
                LoadDefaultPalette();
            }
        }

		public void OnDisable()
		{
            string savedPaletteJSON = JsonUtility.ToJson(new SerializableList<Color>() { list = m_Colors });

            if (m_NormalMapColorWheelTexture != null)
            {
                GameObject.DestroyImmediate(m_NormalMapColorWheelTexture);
                m_NormalMapColorWheelTexture = null;
            }
            //Debug.Log("COLOR PALETTE SAVED: " + savedPaletteJSON);

            EditorPrefs.SetString("ProtoSprite.Editor.ColorPalette.Colors", savedPaletteJSON);
            EditorPrefs.SetInt("ProtoSprite.Editor.ColorPalette.NormalMapMode", m_NormalMapMode? 1 : 0);
            //EditorPrefs.SetInt("ProtoSprite.Editor.ColorPalette.NormalMapDiscreteLevels", m_NormalMapDiscreteLevels);

            for (int i = 0; i < m_ButtonTextures.Count; i++)
            {
                if (m_ButtonTextures[i].texture != null)
                {
                    GameObject.DestroyImmediate(m_ButtonTextures[i].texture);
                    //m_ButtonTextures[i] = null;
                }
            }

            m_ButtonTextures.Clear();
        }

        Texture2D GetNormalMapPaletteTexture()
        {
            Texture2D texture = new Texture2D(150, 150);

            Vector2 centerPos = new Vector2(texture.width, texture.height) * 0.5f;

            for (int x = 0; x < texture.width; x++)
            {
                for (int y = 0; y < texture.height; y++)
                {
                    Vector3 direction = Vector3.forward;

                    Vector2 pos = new Vector2(x, y);

                    // Map x, y coordinates to range [-1, 1]
                    float u = (x + 0.5f) / texture.width * 2f - 1f;
                    float v = (y + 0.5f) / texture.width * 2f - 1f;

                    // Calculate z coordinate using semi-sphere equation
                    float z = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u - v * v));

                    if (Vector2.Distance(pos, centerPos) <= texture.width * 0.5f)
                    {
                        direction = new Vector3(u, v, z);
                    }


                    // Return point on semi-sphere
                    Vector3 normal = direction.normalized;
                     //int discreteLevels = m_NormalMapDiscreteLevels;

                     /*float interval = 2f / (discreteLevels - 1);
                     normal.x = Mathf.Round(normal.x / interval) * interval;
                     normal.y = Mathf.Round(normal.y / interval) * interval;
                     normal.z = Mathf.Round(normal.z / interval) * interval;

                     normal.Normalize();*/

                    // Calculate the angle between the forward vector and the normal vector
                    /*float angle = Vector3.Angle(Vector3.forward, normal);

                    // Quantize the angle based on the threshold
                    float interval = 90f / (discreteLevels - 1); // Angle range from 0 to 90 degrees
                    float quantizedAngle = Mathf.Round(angle / interval) * interval;

                    // Map the quantized angle back to a normal vector
                    Quaternion rotation = Quaternion.RotateTowards(Vector3.forward, normal, quantizedAngle);
                    normal = rotation * Vector3.forward;*/


                    Color color = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f);

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply(false, false);

            return texture;
        }

        public void OnGUI()
        {
            Event e = Event.current;

            //Debug.Log(e.type);
            //if (e.type != EventType.Layout)
                //return;

            //bool isValid = IsSelectionValidProtoSprite(out string invalidReason, out bool isAutoFixable);

            // Top menu bar
            //float menuBarHeight = 20;
            //Rect menuBar = new Rect(0, 0, position.width, menuBarHeight);


            //GUILayout.BeginArea(menuBar, EditorStyles.toolbar);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Populate add/+ menu
            {
                var guiContent = new GUIContent(EditorGUIUtility.IconContent("IN LockButton on act"));

                if (!m_Locked)
                {
                    guiContent = new GUIContent(EditorGUIUtility.IconContent("IN LockButton"));
                }

                guiContent.tooltip = "When unlocked the selected palette color is updated to match the active paint color.";
                if (GUILayout.Button(guiContent, EditorStyles.toolbarButton))
                {
                    m_Locked = !m_Locked;
                }
            }

            {
                var guiContent = new GUIContent(EditorGUIUtility.IconContent("SceneLoadIn"));

                guiContent.tooltip = "Load palette from selection.";

                if (GUILayout.Button(guiContent, EditorStyles.toolbarButton))
                {
                    LoadPaletteFromSelection();
                }
            }

            {
                var guiContent = new GUIContent(EditorGUIUtility.IconContent("SaveActive"));

                guiContent.tooltip = "Export palette as PNG.";
                if (GUILayout.Button(guiContent, EditorStyles.toolbarButton))
                {
                    SavePaletteAsPNG();
                }
            }

            {
                var guiContent = new GUIContent(EditorGUIUtility.IconContent("UnityEditor.FindDependencies"));

                guiContent.tooltip = "Snap selected sprite colors to nearest color palette colors.";
                

                bool isValid = ProtoSpriteWindow.IsSelectionValidProtoSprite(out string invalidReason);

                var temp = GUI.enabled;
                GUI.enabled = isValid;

                if (GUILayout.Button(guiContent, EditorStyles.toolbarButton))
                {
                    SnapColors();
                }

                GUI.enabled = temp;
            }

            {
                var guiContent = new GUIContent(EditorGUIUtility.IconContent("TreeEditor.Material"));

                guiContent.tooltip = "Toggle normal map painting palette.";


                //bool isValid = ProtoSpriteWindow.IsSelectionValidProtoSprite(out string invalidReason);

                //var temp = GUI.enabled;
                //GUI.enabled = isValid;

                if (GUILayout.Button(guiContent, EditorStyles.toolbarButton))
                {
                    m_NormalMapMode = !m_NormalMapMode;
                }

                //GUI.enabled = temp;
            }



            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
            //GUILayout.EndArea();

            //EditorGUILayout.Space(20);

            /*int selGridInt = 3;
            string[] selStrings = { "radio1", "radio2", "radio3" };
            GUILayout.BeginVertical("Box");
            selGridInt = GUILayout.SelectionGrid(selGridInt, selStrings, 2);
            GUILayout.EndVertical();*/

            PaintTool paintToolInstance = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>();

            if (!m_Locked)
            {
                if (m_SelectedIndex >= 0 && m_SelectedIndex < m_Colors.Count && paintToolInstance.PaintColor != m_Colors[m_SelectedIndex])
                {
                    m_Colors[m_SelectedIndex] = paintToolInstance.PaintColor;
                }
            }

            if (m_SelectedIndex < 0 || m_SelectedIndex >= m_Colors.Count || paintToolInstance.PaintColor != m_Colors[m_SelectedIndex])
            {
                m_SelectedIndex = -1;

                for (int i = 0; i < m_Colors.Count; i++)
                {
                    bool isSelected = paintToolInstance.PaintColor == m_Colors[i];
                    if (isSelected)
                    {
                        m_SelectedIndex = i;
                        break;
                    }
                }
            }

            UpdateButtonTextures();

            int columnCount = Mathf.FloorToInt(EditorGUIUtility.currentViewWidth / 28);
            int rowCount = Mathf.CeilToInt(m_Colors.Count / (float)columnCount);

            int indexOffset = 0;

            GUILayout.BeginVertical();

            if (!m_NormalMapMode)
            {
                for (int r = 0; r < rowCount; r++)
                {
                    GUILayout.BeginHorizontal();

                    for (int i = 0; i < columnCount; i++)
                    {
                        if (indexOffset >= m_Colors.Count)
                            break;

                        MakeButton(indexOffset, m_SelectedIndex == indexOffset);
                        indexOffset++;
                    }

                    GUILayout.EndHorizontal();
                }
            }

            //GUILayout.Box(m_NormalMapColorWheelTexture);
            // Draw the texture using GUILayout
            if (m_NormalMapMode)
            {
                GUILayout.Space(10);

                //int discreteLevels = EditorGUILayout.IntSlider(m_NormalMapDiscreteLevels, 3, 32);

                /*if (discreteLevels != m_NormalMapDiscreteLevels)
                {
                    m_NormalMapDiscreteLevels = discreteLevels;
                    m_NormalMapColorWheelTexture = GetNormalMapPaletteTexture();
                }*/

                if (m_NormalMapColorWheelTexture == null)
                {
                    m_NormalMapColorWheelTexture = GetNormalMapPaletteTexture();
                }

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                Rect rect = GUILayoutUtility.GetRect(m_NormalMapColorWheelTexture.width, m_NormalMapColorWheelTexture.height, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                GUI.DrawTexture(rect, m_NormalMapColorWheelTexture);


                // Store the rect of the texture for later use in mouse position calculation
                //textureRect = rect;

                // Check for mouse clicks
                //Event e = Event.current;
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && rect.Contains(e.mousePosition))
                {
                    // Calculate the mouse position relative to the texture
                    Vector2 mousePos = e.mousePosition - new Vector2(rect.x, rect.y);
                    //float xPercentage = mousePos.x / rect.width;
                    //float yPercentage = mousePos.y / rect.height;

                    int2 texel = new int2((int)mousePos.x, (int)mousePos.y);
                    texel.y = (int)rect.height - texel.y;

                    // Do something with the click position
                    //Debug.Log("Clicked on texture at: " + xPercentage + ", " + yPercentage);

                    Color paintColor = ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().PaintColor;
                    Color selectedNormalColor = m_NormalMapColorWheelTexture.GetPixel(texel.x, texel.y);
                    selectedNormalColor.a = paintColor.a;
                    ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().PaintColor = selectedNormalColor;
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }



            GUILayout.EndVertical();
        }


        GUILayoutOption[] options = null;
        GUIContent duplicateGUIContent = new GUIContent("Duplicate");
        GUIContent removeGUIContent = new GUIContent("Remove");
        GUIContent copyGUIContent = new GUIContent("Copy");
        GUIContent pasteGUIContent = new GUIContent("Paste");

        void MakeButton(int colorIndex, bool selected)
        {
            //Debug.Log(Time.frameCount + " make button");
            Color color = m_Colors[colorIndex];

            if (backgroundStyle == null)
            {
                backgroundStyle = new GUIStyle(GUI.skin.button);

                backgroundStyle.normal.background = null;// m_ButtonTextures[colorIndex];// MakeBackgroundTexture(10, 10, color, selected);
                backgroundStyle.normal.scaledBackgrounds = new Texture2D[] { };// new Texture2D[] { m_ButtonTextures[colorIndex] };
                backgroundStyle.border = new RectOffset(4, 4, 4, 4);
            }

            if (options == null)
            {
                options = new GUILayoutOption[] { GUILayout.Width(25), GUILayout.Height(25) };
            }

            backgroundStyle.normal.background = m_ButtonTextures[colorIndex].texture;

            if (GUILayout.Button("", backgroundStyle, options))
            {
                if (Event.current.button == 1)
                {
                    GenericMenu menu = new GenericMenu();

                    menu.AddItem(duplicateGUIContent, false, DuplicateColor, colorIndex);
                    menu.AddItem(removeGUIContent, false, RemoveColor, colorIndex);
                    menu.AddItem(copyGUIContent, false, CopyColor, colorIndex);

                    if (ColorUtility.TryParseHtmlString(GUIUtility.systemCopyBuffer, out Color clipboardColor) || ColorUtility.TryParseHtmlString("#" + GUIUtility.systemCopyBuffer, out clipboardColor))
                    {
                        menu.AddItem(pasteGUIContent, false, PasteColor, colorIndex);
                    }
                    else
                    {
                        menu.AddDisabledItem(pasteGUIContent);
                    }

                    // display the menu
                    menu.ShowAsContext();
                }
                else
                {
                    m_SelectedIndex = colorIndex;
                    SetColor(color);
                }
            }
        }

        float ColorMetric(Color color)
        {
            float r = color.r;
            float g = color.g;
            float b = color.b;

            return 2 * r * r + 4 * g * g + 3 * b * b;
        }

        float DistanceBetweenColors(Color colorA, Color colorB)
        {
            //return Vector4.Distance(colorA, colorB);

            float avgR = 0.5f * (colorA.r + colorB.r);

            float deltaRSq = Mathf.Pow(colorA.r - colorB.r, 2);
            float deltaGSq = Mathf.Pow(colorA.g - colorB.g, 2);
            float deltaBSq = Mathf.Pow(colorA.b - colorB.b, 2);

            return (2.0f + (avgR)) * deltaRSq + 4.0f * deltaGSq + (2.0f + (1.0f - avgR)) * deltaBSq;

            /*if (avgR < 0.5f)
            {
                return 2.0f * deltaRSq + 4.0f * deltaGSq + 3.0f * deltaBSq;
            }
            else
            {
                return 3.0f * deltaRSq + 4.0f * deltaGSq + 2.0f * deltaBSq;
            }*/

            //return Mathf.Abs(ColorMetric(colorA) - ColorMetric(colorB));
        }

        Color getClosestColorInPalette(Color sourceColor)
        {
            float distance = Mathf.Infinity;
            int index = 0;
            for (int i = 0; i < m_Colors.Count; i++)
            {
                float d = DistanceBetweenColors(sourceColor, m_Colors[i]);// Vector4.Distance(sourceColor, m_Colors[i]);
                if (d < distance)
                {
                    distance = d;
                    index = i;
                }
            }

            return m_Colors[index];
        }

        void SnapColors()
        {
            // Get the texture of the selected sprite
            bool isValid = ProtoSpriteWindow.IsSelectionValidProtoSprite(out string invalidReason);// Selection.activeGameObject

            if (!isValid)
                return;

            Sprite sprite = Selection.activeGameObject.GetComponent<SpriteRenderer>().sprite;
            Texture2D texture = sprite.texture;

            if (!EditorUtility.DisplayDialog("Snap Sprite Colors", "Change each color in the selected sprite to the nearest color in the active color palette?" + Environment.NewLine + Environment.NewLine + "SPRITE: " + sprite.name, "Snap", "Cancel"))
                return;

            var undoData = new UndoDataPaint();
            undoData.pixelDataBefore = texture.GetPixels32(0);

            var pixelData = texture.GetPixelData<Color32>(0);

            // Go through the texels

            for (int i = 0; i < pixelData.Length; i++)
            {
                Color32 texelColor = pixelData[i];

                if (texelColor.a == 0.0f)
                    continue;

                int pixelX = i % texture.width;
                int pixelY = i / texture.width;

                if (!sprite.rect.Contains(new Vector2(pixelX, pixelY)))
                    continue;

                Color closestColor = getClosestColorInPalette(texelColor);

                pixelData[i] = closestColor;
            }

            texture.Apply(true, false);

            // If the texel is within the sprite rect

            // find the nearest color to it in the color palette

            // Set it to that color

            // Apply the texture

            // Set the texture for the save data
            ProtoSpriteData.SaveData saveData = new ProtoSpriteData.SaveData(texture);
            ProtoSpriteData.SubmitSaveData(saveData);

            undoData.texture = texture;
            undoData.pixelDataAfter = texture.GetPixels32(0);
            ProtoSpriteData.SubmitUndoData(undoData, "ProtoSprite Snap Colors to Palette");
        }

        void DuplicateColor(object index)
        {
            if (m_Colors.Count >= kMaxPaletteSize)
            {
                Debug.LogWarning("Max palette size reached (" + kMaxPaletteSize + ").");
                return;
            }
            m_Colors.Insert((int)index, m_Colors[(int)index]);
        }

        void RemoveColor(object index)
        {
            if (m_Colors.Count < 2)
                return;
            m_Colors.RemoveAt((int)index);
        }

        void CopyColor(object index)
        {
            Color color = m_Colors[(int)index];
            GUIUtility.systemCopyBuffer = "#" + ColorUtility.ToHtmlStringRGBA(color);
        }

        void PasteColor(object index)
        {
            bool success = ColorUtility.TryParseHtmlString(GUIUtility.systemCopyBuffer, out Color color);

            if (success)
            {
                m_Colors[(int)index] = color;
            }
            else
            {
                success = ColorUtility.TryParseHtmlString("#" + GUIUtility.systemCopyBuffer, out color);

                if (success)
                {
                    m_Colors[(int)index] = color;
                }
            }
        }

        void SetColor(Color color)
        {
            ProtoSpriteWindow.GetInstance().GetToolInstance<PaintTool>().PaintColor = color;
        }

        Texture2D GetTextureFromSelection(out Rect spriteRect)
        {
            Texture2D texture = Selection.activeObject as Texture2D;

            if (texture != null)
            {
                spriteRect = new Rect(0, 0, texture.width, texture.height);
                return texture;
            }

            if (Selection.activeTransform != null)
            {
                bool isValid = ProtoSpriteWindow.IsSelectionValidProtoSprite(out string invalidReason);
                if (isValid)
                {
                    Transform t = Selection.activeTransform;

                    SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
                    Sprite sprite = spriteRenderer.sprite;
                    spriteRect = sprite.rect;
                    return SpriteUtility.GetSpriteTexture(sprite, false);
                }
            }

            spriteRect = new Rect(0, 0, 0, 0);
            return null;
        }

        void LoadPaletteFromSelection()
        {
            Texture2D texture = GetTextureFromSelection(out Rect spriteRect);

            if (texture == null)
            {
                //Debug.LogError("No valid selection to load palette from.");

                if (!EditorUtility.DisplayDialog("Load Default Palette?", "No valid selection to load palette from." + Environment.NewLine + Environment.NewLine + "Load default palette?", "Load Default Palette", "Cancel"))
                    return;

                LoadDefaultPalette();

                return;
            }

            //Debug.Log("Loading palette from texture: " + texture);

            if (!texture.isReadable)
            {
                Debug.LogError("Selected texture is not set as Read/Write enabled in its importer.");
                return;
            }


            if(!EditorUtility.DisplayDialog("Load Palette: " + texture.name, "Load palette from: " + Environment.NewLine + Environment.NewLine + "TEXTURE: " + texture.name + Environment.NewLine + "PATH: " + AssetDatabase.GetAssetPath(texture), "Load", "Cancel"))
               return;

            m_Colors.Clear();

            var pixels = texture.GetPixels((int)spriteRect.x, (int)spriteRect.y, (int)spriteRect.width, (int)spriteRect.height);

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0)
                    continue;

                if (m_Colors.Count >= kMaxPaletteSize)
                {
                    Debug.LogWarning("Max palette size reached (" + kMaxPaletteSize + ").");
                    break;
                }

                if (!m_Colors.Contains(pixels[i]))
                {
                    m_Colors.Add(pixels[i]);
                }
            }

            if (m_Colors.Count == 0)
            {
                m_Colors.Add(Color.black);
            }

            m_SelectedIndex = -1;

            UpdateButtonTextures();
        }

        void LoadDefaultPalette()
        {
            m_SelectedIndex = -1;

            m_Colors.Clear();
            for (int i = 0; i < m_SamplePalette.Length; i++)
            {
                ColorUtility.TryParseHtmlString(m_SamplePalette[i], out Color color);
                m_Colors.Add(color);
            }
            UpdateButtonTextures();
        }

        void SavePaletteAsPNG()
        {
            if (string.IsNullOrEmpty(m_LastSavePaletteFolder) || !Directory.Exists(m_LastSavePaletteFolder))
            {
                m_LastSavePaletteFolder = "Assets";
            }

            string combinedPath = Path.Combine(m_LastSavePaletteFolder, "New Palette.png");

            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(combinedPath);

            string defaultName = Path.GetFileNameWithoutExtension(uniquePath);

            string newPath = EditorUtility.SaveFilePanelInProject("Save Color Palette", defaultName, "png", "Enter file name", m_LastSavePaletteFolder);

            if (newPath.Length == 0)
                return;

            m_LastSavePaletteFolder = Path.GetDirectoryName(newPath);

            // Create new texture
            Texture2D texture = new Texture2D(m_Colors.Count, 1);

            // Set pixels to black
            Color[] pixels = new Color[texture.width * texture.height];
            for (int i = 0; i < (texture.width * texture.height); i++)
            {
                pixels[i] = m_Colors[i];
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            // Write to file
            var bytes = texture.EncodeToPNG();

            GameObject.DestroyImmediate(texture);

            File.WriteAllBytes(newPath, bytes);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(newPath);
            AssetDatabase.Refresh();

            string path = newPath;
            string dataPath = Application.dataPath;
            if (path.StartsWith(dataPath, StringComparison.InvariantCultureIgnoreCase))
            {
                path = "Assets" + path.Substring(dataPath.Length);
            }

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            TextureImporterPlatformSettings defaultSettings = importer.GetDefaultPlatformTextureSettings();
            defaultSettings.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(defaultSettings);

            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            importerSettings.spriteMeshType = SpriteMeshType.FullRect;
            importerSettings.spritePixelsPerUnit = 1;
            importerSettings.spritePivot = Vector2.zero;
            importerSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            importer.SetTextureSettings(importerSettings);

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            // Link sprite renderer to texture
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            EditorGUIUtility.PingObject(sprite.texture);
        }

        private Texture2D MakeBackgroundTexture(int width, int height, Color color, bool selected)
        {
            Color[] pixels = new Color[width * height];

            bool isSelected = selected;

            int whiteBorderSize = 2;
            int blackBorderSize = 2;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (isSelected && (x < whiteBorderSize || y < whiteBorderSize || x >= (width - whiteBorderSize) || y >= (height - whiteBorderSize)))
                    {
                        pixels[x + y * width] = Color.white;

                    }
                    else if (isSelected && (x < whiteBorderSize + blackBorderSize || y < whiteBorderSize + blackBorderSize || x >= width - (whiteBorderSize + blackBorderSize) || y >= height - (whiteBorderSize + blackBorderSize)))
                    {
                        pixels[x + y * width] = Color.black;
                    }
                    else
                    {
                        pixels[x + y * width] = color;

                        // Alpha checkered overlay
                        if (color.a < 1.0f)
                        {
                            float density =  2.0f;

                            float2 uv = new float2(x / (float)width, (float)y / height);
                            uv *= density;

                            float2 c = uv;
                            c = math.floor(c) / 2.0f;

                            float checker = math.frac(c.x + c.y) * 2.0f;

                            Color checkeredColor = new Color(0.3f,0.3f,0.3f);
                            if (checker == 0)
                            {
                                checkeredColor = new Color(0.6f, 0.6f, 0.6f);
                            }
                            pixels[x + y * width] = Color.Lerp(checkeredColor, color, color.a);
                        }
                    }

                }
            }

            Texture2D backgroundTexture = new Texture2D(width, height);
            backgroundTexture.filterMode = FilterMode.Point;
            backgroundTexture.SetPixels(pixels);
            backgroundTexture.Apply();

            return backgroundTexture;
        }

        // https://lospec.com/palette-list/endesga-32
        string[] m_SamplePalette = new string[]
            {
                "#BE4A2F",
                "#D77643",
                "#EAD4AA",
                "#E4A672",
                "#B86F50",
                "#733E39",
                "#3E2731",
                "#A22633",
                "#E43B44",
                "#F77622",
                "#FEAE34",
                "#FEE761",
                "#63C74D",
                "#3E8948",
                "#265C42",
                "#193C3E",
                "#124E89",
                "#0099DB",
                "#2CE8F5",
                "#FFFFFF",
                "#C0CBDC",
                "#8B9BB4",
                "#5A6988",
                "#3A4466",
                "#262B44",
                "#181425",
                "#FF0044",
                "#68386C",
                "#B55088",
                "#F6757A",
                "#E8B796",
                "#C28569",
                "#00000000"
            };
    }

}