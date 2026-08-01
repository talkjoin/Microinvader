using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEngine;

namespace ProtoSprite.Editor
{
    public class CreateSecondaryTextureWindow : EditorWindow
    {
        bool m_InitializedPosition = false;

        string m_TextureName = "";
        bool m_IsNormalMap = false;

        public static Vector2Int DefaultTextureSize
        {
            get
            {
                int x = EditorPrefs.GetInt("ProtoSprite.Editor.DefaultSettingsWindow.DefaultTextureSize.x", 100);
                int y = EditorPrefs.GetInt("ProtoSprite.Editor.DefaultSettingsWindow.DefaultTextureSize.y", 100);
                return new Vector2Int(x, y);
            }
            set
            {
                EditorPrefs.SetInt("ProtoSprite.Editor.DefaultSettingsWindow.DefaultTextureSize.x", value.x);
                EditorPrefs.SetInt("ProtoSprite.Editor.DefaultSettingsWindow.DefaultTextureSize.y", value.y);
            }
        }

        public static float DefaultPPU
        {
            get
            {
                return EditorPrefs.GetFloat("ProtoSprite.Editor.DefaultSettingsWindow.DefaultPPU", 100.0f);
            }
            set
            {
                EditorPrefs.SetFloat("ProtoSprite.Editor.DefaultSettingsWindow.DefaultPPU", value);
            }
        }

        public static Vector2 DefaultPivot
        {
            get
            {
                var x = EditorPrefs.GetFloat("ProtoSprite.Editor.DefaultSettingsWindow.DefaultPivot.x", 0.5f);
                var y = EditorPrefs.GetFloat("ProtoSprite.Editor.DefaultSettingsWindow.DefaultPivot.y", 0.5f);
                return new Vector2(x, y);
            }
            set
            {
                EditorPrefs.SetFloat("ProtoSprite.Editor.DefaultSettingsWindow.DefaultPivot.x", value.x);
                EditorPrefs.SetFloat("ProtoSprite.Editor.DefaultSettingsWindow.DefaultPivot.y", value.y);
            }
        }


        public static void Open()
        {
            var window = GetWindow<CreateSecondaryTextureWindow>(true, "Create Secondary Texture");
            window.m_InitializedPosition = false;

            window.minSize = new Vector2(300, 150);
            window.maxSize = new Vector2(300, 150);
        }

        private void OnLostFocus()
        {
            Close();
        }

        void OnGUI()
        {
            if (!m_InitializedPosition)
            {
                m_InitializedPosition = true;
                Vector2 mousePos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                position = new Rect(mousePos.x, mousePos.y, position.width, position.height);
            }

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Reference Name (e.g. _NormalMap)");
            m_TextureName = EditorGUILayout.TextField(m_TextureName);

            GUILayout.Space(5);

            EditorGUILayout.LabelField("Is Normal Map?");
            m_IsNormalMap = EditorGUILayout.Toggle(m_IsNormalMap);

            GUILayout.Space(10);

            //DefaultTextureSize = EditorGUILayout.Vector2IntField("Size", DefaultTextureSize);
            //DefaultTextureSize = new Vector2Int(Mathf.Clamp(DefaultTextureSize.x, 1, ProtoSpriteWindow.kMaxTextureSize), Mathf.Clamp(DefaultTextureSize.y, 1, ProtoSpriteWindow.kMaxTextureSize));

            //DefaultPivot = EditorGUILayout.Vector2Field("Pivot", DefaultPivot);
            //DefaultPPU = Mathf.Max(0.001f, EditorGUILayout.FloatField("PPU", DefaultPPU));


            if (!IsValid(m_TextureName))
            {
                GUIStyle errorStyle = new GUIStyle(EditorStyles.label);
                errorStyle.normal.textColor = Color.red;

                EditorGUILayout.LabelField(m_TextureName + " already exists.", errorStyle);
            }

            GUI.enabled = !string.IsNullOrWhiteSpace(m_TextureName) && IsValid(m_TextureName);

            if (GUILayout.Button("Create"))
            {
                CreateNewTexture(m_TextureName, m_IsNormalMap);
            }

            GUI.enabled = true;
        }

        bool IsValid(string referenceName)
        {
            GameObject gameObject = Selection.activeGameObject;
            SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
            Sprite selectedSprite = spriteRenderer.sprite;
            //Texture2D selectedMainTexture = SpriteUtility.GetSpriteTexture(selectedSprite, false);
            Texture2D texture = SpriteUtility.GetSpriteTexture(selectedSprite, false);
            string textureAssetPath = AssetDatabase.GetAssetPath(texture);

            TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(textureAssetPath);
            List<SecondarySpriteTexture> secondaryTextures = new List<SecondarySpriteTexture>(textureImporter.secondarySpriteTextures);

            if (secondaryTextures.Any(x => x.name == referenceName))
            {
                return false;
            }

            return true;
        }

