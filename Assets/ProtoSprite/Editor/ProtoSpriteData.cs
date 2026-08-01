using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace ProtoSprite.Editor
{
    public enum BrushShape
    {
        CIRCLE,
        SQUARE,
        CUSTOM
    }

    public enum BlendMode
    {
        REPLACE,
        ALPHA,
        SOURCE
    }

    public class ProtoSpriteData : ScriptableSingleton<ProtoSpriteData>
    {
        [SerializeReference] public List<UndoDataBase> m_UndoClassData = new List<UndoDataBase>();
        [SerializeField] public int m_UndoStackIndex = 0;

        [SerializeField] public int m_PreviousUndoGroup = -1;

        [SerializeReference] public List<SaveData> m_SaveDatas = new List<SaveData>();

        Material m_DrawPreviewGPUMaterial = null;
        Material m_OutlineMaterial = null;

        Mesh m_TempQuadMesh = null;

        ComputeShader m_BresenhamLineComputeShader = null;
        ComputeShader m_CustomBrushComputeShader = null;
        ComputeShader m_ReadPixelsComputeShader = null;

        static string s_ProtoSpriteEditorFolderPath = null;

        public static string ProtoSpriteEditorFolderPath
        {
            get
            {
                if (s_ProtoSpriteEditorFolderPath == null)
                {
                    MonoScript thisScript = MonoScript.FromScriptableObject(instance);

                    s_ProtoSpriteEditorFolderPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript));
                }

                return s_ProtoSpriteEditorFolderPath;
            }
        }

        Material DrawPreviewGPUMaterial
        {
            get
            {
                if (m_DrawPreviewGPUMaterial == null)
                {
                    m_DrawPreviewGPUMaterial = new Material(Shader.Find("Hidden/ProtoSprite/GPUDrawLine"));
                }
                return m_DrawPreviewGPUMaterial;
            }
        }

        public Material OutlineMaterial
        {
            get
            {
                if (m_OutlineMaterial == null)
                {
                    m_OutlineMaterial = new Material(Shader.Find("Hidden/ProtoSprite/Outline"));
                    m_OutlineMaterial.enableInstancing = true;
                }
                return m_OutlineMaterial;
            }
        }

        ComputeShader BresenhamLineComputeShader
        {
            get
            {
                if (m_BresenhamLineComputeShader == null)
                {
                    MonoScript thisScript = MonoScript.FromScriptableObject(instance);

                    string path = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript)), "Shaders", "BresenhamLine.compute");

                    ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

                    m_BresenhamLineComputeShader = cs;
                }

                return m_BresenhamLineComputeShader;
            }
        }

        ComputeShader ReadPixelsComputeShader
        {
            get
            {
                if (m_ReadPixelsComputeShader == null)
                {
                    MonoScript thisScript = MonoScript.FromScriptableObject(instance);

                    string path = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript)), "Shaders", "ReadPixels.compute");

                    ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

                    m_ReadPixelsComputeShader = cs;
                }

                return m_ReadPixelsComputeShader;
            }
        }

        ComputeShader CustomBrushComputeShader
        {
            get
            {
                if (m_CustomBrushComputeShader == null)
                {
                    MonoScript thisScript = MonoScript.FromScriptableObject(instance);

                    string path = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript)), "Shaders", "CustomBrush.compute");

                    ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

                    m_CustomBrushComputeShader = cs;
                }

                return m_CustomBrushComputeShader;
            }
        }

        public static bool HasSaveData()
        {
            if (instance == null)
                return false;

            return instance.m_SaveDatas.Count > 0;
        }

        public static bool HasSaveDataForGUID(GUID guid)
        {
            if (instance == null)
                return false;

            foreach (var sd in instance.m_SaveDatas)
            {
                if (sd.textureGUID == guid)
                {
                    return true;
                }
            }

            return false;
        }

        [Serializable]
        public class SaveData
        {
            [SerializeField] public GUID textureGUID = default;
            [SerializeField] public Color32[] pixelData = new Color32[0];
            [SerializeField] public uint2 textureSize = uint2.zero;

            public SaveData(Texture2D texture)
            {
                pixelData = texture.GetPixelData<Color32>(0).ToArray();
                textureSize = new uint2((uint)texture.width, (uint)texture.height);
                textureGUID = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(texture));
            }

            public void Save(bool reimport = true)
            {
                if (Saving.onWillSave != null)
                {
                    Saving.onWillSave.Invoke(this);
                }

                string filePath = "invalid file path";

                try
                {
                    filePath = AssetDatabase.GUIDToAssetPath(textureGUID);

                    if (!File.Exists(filePath))
                        throw new System.Exception("File no longer exists: " + filePath);

                    byte[] bytes = ImageConversion.EncodeArrayToPNG(pixelData, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, textureSize.x, textureSize.y);

                    File.WriteAllBytes(filePath, bytes);

                    if (reimport)
                        AssetDatabase.ImportAsset(filePath);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("ProtoSprite: Failed to save texture file (" + filePath + ") because of exception: " + e.Message);
                }
            }
        }

        public static void SubmitSaveData(SaveData saveData)
        {
            var saveDatas = instance.m_SaveDatas;

            bool found = false;

            for (int i = 0; i < saveDatas.Count && !found; i++)
            {
                if (saveDatas[i] == null)
                {
                    saveDatas.RemoveAt(i);
                    i--;
                    continue;
                }

                if (saveDatas[i].textureGUID == saveData.textureGUID)
                {
                    saveDatas[i] = saveData;
                    found = true;
                }
            }

            if (!found)
                saveDatas.Add(saveData);

            var dummyAsset = GetDummyAssetFile();
            EditorUtility.SetDirty(dummyAsset);
        }

        static DummyAssetFile GetDummyAssetFile()
        {
            string guid = AssetDatabase.FindAssets("t:DummyAssetFile")[0];
            string filePath = AssetDatabase.GUIDToAssetPath(guid);
            return AssetDatabase.LoadAssetAtPath<DummyAssetFile>(filePath);
        }

        public class Saving : AssetModificationProcessor
        {
            public static OnWillSave onWillSave = null;
            public delegate void OnWillSave(SaveData saveData);

            static string[] OnWillSaveAssets(string[] paths)
            {
                List<string> finalPaths = new List<string>(paths);

                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];

                    DummyAssetFile dummyAssetFile = AssetDatabase.LoadAssetAtPath<DummyAssetFile>(path);

                    if (dummyAssetFile != null)
                    {
                        finalPaths.RemoveAt(i);
                        SaveAll();
                        break;
                    }
                }

                return finalPaths.ToArray();
            }

            public static void SaveAll()
            {
                var saveDatas = new List<SaveData>(instance.m_SaveDatas);
                instance.m_SaveDatas.Clear();
                for (int i = 0; i < saveDatas.Count; i++)
                {
                    if (saveDatas[i] != null)
                        saveDatas[i].Save();
                }

                var dummyAsset = GetDummyAssetFile();
                EditorUtility.ClearDirty(dummyAsset);
            }

            public static void SaveTextureIfDirty(Texture2D texture, bool reimport = true)
            {
                var guid = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(texture));
                SaveTextureIfDirty(guid, reimport);
            }

            public static void SaveTextureIfDirty(GUID guid, bool reimport = true)
            {
                var saveDatas = instance.m_SaveDatas;
                for (int i = 0; i < saveDatas.Count; i++)
                {
                    if (saveDatas[i].textureGUID == guid)
                    {
                        var saveData = saveDatas[i];
                        saveDatas.RemoveAt(i);
                        saveData.Save(reimport);
                        break;
                    }
                }

                if (saveDatas.Count == 0)
                {
                    var dummyAsset = GetDummyAssetFile();
                    EditorUtility.ClearDirty(dummyAsset);
                }
            }

            public static void DiscardModificationsForGUID(GUID guid)
            {
                var saveDatas = instance.m_SaveDatas;
                for (int i = 0; i < saveDatas.Count; i++)
                {
                    if (saveDatas[i].textureGUID == guid)
                    {
                        saveDatas.RemoveAt(i);
                        break;
                    }
                }

                if (saveDatas.Count == 0)
                {
                    var dummyAsset = GetDummyAssetFile();
                    EditorUtility.ClearDirty(dummyAsset);
                }
            }
        }

        public class Importing : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
            {
                if (!HasSaveData())
                    return;

                foreach (string assetPath in importedAssets)
                {
                    if (!assetPath.EndsWith(".png", true, System.Globalization.CultureInfo.CurrentCulture))
                        continue;

                    DisplaySavePromptIfNeeded(assetPath);
                }
            }

            void OnPreprocessTexture()
            {
                if (!HasSaveData())
                    return;

                DisplaySavePromptIfNeeded(assetPath);
            }

            static void DisplaySavePromptIfNeeded(string assetPath)
            {
                GUID guid = AssetDatabase.GUIDFromAssetPath(assetPath);

                if (HasSaveDataForGUID(guid))
                {
                    if (EditorUtility.DisplayDialog("Unsaved ProtoSprite Modifications", "Reimporting a texture with unsaved ProtoSprite modifications. Save or Discard ProtoSprite modifications?" + Environment.NewLine + Environment.NewLine + assetPath, "Save", "Discard"))
                    {
                        Saving.SaveTextureIfDirty(guid, false);
                    }
                    else
                    {
                        Saving.DiscardModificationsForGUID(guid);
                    }
                }
            }
        }


        [InitializeOnLoadMethod]
        static void Initialize()
        {
            // So we can ensure the singleton is created when Unity loads
            var inst = ProtoSpriteData.instance;
        }

        public static void SubmitUndoData(UndoDataBase undoData, string undoName)
        {
            if (instance.m_UndoStackIndex < 0)
                instance.m_UndoStackIndex = 0;

            if (instance.m_UndoStackIndex < instance.m_UndoClassData.Count - 1)
            {
                int countToRemove = instance.m_UndoClassData.Count - 1 - instance.m_UndoStackIndex;

                if (instance.m_UndoStackIndex < 0)
                {
                    countToRemove = instance.m_UndoClassData.Count;
                }

                for (int i = 0; i < countToRemove; i++)
                {
                    int index = instance.m_UndoClassData.Count - 1;
                    if (instance.m_UndoClassData[index] != null)
                    {
                        instance.m_UndoClassData.RemoveAt(index);
                    }
                }
            }

            // Cap undo size to avoid using large amounts of memory for undo data
            // Cap by memory size
            {
                long totalBytes = 0;
                foreach (var ud in instance.m_UndoClassData)
                {
                    if (ud != null)
                        totalBytes += ud.TotalBytes();
                }
                long maxBytes = 1024L * 1024L * 512L; // 512MB

                if (totalBytes > maxBytes)
                {
                    instance.m_UndoClassData.RemoveAt(0);
                    instance.m_UndoStackIndex--;
                }
            }

            // Cap by stack size
            int maxUndoStackSize = 50;
            if (instance.m_UndoClassData.Count > maxUndoStackSize)
            {
                int countToRemove = instance.m_UndoClassData.Count - maxUndoStackSize;
                for (int i = 0; i < countToRemove; i++)
                {
                    instance.m_UndoClassData.RemoveAt(0);
                    instance.m_UndoStackIndex--;
                }
            }

            Undo.RegisterCompleteObjectUndo(GetDummyAssetFile(), undoName);

            int currentUndoGroup = Undo.GetCurrentGroup();

            if (instance.m_PreviousUndoGroup != currentUndoGroup)
            {
                undoData.group = currentUndoGroup;
                instance.m_UndoClassData.Add(undoData);
                instance.m_UndoStackIndex = instance.m_UndoClassData.Count - 1;
            }

            instance.m_PreviousUndoGroup = currentUndoGroup;
        }

        void UndoRedoEvent(in UndoRedoInfo undoRedoInfo)
        {
            if (!undoRedoInfo.undoName.StartsWith("ProtoSprite "))
                return;

            DoUndoRedo(undoRedoInfo);
        }

        void DoUndoRedo(in UndoRedoInfo undoRedoInfo)
        {
            int targetIndex = m_UndoStackIndex;

            bool found = false;
            for (int i = 0; i < m_UndoClassData.Count; i++)
            {
                if (m_UndoClassData[i].group == undoRedoInfo.undoGroup)
                {
                    targetIndex = i;
                    found = true;
                    break;
                }
            }

            if (!undoRedoInfo.isRedo)
            {
                targetIndex--;
            }

            if (!found)
            {
                if (m_UndoClassData.Count == 0)
                    return;

                if (undoRedoInfo.undoGroup < m_UndoClassData[0].group)
                {
                    targetIndex = -1;
                }
                else if (undoRedoInfo.undoGroup > m_UndoClassData[m_UndoClassData.Count - 1].group)
                {
                    targetIndex = m_UndoClassData.Count;
                }
            }

            bool isRedo = targetIndex == m_UndoStackIndex ? undoRedoInfo.isRedo : targetIndex > m_UndoStackIndex;

            while (m_UndoStackIndex != targetIndex)
            {
                if (targetIndex > m_UndoStackIndex)
                {
                    m_UndoStackIndex += 1;
                }

                if (m_UndoStackIndex >= 0 && m_UndoStackIndex < m_UndoClassData.Count)
                {
                    try
                    {
                        if (isRedo)
                        {
                            //Debug.Log("do redo " + m_UndoClassData[m_UndoStackIndex].group);
                            m_UndoClassData[m_UndoStackIndex].DoRedo();
                        }
                        else
                        {
                            //Debug.Log("do undo " + m_UndoClassData[m_UndoStackIndex].group);
                            m_UndoClassData[m_UndoStackIndex].DoUndo();
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("ProtoSprite: Failed to perform undo operation because of exception: " + e.Message);
                    }
                }

                if (targetIndex < m_UndoStackIndex)
                {
                    m_UndoStackIndex -= 1;
                }


            }
        }

        // Called on first load and on after of each domain reload
        private void OnEnable()
        {
            //Debug.Log("ProtoSpriteData OnEnable");

            GlobalKeyEventHandler.OnKeyEvent += OnGlobalKeyPress;
            if (!GlobalKeyEventHandler.RegistrationSucceeded)
                Debug.Log("Failed to register to Ctrl/Cmd+S.");

            Undo.undoRedoEvent += UndoRedoEvent;
            EditorApplication.wantsToQuit += EditorWantsToQuit;
        }

        // Called on Unity exit and before each domain reload
        private void OnDisable()
        {
            GlobalKeyEventHandler.OnKeyEvent -= OnGlobalKeyPress;
            Undo.undoRedoEvent -= UndoRedoEvent;

            DestroyImmediate(m_DrawPreviewGPUMaterial);
            DestroyImmediate(m_OutlineMaterial);
            DestroyImmediate(m_TempQuadMesh);
        }

        public void OnGlobalKeyPress(Event current)
        {
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.S && ((current.modifiers & EventModifiers.Control) != 0 || (current.modifiers & EventModifiers.Command) != 0))
            {
                Saving.SaveAll();
            }
        }

        static bool EditorWantsToQuit()
        {
            // Need to manually deactivate the active tool as otherwise overridden scene selection outline value doesn't get reset
            if (ToolManager.activeToolType != null && ToolManager.activeToolType.IsSubclassOf(typeof(ProtoSpriteTool)))
            {
                var toolInstance = ProtoSpriteWindow.GetInstance().GetToolInstance(ToolManager.activeToolType);
                toolInstance.OnWillBeDeactivated();
            }

            Saving.SaveAll();
            return true;
        }

        /*public static void DrawCustomBrush(Texture srcTexture, Rect srcRect, Texture dstTexture, Rect dstRect, int2 startTexel, int2 endTexel, Color color, Rect dstSpriteRect, bool alphaBlend, bool copyToCPU)
        {
            //dstTexture.Apply(false, false);

        }*/

        public static void DrawCustomBrushCPUToGPU(Texture2D srcTexture, Rect srcRect, Texture2D dstTexture, Rect dstRect, int2 startTexel, int2 endTexel, Color color, bool alphaBlend)
        {
            var srcData = srcTexture.GetPixelData<Color32>(0);
            var dstData = new NativeArray<Color32>(dstTexture.GetPixelData<Color32>(0), Allocator.TempJob);

            int2 srcTextureSize = new int2(srcTexture.width, srcTexture.height);
            int2 dstTextureSize = new int2(dstTexture.width, dstTexture.height);

            int x0 = startTexel.x;
            int x1 = endTexel.x;
            int y0 = startTexel.y;
            int y1 = endTexel.y;

            int dx = math.abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -math.abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;


            // Skip first if doing more than one
            if (!(x0 == x1 && y0 == y1))
            {
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }

            for (; ; )
            {

                int2 pixel = new int2(x0, y0);

                {
                    BlitTexture(srcData, srcTextureSize, dstData, dstTextureSize, srcRect, new Rect(pixel.x, pixel.y, srcRect.width, srcRect.height), dstRect, color, alphaBlend);
                }

                if (x0 == x1 && y0 == y1) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }

            dstTexture.SetPixelData<Color32>(dstData, 0);

            dstTexture.Apply(true, false);

            dstData.Dispose();

            //_DrawCustomBrushGPUToCPU(srcTexture, srcRect, dstTexture, dstRect, startTexel, endTexel, color);
        }

        public static void DrawPreviewGPU(Sprite sprite, int2 startTexel, int2 endTexel, int brushSize, Color color, BrushShape brushShape)
        {
            Texture2D texture = sprite.texture;

            texture.Apply(false, false);

            DrawLineGPU(sprite, startTexel, endTexel, brushSize, color, brushShape);
        }

        static void DrawLineCPU(Sprite sprite, int2 startTexel, int2 endTexel, int brushSize, Color color, BrushShape brushShape)
        {
            Texture2D texture = sprite.texture;
            Rect spriteRect = sprite.rect;

            if (brushSize == 1)
            {
                // Bresenham
                BresenhamLineJob job;
                job.colors = texture.GetPixelData<Color32>(0);
                job.paintColor = color;
                job.textureSize = new int2(texture.width, texture.height);
                job.startPixel = startTexel;
                job.endPixel = endTexel;
                job.spriteRect = new int4((int)spriteRect.xMin, (int)spriteRect.yMin, (int)spriteRect.xMax, (int)spriteRect.yMax);

                job.Schedule().Complete();
            }
            else
            {
                JobHandle cpuJob = DrawJobStart(sprite, startTexel, endTexel, brushSize, color, brushShape);
                cpuJob.Complete();
            }
        }


        static List<Matrix4x4> matrices = new List<Matrix4x4>();
        public static void DrawCustomBrushGPUToCPU(Texture srcTexture, Rect srcRect, Texture dstTexture, Rect dstRect, int2 startTexel, int2 endTexel, Color color, Rect dstSpriteRect, BlendMode alphaBlend, bool copyToCPU)
        {
            //return;
            UnityEngine.Profiling.Profiler.BeginSample("ProtoSprite Draw");

			System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            var activeRT = RenderTexture.active;
            bool sRGBWrite = GL.sRGBWrite;

            //RenderTexture srcRT = null;
            RenderTexture dstRT = null;
            RenderTexture dstRT2 = null;

            /*{
                RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(srcTexture.width, srcTexture.height);
                rtDesc.colorFormat = RenderTextureFormat.ARGB32;
                rtDesc.useMipMap = dstTexture.mipmapCount > 1;
                rtDesc.mipCount = dstTexture.mipmapCount;
                rtDesc.autoGenerateMips = false;
                rtDesc.sRGB = false;// dstTexture.isDataSRGB;
                rtDesc.enableRandomWrite = true;
                srcRT = RenderTexture.GetTemporary(rtDesc);
            }*/

            {
                RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(dstTexture.width, dstTexture.height);
                rtDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                rtDesc.useMipMap = dstTexture.mipmapCount > 1;
                rtDesc.mipCount = dstTexture.mipmapCount;
                rtDesc.autoGenerateMips = false;
                rtDesc.sRGB = dstTexture.isDataSRGB;
                rtDesc.enableRandomWrite = true;
                dstRT = RenderTexture.GetTemporary(rtDesc);
                rtDesc.sRGB = dstTexture.isDataSRGB;
                rtDesc.colorFormat = RenderTextureFormat.ARGB32;
                dstRT2 = RenderTexture.GetTemporary(rtDesc);
            }


            /*instance.OutlineMaterial.EnableKeyword("ALPHA_DIVIDE");
            instance.OutlineMaterial.DisableKeyword("ALPHA_BYPASS");
            instance.OutlineMaterial.DisableKeyword("ALPHA_MULTIPLY");
            //instance.OutlineMaterial.EnableKeyword("ALPHA_DIVIDE");
            //GL.sRGBWrite = false;
            Graphics.Blit(dstRT, dstRT2, ProtoSpriteData.instance.OutlineMaterial, 4);
            if (dstRT2.mipmapCount > 1)
                dstRT2.GenerateMips();*/
            //Graphics.CopyTexture(dstTexture, dstRT);
            //Graphics.CopyTexture(dstTexture, dstRT2);

            //GL.sRGBWrite = true;
            //Graphics.CopyTexture(srcTexture, srcRT);
            //Graphics.CopyTexture(dstTexture, dstRT);

            //if (!copyToCPU)


            if (PlayerSettings.colorSpace == ColorSpace.Linear && dstTexture.isDataSRGB)
                instance.OutlineMaterial.EnableKeyword("SRGBREAD");
            else
                instance.OutlineMaterial.DisableKeyword("SRGBREAD");

            bool doPremultiplyAlpha = alphaBlend == BlendMode.ALPHA;// true;// true;// false;// true;// talphaBlend != BlendMode.MAX;

            if (doPremultiplyAlpha)
            {
                instance.OutlineMaterial.EnableKeyword("ALPHA_MULTIPLY");
                instance.OutlineMaterial.DisableKeyword("ALPHA_BYPASS");
                instance.OutlineMaterial.DisableKeyword("ALPHA_DIVIDE");
                Graphics.Blit(dstTexture, dstRT, ProtoSpriteData.instance.OutlineMaterial, 4);
            }
            else
            {
                //Graphics.Blit(dstTexture, dstRT);
                instance.OutlineMaterial.EnableKeyword("ALPHA_BYPASS");
                instance.OutlineMaterial.DisableKeyword("ALPHA_MULTIPLY");
                instance.OutlineMaterial.DisableKeyword("ALPHA_DIVIDE");
                Graphics.Blit(dstTexture, dstRT, ProtoSpriteData.instance.OutlineMaterial, 4);
            }

            //Graphics.CopyTexture(dstTexture, dstRT2);

            RenderTexture.active = dstRT;

            //Debug.Log("herer");


            //GL.Clear(true, true, Color.clear);

            //Debug.Log(srcRT.isDataSRGB + " " + GL.sRGBWrite);

            Matrix4x4 orthoMatrix = Matrix4x4.Ortho(0, dstTexture.width, 0, dstTexture.height, -1, 1);

            //Graphics.CopyTexture(dstTexture, rt);

            //Graphics.Blit(dstTexture, rt);

            //GL.Clear(true, true, Color.clear);

            /*GL.MultiTexCoord2(0, srcRect.xMin / src.width, srcRect.yMin / src.height);
            GL.MultiTexCoord2(1, dstRect.xMin / dst.width, dstRect.yMin / dst.height);
            //GL.MultiTexCoord2(1, 1, 0);
            GL.Vertex3(dstRect.xMin / dst.width, dstRect.yMin / dst.height, 0);

            GL.MultiTexCoord2(0, srcRect.xMax / src.width, srcRect.yMin / src.height);
            GL.MultiTexCoord2(1, dstRect.xMax / dst.width, dstRect.yMin / dst.height);
            //GL.MultiTexCoord2(1, 0, 0);
            GL.Vertex3(dstRect.xMax / dst.width, dstRect.yMin / dst.height, 0);

            GL.MultiTexCoord2(0, srcRect.xMax / src.width, srcRect.yMax / src.height);
            GL.MultiTexCoord2(1, dstRect.xMax / dst.width, dstRect.yMax / dst.height);
            //GL.MultiTexCoord2(1, 0, 1);
            GL.Vertex3(dstRect.xMax / dst.width, dstRect.yMax / dst.height, 0);

            GL.MultiTexCoord2(0, srcRect.xMin / src.width, srcRect.yMax / src.height);*/

            Mesh mesh = GetTempQuadMesh(new Rect(0, 0, srcRect.width / dstTexture.width, srcRect.height / dstTexture.height), new Rect(srcRect.xMin / srcTexture.width, srcRect.yMin / srcTexture.height, srcRect.width / srcTexture.width, srcRect.height / srcTexture.height), false, false);//, GetFullScreenQuad();// GetQuadMesh(0.5f, 0.5f, new Rect(0, 0, 10, 10), false, false);

            Material mat = instance.OutlineMaterial;

            mat.SetTexture("_MainTex", srcTexture);
            mat.SetTexture("_DstTex", dstTexture);
            mat.SetColor("_Color", color);// (QualitySettings.activeColorSpace == ColorSpace.Linear && dstRT.isDataSRGB) ? color.linear : color);
            mat.SetVector("_SpriteRect", new Vector4(dstRect.xMin / dstTexture.width, dstRect.yMin / dstTexture.height, dstRect.xMax / dstTexture.width, dstRect.yMax / dstTexture.height));

            //Debug.Log(alphaBlend);

            if (alphaBlend == BlendMode.ALPHA)
            {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
                mat.SetInt("_AlphaBlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
                //mat.EnableKeyword("ALPHABLEND");
            }
            /*else if (alphaBlend == BlendMode.MAX)
            {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Max);
                mat.SetInt("_AlphaBlendOp", (int)UnityEngine.Rendering.BlendOp.Max);
                //mat.DisableKeyword("ALPHABLEND");
            }*/
            else if (alphaBlend == BlendMode.SOURCE) // Color Only
            {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
                mat.SetInt("_AlphaBlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
            }
            else // Simple, set color and alpha
            {
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
                mat.SetInt("_AlphaBlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
                //mat.EnableKeyword("ALPHABLEND");
            }

            /*mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Max);
            mat.SetInt("_AlphaBlendOp", (int)UnityEngine.Rendering.BlendOp.Max);*/

            if (doPremultiplyAlpha)
            {
                mat.EnableKeyword("ALPHABLEND");
            }
            else
            {
                mat.DisableKeyword("ALPHABLEND");
            }

            mat.SetPass(3);

            // Total rect
            Rect totalRect = Rect.zero;

            //int2 srcSpriteSize = new int2((int)srcSpriteRect.width, (int)srcSpriteRect.height);

            int x0 = startTexel.x;
            int x1 = endTexel.x;
            int y0 = startTexel.y;
            int y1 = endTexel.y;

            int dx = math.abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -math.abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;

            // Skip first if doing more than one
            if (!(x0 == x1 && y0 == y1))
            {
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }

            Vector2[] uv2 = new Vector2[4];


            //var currentCamera = Camera.current;
            //Camera.SetupCurrent(null);

            //RenderTexture.active = dstRT;


            CommandBuffer cb = new CommandBuffer();

            matrices.Clear();

            //Debug.Log(Camera.current);

            GL.PushMatrix();
            GL.LoadOrtho();

            for (; ; )
            {
                int2 pixel = new int2(x0, y0);

                {
                    Rect dstBlitRect = new Rect(pixel.x, pixel.y, srcRect.width, srcRect.height);
                    /*if (totalRect == Rect.zero)
                    {
                        totalRect = dstBlitRect;
                    }
                    else
                    {
                        Encapsulate(ref totalRect, dstBlitRect);
                    }*/

                    /*uv2[0] = new Vector2(dstRect.xMin / dstRT.width, dstRect.yMin / dstRT.height);
                    uv2[1] = new Vector2(dstRect.xMax / dstRT.width, dstRect.yMin / dstRT.height);
                    uv2[2] = new Vector2(dstRect.xMax / dstRT.width, dstRect.yMax / dstRT.height);
                    uv2[3] = new Vector2(dstRect.xMin / dstRT.width, dstRect.yMax / dstRT.height);
                    mesh.SetUVs(1, uv2);*/

                    //matrices.Add(Matrix4x4.Translate(dstBlitRect.position / new Vector2(dstRT.width, dstRT.height)));
                    //Graphics.DrawMeshNow(mesh, Matrix4x4.Translate(dstBlitRect.position / new Vector2(dstRT.width, dstRT.height)));
                    //cb.DrawMesh(mesh, Matrix4x4.Translate(dstBlitRect.position / new Vector2(dstRT.width, dstRT.height)), mat, 0, 3);
                    
                    //Graphics.ExecuteCommandBuffer(cb);

                    //cb.Clear();

                    //Graphics.DrawMeshNow(mesh, Matrix4x4.Translate(dstBlitRect.position / new Vector2(dstRT.width, dstRT.height)));

                    MyBlit(null, srcTexture, srcRect, dstRT, dstBlitRect, dstRect);
                    //Graphics.CopyTexture(dstRT, dstTexture);
                }

                if (x0 == x1 && y0 == y1) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }

            
            //cb.DrawMesh(mesh, Matrix4x4.Translate(dstBlitRect.position / new Vector2(dstRT.width, dstRT.height)), mat, 0, 3);
            //cb.DrawMeshInstanced(mesh, 0, mat, 3, matrices.ToArray(), matrices.Count);
            //Graphics.ExecuteCommandBuffer(cb);


            //Camera.SetupCurrent(currentCamera);
            GL.PopMatrix();

            cb.Dispose();
            //DestroyImmediate(mesh);

            //Debug.Log("src: " + srcTexture.isDataSRGB + " dst: " + dstTexture.isDataSRGB + " rt: " + rt.isDataSRGB);

            /*ComputeShader cs = instance.CustomBrushComputeShader;
            var kernel = cs.FindKernel("CSMain");

            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                cs.EnableKeyword("LINEAR_SPACE");
            }
            else
            {
                cs.DisableKeyword("LINEAR_SPACE");
            }

            cs.SetTexture(kernel, "hoverPixelData", srcTexture);
            cs.SetTexture(kernel, "targetPixelData", rt);

            cs.SetVector("hoverTextureSize", new Vector4(srcTexture.width, srcTexture.height, 0, 0));
            cs.SetVector("targetTextureSize", new Vector4(dstTexture.width, dstTexture.height, 0, 0));

            cs.SetVector("startTexel", new Vector4(startTexel.x, startTexel.y, 0, 0));
            cs.SetVector("endTexel", new Vector4(endTexel.x, endTexel.y, 0, 0));

            cs.SetVector("spriteRect", new Vector4(dstRect.xMin, dstRect.yMin, dstRect.xMax, dstRect.yMax));
            cs.SetVector("srcSpriteRect", new Vector4(srcRect.position.x, srcRect.position.y, srcRect.width, srcRect.height));

            cs.SetVector("color", (Color)color);

            cs.Dispatch(kernel, 1, 1, 1);*/

            //if (dstRT.mipmapCount > 1)
            //dstRT.GenerateMips();


            instance.OutlineMaterial.DisableKeyword("SRGBREAD");

            if (doPremultiplyAlpha)
            {
                instance.OutlineMaterial.EnableKeyword("ALPHA_DIVIDE");
                instance.OutlineMaterial.DisableKeyword("ALPHA_BYPASS");
                instance.OutlineMaterial.DisableKeyword("ALPHA_MULTIPLY");
                //instance.OutlineMaterial.EnableKeyword("ALPHA_DIVIDE");
                //GL.sRGBWrite = false;
                Graphics.Blit(dstRT, dstRT2, ProtoSpriteData.instance.OutlineMaterial, 4);
                if (dstRT2.mipmapCount > 1)
                    dstRT2.GenerateMips();
                Graphics.CopyTexture(dstRT2, dstTexture);
            }
            else
            {
                instance.OutlineMaterial.DisableKeyword("SRGBREAD");
                instance.OutlineMaterial.EnableKeyword("ALPHA_BYPASS");
                instance.OutlineMaterial.DisableKeyword("ALPHA_DIVIDE");
                instance.OutlineMaterial.DisableKeyword("ALPHA_MULTIPLY");
                //Graphics.Blit(dstRT, dstRT2);
                Graphics.Blit(dstRT, dstRT2, ProtoSpriteData.instance.OutlineMaterial, 4);
                if (dstRT2.mipmapCount > 1)
                    dstRT2.GenerateMips();
                Graphics.CopyTexture(dstRT2, dstTexture);
            }



            //GL.sRGBWrite = false;


            //dstTexture.ReadPixels(new Rect(0,0,dstTexture.width, dstTexture.height), 0, 0);
            //dstTexture.ReadPixels(totalRect, 0, 0);

            totalRect.xMin = Mathf.Clamp(totalRect.xMin, 0, dstTexture.width);
            totalRect.xMax = Mathf.Clamp(totalRect.xMax, 0, dstTexture.width);
            totalRect.yMin = Mathf.Clamp(totalRect.yMin, 0, dstTexture.height);
            totalRect.yMax = Mathf.Clamp(totalRect.yMax, 0, dstTexture.height);


            Rect readPixelsRect = totalRect;

            if (SystemInfo.graphicsUVStartsAtTop)
            {
                //readPixelsRect.y = dstTexture.height - readPixelsRect.y;
                readPixelsRect.y = dstTexture.height - (readPixelsRect.y + readPixelsRect.height);
            }


            Texture2D dstTex2D = dstTexture as Texture2D;

            if (copyToCPU && dstTex2D != null)
            {
                if (readPixelsRect.height > 0 && readPixelsRect.width > 0)
                {
                    // Instead we do gpu => cpu at end of painting
                    //ReadGPUToCPU(dstTex2D);
                }
            }

            //RenderTexture.ReleaseTemporary(srcRT);
            RenderTexture.ReleaseTemporary(dstRT);
            RenderTexture.ReleaseTemporary(dstRT2);

            RenderTexture.active = activeRT;
            GL.sRGBWrite = sRGBWrite;

            UnityEngine.Profiling.Profiler.EndSample();
            stopwatch.Stop();

            //if (stopwatch.Elapsed.TotalSeconds > 0.005f)
                //Debug.Log(stopwatch.Elapsed.TotalSeconds);
        }

        public static void BlitRect(Texture srcTexture, Rect srcRect, RenderTexture dstTexture, Rect dstRect, Material material, int shaderPass)
        {
            var activeRT = RenderTexture.active;
            bool sRGBWrite = GL.sRGBWrite;

            RenderTexture.active = dstTexture;

            material.SetTexture("_MainTex", srcTexture);
            material.SetPass(shaderPass);

            GL.PushMatrix();
            GL.LoadOrtho();

            MyBlit(null, srcTexture, srcRect, dstTexture, dstRect, dstRect);

            GL.PopMatrix();

            RenderTexture.active = activeRT;
            GL.sRGBWrite = sRGBWrite;
        }

        public static void ReadGPUToCPU(Texture2D texture)
        {
            //UnityEngine.Profiling.Profiler.BeginSample("async");
            //var request = AsyncGPUReadback.RequestIntoNativeArray<Color32>(ref pixelData, dstRT2, 0, (int)readPixelsRect.x, (int)readPixelsRect.width, (int)readPixelsRect.y, (int)readPixelsRect.height, 0, 1, TextureFormat.RGBA32);
            //request.WaitForCompletion();
            //UnityEngine.Profiling.Profiler.EndSample();


            //UnityEngine.Profiling.Profiler.BeginSample("readpixels");
            //dstTex2D.ReadPixels(readPixelsRect, (int)totalRect.x, (int)totalRect.y);

            //Debug.Log("newPixelData: " + (dstTex2D.GetPixel((int)readPixelsRect.x, (int)readPixelsRect.y) * 255.0f));// [0].b * 255.0));
            //UnityEngine.Profiling.Profiler.EndSample();

            Rect readPixelsRect = new Rect(0, 0, texture.width, texture.height);

            int kernelID = 0;

            if (QualitySettings.activeColorSpace == ColorSpace.Gamma || !texture.isDataSRGB)
            {
                kernelID = instance.ReadPixelsComputeShader.FindKernel("CSMain_Gamma");
            }
            else
            {
                kernelID = instance.ReadPixelsComputeShader.FindKernel("CSMain_Linear");
            }

            int width = (int)readPixelsRect.width;
            int height = (int)readPixelsRect.height;

            //UnityEngine.Profiling.Profiler.BeginSample("compute readpixels");

            //UnityEngine.Profiling.Profiler.BeginSample("compute dispatch");

            ComputeBuffer cb = new ComputeBuffer(width * height, sizeof(uint));
            //ComputeBuffer cbFloat = new ComputeBuffer(width * height, sizeof(float) * 4);

            instance.ReadPixelsComputeShader.SetTexture(kernelID, "inputTexture", texture);
            instance.ReadPixelsComputeShader.SetBuffer(kernelID, "outputBuffer", cb);
            //instance.ReadPixelsComputeShader.SetBuffer(kernelID, "outputBufferFloat4", cbFloat);
            instance.ReadPixelsComputeShader.SetInt("textureWidth", width);
            instance.ReadPixelsComputeShader.SetVector("rectPosition", new Vector2((int)readPixelsRect.x, (int)readPixelsRect.y));

            

            instance.ReadPixelsComputeShader.Dispatch(kernelID, width, height, 1);

            //UnityEngine.Profiling.Profiler.EndSample();

            //Color[] arrayData = new Color[width * height];


            //UnityEngine.Profiling.Profiler.BeginSample("compute RequestIntoNativeArray");

            //NativeArray<Color32> myNativeArray = new NativeArray<Color32>(width * height, Allocator.Persistent);
            //var r = AsyncGPUReadback.RequestIntoNativeArray(ref myNativeArray, cb);
            //r.WaitForCompletion();

            //cb.GetData(arrayData);

            //UnityEngine.Profiling.Profiler.EndSample();

            //Color[] arrayOfColor = new Color[dstRT2.width * dstRT2.height];
            //cb.GetData(arrayOfColor);

            //Debug.Log("Color:" + arrayOfColor[0]);
            UnityEngine.Profiling.Profiler.BeginSample("compute async readback");

            var req = AsyncGPUReadback.Request(cb);
            req.WaitForCompletion();
            var newPixelData = req.GetData<Color32>();

            /*req = AsyncGPUReadback.Request(cbFloat);
            req.WaitForCompletion();
            var newPixelDataFloat = req.GetData<Color>();

            for (int i = 0; i < newPixelData.Length; i++)
            {
                Color color = newPixelDataFloat[i];
                Color32 color32 = new Color32(
                    (byte)Mathf.Round(color.r * 255f),
                    (byte)Mathf.Round(color.g * 255f),
                    (byte)Mathf.Round(color.b * 255f),
                    (byte)Mathf.Round(color.a * 255f)
                );

                //newPixelData[i] = newPixelDataFloat[i];
                //newPixelData[i] = color32;
            }*/

            UnityEngine.Profiling.Profiler.EndSample();

            UnityEngine.Profiling.Profiler.BeginSample("compute getpixeldata");
            var pixelData = texture.GetPixelData<Color32>(0);
            UnityEngine.Profiling.Profiler.EndSample();

            //Debug.Log("newPixelData: " + (newPixelData[0] * 255.0f));
            UnityEngine.Profiling.Profiler.BeginSample("compute job blit");

            var job = new TextureBlitJob
            {
                sourceData = newPixelData,
                destinationData = pixelData,
                srcTextureSize = new int2(width, height),
                dstTextureSize = new int2(texture.width, texture.height),
                srcRect = new Rect(0,0,width,height),// new int4((int)srcRect.x, (int)srcRect.y, (int)srcRect.width, (int)srcRect.height),
                dstRect = readPixelsRect,// new int4((int)dstRect.x, (int)dstRect.y, (int)dstRect.width, (int)dstRect.height),
                dstClampRect = readPixelsRect// new int4((int)dstClampRect.xMin, (int)dstClampRect.yMin, (int)dstClampRect.xMax, (int)dstClampRect.yMax)
            };

            JobHandle jobHandle = job.Schedule(width * height, 64);
            jobHandle.Complete();

            //UnityEngine.Profiling.Profiler.EndSample();

            //myNativeArray.Dispose();
            newPixelData.Dispose();
            cb.Dispose();
            //cbFloat.Dispose();
            UnityEngine.Profiling.Profiler.EndSample();
        }

        public static void Encapsulate(ref Rect rect, Rect other)
        {
            float minX = Mathf.Min(rect.xMin, other.xMin);
            float minY = Mathf.Min(rect.yMin, other.yMin);
            float maxX = Mathf.Max(rect.xMax, other.xMax);
            float maxY = Mathf.Max(rect.yMax, other.yMax);

            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        static void MyBlit(Mesh mesh, Texture src, Rect srcRect, RenderTexture dst, Rect dstRect, Rect dstSpriteRect)
        {
            //Mesh mesh = GetFullScreenQuad();// GetQuadMesh(0.5f, 0.5f, new Rect(0, 0, 10, 10), false, false);

            //Material mat = new Material(Shader.Find("Hidden/ProtoSprite/Outline"));

            //mat.SetTexture("_MainTex", src);
            //mat.SetPass(3);

            //GL.LoadPixelMatrix(0, 0, 0, 0);

            //Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            //GL.LoadPixelMatrix(0, dstTexture.width, dstTexture.height, 0);

            //mesh.vertic

            /*Vector3[] vertices = new Vector3[4];
            vertices[0] = new Vector3(dstRect.xMin / dst.width, dstRect.yMin / dst.height, 0);
            vertices[1] = new Vector3(dstRect.xMax / dst.width, dstRect.yMin / dst.height, 0);
            vertices[2] = new Vector3(dstRect.xMax / dst.width, dstRect.yMax / dst.height, 0);
            vertices[3] = new Vector3(dstRect.xMin / dst.width, dstRect.yMax / dst.height, 0);
            mesh.vertices = vertices;

            int[] indices = new int[6];
            indices[0] = 0;
            indices[1] = 1;
            indices[2] = 2;
            indices[3] = 0;
            indices[4] = 2;
            indices[5] = 3;
            mesh.triangles = indices;

            Vector2[] uv0 = new Vector2[4];
            uv0[0] = new Vector2(srcRect.xMin / src.width, srcRect.yMin / src.height);
            uv0[1] = new Vector2(srcRect.xMax / src.width, srcRect.yMin / src.height);
            uv0[2] = new Vector2(srcRect.xMax / src.width, srcRect.yMax / src.height);
            uv0[3] = new Vector2(srcRect.xMin / src.width, srcRect.yMax / src.height);
            mesh.uv = uv0;*/

            

            
            //Graphics.DrawMeshNow(mesh, Matrix4x4.Translate(dstRect.position / new Vector2(dst.width, dst.height)));

            GL.Begin(GL.QUADS);

            // Vertex positions
            GL.MultiTexCoord2(0, srcRect.xMin / src.width, srcRect.yMin / src.height);
            GL.MultiTexCoord2(1, dstRect.xMin / dst.width, dstRect.yMin / dst.height);
            //GL.MultiTexCoord2(1, 1, 0);
            GL.Vertex3(dstRect.xMin / dst.width, dstRect.yMin / dst.height, 0);

            GL.MultiTexCoord2(0, srcRect.xMax / src.width, srcRect.yMin / src.height);
            GL.MultiTexCoord2(1, dstRect.xMax / dst.width, dstRect.yMin / dst.height);
            //GL.MultiTexCoord2(1, 0, 0);
            GL.Vertex3(dstRect.xMax / dst.width, dstRect.yMin / dst.height, 0);

            GL.MultiTexCoord2(0, srcRect.xMax / src.width, srcRect.yMax / src.height);
            GL.MultiTexCoord2(1, dstRect.xMax / dst.width, dstRect.yMax / dst.height);
            //GL.MultiTexCoord2(1, 0, 1);
            GL.Vertex3(dstRect.xMax / dst.width, dstRect.yMax / dst.height, 0);

            GL.MultiTexCoord2(0, srcRect.xMin / src.width, srcRect.yMax / src.height);
            GL.MultiTexCoord2(1, dstRect.xMin / dst.width, dstRect.yMax / dst.height);
            //GL.MultiTexCoord2(1, 1, 0.5f);
            GL.Vertex3(dstRect.xMin / dst.width, dstRect.yMax / dst.height, 0);


            GL.End();
           



            //DestroyImmediate(mesh);
            //DestroyImmediate(mat);

            //Debug.Log("src: " + srcTexture.isDataSRGB + " dst: " + dstTexture.isDataSRGB + " rt: " + rt.isDataSRGB);

            /*ComputeShader cs = instance.CustomBrushComputeShader;
            var kernel = cs.FindKernel("CSMain");

            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                cs.EnableKeyword("LINEAR_SPACE");
            }
            else
            {
                cs.DisableKeyword("LINEAR_SPACE");
            }

            cs.SetTexture(kernel, "hoverPixelData", srcTexture);
            cs.SetTexture(kernel, "targetPixelData", rt);

            cs.SetVector("hoverTextureSize", new Vector4(srcTexture.width, srcTexture.height, 0, 0));
            cs.SetVector("targetTextureSize", new Vector4(dstTexture.width, dstTexture.height, 0, 0));

            cs.SetVector("startTexel", new Vector4(startTexel.x, startTexel.y, 0, 0));
            cs.SetVector("endTexel", new Vector4(endTexel.x, endTexel.y, 0, 0));

            cs.SetVector("spriteRect", new Vector4(dstRect.xMin, dstRect.yMin, dstRect.xMax, dstRect.yMax));
            cs.SetVector("srcSpriteRect", new Vector4(srcRect.position.x, srcRect.position.y, srcRect.width, srcRect.height));

            cs.SetVector("color", (Color)color);

            cs.Dispatch(kernel, 1, 1, 1);*/
        }

        static Mesh GetFullScreenQuad()
        {
            Mesh quadMesh = new Mesh();
            quadMesh.vertices = new Vector3[]
            {
            new Vector3(-1, -1, 0),
            new Vector3(1, -1, 0),
            new Vector3(1, 1, 0),
            new Vector3(-1, 1, 0)
            };
            quadMesh.uv = new Vector2[]
            {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
            };
            quadMesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            return quadMesh;
        }

        public static void DrawLineGPU(Texture texture, Rect spriteRect, int2 startTexel, int2 endTexel, int brushSize, Color color, BrushShape brushShape)
        {

            var activeRT = RenderTexture.active;

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(texture.width, texture.height);
            rtDesc.colorFormat = RenderTextureFormat.ARGB32;
            rtDesc.useMipMap = texture.mipmapCount > 1;
            rtDesc.mipCount = texture.mipmapCount;
            rtDesc.autoGenerateMips = false;
            rtDesc.sRGB = texture.isDataSRGB;
            rtDesc.enableRandomWrite = true;
            var rt = RenderTexture.GetTemporary(rtDesc);

            RenderTexture.active = rt;

            if (brushSize == 1)
            {
                // Bresenham
                Graphics.Blit(texture, rt);

                ComputeShader cs = instance.BresenhamLineComputeShader;
                var kernel = cs.FindKernel("CSMain");
                cs.SetVector("startPixel", new Vector4(startTexel.x, startTexel.y, 0, 0));
                cs.SetVector("endPixel", new Vector4(endTexel.x, endTexel.y, 0, 0));
                cs.SetVector("paintColor", (Color)color);
                cs.SetTexture(kernel, "Result", rt);
                cs.SetInt("brushSize", brushSize);
                cs.SetVector("spriteRect", new Vector4(spriteRect.xMin, spriteRect.yMin, spriteRect.xMax, spriteRect.yMax));

                cs.Dispatch(kernel, 1, 1, 1);
            }
            else
            {
                instance.DrawPreviewGPUMaterial.SetVector("_ProtoSprite_CursorPixel", new Vector2(endTexel.x, endTexel.y));
                instance.DrawPreviewGPUMaterial.SetVector("_ProtoSprite_PreviousCursorPixel", new Vector2(startTexel.x, startTexel.y));
                instance.DrawPreviewGPUMaterial.SetInt("_ProtoSprite_BrushSize", brushSize);
                instance.DrawPreviewGPUMaterial.SetColor("_ProtoSprite_Color", (QualitySettings.activeColorSpace == ColorSpace.Linear && texture.isDataSRGB) ? color.linear : color);
                instance.DrawPreviewGPUMaterial.SetVector("_ProtoSprite_SpriteRect", new Vector4(spriteRect.xMin, spriteRect.yMin, spriteRect.xMax, spriteRect.yMax));
                instance.DrawPreviewGPUMaterial.SetInt("_ProtoSprite_BrushShape", (int)brushShape);

                Graphics.Blit(texture, instance.DrawPreviewGPUMaterial);
            }

            if (rt.mipmapCount > 1)
                rt.GenerateMips();

            Graphics.CopyTexture(rt, texture);

            RenderTexture.ReleaseTemporary(rt);

            RenderTexture.active = activeRT;
        }

        static void DrawLineGPU(Sprite sprite, int2 startTexel, int2 endTexel, int brushSize, Color color, BrushShape brushShape)
        {
            Texture2D texture = sprite.texture;
            Rect spriteRect = sprite.rect;

            DrawLineGPU(texture, spriteRect, startTexel, endTexel, brushSize, color, brushShape);
        }

        public static void DrawPreview(Sprite sprite, int2 texturePixel, int brushSize, Color color)
        {
            Texture2D texture = sprite.texture;

            int2 textureSize = new int2(texture.width, texture.height);

            // Is brush overlapping the sprite texture rect
            Rect brushRect = new Rect(texturePixel.x - brushSize / 2, texturePixel.y - brushSize / 2, brushSize, brushSize);

            if (!brushRect.Overlaps(sprite.rect))
            {
                texture.Apply(true, false);
                return;
            }

            NativeArray<Color32> currentPixelData = texture.GetPixelData<Color32>(0);
            NativeArray<Color32> currentPixelDataCopy = new NativeArray<Color32>(currentPixelData, Allocator.Temp);

            Draw(textureSize, texturePixel, color, brushSize, ref currentPixelData, sprite.rect);

            texture.Apply(true, false);
            texture.SetPixelData(currentPixelDataCopy, 0);
            currentPixelDataCopy.Dispose();
        }

        public static Color EditorPrefs_GetColor(string key, Color defaultValue)
        {
            Color color = defaultValue;

            string htmlString = EditorPrefs.GetString(key, ColorUtility.ToHtmlStringRGBA(defaultValue));

            if (!htmlString.StartsWith("#"))
            {
                htmlString = "#" + htmlString;
            }

            if (ColorUtility.TryParseHtmlString(htmlString, out Color parsedColor))
            {
                color = parsedColor;
            }

            return color;
        }

        public static void EditorPrefs_SetColor(string key, Color value)
        {
            EditorPrefs.SetString(key, ColorUtility.ToHtmlStringRGBA(value));
        }

        public static bool SceneSelectionGizmo
        {
            get
            {
                try
                {
                    Type AnnotationUtility = Type.GetType("UnityEditor.AnnotationUtility, UnityEditor");
                    var ShowOutlineOption = AnnotationUtility.GetProperty("showSelectionOutline", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    return (bool)ShowOutlineOption.GetValue(null);
                }
                catch { }
                return false;
            }
            set
            {
                try
                {
                    Type AnnotationUtility = Type.GetType("UnityEditor.AnnotationUtility, UnityEditor");
                    var ShowOutlineOption = AnnotationUtility.GetProperty("showSelectionOutline", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    ShowOutlineOption.SetValue(null, value);
                }
                catch { }
            }
        }

		public static void DrawInvalidHandles()
        {
            Transform[] transforms = Selection.transforms;

            bool didDraw = false;

            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];

                SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null || spriteRenderer.sprite == null)
                {
                    Matrix4x4 tempMatrix = Handles.matrix;
                    Handles.matrix = t.localToWorldMatrix;

                    Vector2 center = Vector2.zero;

                    float size = 0.2f * HandleUtility.GetHandleSize(t.position);

                    Handles.DrawWireDisc(center, t.forward, size);

                    var tempColor = Handles.color;
                    Handles.color = Color.red;
                    Handles.DrawLine(center - Vector2.one.normalized * size, center + Vector2.one.normalized * size);
                    Handles.color = tempColor;

                    Handles.matrix = tempMatrix;

                    continue;
                }

                Sprite sprite = spriteRenderer.sprite;

                // Draw rect outline
                {
                    Rect spriteRect = sprite.rect;
                    Vector2 scale = (new Vector2(spriteRect.width, spriteRect.height) / sprite.pixelsPerUnit);

                    if (spriteRenderer.drawMode != SpriteDrawMode.Simple)
                    {
                        scale = spriteRenderer.size;
                    }

                    Matrix4x4 tempMatrix = Handles.matrix;
                    Handles.matrix = t.localToWorldMatrix;

                    Vector2 normalizedPivot = sprite.pivot / spriteRect.size;

                    if (spriteRenderer.flipX)
                        normalizedPivot.x = 1.0f - normalizedPivot.x;
                    if (spriteRenderer.flipY)
                        normalizedPivot.y = 1.0f - normalizedPivot.y;

                    Vector2 center = (new Vector2(0.5f, 0.5f) - normalizedPivot) * scale;

                    Handles.DrawWireCube(center, scale);

                    var tempColor = Handles.color;
                    Handles.color = Color.red;
                    Handles.DrawLine(center - scale * 0.5f, center + scale * 0.5f);
                    Handles.color = tempColor;

                    Handles.matrix = tempMatrix;

                    didDraw = true;
                }
            }

            if (!didDraw)
                return;

            int passiveControl = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(passiveControl);


            if (Event.current.GetTypeForControl(passiveControl) == EventType.MouseDown && Event.current.button == 0)
            {
                GUIUtility.hotControl = passiveControl;
            }
        }

        public static void GenerateFullRectOverrideGeometry(Sprite sprite)
        {
            sprite.SetVertexCount(4);

            // Generate positions
            // Indices
            // UV0

            Rect spriteRect = sprite.rect;
            Vector2 scale = (new Vector2(spriteRect.width, spriteRect.height) / sprite.pixelsPerUnit);

            Vector2 normalizedPivot = sprite.pivot / spriteRect.size;

            Vector2 center = (new Vector2(0.5f, 0.5f) - normalizedPivot) * scale;

            var vertices = new NativeArray<Vector3>(4, Allocator.Temp);

            vertices[0] = center - new Vector2(-scale.x, -scale.y) * 0.5f; //scale * 0.5f - spritePivot / sprite.pixelsPerUnit + new Vector2(-scale.x, -scale.y) * 0.5f;// new Vector3(0, 0);
            vertices[1] = center - new Vector2(-scale.x, scale.y) * 0.5f; //scale * 0.5f - spritePivot / sprite.pixelsPerUnit + new Vector2(-scale.x, scale.y) * 0.5f;
            vertices[2] = center - new Vector2(scale.x, scale.y) * 0.5f; //scale * 0.5f - spritePivot / sprite.pixelsPerUnit + new Vector2(scale.x, scale.y) * 0.5f;
            vertices[3] = center - new Vector2(scale.x, -scale.y) * 0.5f; //scale * 0.5f - spritePivot / sprite.pixelsPerUnit + new Vector2(scale.x, -scale.y) * 0.5f;

            NativeArray<ushort> indicesOverride = new NativeArray<ushort>(6, Allocator.Temp);

            indicesOverride[0] = 0;
            indicesOverride[1] = 1;
            indicesOverride[2] = 2;
            indicesOverride[3] = 2;
            indicesOverride[4] = 3;
            indicesOverride[5] = 0;

            sprite.SetVertexAttribute<Vector3>(UnityEngine.Rendering.VertexAttribute.Position, vertices);

            sprite.SetIndices(indicesOverride);

            indicesOverride.Dispose();
            vertices.Dispose();

            // This seems to force regenerate the UVs
            var uvs = sprite.GetVertexAttribute<Vector2>(UnityEngine.Rendering.VertexAttribute.TexCoord0);
        }

        public static void RepaintSceneViewsIfUnityFocused()
        {
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                return;

            foreach (var sceneView in SceneView.sceneViews)
            {
                (sceneView as SceneView).Repaint();
            }
        }

        static Type s_UnityEditor_EyeDropper_Type = null;
        static Type UnityEditor_EyeDropper_Type
        {
            get
            {
                if (s_UnityEditor_EyeDropper_Type == null)
                {
                    var assembly = Assembly.GetAssembly(typeof(SceneView));
                    s_UnityEditor_EyeDropper_Type = assembly.GetType("UnityEditor.EyeDropper");
                }

                return s_UnityEditor_EyeDropper_Type;
            }
        }

        static MethodInfo s_UnityEditor_EyeDropper_GetPickedColor = null;
        static MethodInfo UnityEditor_EyeDropper_GetPickedColor
        {
            get
            {
                if (s_UnityEditor_EyeDropper_GetPickedColor == null)
                {
                    s_UnityEditor_EyeDropper_GetPickedColor = UnityEditor_EyeDropper_Type.GetMethod("GetPickedColor");
                }

                return s_UnityEditor_EyeDropper_GetPickedColor;
            }
        }

        public static bool IsColorPickerOpen()
        {
            bool isColorPickerOpen = false;

            try
            {
                var method = UnityEditor_EyeDropper_Type.GetProperty("IsOpened");
                isColorPickerOpen = (bool)method.GetValue(null, null);
            }
            catch { }

            return isColorPickerOpen;
        }

        public static void EyeDropperStart()
        {
            try
            {
                var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView");
                var current = type.GetProperty("current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var assembly = System.Reflection.Assembly.GetAssembly(typeof(SceneView));
                var type2 = assembly.GetType("UnityEditor.EyeDropper");
                var method = type2.GetMethod("Start", new System.Type[] { type });
                method.Invoke(null, new object[] { current.GetValue(null, null) });
            }
            catch { }
        }

        public static Color GetEyeDropperPickedColor()
        {
            try
            {
                Color color = (Color)UnityEditor_EyeDropper_GetPickedColor.Invoke(null, null);
                return color;
            }
            catch { }

            return Color.white;
        }

        public static Color GetEyeDropperLastPickedColor()
        {
            try
            {
                var method = UnityEditor_EyeDropper_Type.GetMethod("GetLastPickedColor");
                Color color = (Color)method.Invoke(null, null);
                return color;
            }
            catch { }

            return Color.white;
        }

        public static int GetEyeDropperColorPickID()
        {
            try
            {
                var assembly = Assembly.GetAssembly(typeof(SceneView));
                var type2 = assembly.GetType("UnityEditor.EditorGUI");
                var field = type2.GetField("s_ColorPickID", BindingFlags.NonPublic | BindingFlags.Static);
                return (int)field.GetValue(null);
            }
            catch { }

            return -1;
        }

        public static void SetEyeDropperColorPickID(int value)
        {
            try
            {
                var assembly = Assembly.GetAssembly(typeof(SceneView));
                var type2 = assembly.GetType("UnityEditor.EditorGUI");
                var field = type2.GetField("s_ColorPickID", BindingFlags.NonPublic | BindingFlags.Static);
                field.SetValue(null, value);
            }
            catch { }
        }

        public static void RepaintSpriteEditorWindow()
        {
            try
            {
                Assembly editorAssembly = Assembly.Load("Unity.2D.Sprite.Editor");
                Type spriteEditorType = editorAssembly.GetType("UnityEditor.U2D.Sprites.SpriteEditorWindow");

                bool hasOpenInstances = (bool)typeof(EditorWindow)
                    .GetMethod("HasOpenInstances")
                    .MakeGenericMethod(spriteEditorType)
                    .Invoke(null, null);

                if (hasOpenInstances)
                {
                    EditorWindow spriteEditor = EditorWindow.GetWindow(spriteEditorType, false, "Sprite Editor", false);
                    spriteEditor.Repaint();
                }
            }
            catch { }
        }

        public static int2 GetPixelCoord()
        {
            return GetPixelCoord(Event.current.mousePosition);
        }

        public static int2 GetPixelCoord(Vector2 mousePosition)
        {
            return GetPixelCoord(mousePosition, Selection.activeTransform);
        }

        public static int2 GetPixelCoord(Vector2 mousePosition, Transform t)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);

            // Get texture
            SpriteRenderer spriteRenderer = t.GetComponent<SpriteRenderer>();
            Sprite sprite = spriteRenderer.sprite;
            Texture2D texture = SpriteUtility.GetSpriteTexture(sprite, false);

            // Translate mouse position to position in texture
            Plane plane = new Plane(t.forward, t.position);
            plane.Raycast(worldRay, out float distance);
            Vector3 intersectPos = worldRay.origin + worldRay.direction * distance;
            Vector2 localPos = t.InverseTransformPoint(intersectPos);

            GenerateFullRectOverrideGeometry(sprite);

            var vvv = sprite.vertices;

            if (vvv.Length != 4)
            {
                return int2.zero;
            }

            var uvs = sprite.GetVertexAttribute<Vector2>(UnityEngine.Rendering.VertexAttribute.TexCoord0);

            var indices = sprite.GetIndices();

            ushort i1 = indices[0];
            ushort i2 = indices[1];
            ushort i3 = indices[2];

            Vector3 uv1 = uvs[indices[0]];
            Vector3 uv2 = uvs[indices[1]];
            Vector3 uv3 = uvs[indices[2]];

            Vector3 p1 = vvv[i1];
            Vector3 p2 = vvv[i2];
            Vector3 p3 = vvv[i3];

            if (spriteRenderer.flipY)
            {
                p1.y = -p1.y;
                p2.y = -p2.y;
                p3.y = -p3.y;
            }

            if (spriteRenderer.flipX)
            {
                p1.x = -p1.x;
                p2.x = -p2.x;
                p3.x = -p3.x;
            }

            Vector3 f = localPos;

            var f1 = p1 - f;
            var f2 = p2 - f;
            var f3 = p3 - f;

            // calculate the areas (parameters order is essential in this case):
            Vector3 va = Vector3.Cross(p1 - p2, p1 - p3); // main triangle cross product
            Vector3 va1 = Vector3.Cross(f2, f3); // p1's triangle cross product
            Vector3 va2 = Vector3.Cross(f3, f1); // p2's triangle cross product
            Vector3 va3 = Vector3.Cross(f1, f2); // p3's triangle cross product
            float a = va.magnitude; // main triangle area

            // calculate barycentric coordinates with sign:
            float a1 = va1.magnitude / a * Mathf.Sign(Vector3.Dot(va, va1));
            float a2 = va2.magnitude / a * Mathf.Sign(Vector3.Dot(va, va2));
            float a3 = va3.magnitude / a * Mathf.Sign(Vector3.Dot(va, va3));

            // find the uv corresponding to point f (uv1/uv2/uv3 are associated to p1/p2/p3):
            Vector2 uv = uv1 * a1 + uv2 * a2 + uv3 * a3;

            int2 pixel = int2.zero;

            pixel.x = Mathf.FloorToInt(uv.x * texture.width);
            pixel.y = Mathf.FloorToInt(uv.y * texture.height);

            return pixel;
        }

        public static void DrawLineSimultaneousCPUAndGPU(Sprite sprite, int2 startTexel, int2 endTexel, int brushSize, Color color, BrushShape brushShape)
        {
            DrawLineCPU(sprite, startTexel, endTexel, brushSize, color, brushShape);
            DrawLineGPU(sprite, startTexel, endTexel, brushSize, color, brushShape);

            /*JobHandle cpuJob = DrawJobStart(sprite, startTexel, endTexel, brushSize, color, brushShape);

            Texture2D texture = sprite.texture;

            var activeRT = RenderTexture.active;

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(texture.width, texture.height);
            rtDesc.colorFormat = RenderTextureFormat.ARGB32;
            rtDesc.useMipMap = texture.mipmapCount > 1;
            rtDesc.mipCount = texture.mipmapCount;
            rtDesc.autoGenerateMips = false;
            rtDesc.sRGB = texture.isDataSRGB;

            var rt = RenderTexture.GetTemporary(rtDesc);

            RenderTexture.active = rt;

            Rect spriteRect = sprite.rect;

            instance.DrawPreviewGPUMaterial.SetVector("_ProtoSprite_CursorPixel", new Vector2(endTexel.x, endTexel.y));
            instance.DrawPreviewGPUMaterial.SetVector("_ProtoSprite_PreviousCursorPixel", new Vector2(startTexel.x, startTexel.y));
            instance.DrawPreviewGPUMaterial.SetInt("_ProtoSprite_BrushSize", brushSize);
            instance.DrawPreviewGPUMaterial.SetColor("_ProtoSprite_Color", (QualitySettings.activeColorSpace == ColorSpace.Linear && texture.isDataSRGB) ? color.linear : color);
            instance.DrawPreviewGPUMaterial.SetVector("_ProtoSprite_SpriteRect", new Vector4(spriteRect.xMin, spriteRect.yMin, spriteRect.xMax, spriteRect.yMax));

            Graphics.Blit(texture, instance.DrawPreviewGPUMaterial);

            if (rt.mipmapCount > 1)
                rt.GenerateMips();

            // Copy GPU memory
            Graphics.CopyTexture(rt, texture);

            RenderTexture.ReleaseTemporary(rt);

            RenderTexture.active = activeRT;

            cpuJob.Complete();*/
        }

        public static JobHandle DrawJobStart(Sprite sprite, int2 startTexel, int2 endTexel, int brushSize, Color color, BrushShape brushShape)
        {
            Texture2D texture = sprite.texture;
            int2 textureSize = new int2(texture.width, texture.height);
            var pixelDataRaw = texture.GetPixelData<Color32>(0);

            return DrawCircleJobStart(textureSize, endTexel, startTexel, brushSize, color, ref pixelDataRaw, sprite.rect, brushShape);
        }

        public static void Draw(Sprite sprite, int2 startTexel, int2 endTexel, int brushSize, Color color)
        {
            Texture2D texture = sprite.texture;
            int2 textureSize = new int2(texture.width, texture.height);
            var pixelData = texture.GetPixelData<Color32>(0);

            DrawCircle(textureSize, endTexel, startTexel, brushSize, color, ref pixelData, sprite.rect);
        }

        public static void Draw(int2 textureSize, int2 pixel, int2 previousPixel, Color color, int size, ref NativeArray<Color32> pixelData, Rect pixelRect)
        {
            DrawCircle(textureSize, pixel, previousPixel, size, color, ref pixelData, pixelRect);
        }

        public static void Draw(int2 textureSize, int2 pixel, Color color, int size, ref NativeArray<Color32> pixelData, Rect pixelRect)
        {
            DrawCircle(textureSize, pixel, pixel, size, color, ref pixelData, pixelRect);
        }

        public static void Draw(int2 textureSize, int2 pixel, Color color, int size, ref NativeArray<Color32> pixelData)
        {
            Rect pixelRect = new Rect(0, 0, textureSize.x, textureSize.y);
            DrawCircle(textureSize, pixel, pixel, size, color, ref pixelData, pixelRect);
        }

        public static void DrawCircle(int2 textureSize, int2 pixel, int2 previousPixel, int brushSize, Color color, ref NativeArray<Color32> pixelData, Rect pixelRect)
        {
            JobHandle jobHandle = DrawCircleJobStart(textureSize, pixel, previousPixel, brushSize, color, ref pixelData, pixelRect, BrushShape.CIRCLE);
            jobHandle.Complete();
        }

        public static void Draw(int2 textureSize, int2 pixel, int2 previousPixel, int brushSize, Color color, ref NativeArray<Color32> pixelData, Rect pixelRect, BrushShape brushShape)
        {
            JobHandle jobHandle = DrawCircleJobStart(textureSize, pixel, previousPixel, brushSize, color, ref pixelData, pixelRect, brushShape);
            jobHandle.Complete();
        }

        public static JobHandle DrawCircleJobStart(int2 textureSize, int2 pixel, int2 previousPixel, int brushSize, Color color, ref NativeArray<Color32> pixelData, Rect pixelRect, BrushShape brushShape)
        {
            int radius = brushSize / 2;
            float floatRadius = (float)brushSize * 0.5f;

            // Radius offset to get nicer 3 pixel circle brush size as a cross shape rather than a 3x3 square
            if (brushShape == BrushShape.CIRCLE && brushSize == 3)
                floatRadius -= 0.1f;

            float floatSqrRadius = floatRadius * floatRadius;

            // Actual center of the circle we're drawing
            Vector2 floatCenter = new Vector2(pixel.x, pixel.y);
            Vector2 floatPreviousCenter = new Vector2(previousPixel.x, previousPixel.y);
            if (brushSize % 2 == 1)
            {
                floatCenter += Vector2.one * 0.5f;
                floatPreviousCenter += Vector2.one * 0.5f;
            }

            Rect targetRect = new Rect();
            targetRect.xMin = math.clamp(math.min(pixel.x, previousPixel.x) - brushSize, 0, textureSize.x);
            targetRect.xMax = math.clamp(math.max(pixel.x, previousPixel.x) + brushSize, 0, textureSize.x);
            targetRect.yMin = math.clamp(math.min(pixel.y, previousPixel.y) - brushSize, 0, textureSize.y);
            targetRect.yMax = math.clamp(math.max(pixel.y, previousPixel.y) + brushSize, 0, textureSize.y);

            if (brushShape == BrushShape.CIRCLE)
            {
                var job = new CircleJob()
                {
                    colors = pixelData,
                    brushSize = brushSize,
                    centerX = pixel.x,
                    centerY = pixel.y,
                    radius = radius,
                    floatSqrRadius = floatSqrRadius,
                    floatRadius = floatRadius,
                    floatCenter = floatCenter,
                    floatPreviousCenter = floatPreviousCenter,
                    textureSize = textureSize,
                    paintColor = color,
                    pixelRect = pixelRect,
                    targetRect = targetRect
                };

                return job.Schedule((int)targetRect.width * (int)targetRect.height, 32);
            }
            else
            {
                var job = new SquareJob()
                {
                    colors = pixelData,
                    brushSize = brushSize,
                    centerX = pixel.x,
                    centerY = pixel.y,
                    radius = radius,
                    floatSqrRadius = floatSqrRadius,
                    floatRadius = floatRadius,
                    floatCenter = floatCenter,
                    floatPreviousCenter = floatPreviousCenter,
                    textureSize = textureSize,
                    paintColor = color,
                    pixelRect = pixelRect,
                    targetRect = targetRect
                };

                return job.Schedule((int)targetRect.width * (int)targetRect.height, 32);
            }
        }

        // Job that draws a circle into a texture
        [BurstCompile]
        struct CircleJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Color32> colors;

            [ReadOnly] public int brushSize;
            [ReadOnly] public int centerY;
            [ReadOnly] public int centerX;
            [ReadOnly] public int radius;
            [ReadOnly] public float floatSqrRadius;
            [ReadOnly] public float floatRadius;
            [ReadOnly] public float2 floatCenter;
            [ReadOnly] public float2 floatPreviousCenter;
            [ReadOnly] public int2 textureSize;
            [ReadOnly] public Color32 paintColor;
            [ReadOnly] public Rect pixelRect;
            [ReadOnly] public Rect targetRect;

            public void Execute(int i)
            {
                int pixelX = (int)targetRect.xMin + i % (int)targetRect.width;
                int pixelY = (int)targetRect.yMin + i / (int)targetRect.width;

                if (pixelX < pixelRect.xMin || pixelY < pixelRect.yMin || pixelX >= pixelRect.xMax || pixelY >= pixelRect.yMax)
                    return;

                float2 floatPixel = new float2(pixelX + 0.5f, pixelY + 0.5f);

                float sqDistanceToLine = minimum_distance_sq(floatPixel, floatCenter, floatPreviousCenter);

                if (sqDistanceToLine < floatSqrRadius)
                {
                    Color32 dstCol = colors[pixelX + pixelY * textureSize.x];
                    Color32 srcCol = paintColor;

                    colors[pixelX + pixelY * textureSize.x] = AlphaBlend(srcCol, dstCol);// srcCol * srcCol.a + dstCol * (1.0f - srcCol.a);
                    // = paintColor;
                }
            }

            Color AlphaBlend(Color srcCol, Color dstCol)
            {
                Color col = Color.clear;
                col.r = srcCol.r * srcCol.a + dstCol.r * (1.0f - srcCol.a);
                col.g = srcCol.g * srcCol.a + dstCol.g * (1.0f - srcCol.a);
                col.b = srcCol.b * srcCol.a + dstCol.b * (1.0f - srcCol.a);
                col.a = srcCol.a + dstCol.a * (1.0f - srcCol.a);

                return col;
            }

            float minimum_distance_sq(float2 p, float2 v, float2 w)
            {
                // Return minimum distance between line segment vw and point p
                float l2 = math.distancesq(v, w);  // i.e. |w-v|^2 -  avoid a sqrt
                if (l2 == 0.0) return math.distancesq(p, v);   // v == w case

                // Consider the line extending the segment, parameterized as v + t (w - v).
                // We find projection of point p onto the line. 
                // It falls where t = [(p-v) . (w-v)] / |w-v|^2
                // We clamp t from [0,1] to handle points outside the segment vw.
                float t = math.max(0, math.min(1, math.dot(p - v, w - v) / l2));
                float2 projection = v + t * (w - v);  // Projection falls on the segment
                return math.distancesq(p, projection);
            }
        }

        // Job that draws a square into a texture
        [BurstCompile]
        struct SquareJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Color32> colors;

            [ReadOnly] public int brushSize;
            [ReadOnly] public int centerY;
            [ReadOnly] public int centerX;
            [ReadOnly] public int radius;
            [ReadOnly] public float floatSqrRadius;
            [ReadOnly] public float floatRadius;
            [ReadOnly] public float2 floatCenter;
            [ReadOnly] public float2 floatPreviousCenter;
            [ReadOnly] public int2 textureSize;
            [ReadOnly] public Color paintColor;
            [ReadOnly] public Rect pixelRect;
            [ReadOnly] public Rect targetRect;

            public void Execute(int i)
            {
                int pixelX = (int)targetRect.xMin + i % (int)targetRect.width;
                int pixelY = (int)targetRect.yMin + i / (int)targetRect.width;

                if (pixelX < pixelRect.xMin || pixelY < pixelRect.yMin || pixelX >= pixelRect.xMax || pixelY >= pixelRect.yMax)
                    return;

                float2 floatPixel = new float2(pixelX + 0.5f, pixelY + 0.5f);


                float2 offset = new float2(floatRadius, floatRadius);

                if ((floatPreviousCenter.x > floatCenter.x && floatPreviousCenter.y > floatCenter.y)
                    || (floatPreviousCenter.x < floatCenter.x && floatPreviousCenter.y < floatCenter.y))
                {
                    offset = new float2(-floatRadius, floatRadius);
                }

                float2 p1 = floatPreviousCenter - offset;
                float2 p2 = floatCenter - offset;

                float2 p3 = floatPreviousCenter + offset;
                float2 p4 = floatCenter + offset;


                // If pixel is between lines 1-2 and 3-4 then color it
                float2 pixelOnLine1 = CalculateClosestPointOnLine(floatPixel, p1, p2);
                float2 pixelOnLine2 = CalculateClosestPointOnLine(floatPixel, p3, p4);

                float2 minBound = math.min(floatPreviousCenter, floatCenter) - floatRadius;
                float2 maxBound = math.max(floatPreviousCenter, floatCenter) + floatRadius;

                if ((floatPixel.y > pixelOnLine1.y && floatPixel.y < pixelOnLine2.y) || (floatPixel.x > pixelOnLine1.x && floatPixel.x < pixelOnLine2.x))
                {
                    if (floatPixel.x > minBound.x && floatPixel.x < maxBound.x && floatPixel.y > minBound.y && floatPixel.y < maxBound.y)
                        colors[pixelX + pixelY * textureSize.x] = paintColor;
                }
            }

            float2 CalculateClosestPointOnLine(float2 target, float2 p1, float2 p2)
            {
                float2 lineDirection = p2 - p1;
                float lineLength = math.length(lineDirection);

                // Ensure the line has a length greater than zero
                if (lineLength > 0)
                {
                    lineDirection /= lineLength;

                    // Calculate the projection of T onto the line
                    float dotProduct = math.dot(target - p1, lineDirection);
                    //dotProduct = clamp(dotProduct, 0, lineLength);

                    return (p1 + lineDirection * dotProduct);
                }

                // If the line has zero length, return one of the line endpoints
                return p1;
            }
        }

        // Job that draws a square into a texture
        [BurstCompile]
        struct BresenhamLineJob : IJob
        {
            [NativeDisableParallelForRestriction] public NativeArray<Color32> colors;

            [ReadOnly] public int2 startPixel;
            [ReadOnly] public int2 endPixel;
            [ReadOnly] public int2 textureSize;
            [ReadOnly] public Color paintColor;
            [ReadOnly] public int4 spriteRect;
            //[ReadOnly] public Rect pixelRect;
            //[ReadOnly] public Rect targetRect;

            public void Execute()
            {
                int x0 = startPixel.x;
                int x1 = endPixel.x;
                int y0 = startPixel.y;
                int y1 = endPixel.y;


                int dx = math.abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
                int dy = -math.abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
                int err = dx + dy, e2; /* error value e_xy */

                for (; ; )
                {  /* loop */
                    //setPixel(x0, y0);
                    int2 pixel = new int2(x0, y0);
                    if (pixel.x >= spriteRect.x && pixel.y >= spriteRect.y && pixel.x < spriteRect.z && pixel.y < spriteRect.w)
                    {
                        colors[x0 + y0 * textureSize.x] = paintColor;
                    }

                    if (x0 == x1 && y0 == y1) break;
                    e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; } /* e_xy+e_x > 0 */
                    if (e2 <= dx) { err += dx; y0 += sy; } /* e_xy+e_y < 0 */
                }


                /*int pixelX = (int)targetRect.xMin + i % (int)targetRect.width;
                int pixelY = (int)targetRect.yMin + i / (int)targetRect.width;

                if (pixelX < pixelRect.xMin || pixelY < pixelRect.yMin || pixelX >= pixelRect.xMax || pixelY >= pixelRect.yMax)
                    return;

                float2 floatPixel = new float2(pixelX + 0.5f, pixelY + 0.5f);


                float2 offset = new float2(floatRadius, floatRadius);

                if ((floatPreviousCenter.x > floatCenter.x && floatPreviousCenter.y > floatCenter.y)
                    || (floatPreviousCenter.x < floatCenter.x && floatPreviousCenter.y < floatCenter.y))
                {
                    offset = new float2(-floatRadius, floatRadius);
                }

                float2 p1 = floatPreviousCenter - offset;
                float2 p2 = floatCenter - offset;

                float2 p3 = floatPreviousCenter + offset;
                float2 p4 = floatCenter + offset;


                // If pixel is between lines 1-2 and 3-4 then color it
                float2 pixelOnLine1 = CalculateClosestPointOnLine(floatPixel, p1, p2);
                float2 pixelOnLine2 = CalculateClosestPointOnLine(floatPixel, p3, p4);

                float2 minBound = math.min(floatPreviousCenter, floatCenter) - floatRadius;
                float2 maxBound = math.max(floatPreviousCenter, floatCenter) + floatRadius;

                if ((floatPixel.y > pixelOnLine1.y && floatPixel.y < pixelOnLine2.y) || (floatPixel.x > pixelOnLine1.x && floatPixel.x < pixelOnLine2.x))
                {
                    if (floatPixel.x > minBound.x && floatPixel.x < maxBound.x && floatPixel.y > minBound.y && floatPixel.y < maxBound.y)
                        colors[pixelX + pixelY * textureSize.x] = paintColor;
                }*/
            }

            float2 CalculateClosestPointOnLine(float2 target, float2 p1, float2 p2)
            {
                float2 lineDirection = p2 - p1;
                float lineLength = math.length(lineDirection);

                // Ensure the line has a length greater than zero
                if (lineLength > 0)
                {
                    lineDirection /= lineLength;

                    // Calculate the projection of T onto the line
                    float dotProduct = math.dot(target - p1, lineDirection);
                    //dotProduct = clamp(dotProduct, 0, lineLength);

                    return (p1 + lineDirection * dotProduct);
                }

                // If the line has zero length, return one of the line endpoints
                return p1;
            }
        }

        static void BlitTexture(NativeArray<Color32> sourceData, int2 srcTextureSize, NativeArray<Color32> destinationData, int2 dstTextureSize, Rect srcRect, Rect dstRect, Rect dstClampRect, Color32 color, bool alphaBlend)
        {
            /*int srcWidth = (int)srcRect.width;
            int dstTextureWidth = (int)dstRect.width;

            var job = new TextureBlitJob
            {
                sourceData = sourceData,
                destinationData = destinationData,
                srcTextureSize = srcTextureSize,
                dstTextureSize = dstTextureSize,
                srcRect = srcRect,// new int4((int)srcRect.x, (int)srcRect.y, (int)srcRect.width, (int)srcRect.height),
                dstRect = dstRect,// new int4((int)dstRect.x, (int)dstRect.y, (int)dstRect.width, (int)dstRect.height),
                dstClampRect = dstClampRect,// new int4((int)dstClampRect.xMin, (int)dstClampRect.yMin, (int)dstClampRect.xMax, (int)dstClampRect.yMax)
                color = color,
                alphaBlend = alphaBlend
            };

            JobHandle jobHandle = job.Schedule(srcWidth * (int)srcRect.height, 64);
            jobHandle.Complete();*/

            // Tasks
            // Divide work among multiple tasks
            /*int srcHeight = (int)srcRect.height;
            int batchSize = srcWidth * srcHeight / Environment.ProcessorCount;
            Task[] tasks = new Task[Environment.ProcessorCount];

            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                int startIdx = i * batchSize;
                int endIdx = i < Environment.ProcessorCount - 1 ? (i + 1) * batchSize : srcWidth * srcHeight;

                tasks[i] = Task.Run(() =>
                {
                    for (int j = startIdx; j < endIdx; j++)
                    {
                        int x = j % srcWidth;
                        int y = j / srcWidth;

                        // Calculate corresponding pixel position on destination texture
                        int dstX = (int)Mathf.Lerp(dstRect.x, dstRect.x + dstRect.width, x / (float)srcWidth);
                        int dstY = (int)Mathf.Lerp(dstRect.y, dstRect.y + dstRect.height, y / (float)srcRect.height);


                        if (dstX < dstClampRect.xMin || dstX >= dstClampRect.xMax || dstY < dstClampRect.yMin || dstY >= dstClampRect.yMax)
                            continue;


                        Color32 src = sourceData[(int)(srcRect.y + y) * srcWidth + (int)(srcRect.x + x)];

                        if (src.a == 0.0f)
                            continue;

                        // Copy pixel from source to destination
                        int i = dstY * dstWidth + dstX;

                        destinationData[i] = src;
                    }
                });
            }

            // Wait for all tasks to complete
            Task.WaitAll(tasks);*/
        }

        [BurstCompile]
        public struct TextureBlitJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> sourceData;
            [NativeDisableParallelForRestriction] public NativeArray<Color32> destinationData;
            public int2 srcTextureSize;
            public int2 dstTextureSize;
            public Rect srcRect;
            public Rect dstRect;
            public Rect dstClampRect;
            public Color32 color;
            public bool alphaBlend;

            public void Execute(int index)
            {
                int srcX = index % (int)srcRect.width;
                int srcY = index / (int)srcRect.width;

                // Calculate corresponding pixel position on source texture
                float srcXFloat = Mathf.Lerp(srcRect.xMin, srcRect.xMax + 1, srcX / (float)srcRect.width);
                float srcYFloat = Mathf.Lerp(srcRect.yMin, srcRect.yMax + 1, srcY / (float)srcRect.height);

                // Calculate corresponding pixel position on destination texture
                float dstXFloat = Mathf.Lerp(dstRect.xMin, dstRect.xMax + 1, srcX / (float)srcRect.width);
                float dstYFloat = Mathf.Lerp(dstRect.yMin, dstRect.yMax + 1, srcY / (float)srcRect.height);

                // Clamp the destination pixel coordinates within the bounds of the destination texture
                int dstX = (int)math.floor(dstXFloat);// (int)dstXFloat;// Mathf.Clamp((int)dstXFloat, 0, dstWidth - 1);
                int dstY = (int)math.floor(dstYFloat);//(int)dstYFloat;// Mathf.Clamp((int)dstYFloat, 0, destinationData.Length / dstWidth - 1);


                int sourceIndex = (int)srcYFloat * srcTextureSize.x + (int)srcXFloat;
                Color32 sourceColor = sourceData[sourceIndex];

                int dstIndex = dstY * dstTextureSize.x + dstX;

                destinationData[dstIndex] = sourceColor;// new Color32((byte)(int)math.round(sourceColor.r * 255.0f), (byte)(int)math.round(sourceColor.g * 255.0f), (byte)(int)math.round(sourceColor.b * 255.0f), (byte)(int)math.round(sourceColor.a * 255.0f));

                return;

                /*if (dstX < dstClampRect.xMin || dstX >= dstClampRect.xMax || dstY < dstClampRect.yMin || dstY >= dstClampRect.yMax)
                {
                    return;
                }


                if (sourceIndex < 0 || sourceIndex >= sourceData.Length)
                    return;


                if (sourceColor.a == 0)
                    return;

                sourceColor = MultiplyColors(sourceColor, color);
                //sourceColor = color;

                //if (dstIndex < 0 || dstIndex >= destinationData.Length)
                //return;

                var dstColor = destinationData[dstIndex];

                // Copy pixel from source to destination
                if (alphaBlend)
                {
                    destinationData[dstIndex] = AlphaBlend32(sourceColor, dstColor);
                }
                else
                {
                    destinationData[dstIndex] = sourceColor;
                }
                //destinationData[dstIndex] = AlphaBlendColors(sourceColor, dstColor);
                */

                /*int x = index % srcWidth;
                int y = index / srcWidth;

                // Calculate corresponding pixel position on destination texture
                int dstX = (int)math.lerp(dstRect.x, dstRect.x + dstRect.z, x / (float)srcWidth);
                int dstY = (int)math.lerp(dstRect.y, dstRect.y + dstRect.w, y / (float)srcRect.w);


                if (dstX < dstClampRect.x || dstX >= dstClampRect.z || dstY < dstClampRect.y || dstY >= dstClampRect.w)
                    return;


                Color32 src = sourceData[(int)(srcRect.y + y) * srcWidth + (int)(srcRect.x + x)];

                if (src.a == 0)
                    return;

                // Copy pixel from source to destination
                int i = dstY * dstWidth + dstX;

                destinationData[i] = src;*/
            }

            Color AlphaBlend(Color srcCol, Color dstCol)
            {
                Color col = Color.clear;
                col.r = srcCol.r * srcCol.a + dstCol.r * (1.0f - srcCol.a);
                col.g = srcCol.g * srcCol.a + dstCol.g * (1.0f - srcCol.a);
                col.b = srcCol.b * srcCol.a + dstCol.b * (1.0f - srcCol.a);
                col.a = srcCol.a + dstCol.a * (1.0f - srcCol.a);

                return col;
            }

            Color32 AlphaBlend32(Color32 srcCol, Color32 dstCol)
            {
                int srcA = srcCol.a;
                int oneMinusSrcA = 255 - srcCol.a;


                int r = 0;
                int g = 0;
                int b = 0;
                int a = 0;

                float rFloat = (srcCol.r * srcA + dstCol.r * oneMinusSrcA) / 255.0f;

                if (srcCol.r > rFloat)
                {
                    r = Mathf.CeilToInt(rFloat);
                }
                else
                {
                    r = Mathf.FloorToInt(rFloat);
                }

                float gFloat = (srcCol.g * srcA + dstCol.g * oneMinusSrcA) / 255.0f;

                if (srcCol.g > gFloat)
                {
                    g = Mathf.CeilToInt(gFloat);
                }
                else
                {
                    g = Mathf.FloorToInt(gFloat);
                }

                float bFloat = (srcCol.b * srcA + dstCol.b * oneMinusSrcA) / 255.0f;

                if (srcCol.b > bFloat)
                {
                    b = Mathf.CeilToInt(bFloat);
                }
                else
                {
                    b = Mathf.FloorToInt(bFloat);
                }

                r = (int)rFloat;
                g = (int)gFloat;
                b = (int)bFloat;

                a = Mathf.CeilToInt(dstCol.a + ((255 - dstCol.a) * srcCol.a) / 255.0f);

                //int g = (srcCol.g * srcA + dstCol.g * oneMinusSrcA) / 255;
                //int b = (srcCol.b * srcA + dstCol.b * oneMinusSrcA) / 255;
                //int a = (srcCol.a + (dstCol.a * oneMinusSrcA) / 255);
                //int a = Mathf.CeilToInt(dstCol.a + ((255 - dstCol.a) * srcCol.a) / 255.0f);// * (srcCol.a + (dstCol.a * oneMinusSrcA) / 255);

                //Debug.Log(r + " " + g + " " + b + " " + a);
                //Debug.Log(((srcCol.g * srcA + dstCol.g * oneMinusSrcA) / 255.0));
                //g = 255;

                Color32 result = new Color32((byte)r, (byte)g, (byte)b, (byte)a);
                

                return result;

                /*Color col = Color.clear;
                col.r = srcCol.r * srcCol.a + dstCol.r * (1.0f - srcCol.a);
                col.g = srcCol.g * srcCol.a + dstCol.g * (1.0f - srcCol.a);
                col.b = srcCol.b * srcCol.a + dstCol.b * (1.0f - srcCol.a);
                col.a = srcCol.a + dstCol.a * (1.0f - srcCol.a);

                return col;*/
            }

            Color32 AlphaBlendColors(Color32 srcColor, Color32 dstColor)
            {
                // Calculate the resulting alpha
                int srcAlpha = srcColor.a;
                int dstAlpha = dstColor.a;
                int resultAlpha = srcAlpha + ((255 - srcAlpha) * dstAlpha) / 255;

                // Calculate the resulting color components
                byte resultR = (byte)((srcColor.r * srcAlpha + dstColor.r * (255 - srcAlpha)) / (resultAlpha == 0 ? 1 : resultAlpha));
                byte resultG = (byte)((srcColor.g * srcAlpha + dstColor.g * (255 - srcAlpha)) / (resultAlpha == 0 ? 1 : resultAlpha));
                byte resultB = (byte)((srcColor.b * srcAlpha + dstColor.b * (255 - srcAlpha)) / (resultAlpha == 0 ? 1 : resultAlpha));

                // Return the resulting Color32
                return new Color32(resultR, resultG, resultB, (byte)resultAlpha);
            }

            Color32 MultiplyColors(Color32 color1, Color32 color2)
            {
                // Multiply each component individually
                byte r = (byte)((color1.r * color2.r) / 255);
                byte g = (byte)((color1.g * color2.g) / 255);
                byte b = (byte)((color1.b * color2.b) / 255);
                byte a = (byte)((color1.a * color2.a) / 255);

                // Return the resulting Color32
                return new Color32(r, g, b, a);
            }
        }

        public static Mesh GetTempQuadMesh(Rect verticesRect, Rect spriteRect, bool flipXUV, bool flipYUV)
        {
            if (instance.m_TempQuadMesh == null)
            {
                instance.m_TempQuadMesh = new Mesh();
                //instance.m_TempQuadMesh.MarkDynamic();
            }

            Mesh mesh = instance.m_TempQuadMesh;

            

            Vector3[] vertices = new Vector3[4]
            {
            new Vector3(verticesRect.xMin, verticesRect.yMin, 0),
            new Vector3(verticesRect.xMax, verticesRect.yMin, 0),
            new Vector3(verticesRect.xMin, verticesRect.yMax, 0),
            new Vector3(verticesRect.xMax, verticesRect.yMax, 0)
            };



            /*new Vector3(-width * 0.5f, -height * 0.5f, 0),
            new Vector3(width * 0.5f, -height * 0.5f, 0),
            new Vector3(-width * 0.5f, height * 0.5f, 0),
            new Vector3(width * 0.5f, height * 0.5f, 0)*/

            mesh.vertices = vertices;

            int[] tris = new int[6]
            {
            // lower left triangle
            0, 2, 1,
            // upper right triangle
            2, 3, 1
            };
            mesh.triangles = tris;

            Vector3[] normals = new Vector3[4]
            {
            -Vector3.forward,
            -Vector3.forward,
            -Vector3.forward,
            -Vector3.forward
            };
            mesh.normals = normals;

            Vector2[] uv = new Vector2[4]
            {
            new Vector2(flipXUV? spriteRect.xMax : spriteRect.xMin, flipYUV? spriteRect.yMax : spriteRect.yMin),
            new Vector2(flipXUV? spriteRect.xMin : spriteRect.xMax, flipYUV? spriteRect.yMax : spriteRect.yMin),
            new Vector2(flipXUV? spriteRect.xMax : spriteRect.xMin, flipYUV? spriteRect.yMin : spriteRect.yMax),
            new Vector2(flipXUV? spriteRect.xMin : spriteRect.xMax, flipYUV? spriteRect.yMin : spriteRect.yMax)
            };

            Vector2[] uv2 = new Vector2[4]
            {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
            };


            mesh.uv = uv;
            mesh.uv2 = uv2;

            return mesh;
        }

        [InitializeOnLoad]
        public static class GlobalKeyEventHandler
        {
            public static event Action<Event> OnKeyEvent;
            public static bool RegistrationSucceeded = false;

            static GlobalKeyEventHandler()
            {
                RegistrationSucceeded = false;
                string msg = "";
                try
                {
                    System.Reflection.FieldInfo info = typeof(EditorApplication).GetField(
                        "globalEventHandler",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                        );
                    if (info != null)
                    {
                        EditorApplication.CallbackFunction value = (EditorApplication.CallbackFunction)info.GetValue(null);

                        value -= onKeyPressed;
                        value += onKeyPressed;

                        info.SetValue(null, value);

                        RegistrationSucceeded = true;
                    }
                    else
                    {
                        msg = "globalEventHandler not found";
                    }
                }
                catch (Exception e)
                {
                    msg = e.Message;
                }
                finally
                {
                    if (!RegistrationSucceeded)
                    {
                        Debug.LogWarning("GlobalKeyEventHandler: error while registering for globalEventHandler: " + msg);
                    }
                }
            }

            private static void onKeyPressed()
            {
                if (OnKeyEvent == null)
                    return;
                OnKeyEvent.Invoke(Event.current);
            }
        }
    }
}