        void CreateNewTexture(string referenceName, bool isNormalMap)
        {
            GameObject gameObject = Selection.activeGameObject;
            SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
            Sprite selectedSprite = spriteRenderer.sprite;
            //Texture2D selectedMainTexture = SpriteUtility.GetSpriteTexture(selectedSprite, false);
            Texture2D texture = SpriteUtility.GetSpriteTexture(selectedSprite, false);
            string textureAssetPath = AssetDatabase.GetAssetPath(texture);
            string assetName = Path.GetFileNameWithoutExtension(textureAssetPath);
            string folderPath = Path.GetDirectoryName(textureAssetPath);
            string pathAttempt = Path.Combine(folderPath, assetName + referenceName + ".png");

            pathAttempt = AssetDatabase.GenerateUniqueAssetPath(pathAttempt);

            pathAttempt = EditorUtility.SaveFilePanelInProject("Create Secondary Texture (" + referenceName +")", Path.GetFileNameWithoutExtension(pathAttempt), "png", "Enter file name", Path.GetDirectoryName(pathAttempt));

            if (pathAttempt.Length != 0)
            {
                //ProtoSpriteData.Saving.SaveTextureIfDirty(selectedTexture);

                // Create new texture
                AssetDatabase.CopyAsset(textureAssetPath, pathAttempt);
                AssetDatabase.ImportAsset(pathAttempt);

                TextureImporter newTextureImporter = (TextureImporter)AssetImporter.GetAtPath(pathAttempt);

                newTextureImporter.sRGBTexture = !isNormalMap;

                Texture2D newTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(pathAttempt);

                Color32[] pixels = newTexture.GetPixels32();

                for (int i = 0; i < pixels.Length; i++)
                {
                    if (isNormalMap)
                    {
                        pixels[i] = new Color(0.5f, 0.5f, 1.0f, 1.0f);// (Color)(Vector4)(Vector3.forward * 0.5f + Vector3.one * 0.5f);
                    }
                    else
                    {
                        pixels[i] = Color.clear;
                    }
                }


                newTexture.SetPixels32(pixels);

                newTexture.Apply(true, false);

                // Write to file
                var bytes = newTexture.EncodeToPNG();

                File.WriteAllBytes(pathAttempt, bytes);

                newTextureImporter.secondarySpriteTextures = new SecondarySpriteTexture[0];

                newTextureImporter.SaveAndReimport();
                //AssetDatabase.Refresh();
                //AssetDatabase.ImportAsset(pathAttempt);
                //AssetDatabase.Refresh();


                EditorGUIUtility.PingObject(newTexture);

                TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(textureAssetPath);
                List<SecondarySpriteTexture> secondaryTextures = new List<SecondarySpriteTexture>(textureImporter.secondarySpriteTextures);

                SecondarySpriteTexture element = new SecondarySpriteTexture();
                element.name = referenceName;
                element.texture = newTexture;

                secondaryTextures.Add(element);

                textureImporter.secondarySpriteTextures = secondaryTextures.ToArray();
                textureImporter.SaveAndReimport();

                m_TextureName = "";
                m_IsNormalMap = false;

                Close();

                /*Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(newPath).OfType<Sprite>().ToArray();

                Sprite sprite = sprites[0];

                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i].name == selectedSprite.name)
                    {
                        sprite = sprites[i];
                        break;
                    }
                }*/

                //Undo.RecordObject(spriteRenderer, "ProtoSprite Duplicate Sprite");

                /*spriteRenderer.sprite = sprite;

                EditorUtility.SetDirty(spriteRenderer);


                List<SecondarySpriteTexture> newSecondaryTextures = new List<SecondarySpriteTexture>();

                for (int i = 0; i < secondaryTextures.Length; i++)
                {
                    Texture2D sTex = secondaryTextures[i].texture;

                    SecondarySpriteTexture data = new SecondarySpriteTexture();
                    data.name = secondaryTextures[i].name;
                    data.texture = null;

                    string path = AssetDatabase.GetAssetPath(secondaryTextures[i].texture);

                    string assetSTPath = AssetDatabase.GenerateUniqueAssetPath(path);
                    string assetSTName = Path.GetFileNameWithoutExtension(assetSTPath);
                    string folderSTPath = Path.GetDirectoryName(assetPath);

                    string newSTPath = EditorUtility.SaveFilePanelInProject("Create ProtoSprite Texture", assetSTName, "png", "Enter file name", folderSTPath);
                    ProtoSpriteData.Saving.SaveTextureIfDirty(sTex);
                    AssetDatabase.CopyAsset(path, newSTPath);
                    AssetDatabase.ImportAsset(newSTPath);

                    Texture2D newSTex = AssetDatabase.LoadAssetAtPath<Texture2D>(newSTPath);

                    data.texture = newSTex;

                    newSecondaryTextures.Add(data);
                }

                newTextureImporter.secondarySpriteTextures = newSecondaryTextures.ToArray();
                newTextureImporter.SaveAndReimport();*/


                /*if (isMainTexture)
                {
                    


                }
                else
                {
                    Undo.RegisterImporterUndo(textureImporter.assetPath, "ProtoSprite Duplicate Secondary Texture");

                    secondaryTextures[targetTextureIndex - 1].texture = AssetDatabase.LoadAssetAtPath<Texture2D>(newPath);

                    textureImporter.secondarySpriteTextures = secondaryTextures;

                    textureImporter.SaveAndReimport();

                    EditorGUIUtility.PingObject(textureImporter.secondarySpriteTextures[targetTextureIndex - 1].texture);
                }*/
            }
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}